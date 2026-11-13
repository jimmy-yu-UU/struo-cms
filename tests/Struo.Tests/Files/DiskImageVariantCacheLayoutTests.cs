using AwesomeAssertions;
using Struo.Application.Files;
using Struo.Infrastructure.Files;
using Struo.Tests.Support;
using Xunit;
using File = System.IO.File;

namespace Struo.Tests.Files;

/// <summary>The per-file directory layout of the disk cache: removal by file, version replacement,
/// temp files, the removal of earlier-layout files, and the shape of every path it builds.</summary>
public class DiskImageVariantCacheLayoutTests : IDisposable
{
    private static readonly ImageTransformRequest Small = new(100, null, "webp", "inside", 80);
    private static readonly ImageTransformRequest Large = new(400, null, "webp", "inside", 80);
    private static readonly string[] HostileKeys =
    [
        "",
        "..",
        "../x",
        "..\\..\\x",
        "/etc/passwd",
        "C:\\Windows\\x",
        "a/b",
        "a/b/c/d",
        "00000000-0000-0000-0000-000000000000/../../x",
        "00000000-0000-0000-0000-000000000000/00000000000000000000000000000000/..",
        "00000000000000000000000000000000/00000000000000000000000000000000/" + "0000000000000000000000000000000000000000000000000000000000000000",
        "{00000000-0000-0000-0000-000000000000}/00000000000000000000000000000000/" + "0000000000000000000000000000000000000000000000000000000000000000",
        "00000000-0000-0000-0000-000000000000/0000000000000000000000000000000G/" + "0000000000000000000000000000000000000000000000000000000000000000",
    ];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "struo-variant-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private DiskImageVariantCache NewCache(long maxBytes = 0) => new(_root, maxBytes);

    private static async Task Put(DiskImageVariantCache cache, Guid file, string version, ImageTransformRequest req, int size = 10)
    {
        await cache.SetAsync(cache.DeriveKey(file, version, req), new byte[size], default);
    }

