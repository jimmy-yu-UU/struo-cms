using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetVips;
using Struo.Application.Files;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Files;

[Collection("ApiIntegration")]
public class ImageTransformHardeningEndpointTests(ApiFactory factory)
{
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private static async Task<string> Upload(HttpClient c, byte[] bytes, string contentType, string name)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        var resp = await c.PostAsync("/api/files", new MultipartFormDataContent { { content, "file", name } });
        resp.EnsureSuccessStatusCode();
        return Root(await resp.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;
    }

    private static Task<string> UploadPng(HttpClient c, int w, int h) =>
        Upload(c, Image.Black(w, h).Cast(Enums.BandFormat.Uchar).WriteToBuffer(".png"), "image/png", "img.png");

    private HttpClient HostWith(
        IImageTransformer? transformer, int maxConcurrent, int timeoutSeconds,
        Action<IServiceCollection>? configure = null, long? maxSourcePixels = null) =>
        factory.WithWebHostBuilder(b =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["Struo:Files:ImageTransform:MaxConcurrentTransforms"] = maxConcurrent.ToString(),
                ["Struo:Files:ImageTransform:QueueTimeoutSeconds"] = timeoutSeconds.ToString(),
            };
            if (maxSourcePixels is not null)
                settings["Struo:Files:ImageTransform:MaxSourcePixels"] = maxSourcePixels.Value.ToString();
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(settings));
            b.ConfigureTestServices(s =>
            {
                if (transformer is not null) s.AddSingleton(transformer);
                configure?.Invoke(s);
            });
        }).CreateClient();

    [Fact]
    public async Task Svg_with_transform_params_returns_the_original_bytes()
    {
        var c = await factory.CreateAuthenticatedClientAsync();
        const string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><rect width=\"10\" height=\"10\"/></svg>";
        var id = await Upload(c, Encoding.UTF8.GetBytes(svg), "image/svg+xml", "a.svg");

        var resp = await c.GetAsync($"/api/files/{id}/content?width=50&format=png");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resp.Content.ReadAsStringAsync()).Should().Be(svg);
    }

    [Fact]
    public async Task Source_over_the_pixel_limit_returns_the_original_bytes()
    {
        var c = await factory.CreateAuthenticatedClientAsync();
        var original = Image.Black(10000, 6000).Cast(Enums.BandFormat.Uchar).WriteToBuffer(".png");   // 60 MP
        var id = await Upload(c, original, "image/png", "big.png");

        var resp = await c.GetAsync($"/api/files/{id}/content?width=128&format=png");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resp.Content.ReadAsByteArrayAsync()).Should().Equal(original);
    }

    [Fact]
    public async Task Avif_grid_image_returns_the_original_bytes_and_a_plain_avif_is_transformed()
    {
        var c = await factory.CreateAuthenticatedClientAsync();
        byte[] grid =
        [
            0, 0, 0, 16, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'a', (byte)'v', (byte)'i', (byte)'f', 0, 0, 0, 0,
            0, 0, 0, 20, (byte)'i', (byte)'s', (byte)'p', (byte)'e', 0, 0, 0, 0, 0, 0, 0, 64, 0, 0, 0, 64,
            0, 0, 0, 21, (byte)'i', (byte)'n', (byte)'f', (byte)'e', 2, 0, 0, 0, 0, 1, 0, 0,
            (byte)'g', (byte)'r', (byte)'i', (byte)'d'
        ];
        var gridId = await Upload(c, grid, "image/avif", "grid.avif");
        var plain = Image.Black(300, 200).Cast(Enums.BandFormat.Uchar).WriteToBuffer(".avif");
        var plainId = await Upload(c, plain, "image/avif", "plain.avif");

        var gridResp = await c.GetAsync($"/api/files/{gridId}/content?width=128&format=png");
        var plainResp = await c.GetAsync($"/api/files/{plainId}/content?width=128&format=png");

        (await gridResp.Content.ReadAsByteArrayAsync()).Should().Equal(grid);
        using var img = Image.NewFromBuffer(await plainResp.Content.ReadAsByteArrayAsync());
        img.Width.Should().Be(128);
    }

    [Fact]
    public async Task Avif_grid_item_with_a_64_bit_box_size_returns_the_original_bytes()
    {
        var c = await factory.CreateAuthenticatedClientAsync();
        byte[] grid =
        [
            0, 0, 0, 16, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'a', (byte)'v', (byte)'i', (byte)'f', 0, 0, 0, 0,
            0, 0, 0, 20, (byte)'i', (byte)'s', (byte)'p', (byte)'e', 0, 0, 0, 0, 0, 0, 0, 64, 0, 0, 0, 64,
            0, 0, 0, 1, (byte)'i', (byte)'n', (byte)'f', (byte)'e', 0, 0, 0, 0, 0, 0, 0, 29,
            2, 0, 0, 0, 0, 1, 0, 0, (byte)'g', (byte)'r', (byte)'i', (byte)'d'
        ];
        var id = await Upload(c, grid, "image/avif", "grid64.avif");

        var resp = await c.GetAsync($"/api/files/{id}/content?width=128&format=png");

        (await resp.Content.ReadAsByteArrayAsync()).Should().Equal(grid);
    }

    [Fact]
    public async Task Source_whose_decoded_size_exceeds_the_limit_returns_the_original_bytes_even_when_the_probe_is_small()
    {
        var host = HostWith(null, maxConcurrent: 1, timeoutSeconds: 5,
            s => s.AddSingleton<IImageDimensionReader>(new FixedDimensions((10, 10))), maxSourcePixels: 100_000);
        var original = Image.Black(600, 600).Cast(Enums.BandFormat.Uchar).WriteToBuffer(".png");
        var id = await Upload(await factory.CreateAuthenticatedClientAsync(), original, "image/png", "y.png");

        var resp = await host.GetAsync($"/api/files/{id}/content?width=128&format=png");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resp.Content.ReadAsByteArrayAsync()).Should().Equal(original);
    }

    [Fact]
    public async Task Unknown_fit_is_a_400()
    {
        var c = await factory.CreateAuthenticatedClientAsync();
        var id = await UploadPng(c, 100, 100);

        var resp = await c.GetAsync($"/api/files/{id}/content?width=64&fit=garbage");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cover_keeps_the_requested_aspect_ratio_after_snapping()
    {
        var c = await factory.CreateAuthenticatedClientAsync();
        var id = await UploadPng(c, 1200, 1200);

        var resp = await c.GetAsync($"/api/files/{id}/content?width=300&height=200&fit=cover&format=png");

        using var img = Image.NewFromBuffer(await resp.Content.ReadAsByteArrayAsync());
        (img.Width, img.Height).Should().Be((320, 216));
    }

    [Fact]
    public async Task Requests_that_snap_to_the_same_size_share_one_transform()
    {
        var fake = new CountingTransformer(TimeSpan.Zero);
        var host = HostWith(fake, maxConcurrent: 2, timeoutSeconds: 5);
        var id = await UploadPng(await factory.CreateAuthenticatedClientAsync(), 600, 600);

        foreach (var w in new[] { 100, 101, 120, 128 })
            (await host.GetAsync($"/api/files/{id}/content?width={w}&format=png")).StatusCode.Should().Be(HttpStatusCode.OK);

        fake.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Identical_cold_requests_both_succeed_and_never_exceed_the_concurrency_cap()
    {
        var fake = new CountingTransformer(TimeSpan.FromMilliseconds(250));
        var host = HostWith(fake, maxConcurrent: 1, timeoutSeconds: 10);
        var id = await UploadPng(await factory.CreateAuthenticatedClientAsync(), 600, 600);

        var responses = await Task.WhenAll(
            host.GetAsync($"/api/files/{id}/content?width=128&format=png"),
            host.GetAsync($"/api/files/{id}/content?width=128&format=png"));

        responses.Select(r => r.StatusCode).Should().AllBeEquivalentTo(HttpStatusCode.OK);
        fake.MaxConcurrent.Should().Be(1);
    }

    [Fact]
    public async Task A_request_that_cannot_get_a_slot_in_time_is_a_503_envelope()
    {
        var blocking = new BlockingTransformer();
        var host = HostWith(blocking, maxConcurrent: 1, timeoutSeconds: 1);
        var id = await UploadPng(await factory.CreateAuthenticatedClientAsync(), 600, 600);

        var first = host.GetAsync($"/api/files/{id}/content?width=128&format=png");
        blocking.Entered.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();

        var second = await host.GetAsync($"/api/files/{id}/content?width=256&format=png");

        second.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        Root(await second.Content.ReadAsStringAsync()).GetProperty("error").GetProperty("code").GetString()
            .Should().Be("IMAGE_TRANSFORM_BUSY");

        blocking.Release.Set();
        (await first).StatusCode.Should().Be(HttpStatusCode.OK);

        // The slot is free again afterwards.
        (await host.GetAsync($"/api/files/{id}/content?width=480&format=png")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_cache_is_checked_again_after_the_slot_is_acquired()
    {
        var cache = new MissThenHitCache();
        var fake = new CountingTransformer(TimeSpan.Zero);
        var host = HostWith(fake, maxConcurrent: 1, timeoutSeconds: 5,
            s => s.AddSingleton<IImageVariantCache>(cache));
        var id = await UploadPng(await factory.CreateAuthenticatedClientAsync(), 600, 600);

        var resp = await host.GetAsync($"/api/files/{id}/content?width=128&format=png");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resp.Content.ReadAsByteArrayAsync()).Should().Equal(MissThenHitCache.Payload);
        fake.Calls.Should().Be(0);
    }

    [Fact]
    public async Task A_probe_reporting_a_non_positive_size_returns_the_original_bytes()
    {
        var fake = new CountingTransformer(TimeSpan.Zero);
        var host = HostWith(fake, maxConcurrent: 1, timeoutSeconds: 5,
            s => s.AddSingleton<IImageDimensionReader>(new FixedDimensions((0, 10))));
        var original = Image.Black(50, 50).Cast(Enums.BandFormat.Uchar).WriteToBuffer(".png");
        var id = await Upload(await factory.CreateAuthenticatedClientAsync(), original, "image/png", "z.png");

        var resp = await host.GetAsync($"/api/files/{id}/content?width=128&format=png");

        (await resp.Content.ReadAsByteArrayAsync()).Should().Equal(original);
        fake.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Retry_after_is_at_least_one_second_when_the_queue_does_not_wait()
    {
        var blocking = new BlockingTransformer();
        var host = HostWith(blocking, maxConcurrent: 1, timeoutSeconds: 0);
        var id = await UploadPng(await factory.CreateAuthenticatedClientAsync(), 600, 600);

        var first = host.GetAsync($"/api/files/{id}/content?width=128&format=png");
        blocking.Entered.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
        var second = await host.GetAsync($"/api/files/{id}/content?width=256&format=png");

        second.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        second.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(1));
        blocking.Release.Set();
        await first;
    }

    private sealed class MissThenHitCache : IImageVariantCache
    {
        public static readonly byte[] Payload = [9, 8, 7];
        private int _gets;
        public string DeriveKey(Guid fileId, string fileVersion, ImageTransformRequest req) => "ab";
        public Task<byte[]?> TryGetAsync(string key, CancellationToken ct) =>
            Task.FromResult(Interlocked.Increment(ref _gets) == 1 ? null : Payload);
        public Task SetAsync(string key, byte[] bytes, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FixedDimensions((int, int)? size) : IImageDimensionReader
    {
        public (int Width, int Height)? TryRead(Stream seekable, string contentType) => size;
    }

    private sealed class CountingTransformer(TimeSpan work) : IImageTransformer
    {
        private int _calls, _current, _max;
        public int Calls => _calls;
        public int MaxConcurrent => _max;

        public ImageTransformResult? Transform(ReadOnlySpan<byte> source, ImageTransformRequest req, long maxSourcePixels)
        {
            Interlocked.Increment(ref _calls);
            var now = Interlocked.Increment(ref _current);
            int seen;
            while (now > (seen = _max) && Interlocked.CompareExchange(ref _max, now, seen) != seen) { }
            Thread.Sleep(work);
            Interlocked.Decrement(ref _current);
            return new ImageTransformResult([1, 2, 3], "image/png", 1, 1);
        }
    }

    private sealed class BlockingTransformer : IImageTransformer
    {
        public SemaphoreSlim Entered { get; } = new(0);
        public ManualResetEventSlim Release { get; } = new(false);

        public ImageTransformResult? Transform(ReadOnlySpan<byte> source, ImageTransformRequest req, long maxSourcePixels)
        {
            Entered.Release();
            Release.Wait(TimeSpan.FromSeconds(30));
            return new ImageTransformResult([1, 2, 3], "image/png", 1, 1);
        }
    }
}
