using System.Globalization;

namespace Struo.Infrastructure.Files;

/// <summary>
/// The on-disk shape of the image variant cache: <c>&lt;root&gt;/&lt;file id&gt;/&lt;version&gt;/&lt;variant&gt;</c>.
/// Every path is built from components validated here (a canonical lowercase GUID, then lowercase hex),
/// so no caller text can name a location outside the root. Temp files sit beside the variant they stage.
/// </summary>
internal static class VariantCacheLayout
{
    internal const int VersionLength = 32;
    internal const int VariantLength = 64;
    internal const int TempGuidLength = 32;
    internal const string TempSuffix = ".tmp";
    internal const char KeySeparator = '/';

    /// <summary>
    /// An empty <c>&lt;file id&gt;</c> or <c>&lt;file id&gt;/&lt;version&gt;</c> directory is pruned only when its last
    /// write (UTC) is older than this. A writer creates a directory and fills it moments later, so a young one
    /// may be about to receive a variant. Its own constant, not <see cref="StaleTempAge"/>: the reason differs
    /// (a write in flight, not an interrupted one), the value happens to match.
    /// </summary>
    internal static readonly TimeSpan EmptyDirectoryMinAge = TimeSpan.FromHours(1);

    /// <summary>A temp file older than this is a leftover of a write that never finished.</summary>
    internal static readonly TimeSpan StaleTempAge = TimeSpan.FromHours(1);

    internal readonly record struct VariantKey(string FileDir, string Version, string Variant);

    internal static string FileDirName(Guid fileId) => fileId.ToString("D", CultureInfo.InvariantCulture);

    internal static string ComposeKey(Guid fileId, string versionHex, string variantHex) =>
        string.Join(KeySeparator, FileDirName(fileId), versionHex, variantHex);

    internal static bool TryParseKey(string key, out VariantKey parsed)
    {
        parsed = default;
        var parts = key.Split(KeySeparator);
        if (parts.Length != 3 || !IsFileDirName(parts[0])
            || !IsLowerHex(parts[1], VersionLength) || !IsLowerHex(parts[2], VariantLength))
            return false;
        parsed = new VariantKey(parts[0], parts[1], parts[2]);
        return true;
    }

    internal static string VersionDirectory(string root, VariantKey key) =>
        Path.Combine(root, key.FileDir, key.Version);

    internal static string VariantPath(string root, VariantKey key) =>
        Path.Combine(VersionDirectory(root, key), key.Variant);

    internal static string TempName(string variantHex) =>
        $".{variantHex}.{Guid.NewGuid():N}{TempSuffix}";

    internal static bool IsFileDirName(string name) =>
        Guid.TryParseExact(name, "D", out var id) && FileDirName(id) == name;

    internal static bool IsVersionDirName(string name) => IsLowerHex(name, VersionLength);

    internal static bool IsVariantName(string name) => IsLowerHex(name, VariantLength);

    /// <summary><c>.&lt;variant&gt;.&lt;guid&gt;.tmp</c>, the staging name <see cref="TempName"/> produces.</summary>
    internal static bool IsTempName(string name) =>
        name.Length == 1 + VariantLength + 1 + TempGuidLength + TempSuffix.Length
        && name[0] == '.'
        && IsLowerHex(name.AsSpan(1, VariantLength), VariantLength)
        && name[1 + VariantLength] == '.'
        && IsLowerHex(name.AsSpan(2 + VariantLength, TempGuidLength), TempGuidLength)
        && name.EndsWith(TempSuffix, StringComparison.Ordinal);

    /// <summary>
    /// A path that is not there: the directory (or the file in it) was removed by a purge, a version change
    /// or a maintenance pass. Linux reports a vanished directory as <see cref="DirectoryNotFoundException"/>,
    /// and as <see cref="FileNotFoundException"/> when the directory is back by the time the error is classified.
    /// </summary>
    internal static bool IsMissingPath(Exception error) =>
        error is DirectoryNotFoundException or FileNotFoundException;

    /// <summary>A symbolic link or junction: the cache never descends into one or deletes through one.</summary>
    internal static bool IsReparsePoint(FileSystemInfo entry) =>
        (entry.Attributes & FileAttributes.ReparsePoint) != 0;

    // "Directory not empty" as .NET reports it: ERROR_DIR_NOT_EMPTY on Windows (0x80070091) and ENOTEMPTY
    // on Linux (39), both observed by running; ENOTEMPTY on macOS and FreeBSD (66) is from the platform
    // documentation, not observed.
    // Another platform has no code here, so its failure is not treated as a race.
    private static int? DirectoryNotEmptyCode()
    {
        if (OperatingSystem.IsWindows()) return unchecked((int)0x80070091);
        if (OperatingSystem.IsLinux()) return 39;
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD()) return 66;
        return null;
    }

    /// <summary>
    /// A failure that only says another writer, purge or maintenance pass changed the tree first: the
    /// directory is gone, or it holds an entry that stops its removal. It is not a fault of the disk.
    /// </summary>
    internal static bool IsBenignRace(Exception error) =>
        error is DirectoryNotFoundException or FileNotFoundException
        || (error is IOException io && IsDirectoryNotEmpty(io));

    /// <summary>
    /// <see cref="IsBenignRace(Exception)"/> for a failure under <paramref name="rootPath"/>: when the cache
    /// root itself is missing (an unmounted volume, say) nothing raced, so it is not benign.
    /// </summary>
    internal static bool IsBenignRace(Exception error, string rootPath) =>
        IsBenignRace(error) && Directory.Exists(rootPath);

    internal static bool IsDirectoryNotEmpty(IOException error) =>
        DirectoryNotEmptyCode() is { } code && error.HResult == code;

    internal static bool IsLowerHex(ReadOnlySpan<char> text, int length)
    {
        if (text.Length != length) return false;
        foreach (var c in text)
            if (!(c is >= '0' and <= '9' or >= 'a' and <= 'f')) return false;
        return true;
    }
}