    private static IEnumerable<string> FilesUnder(string dir) =>
        Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories) : [];

    private string DirOf(Guid file) => Path.Combine(_root, file.ToString("D"));

    [Fact]
    public async Task Every_variant_of_a_file_lives_under_one_directory_named_for_its_id()
    {
        var cache = NewCache();
        var file = Guid.NewGuid();
        await Put(cache, file, "1", Small);
        await Put(cache, file, "1", Large);

        FilesUnder(_root).Should().HaveCount(2).And.OnlyContain(p => p.StartsWith(DirOf(file) + Path.DirectorySeparatorChar));
    }

    [Fact]
    public async Task Removing_a_file_removes_all_its_variants_and_none_of_another_files()
    {
        var cache = NewCache();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await Put(cache, a, "1", Small);
        await Put(cache, a, "1", Large);
        await Put(cache, b, "1", Small);
        var keptKey = cache.DeriveKey(b, "1", Small);

        await cache.RemoveAllForFileAsync(a, default);

        FilesUnder(DirOf(a)).Should().BeEmpty();
        Directory.Exists(DirOf(a)).Should().BeFalse();
        (await cache.TryGetAsync(keptKey, default)).Should().NotBeNull();
    }

    [Fact]
    public async Task Removing_a_file_that_has_no_variants_is_a_no_op()
    {
        var cache = NewCache();
        var act = () => cache.RemoveAllForFileAsync(Guid.NewGuid(), default);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Writing_a_new_version_removes_the_variants_of_other_versions_of_that_file_only()
    {
        var cache = NewCache();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await Put(cache, a, "1", Small);
        await Put(cache, a, "1", Large);
        await Put(cache, b, "1", Small);
        var oldKey = cache.DeriveKey(a, "1", Small);

        await Put(cache, a, "2", Small);

        (await cache.TryGetAsync(oldKey, default)).Should().BeNull();
        (await cache.TryGetAsync(cache.DeriveKey(a, "1", Large), default)).Should().BeNull();
        (await cache.TryGetAsync(cache.DeriveKey(a, "2", Small), default)).Should().NotBeNull();
        (await cache.TryGetAsync(cache.DeriveKey(b, "1", Small), default)).Should().NotBeNull();
    }

    [Fact]
    public async Task Writing_another_size_of_the_same_version_keeps_the_existing_variants()
    {
        var cache = NewCache();
        var a = Guid.NewGuid();
        await Put(cache, a, "1", Small);
        await Put(cache, a, "1", Large);

        (await cache.TryGetAsync(cache.DeriveKey(a, "1", Small), default)).Should().NotBeNull();
    }

    private string PlantTemp(Guid file, string version, DateTime lastWriteUtc)
    {
        var cache = NewCache();
        var key = cache.DeriveKey(file, version, Small);
        var parts = key.Split('/');
        var dir = Path.Combine(_root, parts[0], parts[1]);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $".{parts[2]}.{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(path, new byte[500]);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }

    [Fact]
    public void Maintenance_removes_a_stale_temp_file_and_leaves_a_fresh_one()
    {
        var file = Guid.NewGuid();
        var stale = PlantTemp(file, "1", DateTime.UtcNow - VariantCacheLayout.StaleTempAge - TimeSpan.FromMinutes(5));
        var fresh = PlantTemp(file, "1", DateTime.UtcNow - TimeSpan.FromMinutes(5));

        NewCache().RunMaintenance();

        File.Exists(stale).Should().BeFalse();
        File.Exists(fresh).Should().BeTrue();
    }

    [Fact]
    public async Task An_eviction_sweep_removes_a_stale_temp_file_and_never_counts_a_temp_file_as_a_variant()
    {
        var cache = NewCache(maxBytes: 250);
        var file = Guid.NewGuid();
        var elsewhere = Guid.NewGuid();   // a file the writes below never touch
        var stale = PlantTemp(elsewhere, "1", DateTime.UtcNow - VariantCacheLayout.StaleTempAge - TimeSpan.FromMinutes(5));
        var fresh = PlantTemp(elsewhere, "1", DateTime.UtcNow);

        // 200 bytes of variants fit under the cap; the 1000 temp bytes on disk must not trigger a sweep.
        await Put(cache, file, "1", Small, 100);
        await Put(cache, file, "1", Large, 100);
        (await cache.TryGetAsync(cache.DeriveKey(file, "1", Small), default)).Should().NotBeNull();
        File.Exists(stale).Should().BeTrue("no sweep has run yet");

        await Put(cache, file, "1", new ImageTransformRequest(800, null, "webp", "inside", 80), 100);   // 300 > 250: sweep

        File.Exists(stale).Should().BeFalse();
        File.Exists(fresh).Should().BeTrue();
    }

    [Fact]
    public async Task Removing_a_file_frees_its_bytes_from_the_running_total()
    {
        var cache = NewCache(maxBytes: 250);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await Put(cache, a, "1", Small, 100);
        await Put(cache, a, "1", Large, 100);

        cache.TrackedBytes.Should().Be(200);
        await cache.RemoveAllForFileAsync(a, default);
        cache.TrackedBytes.Should().Be(0, "the purge frees its bytes before any further write");
        await Put(cache, b, "1", Small, 100);
        await Put(cache, b, "1", Large, 100);
        cache.TrackedBytes.Should().Be(200);

        (await cache.TryGetAsync(cache.DeriveKey(b, "1", Small), default)).Should().NotBeNull();
        (await cache.TryGetAsync(cache.DeriveKey(b, "1", Large), default)).Should().NotBeNull();
    }

    [Fact]
    public async Task Replacing_a_version_frees_its_bytes_from_the_running_total()
    {
        var cache = NewCache(maxBytes: 1000);   // far above the bytes written, so no sweep re-counts the tree
        var a = Guid.NewGuid();
        await Put(cache, a, "1", Small, 100);
        await Put(cache, a, "1", Large, 100);

        cache.TrackedBytes.Should().Be(200);
        await Put(cache, a, "2", Small, 100);
        cache.TrackedBytes.Should().Be(100, "the replaced version's 200 bytes are gone, the new 100 counted");
        await Put(cache, a, "2", Large, 100);
        cache.TrackedBytes.Should().Be(200);

        (await cache.TryGetAsync(cache.DeriveKey(a, "2", Small), default)).Should().NotBeNull();
        (await cache.TryGetAsync(cache.DeriveKey(a, "2", Large), default)).Should().NotBeNull();
    }

    private static string LegacyKey(char first, char second, char fill) =>
        new string([first, second]) + new string(fill, 62);

    [Fact]
    public void Startup_cleanup_removes_exactly_the_earlier_layout_files_and_nothing_else()
    {
        var legacyKey = LegacyKey('a', 'b', '1');
        var legacyDir = Path.Combine(_root, "ab");
        Directory.CreateDirectory(legacyDir);
        var legacyVariant = Path.Combine(legacyDir, legacyKey);
        var legacyTemp = Path.Combine(legacyDir, $".{legacyKey}.{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(legacyVariant, [1]);
        File.WriteAllBytes(legacyTemp, [1]);

        var emptyShard = Path.Combine(_root, "cd");
        Directory.CreateDirectory(emptyShard);
        File.WriteAllBytes(Path.Combine(emptyShard, LegacyKey('c', 'd', '2')), [1]);

        // Look-alikes that are not the earlier layout.
        var keptShard = Path.Combine(_root, "ef");
        Directory.CreateDirectory(keptShard);
        var wrongShard = Path.Combine(keptShard, LegacyKey('a', 'b', '3'));   // key does not start with "ef"
        var notes = Path.Combine(keptShard, "notes.txt");
        var shortHex = Path.Combine(keptShard, "ef" + new string('4', 61));
        var upper = Path.Combine(keptShard, "EF" + new string('A', 62));
        var matching = Path.Combine(keptShard, LegacyKey('e', 'f', '5'));
        foreach (var path in new[] { wrongShard, notes, shortHex, upper, matching }) File.WriteAllBytes(path, [1]);
        var nested = Path.Combine(keptShard, "nested");
        Directory.CreateDirectory(nested);
        var nestedFile = Path.Combine(nested, LegacyKey('e', 'f', '6'));
        File.WriteAllBytes(nestedFile, [1]);

        var unrelatedFile = Path.Combine(_root, "readme.txt");
        File.WriteAllBytes(unrelatedFile, [1]);
        var unrelatedDir = Path.Combine(_root, "keep-me");
        Directory.CreateDirectory(unrelatedDir);
        var unrelatedInDir = Path.Combine(unrelatedDir, LegacyKey('k', 'e', '7'));
        File.WriteAllBytes(unrelatedInDir, [1]);
        var topLevelHex = Path.Combine(_root, LegacyKey('a', 'a', '8'));
        File.WriteAllBytes(topLevelHex, [1]);

        NewCache().RunMaintenance();

        File.Exists(legacyVariant).Should().BeFalse();
        File.Exists(legacyTemp).Should().BeFalse();
        Directory.Exists(legacyDir).Should().BeFalse("the shard directory was left empty");
        Directory.Exists(emptyShard).Should().BeFalse();
        Directory.Exists(keptShard).Should().BeTrue();
        File.Exists(matching).Should().BeFalse("it has the earlier shape");
        new[] { wrongShard, notes, shortHex, upper, nestedFile, unrelatedFile, unrelatedInDir, topLevelHex }
            .Should().OnlyContain(p => File.Exists(p));
    }

    [Fact]
    public async Task Startup_cleanup_leaves_the_current_layout_alone()
    {
        var cache = NewCache();
        var file = Guid.NewGuid();
        await Put(cache, file, "1", Small);

        cache.RunMaintenance();

        (await cache.TryGetAsync(cache.DeriveKey(file, "1", Small), default)).Should().NotBeNull();
    }

    [Fact]
    public void Maintenance_on_a_missing_root_does_nothing()
    {
        var act = () => NewCache().RunMaintenance();
        act.Should().NotThrow();
        Directory.Exists(_root).Should().BeFalse();
    }

    [Fact]
    public async Task Variants_of_the_earlier_layout_and_other_files_do_not_count_towards_the_cap()
    {
        Directory.CreateDirectory(Path.Combine(_root, "ab"));
        File.WriteAllBytes(Path.Combine(_root, "ab", LegacyKey('a', 'b', '1')), new byte[1000]);
        File.WriteAllBytes(Path.Combine(_root, "unrelated.bin"), new byte[1000]);
        var cache = NewCache(maxBytes: 250);
        var file = Guid.NewGuid();

        await Put(cache, file, "1", Small, 100);
        await Put(cache, file, "1", Large, 100);

        (await cache.TryGetAsync(cache.DeriveKey(file, "1", Small), default)).Should().NotBeNull();
        File.Exists(Path.Combine(_root, "unrelated.bin")).Should().BeTrue();
    }

    [Fact]
    public void A_derived_key_names_its_file_and_is_made_of_validated_components()
    {
        var file = Guid.NewGuid();
        var key = NewCache().DeriveKey(file, "7", Small);

        var parts = key.Split('/');
        parts.Should().HaveCount(3);
        parts[0].Should().Be(file.ToString("D"));
        VariantCacheLayout.TryParseKey(key, out _).Should().BeTrue();
    }

    [Fact]
    public async Task A_key_that_is_not_made_of_validated_components_is_refused_and_touches_nothing()
    {
        var cache = NewCache();
        var outside = Path.Combine(Path.GetTempPath(), "struo-variant-outside-" + Guid.NewGuid().ToString("N"));
        foreach (var key in HostileKeys.Append("../" + Path.GetFileName(outside)))
        {
            var get = () => cache.TryGetAsync(key, default);
            var set = () => cache.SetAsync(key, [1], default);
            await get.Should().ThrowAsync<ArgumentException>(because: key);
            await set.Should().ThrowAsync<ArgumentException>(because: key);
        }

        Directory.Exists(outside).Should().BeFalse();
        FilesUnder(_root).Should().BeEmpty();
    }

    [Fact]
    public void Every_path_built_from_a_parsed_key_stays_inside_the_root()
    {
        var root = Path.GetFullPath(_root);
        var key = NewCache().DeriveKey(Guid.NewGuid(), "x", Small);
        VariantCacheLayout.TryParseKey(key, out var parsed).Should().BeTrue();

        var path = Path.GetFullPath(VariantCacheLayout.VariantPath(root, parsed));

        path.Should().StartWith(root + Path.DirectorySeparatorChar);
    }

    [Theory]
    [InlineData(-1L, -100L, -1L)]
    [InlineData(-1L, 100L, -1L)]
    [InlineData(0L, -100L, 0L)]
    [InlineData(50L, -100L, 0L)]
    [InlineData(50L, 25L, 75L)]
    public void An_unseeded_total_stays_unseeded_and_a_seeded_total_never_goes_negative(long current, long delta, long expected)
    {
        DiskImageVariantCache.ApplyDelta(current, delta).Should().Be(expected);
    }

    private static string TempIn(string dir, string variantHex, DateTime lastWriteUtc)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $".{variantHex}.{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(path, [1]);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }

    [Fact]
    public async Task Writing_a_variant_removes_stale_temp_files_of_that_file_even_without_a_cap()
    {
        var cache = NewCache(maxBytes: 0);
        var file = Guid.NewGuid();
        await Put(cache, file, "1", Small);
        var key = cache.DeriveKey(file, "1", Small).Split('/');
        var versionDir = Path.Combine(_root, key[0], key[1]);
        var stale = TempIn(versionDir, key[2], DateTime.UtcNow - VariantCacheLayout.StaleTempAge - TimeSpan.FromMinutes(5));
        var fresh = TempIn(versionDir, key[2], DateTime.UtcNow);
        var otherFile = Guid.NewGuid();
        await Put(cache, otherFile, "1", Small);
        var otherKey = cache.DeriveKey(otherFile, "1", Small).Split('/');
        var otherStale = TempIn(Path.Combine(_root, otherKey[0], otherKey[1]), otherKey[2],
            DateTime.UtcNow - VariantCacheLayout.StaleTempAge - TimeSpan.FromMinutes(5));

        await Put(cache, file, "1", Large);

        File.Exists(stale).Should().BeFalse();
        File.Exists(fresh).Should().BeTrue();
        File.Exists(otherStale).Should().BeTrue("only the written file's directory is tidied");
    }

    private string VersionDir(DiskImageVariantCache cache, Guid file, string version, ImageTransformRequest req)
    {
        var parts = cache.DeriveKey(file, version, req).Split('/');
        return Path.Combine(_root, parts[0], parts[1]);
    }

    [Fact]
    public async Task An_eviction_sweep_removes_empty_version_and_file_directories_of_the_layout_only()
    {
        var cache = NewCache(maxBytes: 250);
        var emptied = Guid.NewGuid();
        await Put(cache, emptied, "1", Small, 100);
        var emptiedVersion = VersionDir(cache, emptied, "1", Small);
        File.Delete(Directory.GetFiles(emptiedVersion).Single());
        Age(emptiedVersion, DirOf(emptied));
        var emptyFileDir = Path.Combine(_root, Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(emptyFileDir);
        Age(emptyFileDir);
        var unrelatedEmpty = Directory.CreateDirectory(Path.Combine(_root, "not-a-cache-directory")).FullName;
        var emptyUnderUnrelated = Directory.CreateDirectory(Path.Combine(unrelatedEmpty, "inner")).FullName;
        var keep = Guid.NewGuid();
        await Put(cache, keep, "1", Small, 100);
        await Put(cache, keep, "1", Large, 100);

        await Put(cache, keep, "1", new ImageTransformRequest(800, null, "webp", "inside", 80), 100);   // over the cap: sweep

        Directory.Exists(emptiedVersion).Should().BeFalse();
        Directory.Exists(Path.Combine(_root, emptied.ToString("D"))).Should().BeFalse();
        Directory.Exists(emptyFileDir).Should().BeFalse();
        Directory.Exists(unrelatedEmpty).Should().BeTrue();
        Directory.Exists(emptyUnderUnrelated).Should().BeTrue();
        Directory.Exists(_root).Should().BeTrue();
        Directory.Exists(DirOf(keep)).Should().BeTrue();
    }

    [Fact]
    public void Startup_maintenance_removes_empty_layout_directories_and_never_the_root()
    {
        var file = Guid.NewGuid();
        var emptyVersion = Path.Combine(_root, file.ToString("D"), new string('a', 32));
        Directory.CreateDirectory(emptyVersion);
        Age(emptyVersion, DirOf(file));
        var nonEmptyFile = Guid.NewGuid();
        var keptVersion = Path.Combine(_root, nonEmptyFile.ToString("D"), new string('b', 32));
        Directory.CreateDirectory(keptVersion);
        File.WriteAllBytes(Path.Combine(keptVersion, new string('c', 64)), [1]);
        var misnamedVersion = Path.Combine(_root, nonEmptyFile.ToString("D"), "short");
        Directory.CreateDirectory(misnamedVersion);
        var unrelated = Directory.CreateDirectory(Path.Combine(_root, "other")).FullName;

        NewCache().RunMaintenance();

        Directory.Exists(emptyVersion).Should().BeFalse();
        Directory.Exists(Path.Combine(_root, file.ToString("D"))).Should().BeFalse();
        Directory.Exists(keptVersion).Should().BeTrue();
        Directory.Exists(misnamedVersion).Should().BeTrue();
        Directory.Exists(unrelated).Should().BeTrue();
        Directory.Exists(_root).Should().BeTrue();
    }

    [WindowsOnlyFact("relies on Windows refusing to delete a directory that holds an open file")]
    public async Task A_replaced_version_that_cannot_be_removed_leaves_the_write_in_place_and_the_total_unseeded()
    {
        var cache = NewCache(maxBytes: 1000);
        var file = Guid.NewGuid();
        await Put(cache, file, "1", Small, 100);
        var held = Directory.GetFiles(VersionDir(cache, file, "1", Small)).Single();
        using var lockHandle = new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None);

        var act = () => Put(cache, file, "2", Small, 100);

        await act.Should().NotThrowAsync();
        (await cache.TryGetAsync(cache.DeriveKey(file, "2", Small), default)).Should().NotBeNull();
        cache.TrackedBytes.Should().Be(-1, "the total is counted again by the next write");
    }

    [WindowsOnlyFact("relies on Windows refusing to delete a directory that holds an open file")]
    public async Task A_purge_that_cannot_remove_the_directory_throws_and_leaves_the_total_unseeded()
    {
        var cache = NewCache(maxBytes: 1000);
        var file = Guid.NewGuid();
        await Put(cache, file, "1", Small, 100);
        var held = Directory.GetFiles(VersionDir(cache, file, "1", Small)).Single();
        using var lockHandle = new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None);

        var act = () => cache.RemoveAllForFileAsync(file, default);

        await act.Should().ThrowAsync<IOException>();
        cache.TrackedBytes.Should().Be(-1);
    }

    [Fact]
    public void Only_a_link_counts_as_a_reparse_point()
    {
        Directory.CreateDirectory(_root);
        var file = Path.Combine(_root, "plain.bin");
        File.WriteAllBytes(file, [1]);

        VariantCacheLayout.IsReparsePoint(new DirectoryInfo(_root)).Should().BeFalse();
        VariantCacheLayout.IsReparsePoint(new FileInfo(file)).Should().BeFalse();
    }

    [Fact]
    public void A_vanished_or_non_empty_directory_is_a_benign_race_and_other_failures_are_not()
    {
        VariantCacheLayout.IsBenignRace(new DirectoryNotFoundException()).Should().BeTrue();
        VariantCacheLayout.IsBenignRace(new FileNotFoundException()).Should().BeTrue();
        VariantCacheLayout.IsBenignRace(new IOException("not empty", ThisPlatformsNotEmptyCode)).Should().BeTrue();
        VariantCacheLayout.IsBenignRace(new IOException("other platform", OtherPlatformsNotEmptyCode)).Should().BeFalse();
        VariantCacheLayout.IsBenignRace(new IOException("disk full", unchecked((int)0x80070070))).Should().BeFalse();
        VariantCacheLayout.IsBenignRace(new UnauthorizedAccessException()).Should().BeFalse();
    }

    // Directory.Delete without recursion removes the link only, never the target.
    private static void RemoveJunction(string link)
    {
        if (Directory.Exists(link)) Directory.Delete(link, recursive: false);
    }

    // A directory junction on Windows (no elevation needed), a symbolic link elsewhere: both are reparse points.
    private static void MakeJunction(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }

        var info = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var process = System.Diagnostics.Process.Start(info)!;
        process.WaitForExit();
        process.ExitCode.Should().Be(0, "a junction needs no elevation");
    }

    [Fact]
    public async Task Maintenance_a_sweep_and_a_version_tidy_never_go_through_a_junction()
    {
        var outside = Path.Combine(Path.GetTempPath(), "struo-variant-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            var cache = NewCache(maxBytes: 250);
            var file = Guid.NewGuid();
            var versionKey = new string('d', VariantCacheLayout.VersionLength);
            var linkedVersion = Path.Combine(_root, file.ToString("D"), versionKey);
            Directory.CreateDirectory(Path.GetDirectoryName(linkedVersion)!);
            MakeJunction(linkedVersion, outside);
            var outsideVariant = Path.Combine(outside, new string('e', VariantCacheLayout.VariantLength));
            var outsideTemp = Path.Combine(outside, $".{new string('e', VariantCacheLayout.VariantLength)}.{Guid.NewGuid():N}.tmp");
            File.WriteAllBytes(outsideVariant, new byte[400]);
            File.WriteAllBytes(outsideTemp, [1]);
            File.SetLastWriteTimeUtc(outsideTemp, DateTime.UtcNow - VariantCacheLayout.StaleTempAge - TimeSpan.FromMinutes(5));

            cache.RunMaintenance();
            await Put(cache, file, "1", Small, 100);          // tidy: the junction is an "other version"
            await Put(cache, file, "1", Large, 100);
            await Put(cache, file, "1", new ImageTransformRequest(800, null, "webp", "inside", 80), 100);   // sweep

            File.Exists(outsideVariant).Should().BeTrue("a variant behind a junction is neither counted nor evicted");
            File.Exists(outsideTemp).Should().BeTrue("a temp file behind a junction is not pruned");
            Directory.Exists(linkedVersion).Should().BeTrue("the junction itself stays");
        }
        finally
        {
            foreach (var link in Directory.Exists(_root) ? Directory.EnumerateDirectories(_root, "*", SearchOption.AllDirectories).Where(d => VariantCacheLayout.IsReparsePoint(new DirectoryInfo(d))).ToList() : [])
                RemoveJunction(link);
            if (Directory.Exists(outside)) Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void An_empty_directory_behind_a_junction_is_not_removed()
    {
        var outside = Path.Combine(Path.GetTempPath(), "struo-variant-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            var file = Guid.NewGuid();
            var linkedVersion = Path.Combine(_root, file.ToString("D"), new string('d', VariantCacheLayout.VersionLength));
            Directory.CreateDirectory(Path.GetDirectoryName(linkedVersion)!);
            MakeJunction(linkedVersion, outside);

            NewCache().RunMaintenance();

            Directory.Exists(linkedVersion).Should().BeTrue();
            Directory.Exists(outside).Should().BeTrue();
        }
        finally
        {
            foreach (var link in Directory.Exists(_root) ? Directory.EnumerateDirectories(_root, "*", SearchOption.AllDirectories).Where(d => VariantCacheLayout.IsReparsePoint(new DirectoryInfo(d))).ToList() : [])
                RemoveJunction(link);
            if (Directory.Exists(outside)) Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task A_purge_does_not_delete_through_a_junction_named_like_a_file_id()
    {
        var outside = Path.Combine(Path.GetTempPath(), "struo-variant-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            var file = Guid.NewGuid();
            Directory.CreateDirectory(_root);
            MakeJunction(DirOf(file), outside);
            var keep = Path.Combine(outside, "keep.bin");
            File.WriteAllBytes(keep, [1]);

            await NewCache().RemoveAllForFileAsync(file, default);

            File.Exists(keep).Should().BeTrue();
        }
        finally
        {
            foreach (var link in Directory.Exists(_root) ? Directory.EnumerateDirectories(_root, "*", SearchOption.AllDirectories).Where(d => VariantCacheLayout.IsReparsePoint(new DirectoryInfo(d))).ToList() : [])
                RemoveJunction(link);
            if (Directory.Exists(outside)) Directory.Delete(outside, recursive: true);
        }
    }

    // Backdates directories past the pruning threshold (the last write of a directory is when an entry
    // was last added or removed).
    private static void Age(params string[] directories)
    {
        var old = DateTime.UtcNow - VariantCacheLayout.EmptyDirectoryMinAge - TimeSpan.FromMinutes(5);
        foreach (var directory in directories) Directory.SetLastWriteTimeUtc(directory, old);
    }

    [Fact]
    public void Startup_maintenance_leaves_a_young_empty_directory_and_removes_an_old_one()
    {
        var young = Path.Combine(_root, Guid.NewGuid().ToString("D"), new string('a', 32));
        var old = Path.Combine(_root, Guid.NewGuid().ToString("D"), new string('b', 32));
        Directory.CreateDirectory(young);
        Directory.CreateDirectory(old);
        Age(old, Path.GetDirectoryName(old)!);

        NewCache().RunMaintenance();

        Directory.Exists(young).Should().BeTrue("a young directory may be about to receive a variant");
        Directory.Exists(Path.GetDirectoryName(young)!).Should().BeTrue();
        Directory.Exists(old).Should().BeFalse();
        Directory.Exists(Path.GetDirectoryName(old)!).Should().BeFalse();
    }

    [Fact]
    public async Task An_eviction_sweep_leaves_a_young_empty_directory_and_removes_an_old_one()
    {
        var cache = NewCache(maxBytes: 250);
        var young = Path.Combine(_root, Guid.NewGuid().ToString("D"), new string('a', 32));
        var old = Path.Combine(_root, Guid.NewGuid().ToString("D"), new string('b', 32));
        Directory.CreateDirectory(young);
        Directory.CreateDirectory(old);
        Age(old, Path.GetDirectoryName(old)!);
        var file = Guid.NewGuid();
        await Put(cache, file, "1", Small, 100);
        await Put(cache, file, "1", Large, 100);

        await Put(cache, file, "1", new ImageTransformRequest(800, null, "webp", "inside", 80), 100);   // sweep

        Directory.Exists(young).Should().BeTrue();
        Directory.Exists(old).Should().BeFalse();
    }

    [Fact]
    public void A_file_directory_that_was_old_before_its_last_version_was_pruned_goes_with_it()
    {
        var versionDir = Path.Combine(_root, Guid.NewGuid().ToString("D"), new string('a', 32));
        Directory.CreateDirectory(versionDir);
        Age(versionDir, Path.GetDirectoryName(versionDir)!);

        NewCache().RunMaintenance();

        Directory.Exists(Path.GetDirectoryName(versionDir)!).Should().BeFalse();
    }

    private sealed class WarningCounter : Microsoft.Extensions.Logging.ILogger
    {
        private int _warnings;
        private int _errors;

        public int Warnings => _warnings;

        public int Errors => _errors;

        public Exception? LastWarningError { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Warning)
            {
                Interlocked.Increment(ref _warnings);
                LastWarningError = exception;
            }
            if (logLevel >= Microsoft.Extensions.Logging.LogLevel.Error) Interlocked.Increment(ref _errors);
        }
    }

    [Fact]
    public async Task A_directory_that_vanishes_between_creation_and_write_is_created_again()
    {
        var cache = NewCache(maxBytes: 1000);
        var file = Guid.NewGuid();
        var calls = 0;
        cache.DirectoryEnsured = dir =>
        {
            if (Interlocked.Increment(ref calls) == 1) Directory.Delete(dir, recursive: true);
        };

        await Put(cache, file, "1", Small, 100);

        calls.Should().Be(2, "the first attempt lost its directory, the second succeeded");
        (await cache.TryGetAsync(cache.DeriveKey(file, "1", Small), default)).Should().HaveCount(100);
        cache.TrackedBytes.Should().Be(100);
    }

    [Fact]
    public async Task A_directory_that_keeps_vanishing_ends_as_a_cache_miss_with_one_warning_and_no_bytes_counted()
    {
        var logger = new WarningCounter();
        var cache = new DiskImageVariantCache(_root, 1000, logger);
        var file = Guid.NewGuid();
        var calls = 0;
        cache.DirectoryEnsured = dir =>
        {
            Interlocked.Increment(ref calls);
            Directory.Delete(dir, recursive: true);
        };

        var act = () => Put(cache, file, "1", Small, 100);

        await act.Should().NotThrowAsync();
        calls.Should().Be(DiskImageVariantCache.StoreAttempts);
        (await cache.TryGetAsync(cache.DeriveKey(file, "1", Small), default)).Should().BeNull();
        cache.TrackedBytes.Should().Be(0, "nothing landed, so nothing is counted");
        logger.Warnings.Should().Be(1);
        logger.Errors.Should().Be(0);
        logger.LastWarningError.Should().NotBeNull("the warning names the last failure");
        VariantCacheLayout.IsMissingPath(logger.LastWarningError!).Should().BeTrue();
        FilesUnder(_root).Should().BeEmpty("no temp file is left behind");
    }

    [Fact]
    public async Task A_vanishing_directory_is_also_handled_without_a_cap()
    {
        var cache = NewCache(maxBytes: 0);
        var file = Guid.NewGuid();
        cache.DirectoryEnsured = dir => Directory.Delete(dir, recursive: true);

        var act = () => Put(cache, file, "1", Small, 100);

        await act.Should().NotThrowAsync();
    }

    // What this platform throws, observed here and pinned: the classification must hold on Windows and Linux.
    [Fact]
    public void The_exceptions_this_platform_throws_for_a_vanished_or_non_empty_directory_are_classified()
    {
        Directory.CreateDirectory(_root);
        var missing = Path.Combine(_root, "missing");
        var nonEmpty = Path.Combine(_root, "non-empty");
        Directory.CreateDirectory(nonEmpty);
        File.WriteAllBytes(Path.Combine(nonEmpty, "entry"), [1]);

        Action writeIntoMissing = () => File.WriteAllBytes(Path.Combine(missing, ".temp.tmp"), [1]);
        Action moveFromMissing = () => File.Move(Path.Combine(missing, ".temp.tmp"), Path.Combine(_root, "target"), true);
        Action deleteMissing = () => Directory.Delete(missing);
        Func<bool> enumerateMissing = () => new DirectoryInfo(missing).EnumerateFileSystemInfos().Any();
        Action deleteNonEmpty = () => Directory.Delete(nonEmpty);

        foreach (var missingPath in new Action[] { writeIntoMissing, moveFromMissing, deleteMissing, () => enumerateMissing() })
        {
            var error = Record(missingPath);
            VariantCacheLayout.IsMissingPath(error!).Should().BeTrue(error!.GetType().Name);
            VariantCacheLayout.IsBenignRace(error!).Should().BeTrue(error!.GetType().Name);
        }

        var notEmpty = Record(deleteNonEmpty)!;
        notEmpty.Should().BeOfType<IOException>();
        VariantCacheLayout.IsMissingPath(notEmpty).Should().BeFalse();
        VariantCacheLayout.IsBenignRace(notEmpty).Should().BeTrue($"HResult {notEmpty.HResult}");
    }

    private static IOException? Record(Action action)
    {
        try { action(); return null; }
        catch (IOException ex) { return ex; }
    }

    [Fact]
    public void A_missing_file_and_a_missing_directory_are_both_a_missing_path()
    {
        VariantCacheLayout.IsMissingPath(new FileNotFoundException()).Should().BeTrue();
        VariantCacheLayout.IsMissingPath(new DirectoryNotFoundException()).Should().BeTrue();
        VariantCacheLayout.IsMissingPath(new IOException("disk full")).Should().BeFalse();
        VariantCacheLayout.IsMissingPath(new UnauthorizedAccessException()).Should().BeFalse();
    }

    // Verified by running: Windows 0x80070091, Linux 39. macOS 66 is from the platform documentation.
    private static int ThisPlatformsNotEmptyCode =>
        OperatingSystem.IsWindows() ? unchecked((int)0x80070091) : OperatingSystem.IsMacOS() ? 66 : 39;

    private static int OtherPlatformsNotEmptyCode => OperatingSystem.IsWindows() ? 39 : unchecked((int)0x80070091);

    [Fact]
    public void A_missing_cache_root_is_not_a_benign_race_but_a_missing_entry_below_it_is()
    {
        Directory.CreateDirectory(_root);
        var vanished = new DirectoryNotFoundException();
        VariantCacheLayout.IsBenignRace(vanished, _root).Should().BeTrue("the root is there, an entry below it vanished");

        Directory.Delete(_root, recursive: true);

        VariantCacheLayout.IsBenignRace(vanished, _root).Should().BeFalse("the root itself is missing");
    }

    [Fact]
    public async Task A_retry_does_not_recreate_a_file_directory_that_a_purge_removed()
    {
        var logger = new WarningCounter();
        var cache = new DiskImageVariantCache(_root, 1000, logger);
        var file = Guid.NewGuid();
        var calls = 0;
        cache.DirectoryEnsured = dir =>
        {
            Interlocked.Increment(ref calls);
            Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true);   // the purge of the file
        };

        var act = () => Put(cache, file, "1", Small, 100);

        await act.Should().NotThrowAsync();
        calls.Should().Be(1, "the second attempt finds the file directory gone and ends as a miss");
        Directory.Exists(DirOf(file)).Should().BeFalse("nothing is recreated for a purged file");
        (await cache.TryGetAsync(cache.DeriveKey(file, "1", Small), default)).Should().BeNull();
        cache.TrackedBytes.Should().Be(0);
        logger.Warnings.Should().Be(0, "a purge racing a write is expected, not a warning");
    }

    [Fact]
    public async Task A_retried_replace_counts_the_bytes_exactly()
    {
        var cache = NewCache(maxBytes: 10_000);
        var file = Guid.NewGuid();
        await Put(cache, file, "1", Small, 100);
        cache.TrackedBytes.Should().Be(100);
        var attempts = 0;
        var nested = false;
        cache.DirectoryEnsured = _ =>
        {
            if (nested) return;   // the other writer's own calls are not attempts of this write
            if (++attempts != 1) return;
            // Another writer stores version 2 of this file: its tidy removes version 1, the directory this
            // write has just ensured, and takes the 100 bytes off the total.
            nested = true;
            Put(cache, file, "2", Small, 50).GetAwaiter().GetResult();
            nested = false;
        };

        await Put(cache, file, "1", Small, 150);

        attempts.Should().Be(2);
        var onDisk = FilesUnder(_root).Sum(f => new FileInfo(f).Length);
        onDisk.Should().Be(150, "the landed write's own tidy removed version 2");
        cache.TrackedBytes.Should().Be(onDisk, "the total matches the disk after a retried replace");
    }

    [Fact]
    public async Task A_purge_that_meets_a_concurrent_writer_retries_and_resets_the_total()
    {
        var cache = NewCache(maxBytes: 1000);
        var file = Guid.NewGuid();
        await Put(cache, file, "1", Small, 100);
        var calls = 0;
        cache.DeleteTree = dir =>
        {
            if (Interlocked.Increment(ref calls) < 3)
                throw new IOException("directory not empty", ThisPlatformsNotEmptyCode);
            Directory.Delete(dir, recursive: true);
        };

        await cache.RemoveAllForFileAsync(file, default);

        calls.Should().Be(3);
        Directory.Exists(DirOf(file)).Should().BeFalse();
        cache.TrackedBytes.Should().Be(-1, "bytes a writer added meanwhile are unknown, so the next write counts again");
    }

    [Fact]
    public async Task A_purge_that_cannot_empty_the_directory_warns_and_resets_the_total()
    {
        var logger = new WarningCounter();
        var cache = new DiskImageVariantCache(_root, 1000, logger);
        var file = Guid.NewGuid();
        await Put(cache, file, "1", Small, 100);
        var calls = 0;
        cache.DeleteTree = _ =>
        {
            Interlocked.Increment(ref calls);
            throw new IOException("directory not empty", ThisPlatformsNotEmptyCode);
        };

        var act = () => cache.RemoveAllForFileAsync(file, default);

        await act.Should().NotThrowAsync();
        calls.Should().Be(DiskImageVariantCache.PurgeAttempts);
        logger.Warnings.Should().Be(1, "entries that stay after a purge are not a benign race");
        cache.TrackedBytes.Should().Be(-1);
    }

    [Fact]
    public async Task A_purge_that_finds_the_directory_gone_is_done_and_changes_no_count()
    {
        var cache = NewCache(maxBytes: 1000);
        var file = Guid.NewGuid();
        await Put(cache, file, "1", Small, 100);
        cache.DeleteTree = dir =>
        {
            Directory.Delete(dir, recursive: true);
            throw new DirectoryNotFoundException();   // a second purge got there first
        };

        await cache.RemoveAllForFileAsync(file, default);

        cache.TrackedBytes.Should().Be(100, "the purge that removed it accounts for it");
    }

    [Fact]
    public void Earlier_layout_cleanup_does_not_follow_a_link_named_like_a_shard()
    {
        var outside = Path.Combine(Path.GetTempPath(), "struo-variant-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        var key = "ab" + new string('1', 62);
        var legacyLookalike = Path.Combine(outside, key);
        File.WriteAllBytes(legacyLookalike, [1]);
        try
        {
            Directory.CreateDirectory(_root);
            MakeJunction(Path.Combine(_root, "ab"), outside);

            NewCache().RunMaintenance();

            File.Exists(legacyLookalike).Should().BeTrue("a link is neither followed nor cleaned through");
        }
        finally
        {
            RemoveJunction(Path.Combine(_root, "ab"));
            if (Directory.Exists(outside)) Directory.Delete(outside, recursive: true);
        }
    }
}
