using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Struo.Application.Files;
using Struo.Infrastructure.Files;
using Struo.Tests.Support;
using Xunit;
using File = System.IO.File;

namespace Struo.Tests.Files;

/// <summary>
/// What the disk cache does when the file system refuses it: a cache is an optimization, so a failure costs
/// the caching and is logged, and neither a write nor the upkeep nor a purge's cleanup takes the caller down.
/// The tests that remove permissions with a Unix file mode run wherever the suite runs as a normal user.
/// </summary>
public sealed class DiskImageVariantCacheFailureTests : IDisposable
{
    private static readonly ImageTransformRequest Small = new(100, null, "webp", "inside", 80);
    private static readonly ImageTransformRequest Large = new(400, null, "webp", "inside", 80);
    private const UnixFileMode NoAccess = 0;
    private const UnixFileMode ReadOnly = UnixFileMode.UserRead | UnixFileMode.UserExecute;
    private const UnixFileMode Full = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "struo-variant-fail-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _locked = [];

    public void Dispose()
    {
        if (!OperatingSystem.IsWindows())
            foreach (var path in _locked.Where(p => Directory.Exists(p) || File.Exists(p)))
                File.SetUnixFileMode(path, Full);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private sealed class RecordingLogger : ILogger
    {
        private readonly List<(LogLevel Level, Exception? Error)> _entries = [];

        public IReadOnlyList<(LogLevel Level, Exception? Error)> Entries
        {
            get { lock (_entries) return _entries.ToList(); }
        }

        public int Warnings => Entries.Count(e => e.Level == LogLevel.Warning);

        public int Debugs => Entries.Count(e => e.Level == LogLevel.Debug);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_entries) _entries.Add((logLevel, exception));
        }
    }

    private DiskImageVariantCache NewCache(RecordingLogger logger, long maxBytes = 0) => new(_root, maxBytes, logger);

    private static async Task Put(DiskImageVariantCache cache, Guid file, string version, ImageTransformRequest req, int size = 100) =>
        await cache.SetAsync(cache.DeriveKey(file, version, req), new byte[size], default);

    private string FileDir(Guid file) => Path.Combine(_root, file.ToString("D"));

    private string VersionDir(DiskImageVariantCache cache, Guid file, string version)
    {
        var parts = cache.DeriveKey(file, version, Small).Split('/');
        return Path.Combine(_root, parts[0], parts[1]);
    }

    private string VariantPath(DiskImageVariantCache cache, Guid file, string version, ImageTransformRequest req) =>
        Path.Combine([_root, .. cache.DeriveKey(file, version, req).Split('/')]);

    private static IEnumerable<string> FilesUnder(string dir) =>
        Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories) : [];

    private void Lock(string path, UnixFileMode mode)
    {
        _locked.Add(path);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, mode);   // the tests that lock are skipped on Windows
    }

    private static void Age(string directory) =>
        Directory.SetLastWriteTimeUtc(directory,
            DateTime.UtcNow - VariantCacheLayout.EmptyDirectoryMinAge - TimeSpan.FromMinutes(5));

    private static string LegacyKey(char first, char second, char fill) =>
        new string([first, second]) + new string(fill, 62);

    // ---------- failures that need no permission tricks

    [Fact]
    public async Task A_write_that_is_cancelled_throws_and_leaves_no_file_and_no_count()
    {
        var cache = NewCache(new RecordingLogger(), maxBytes: 1000);
        var file = Guid.NewGuid();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => cache.SetAsync(cache.DeriveKey(file, "1", Small), new byte[100], cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        FilesUnder(_root).Should().BeEmpty("the temp file is cleaned up");
        cache.TrackedBytes.Should().Be(0);
    }

    [Fact]
    public async Task A_destination_that_is_a_directory_ends_as_a_miss_with_one_warning_and_no_count()
    {
        var logger = new RecordingLogger();
        var cache = NewCache(logger, maxBytes: 1000);
        var file = Guid.NewGuid();
        Directory.CreateDirectory(VariantPath(cache, file, "1", Small));   // where the variant file would go

        var act = () => Put(cache, file, "1", Small);

        await act.Should().NotThrowAsync();
        logger.Warnings.Should().Be(1, "the move gave up after its attempts");
        FilesUnder(_root).Should().BeEmpty("no temp file is left behind");
        cache.TrackedBytes.Should().Be(0);
    }

    [Fact]
    public async Task A_directory_removed_after_the_variant_landed_ends_the_tidy_quietly()
    {
        var logger = new RecordingLogger();
        var cache = NewCache(logger, maxBytes: 1000);
        var file = Guid.NewGuid();
        cache.VariantLanded = _ => Directory.Delete(FileDir(file), recursive: true);   // a purge of the file

        var act = () => Put(cache, file, "1", Small);

        await act.Should().NotThrowAsync();
        logger.Warnings.Should().Be(0, "a vanished directory is a race, not a fault");
        logger.Debugs.Should().Be(1);
    }

    [Fact]
    public void An_upkeep_failure_is_a_warning_unless_an_entry_below_an_existing_root_vanished()
    {
        Directory.CreateDirectory(_root);
        var logger = new RecordingLogger();
        var cache = NewCache(logger);

        cache.LogMaintenanceFailure(new DirectoryNotFoundException(), Path.Combine(_root, "entry"));
        logger.Debugs.Should().Be(1);
        logger.Warnings.Should().Be(0);

        cache.LogMaintenanceFailure(new UnauthorizedAccessException(), Path.Combine(_root, "entry"));
        logger.Warnings.Should().Be(1, "a permission failure is a real failure");

        Directory.Delete(_root, recursive: true);
        cache.LogMaintenanceFailure(new DirectoryNotFoundException(), _root);
        logger.Warnings.Should().Be(2, "a missing cache root is not a race");
    }

    [Fact]
    public void An_earlier_layout_cleanup_failure_is_classified_the_same_way()
    {
        Directory.CreateDirectory(_root);
        var logger = new RecordingLogger();

        LegacyVariantLayout.Log(new DirectoryNotFoundException(), logger, _root, Path.Combine(_root, "ab"));
        LegacyVariantLayout.Log(new UnauthorizedAccessException(), logger, _root, Path.Combine(_root, "ab"));
        Directory.Delete(_root, recursive: true);
        LegacyVariantLayout.Log(new DirectoryNotFoundException(), logger, _root, _root);

        logger.Debugs.Should().Be(1, "only the vanished entry under an existing root is a race");
        logger.Warnings.Should().Be(2);
    }

    // ---------- failures that need a directory to refuse access (a normal user, Unix)

    [UnprivilegedUnixFact("removes read permission from a variant file")]
    public async Task A_variant_file_that_cannot_be_read_is_a_miss()
    {
        var cache = NewCache(new RecordingLogger());
        var file = Guid.NewGuid();
        await Put(cache, file, "1", Small);
        Lock(VariantPath(cache, file, "1", Small), NoAccess);

        (await cache.TryGetAsync(cache.DeriveKey(file, "1", Small), default)).Should().BeNull();
    }

    [UnprivilegedUnixFact("removes write permission from a variant directory")]
    public async Task A_write_into_a_directory_that_refuses_it_throws_and_counts_nothing()
    {
        var cache = NewCache(new RecordingLogger(), maxBytes: 1000);
        var file = Guid.NewGuid();
        await Put(cache, file, "1", Small);
        Lock(VersionDir(cache, file, "1"), ReadOnly);

        var act = () => Put(cache, file, "1", Large);

        await act.Should().ThrowAsync<UnauthorizedAccessException>("an unwritable cache is reported to the caller");
        cache.TrackedBytes.Should().Be(100);
        FilesUnder(_root).Should().ContainSingle("no temp file stays behind");
    }

    [UnprivilegedUnixFact("removes write permission from a file directory")]
    public async Task A_version_that_cannot_be_removed_is_logged_and_the_write_still_lands()
    {
        var logger = new RecordingLogger();
        var cache = NewCache(logger, maxBytes: 1000);
        var file = Guid.NewGuid();
        await Put(cache, file, "1", Small);
        Directory.CreateDirectory(VersionDir(cache, file, "2"));
        Lock(FileDir(file), ReadOnly);   // version 1 cannot be removed, version 2 can still be written

        await Put(cache, file, "2", Small);

        (await cache.TryGetAsync(cache.DeriveKey(file, "2", Small), default)).Should().NotBeNull();
        logger.Warnings.Should().Be(1, "the tidy failure is logged once");
        cache.TrackedBytes.Should().Be(-1, "a half-done removal resets the total for a recount");
    }

    [UnprivilegedUnixFact("removes write permission from a variant directory")]
    public async Task An_eviction_that_cannot_delete_a_file_is_logged_and_the_sweep_carries_on()
    {
        var logger = new RecordingLogger();
        var cache = NewCache(logger, maxBytes: 250);
        var stuck = Guid.NewGuid();
        await Put(cache, stuck, "1", Small);
        var stuckPath = VariantPath(cache, stuck, "1", Small);
        File.SetLastWriteTimeUtc(stuckPath, DateTime.UtcNow - TimeSpan.FromHours(2));   // the least recently used
        Lock(VersionDir(cache, stuck, "1"), ReadOnly);
        var other = Guid.NewGuid();
        await Put(cache, other, "1", Small);
        File.SetLastWriteTimeUtc(VariantPath(cache, other, "1", Small), DateTime.UtcNow - TimeSpan.FromHours(1));

        var act = () => Put(cache, Guid.NewGuid(), "1", Small);   // 300 > 250: a sweep

        await act.Should().NotThrowAsync();
        File.Exists(stuckPath).Should().BeTrue("it could not be deleted");
        logger.Warnings.Should().BeGreaterThanOrEqualTo(1, "the failed eviction is logged");
        File.Exists(VariantPath(cache, other, "1", Small)).Should().BeFalse("the sweep went on to the next variant");
    }

    [UnprivilegedUnixFact("removes all permissions from a file directory")]
    public async Task A_cache_that_cannot_be_counted_skips_the_write_and_logs_once()
    {
        var logger = new RecordingLogger();
        var first = NewCache(logger, maxBytes: 1000);
        await Put(first, Guid.NewGuid(), "1", Small);
        var unreadable = Path.Combine(_root, Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(Path.Combine(unreadable, new string('a', 32)));
        Lock(unreadable, NoAccess);
        var logger2 = new RecordingLogger();
        var second = NewCache(logger2, maxBytes: 1000);   // not counted yet
        var file = Guid.NewGuid();

        await Put(second, file, "1", Small);

        logger2.Warnings.Should().Be(1, "the count failed");
        File.Exists(VariantPath(second, file, "1", Small)).Should().BeFalse("the variant is served uncached");
    }

    [UnprivilegedUnixFact("removes read permission from a file directory")]
    public async Task A_sweep_that_cannot_list_the_cache_is_logged_and_does_not_fail_the_write()
    {
        var logger = new RecordingLogger();
        var cache = NewCache(logger, maxBytes: 250);
        await Put(cache, Guid.NewGuid(), "1", Small);
        await Put(cache, Guid.NewGuid(), "1", Small);
        var unreadable = Path.Combine(_root, Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(unreadable);
        Lock(unreadable, NoAccess);

        var act = () => Put(cache, Guid.NewGuid(), "1", Small);   // 300 > 250: a sweep that cannot list the tree

        await act.Should().NotThrowAsync();
        logger.Warnings.Should().BeGreaterThanOrEqualTo(1);
    }

    [UnprivilegedUnixFact("removes write permission from the cache root")]
    public async Task A_purge_that_cannot_remove_the_directory_throws_and_resets_the_total()
    {
        var cache = NewCache(new RecordingLogger(), maxBytes: 1000);
        var file = Guid.NewGuid();
        await Put(cache, file, "1", Small);
        Lock(_root, ReadOnly);   // the file directory cannot be removed from the root

        var act = () => cache.RemoveAllForFileAsync(file, default);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        cache.TrackedBytes.Should().Be(-1);
    }

    [UnprivilegedUnixFact("removes all permissions from a file directory")]
    public async Task A_purge_of_a_directory_it_cannot_read_throws_and_resets_the_total()
    {
        var cache = NewCache(new RecordingLogger(), maxBytes: 1000);
        var file = Guid.NewGuid();
        await Put(cache, file, "1", Small);
        Lock(FileDir(file), NoAccess);

        var act = () => cache.RemoveAllForFileAsync(file, default);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        cache.TrackedBytes.Should().Be(-1);
    }

    [UnprivilegedUnixFact("removes write permission from a legacy shard directory")]
    public void Upkeep_carries_on_past_an_earlier_layout_file_it_cannot_delete()
    {
        var logger = new RecordingLogger();
        var stuckShard = Path.Combine(_root, "ab");
        var stuckFile = Path.Combine(stuckShard, LegacyKey('a', 'b', '1'));
        var okShard = Path.Combine(_root, "cd");
        var okFile = Path.Combine(okShard, LegacyKey('c', 'd', '2'));
        Directory.CreateDirectory(stuckShard);
        Directory.CreateDirectory(okShard);
        File.WriteAllBytes(stuckFile, [1]);
        File.WriteAllBytes(okFile, [1]);
        Lock(stuckShard, ReadOnly);

        NewCache(logger).RunMaintenance();

        File.Exists(stuckFile).Should().BeTrue("it could not be deleted");
        File.Exists(okFile).Should().BeFalse("the cleanup went on to the next shard");
        logger.Warnings.Should().BeGreaterThanOrEqualTo(1);
    }

    [UnprivilegedUnixFact("removes read permission from a legacy shard directory")]
    public void Upkeep_carries_on_past_an_earlier_layout_directory_it_cannot_list()
    {
        var logger = new RecordingLogger();
        var unreadable = Path.Combine(_root, "ab");
        var okShard = Path.Combine(_root, "cd");
        var okFile = Path.Combine(okShard, LegacyKey('c', 'd', '2'));
        Directory.CreateDirectory(unreadable);
        Directory.CreateDirectory(okShard);
        File.WriteAllBytes(okFile, [1]);
        Lock(unreadable, NoAccess);

        NewCache(logger).RunMaintenance();

        File.Exists(okFile).Should().BeFalse();
        logger.Warnings.Should().BeGreaterThanOrEqualTo(1);
    }

    [UnprivilegedUnixFact("removes write permission from the cache root")]
    public void Upkeep_logs_an_earlier_layout_directory_it_cannot_remove_and_carries_on()
    {
        var logger = new RecordingLogger();
        var shard = Path.Combine(_root, "ab");
        Directory.CreateDirectory(shard);   // empty, but the root will not let it go
        Lock(_root, ReadOnly);

        var act = () => NewCache(logger).RunMaintenance();

        act.Should().NotThrow();
        Directory.Exists(shard).Should().BeTrue();
        logger.Warnings.Should().Be(1);
    }

    [UnprivilegedUnixFact("removes read permission from the cache root")]
    public void Upkeep_over_an_unreadable_root_logs_warnings_and_does_not_throw()
    {
        var logger = new RecordingLogger();
        Directory.CreateDirectory(_root);
        Lock(_root, NoAccess);

        var act = () => NewCache(logger).RunMaintenance();

        act.Should().NotThrow();
        logger.Warnings.Should().BeGreaterThanOrEqualTo(1, "an unreadable root is a fault, not a race");
    }

    [UnprivilegedUnixFact("removes write permission from a file directory")]
    public void Upkeep_logs_an_empty_old_directory_it_cannot_remove_and_removes_the_next_one()
    {
        var logger = new RecordingLogger();
        var stuckFile = Guid.NewGuid();
        var stuckVersion = Path.Combine(FileDir(stuckFile), new string('a', 32));
        var okFile = Guid.NewGuid();
        var okVersion = Path.Combine(FileDir(okFile), new string('b', 32));
        Directory.CreateDirectory(stuckVersion);
        Directory.CreateDirectory(okVersion);
        Age(stuckVersion);
        Age(FileDir(stuckFile));
        Age(okVersion);
        Age(FileDir(okFile));
        Lock(FileDir(stuckFile), ReadOnly);

        NewCache(logger).RunMaintenance();

        Directory.Exists(stuckVersion).Should().BeTrue("its parent would not let it go");
        Directory.Exists(okVersion).Should().BeFalse();
        logger.Warnings.Should().BeGreaterThanOrEqualTo(1);
    }

    [UnprivilegedUnixFact("removes write permission from a version directory")]
    public void Upkeep_logs_a_stale_temp_file_it_cannot_delete_and_carries_on()
    {
        var logger = new RecordingLogger();
        var cache = NewCache(logger);
        var file = Guid.NewGuid();
        var versionDir = Path.Combine(FileDir(file), new string('a', 32));
        Directory.CreateDirectory(versionDir);
        var stuck = Path.Combine(versionDir, $".{new string('c', 64)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(stuck, [1]);
        File.SetLastWriteTimeUtc(stuck, DateTime.UtcNow - VariantCacheLayout.StaleTempAge - TimeSpan.FromMinutes(5));
        var other = Guid.NewGuid();
        var otherDir = Path.Combine(FileDir(other), new string('b', 32));
        Directory.CreateDirectory(otherDir);
        var okTemp = Path.Combine(otherDir, $".{new string('d', 64)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(okTemp, [1]);
        File.SetLastWriteTimeUtc(okTemp, DateTime.UtcNow - VariantCacheLayout.StaleTempAge - TimeSpan.FromMinutes(5));
        Lock(versionDir, ReadOnly);

        cache.RunMaintenance();

        File.Exists(stuck).Should().BeTrue();
        File.Exists(okTemp).Should().BeFalse("the pass went on to the next file");
        logger.Warnings.Should().BeGreaterThanOrEqualTo(1);
    }

    [UnprivilegedUnixFact("removes read permission from the cache root")]
    public void An_earlier_layout_cleanup_logs_an_unreadable_root_and_returns_nothing()
    {
        var logger = new RecordingLogger();
        Directory.CreateDirectory(_root);
        Lock(_root, NoAccess);

        LegacyVariantLayout.Remove(_root, logger).Should().Be(0);

        logger.Warnings.Should().Be(1);
    }
}
