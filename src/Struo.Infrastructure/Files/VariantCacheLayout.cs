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

    internal static bool IsLowerHex(ReadOnlySpan<char> text, int length)
    {
        if (text.Length != length) return false;
        foreach (var c in text)
            if (!(c is >= '0' and <= '9' or >= 'a' and <= 'f')) return false;
        return true;
    }
}
