using AwesomeAssertions;
using Struo.Application.Files;
using Struo.Infrastructure.Files;
using Xunit;

namespace Struo.Tests.Files;

public class DiskImageVariantCacheTests
{
    private static DiskImageVariantCache New() =>
        new(Path.Combine(Path.GetTempPath(), "struo-variant-" + Guid.NewGuid().ToString("N")));

    [Fact]
    public async Task Set_then_get_roundtrips()
    {
        var c = New();
        var key = c.DeriveKey(Guid.NewGuid(), "v1",
            new ImageTransformRequest(100, null, "webp", "inside", 82));
        (await c.TryGetAsync(key, default)).Should().BeNull();
        await c.SetAsync(key, [1, 2, 3], default);
        (await c.TryGetAsync(key, default)).Should().Equal([1, 2, 3]);
    }

    [Fact]
    public void Key_changes_with_params_and_version()
    {
        var c = New();
        var id = Guid.NewGuid();
        var a = c.DeriveKey(id, "v1", new ImageTransformRequest(100, null, "webp", "inside", 82));
        var b = c.DeriveKey(id, "v2", new ImageTransformRequest(100, null, "webp", "inside", 82));
        var d = c.DeriveKey(id, "v1", new ImageTransformRequest(200, null, "webp", "inside", 82));
        a.Should().NotBe(b);
        a.Should().NotBe(d);
    }

    [Fact]
    public async Task SetAsync_overwrites_existing_key_atomically()
    {
        var c = New();
        var key = c.DeriveKey(Guid.NewGuid(), "v1",
            new ImageTransformRequest(100, null, "webp", "inside", 82));
        await c.SetAsync(key, [1, 2, 3], default);
        await c.SetAsync(key, [4, 5, 6, 7], default);
        (await c.TryGetAsync(key, default)).Should().Equal([4, 5, 6, 7]);
    }
}
