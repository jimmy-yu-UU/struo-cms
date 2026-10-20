using System.Text.RegularExpressions;

namespace Struo.Infrastructure.Files;

public static partial class StorageKey
{
    /// <summary>Builds a collision- and traversal-safe key: <c>{yyyy}/{MM}/{guid:N}{ext}</c>.</summary>
    public static string Create(string originalFileName)
    {
        var ext = Path.GetExtension(originalFileName); // includes leading dot, may be empty
        var now = DateTime.UtcNow;
        return $"{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{ext}";
    }

    /// <summary>True when <paramref name="key"/> has the shape <see cref="Create"/> produces.</summary>
    public static bool IsStorageKey(string key) => KeyShape().IsMatch(key);

    [GeneratedRegex(@"^\d{4}/\d{2}/[0-9a-f]{32}(\.[^/]*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyShape();
}
