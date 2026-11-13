using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetVips;
using Struo.Application.Files;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Files;

/// <summary>A permanent delete of a file, from either route, removes that file's cached variants.</summary>
[Collection("ApiIntegration")]
public class ImageVariantPurgeTests(ApiFactory factory) : IDisposable
{
    private readonly string _cacheRoot = Path.Combine(Path.GetTempPath(), "struo-purge-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_cacheRoot)) Directory.Delete(_cacheRoot, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);

    private WebApplicationFactory<Program> Host(Action<IServiceCollection>? configure = null, long? cacheMaxBytes = null) =>
        factory.WithWebHostBuilder(b =>
        {
            var settings = new Dictionary<string, string?> { ["Struo:Files:ImageTransform:CachePath"] = _cacheRoot };
            if (cacheMaxBytes is not null)
                settings["Struo:Files:ImageTransform:CacheMaxBytes"] = cacheMaxBytes.Value.ToString();
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(settings));
            b.ConfigureTestServices(s => configure?.Invoke(s));
        });

    private async Task<HttpClient> SignedInAsync(WebApplicationFactory<Program> host)
    {
        await factory.EnsureAdminSeededAsync();
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(Struo.Api.Auth.CsrfProtectionMiddleware.HeaderName, "1");
        (await client.PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword })).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<string> UploadPng(HttpClient c)
    {
        var bytes = Image.Black(300, 300).Cast(Enums.BandFormat.Uchar).WriteToBuffer(".png");
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var resp = await c.PostAsync("/api/files", new MultipartFormDataContent { { content, "file", "img.png" } });
        resp.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement
            .GetProperty("data").GetProperty("id").GetString()!;
    }

    private static async Task RequestVariantsAsync(HttpClient c, string id)
    {
        foreach (var width in new[] { 64, 128 })
            (await c.GetAsync($"/api/files/{id}/content?width={width}&format=png")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private int VariantCount(string id)
    {
        var dir = Path.Combine(_cacheRoot, id);
        return Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Count() : 0;
    }

    [Theory]
    [InlineData("/api/files/{0}?purge=true")]
    [InlineData("/api/items/file/{0}?purge=true")]
    public async Task Purging_a_file_removes_its_variants_and_not_another_files(string route)
    {
        using var host = Host();
        var c = await SignedInAsync(host);
        var doomed = await UploadPng(c);
        var other = await UploadPng(c);
        await RequestVariantsAsync(c, doomed);
        await RequestVariantsAsync(c, other);
        VariantCount(doomed).Should().Be(2);
        VariantCount(other).Should().Be(2);

        (await c.DeleteAsync(string.Format(route, doomed))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        VariantCount(doomed).Should().Be(0);
        Directory.Exists(Path.Combine(_cacheRoot, doomed)).Should().BeFalse();
        VariantCount(other).Should().Be(2);
    }

    [Fact]
    public async Task Trash_and_restore_leave_the_variants_alone()
    {
        using var host = Host();
        var c = await SignedInAsync(host);
        var id = await UploadPng(c);
        await RequestVariantsAsync(c, id);

        (await c.DeleteAsync($"/api/files/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        VariantCount(id).Should().Be(2);
        (await c.PostAsync($"/api/files/{id}/restore", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        VariantCount(id).Should().Be(2);
    }

    private sealed class ThrowingCache : IImageVariantCache
    {
        public string DeriveKey(Guid fileId, string fileVersion, ImageTransformRequest req) => "unused";
        public Task<byte[]?> TryGetAsync(string key, CancellationToken ct) => Task.FromResult<byte[]?>(null);
        public Task SetAsync(string key, byte[] bytes, CancellationToken ct) => Task.CompletedTask;
        public Task RemoveAllForFileAsync(Guid fileId, CancellationToken ct) => throw new IOException("cache directory is locked");
    }

    // The host replaces the logging providers, so the service under test gets its logger directly.
    private sealed class WarningCollector<T> : ILogger<T>
    {
        public readonly ConcurrentBag<string> Warnings = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Warnings.Add(formatter(state, exception));
        }
    }

    [Fact]
    public async Task A_cache_that_cannot_be_cleaned_does_not_fail_the_delete_and_is_logged()
    {
        var logs = new WarningCollector<Struo.Infrastructure.Files.FileService>();
        using var host = Host(s =>
        {
            s.AddSingleton<IImageVariantCache>(new ThrowingCache());
            s.AddSingleton<ILogger<Struo.Infrastructure.Files.FileService>>(logs);
        });
        var c = await SignedInAsync(host);
        var id = await UploadPng(c);

        (await c.DeleteAsync($"/api/files/{id}?purge=true")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await c.GetAsync($"/api/files/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        logs.Warnings.Should().ContainSingle(w => w.Contains(id, StringComparison.OrdinalIgnoreCase),
            "a cleanup that cannot run is one warning, never silent and never repeated");
    }

    [Fact]
    public async Task Host_startup_removes_earlier_layout_files_and_leaves_other_files()
    {
        var shard = Path.Combine(_cacheRoot, "ab");
        Directory.CreateDirectory(shard);
        var legacy = Path.Combine(shard, "ab" + new string('1', 62));
        var unrelated = Path.Combine(_cacheRoot, "readme.txt");
        await File.WriteAllBytesAsync(legacy, [1]);
        await File.WriteAllBytesAsync(unrelated, [1]);

        using var host = Host();
        using var client = host.CreateClient();   // builds the host and runs its startup checks

        // The upkeep runs in the background; wait for its effect rather than for a fixed time.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (File.Exists(legacy) && DateTime.UtcNow < deadline) await Task.Delay(50);

        File.Exists(legacy).Should().BeFalse();
        File.Exists(unrelated).Should().BeTrue();
    }

    // The cleanup's outer catch (an exception that is not an IO error) has no seam without changing the
    // production shape; this covers the failure it can actually meet, a directory it may not list.
    [WindowsOnlyFact("uses a deny ACL on a subdirectory")]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task Host_startup_survives_a_cache_directory_it_cannot_list_and_logs_a_warning()
    {
        var shard = Directory.CreateDirectory(Path.Combine(_cacheRoot, "ab"));
        var me = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var deny = new System.Security.AccessControl.FileSystemAccessRule(
            me, System.Security.AccessControl.FileSystemRights.ListDirectory,
            System.Security.AccessControl.AccessControlType.Deny);
        var acl = shard.GetAccessControl();
        acl.AddAccessRule(deny);
        shard.SetAccessControl(acl);
        try
        {
            var logs = new WarningCollector<Struo.Infrastructure.Files.DiskImageVariantCache>();
            using var host = Host(s => s.AddSingleton<ILogger<Struo.Infrastructure.Files.DiskImageVariantCache>>(logs));
            using var client = host.CreateClient();

            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (logs.Warnings.IsEmpty && DateTime.UtcNow < deadline) await Task.Delay(50);

            logs.Warnings.Should().NotBeEmpty();
            (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the host is serving");
        }
        finally
        {
            acl.RemoveAccessRule(deny);
            shard.SetAccessControl(acl);
        }
    }

    /// <summary>Holds the first transform until released, so a delete can happen while it is in flight.</summary>
    private sealed class GatedTransformer : IImageTransformer
    {
        private int _calls;
        public readonly TaskCompletionSource Reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ImageTransformResult? Transform(ReadOnlySpan<byte> source, ImageTransformRequest req, long maxSourcePixels)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                Reached.TrySetResult();
                Release.Task.Wait(Wait);
            }
            return new ImageTransformResult([1, 2, 3, 4], "image/png", 1, 1);
        }
    }

    [Theory]
    [InlineData("/api/files/{0}?purge=true", 0L)]
    [InlineData("/api/items/file/{0}?purge=true", 0L)]
    [InlineData("/api/files/{0}?purge=true", 100_000_000L)]
    [InlineData("/api/items/file/{0}?purge=true", 100_000_000L)]
    public async Task A_purge_that_completes_while_a_transform_is_in_flight_leaves_no_variant(string route, long cap)
    {
        var gate = new GatedTransformer();
        using var host = Host(s => s.AddSingleton<IImageTransformer>(gate), cap);
        var c = await SignedInAsync(host);
        var id = await UploadPng(c);

        var inFlight = c.GetAsync($"/api/files/{id}/content?width=64&format=png");
        await gate.Reached.Task.WaitAsync(Wait);
        (await c.DeleteAsync(string.Format(route, id))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        gate.Release.SetResult();
        var response = await inFlight;

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the request that was in flight is still answered");
        VariantCount(id).Should().Be(0, "the variant landed after the purge removed the others, so the writer removes it");
        Directory.Exists(Path.Combine(_cacheRoot, id)).Should().BeFalse();
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(100_000_000L)]
    public async Task Trashing_the_file_while_a_transform_is_in_flight_keeps_the_variant(long cap)
    {
        var gate = new GatedTransformer();
        using var host = Host(s => s.AddSingleton<IImageTransformer>(gate), cap);
        var c = await SignedInAsync(host);
        var id = await UploadPng(c);

        var inFlight = c.GetAsync($"/api/files/{id}/content?width=64&format=png");
        await gate.Reached.Task.WaitAsync(Wait);
        (await c.DeleteAsync($"/api/files/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        gate.Release.SetResult();
        var response = await inFlight;

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        VariantCount(id).Should().Be(1, "trash keeps the file, so it keeps its variants");
    }

    /// <summary>A cache whose removal of a file's variants always fails; everything else is the real thing.</summary>
    private sealed class RemovalFailsCache(IImageVariantCache inner) : IImageVariantCache
    {
        public string DeriveKey(Guid fileId, string fileVersion, ImageTransformRequest req) =>
            inner.DeriveKey(fileId, fileVersion, req);

        public Task<byte[]?> TryGetAsync(string key, CancellationToken ct) => inner.TryGetAsync(key, ct);

        public Task SetAsync(string key, byte[] bytes, CancellationToken ct) => inner.SetAsync(key, bytes, ct);

        public Task RemoveAllForFileAsync(Guid fileId, CancellationToken ct) =>
            throw new IOException("variant removal failed");
    }

    [Fact]
    public async Task A_failing_variant_removal_after_a_purge_in_flight_is_logged_and_the_response_still_succeeds()
    {
        var gate = new GatedTransformer();
        var controllerLog = new WarningCollector<Struo.Api.Controllers.FilesController>();
        using var host = Host(s =>
        {
            s.AddSingleton<IImageTransformer>(gate);
            s.AddSingleton<IImageVariantCache>(new RemovalFailsCache(new Struo.Infrastructure.Files.DiskImageVariantCache(_cacheRoot)));
            s.AddSingleton<ILogger<Struo.Api.Controllers.FilesController>>(controllerLog);
        });
        var c = await SignedInAsync(host);
        var id = await UploadPng(c);

        var inFlight = c.GetAsync($"/api/files/{id}/content?width=64&format=png");
        await gate.Reached.Task.WaitAsync(Wait);
        (await c.DeleteAsync($"/api/files/{id}?purge=true")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        gate.Release.SetResult();
        var response = await inFlight;

        response.StatusCode.Should().Be(HttpStatusCode.OK, "a failed cleanup costs disk, not the response");
        controllerLog.Warnings.Should().ContainSingle(w => w.Contains(id, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class InstantTransformer : IImageTransformer
    {
        public ImageTransformResult? Transform(ReadOnlySpan<byte> source, ImageTransformRequest req, long maxSourcePixels) =>
            new([1, 2, 3, 4], "image/png", 1, 1);
    }

    // The other order of the interleaving: the variant has landed, the purge completes, and only then does
    // the writer check that the file still exists.
    [Theory]
    [InlineData("/api/files/{0}?purge=true", 0L)]
    [InlineData("/api/items/file/{0}?purge=true", 0L)]
    [InlineData("/api/files/{0}?purge=true", 100_000_000L)]
    [InlineData("/api/items/file/{0}?purge=true", 100_000_000L)]
    public async Task A_purge_that_completes_after_the_variant_landed_and_before_the_row_check_leaves_no_variant(
        string route, long cap)
    {
        using var landed = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var cache = new Struo.Infrastructure.Files.DiskImageVariantCache(_cacheRoot, cap)
        {
            VariantLanded = _ =>
            {
                landed.Set();
                release.Wait(Wait);
            },
        };
        using var host = Host(s =>
        {
            s.AddSingleton<IImageTransformer>(new InstantTransformer());
            s.AddSingleton<IImageVariantCache>(cache);
        });
        var c = await SignedInAsync(host);
        var id = await UploadPng(c);

        var inFlight = c.GetAsync($"/api/files/{id}/content?width=64&format=png");
        landed.Wait(Wait).Should().BeTrue("the variant landed");
        VariantCount(id).Should().Be(1);
        (await c.DeleteAsync(string.Format(route, id))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        release.Set();
        var response = await inFlight;

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the image is still served");
        VariantCount(id).Should().Be(0, "no variant of a purged file remains, whichever side removed it");
        Directory.Exists(Path.Combine(_cacheRoot, id)).Should().BeFalse();
    }
}
