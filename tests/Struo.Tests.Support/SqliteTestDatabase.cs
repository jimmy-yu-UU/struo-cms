namespace Struo.Tests.Support;

/// <summary>Unique temp-file SQLite database, deleted on dispose. Provider-agnostic.</summary>
public sealed class SqliteTestDatabase : IDisposable
{
    public string FilePath { get; } =
        Path.Combine(Path.GetTempPath(), $"struo_test_{Guid.NewGuid():N}.db");

    public string ConnectionString => $"Data Source={FilePath}";

    public void Dispose()
    {
        if (File.Exists(FilePath))
        {
            try { File.Delete(FilePath); } catch { /* best effort */ }
        }
    }
}
