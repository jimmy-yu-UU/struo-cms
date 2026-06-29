namespace Struo.Infrastructure.Files;

public static class StorageKey
{
    /// <summary>Builds a collision- and traversal-safe key: <c>{yyyy}/{MM}/{guid:N}{ext}</c>.</summary>
    public static string Create(string originalFileName)
    {
        var ext = Path.GetExtension(originalFileName); // includes leading dot, may be empty
        var now = DateTime.UtcNow;
        return $"{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{ext}";
    }
}
