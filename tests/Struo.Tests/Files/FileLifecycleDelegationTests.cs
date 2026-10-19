using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SqlSugar;
using Struo.Api.Auth;
using Struo.Application.Files;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Settings;
using Struo.Tests.Support;
using Xunit;
using File = Struo.Infrastructure.Files.File;

namespace Struo.Tests.Files;

// The generic items API and GraphQL delete of a `file` row do exactly what the media library does:
// trash clears the brand-logo reference; purge removes translations, the row, the logo reference
// and the stored blob. A failing blob delete never fails the request and is logged with its key.
[Collection("ApiIntegration")]
public sealed class FileLifecycleDelegationTests(ApiFactory factory)
{
    private sealed class RecordingStorage : IFileStorage
    {
        public readonly Dictionary<string, byte[]> Blobs = new();
        public readonly List<string> Deleted = [];
        public bool ThrowOnDelete { get; set; }

        public Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            content.CopyTo(ms);
            Blobs[key] = ms.ToArray();
            return Task.CompletedTask;
        }

        public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) =>
            Task.FromResult<Stream>(new MemoryStream(Blobs[key]));

        public Task DeleteAsync(string key, CancellationToken ct = default)
        {
            if (ThrowOnDelete) throw new IOException("boom");
            Deleted.Add(key);
            Blobs.Remove(key);
            return Task.CompletedTask;
        }

        public bool SupportsPresignedUrls => false;
        public Task<string?> GetPresignedUrlAsync(string key, TimeSpan ttl, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed record Host(
        WebApplicationFactory<Program> Factory, HttpClient Client, RecordingStorage Storage, ListLogger<FileService> Log);

    private async Task<Host> CreateHostAsync()
    {
        var storage = new RecordingStorage();
        var log = new ListLogger<FileService>();
        var derived = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<IFileStorage>(storage);
            s.AddSingleton<ILogger<FileService>>(log);
        }));
        await factory.EnsureAdminSeededAsync();
        var client = derived.CreateClient();
        client.DefaultRequestHeaders.Add(CsrfProtectionMiddleware.HeaderName, "1");
        (await client.PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword })).EnsureSuccessStatusCode();
        return new Host(derived, client, storage, log);
    }

    private static async Task<(string Id, string Key)> UploadAsync(Host h, string name)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("payload"));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        var resp = await h.Client.PostAsync("/api/files", new MultipartFormDataContent { { content, "file", name } });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var data = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        var id = data.GetProperty("id").GetString()!;
        using var scope = h.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var row = await db.Queryable<File>().In(Guid.Parse(id)).FirstAsync();
        return (id, row.StorageKey);
    }

    private static async Task SetLogoAsync(Host h, string? fileId) =>
        (await h.Client.PutAsJsonAsync("/api/settings/branding", new { brandName = "Brand", logoFileId = fileId }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

    private static async Task<Guid?> LogoAsync(Host h)
    {
        using var scope = h.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var row = await db.Queryable<SiteSettings>().Where(s => s.Id == SiteSettings.SingletonId).FirstAsync();
        return row?.LogoFileId;
    }

    private static async Task<int> TranslationCountAsync(Host h, string id)
    {
        using var scope = h.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var fileId = Guid.Parse(id);
        return await db.Queryable<FileTranslation>().Where(t => t.FileId == fileId).CountAsync();
    }

    private static async Task<bool> RowExistsAsync(Host h, string id)
    {
        using var scope = h.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var fileId = Guid.Parse(id);
        return await db.Queryable<File>().ClearFilter<Struo.Domain.Auditing.ISoftDeletable>()
            .Where(f => f.Id == fileId).AnyAsync();
    }

    [Fact]
    public async Task Generic_trash_of_the_logo_file_clears_the_logo_reference()
    {
        var h = await CreateHostAsync();
        var (id, _) = await UploadAsync(h, "logo-trash.txt");
        await SetLogoAsync(h, id);
        try
        {
            (await h.Client.DeleteAsync($"/api/items/file/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

            (await LogoAsync(h)).Should().BeNull();
            (await RowExistsAsync(h, id)).Should().BeTrue("trash keeps the row for restore");
        }
        finally { await SetLogoAsync(h, null); }
    }

    [Fact]
    public async Task Generic_trash_of_an_already_trashed_file_is_still_success()
    {
        var h = await CreateHostAsync();
        var (id, _) = await UploadAsync(h, "twice.txt");

        (await h.Client.DeleteAsync($"/api/items/file/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await h.Client.DeleteAsync($"/api/items/file/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await h.Client.DeleteAsync($"/api/items/file/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Generic_purge_removes_blob_translations_row_and_logo_reference()
    {
        var h = await CreateHostAsync();
        var (id, key) = await UploadAsync(h, "purge-generic.txt");
        await SetLogoAsync(h, id);
        (await TranslationCountAsync(h, id)).Should().BeGreaterThan(0);
        try
        {
            (await h.Client.DeleteAsync($"/api/items/file/{id}?purge=true")).StatusCode
                .Should().Be(HttpStatusCode.NoContent);

            h.Storage.Deleted.Should().Contain(key);
            (await TranslationCountAsync(h, id)).Should().Be(0);
            (await RowExistsAsync(h, id)).Should().BeFalse();
            (await LogoAsync(h)).Should().BeNull();
        }
        finally { await SetLogoAsync(h, null); }
    }

    [Fact]
    public async Task GraphQl_delete_of_a_file_cleans_up_like_the_media_library()
    {
        var h = await CreateHostAsync();
        var (id, key) = await UploadAsync(h, "purge-graphql.txt");
        await SetLogoAsync(h, id);
        try
        {
            var resp = await h.Client.PostAsJsonAsync("/graphql", new
            {
                query = $"mutation {{ deleteFile(id: \"{id}\", purge: true) }}"
            });

            var body = await resp.Content.ReadAsStringAsync();
            JsonDocument.Parse(body).RootElement.GetProperty("data").GetProperty("deleteFile").GetBoolean()
                .Should().BeTrue(body);
            h.Storage.Deleted.Should().Contain(key);
            (await TranslationCountAsync(h, id)).Should().Be(0);
            (await LogoAsync(h)).Should().BeNull();
        }
        finally { await SetLogoAsync(h, null); }
    }

    [Fact]
    public async Task Failing_blob_delete_keeps_purge_successful_and_logs_the_key()
    {
        var h = await CreateHostAsync();
        var (id, key) = await UploadAsync(h, "purge-throws.txt");
        h.Storage.ThrowOnDelete = true;

        (await h.Client.DeleteAsync($"/api/items/file/{id}?purge=true")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        (await RowExistsAsync(h, id)).Should().BeFalse();
        h.Log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains(key));
    }
}
