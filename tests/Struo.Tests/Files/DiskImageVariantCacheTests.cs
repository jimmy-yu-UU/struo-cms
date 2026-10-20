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

    private static string Root() =>
        Path.Combine(Path.GetTempPath(), "struo-variant-" + Guid.NewGuid().ToString("N"));

    private static long TotalBytes(string root) =>
        Directory.Exists(root)
            ? new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)
            : 0;

    private static string Key(DiskImageVariantCache c, int n) =>
        c.DeriveKey(Guid.NewGuid(), "v1", new ImageTransformRequest(n, null, "webp", "inside", 80));

    [Fact]
    public async Task Total_size_stays_under_the_cap_after_many_writes_and_the_newest_survives()
    {
        var root = Root();
        var c = new DiskImageVariantCache(root, maxBytes: 450);
        string last = "";
        for (var i = 0; i < 12; i++)
        {
            last = Key(c, i);
            await c.SetAsync(last, new byte[100], default);
            await Task.Delay(15);
        }

        TotalBytes(root).Should().BeLessThanOrEqualTo(450);
        (await c.TryGetAsync(last, default)).Should().NotBeNull();
    }

    [Fact]
    public async Task A_hit_marks_the_variant_recently_used_so_the_other_one_is_evicted_first()
    {
        var c = new DiskImageVariantCache(Root(), maxBytes: 250);
        var a = Key(c, 1);
        var b = Key(c, 2);
        var d = Key(c, 3);
        await c.SetAsync(a, new byte[100], default);
        await Task.Delay(30);
        await c.SetAsync(b, new byte[100], default);
        await Task.Delay(30);
        (await c.TryGetAsync(a, default)).Should().NotBeNull();   // touch a
        await Task.Delay(30);
        await c.SetAsync(d, new byte[100], default);              // 300 > 250 -> evict LRU

        (await c.TryGetAsync(b, default)).Should().BeNull();
        (await c.TryGetAsync(a, default)).Should().NotBeNull();
        (await c.TryGetAsync(d, default)).Should().NotBeNull();
    }

    [Fact]
    public async Task Rewriting_one_key_does_not_count_its_bytes_twice()
    {
        var c = new DiskImageVariantCache(Root(), maxBytes: 250);
        var key = Key(c, 1);
        for (var i = 0; i < 10; i++) await c.SetAsync(key, new byte[100], default);

        (await c.TryGetAsync(key, default)).Should().NotBeNull();
    }

    [Fact]
    public async Task Variants_already_on_disk_count_towards_the_cap()
    {
        var root = Root();
        var first = new DiskImageVariantCache(root, maxBytes: 250);
        for (var i = 0; i < 2; i++) await first.SetAsync(Key(first, i), new byte[100], default);
        await Task.Delay(30);

        var second = new DiskImageVariantCache(root, maxBytes: 250);
        await second.SetAsync(Key(second, 9), new byte[100], default);

        TotalBytes(root).Should().BeLessThanOrEqualTo(250);
    }

    [Fact]
    public async Task Eviction_goes_below_the_cap_so_the_next_write_does_not_sweep_again()
    {
        var root = Root();
        var c = new DiskImageVariantCache(root, maxBytes: 1000);
        for (var i = 0; i < 11; i++)
        {
            await c.SetAsync(Key(c, i), new byte[100], default);
            await Task.Delay(15);
        }

        TotalBytes(root).Should().BeLessThanOrEqualTo(900);
    }

    [Fact]
    public async Task Zero_cap_means_unbounded()
    {
        var root = Root();
        var c = new DiskImageVariantCache(root, maxBytes: 0);
        for (var i = 0; i < 5; i++) await c.SetAsync(Key(c, i), new byte[100], default);
        TotalBytes(root).Should().Be(500);
    }

    [Fact]
    public async Task An_evicted_variant_reads_as_a_miss_and_never_throws_under_concurrent_eviction()
    {
        var root = Root();
        var c = new DiskImageVariantCache(root, maxBytes: 300);
        var keys = Enumerable.Range(0, 8).Select(i => Key(c, i)).ToArray();

        var writers = Enumerable.Range(0, 4).Select(w => Task.Run(async () =>
        {
            for (var i = 0; i < 40; i++)
                await c.SetAsync(keys[(w + i) % keys.Length], new byte[100], default);
        }));
        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            for (var i = 0; i < 80; i++)
            {
                var hit = await c.TryGetAsync(keys[i % keys.Length], default);
                if (hit is not null) hit.Length.Should().Be(100);
            }
        }));

        var act = () => Task.WhenAll(writers.Concat(readers));
        await act.Should().NotThrowAsync();

        // Whether a reader saw a miss depends on scheduling. Eviction itself is deterministic once the
        // concurrency stops: ten more sequential writes of new keys push the running total past the cap,
        // so a sweep runs, and the sweep leaves at most three 100-byte variants, which cannot hold all
        // eight original keys.
        for (var i = 8; i < 18; i++) await c.SetAsync(Key(c, i), new byte[100], default);

        TotalBytes(root).Should().BeLessThanOrEqualTo(300);
        var results = new List<byte[]?>();
        foreach (var key in keys) results.Add(await c.TryGetAsync(key, default));
        results.Should().Contain(r => r == null);
    }

    [Fact]
    public async Task An_eviction_that_cannot_delete_a_file_is_logged_and_not_thrown()
    {
        if (!OperatingSystem.IsWindows()) return;   // relies on Windows refusing to delete an open file

        var root = Root();
        var logger = new CapturingLogger();
        var c = new DiskImageVariantCache(root, maxBytes: 150, logger);
        var first = Key(c, 1);
        await c.SetAsync(first, new byte[100], default);
        var firstPath = Directory.EnumerateFiles(root, first, SearchOption.AllDirectories).Single();
        await Task.Delay(30);

        using var locked = new FileStream(firstPath, FileMode.Open, FileAccess.Read, FileShare.None);
        var act = () => c.SetAsync(Key(c, 2), new byte[100], default);
        await act.Should().NotThrowAsync();
        logger.Warnings.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task A_failure_while_counting_the_existing_variants_is_logged_and_the_write_is_skipped()
    {
        if (!OperatingSystem.IsWindows()) return;   // uses a deny ACL on a subdirectory

        var root = Root();
        var blocked = Directory.CreateDirectory(Path.Combine(root, "zz"));
        var me = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var deny = new System.Security.AccessControl.FileSystemAccessRule(
            me, System.Security.AccessControl.FileSystemRights.ListDirectory,
            System.Security.AccessControl.AccessControlType.Deny);
        var acl = blocked.GetAccessControl();
        acl.AddAccessRule(deny);
        blocked.SetAccessControl(acl);
        try
        {
            var logger = new CapturingLogger();
            var c = new DiskImageVariantCache(root, maxBytes: 1000, logger);

            var act = () => c.SetAsync(Key(c, 1), new byte[100], default);

            await act.Should().NotThrowAsync();
            logger.Warnings.Should().BeGreaterThan(0);
        }
        finally
        {
            acl.RemoveAccessRule(deny);
            blocked.SetAccessControl(acl);
        }
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public int Warnings;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= Microsoft.Extensions.Logging.LogLevel.Warning) Interlocked.Increment(ref Warnings);
        }
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
