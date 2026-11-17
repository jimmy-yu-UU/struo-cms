using System.Text.RegularExpressions;
using MySqlConnector;
using Struo.Tests.Support;

namespace Struo.Tests.Migrations.Utf8;

/// <summary>
/// A MySQL or MariaDB database whose default character set the test chooses, created next to the configured
/// test database under a random name that contains "test". The client character set of its connection is
/// utf8mb4, so a test can store text in a column whatever the database default is. Disposing drops it.
/// The raw SQL is the CREATE and DROP of this lifecycle, with a name that passes a pattern and a character
/// set from a fixed list.
/// </summary>
internal sealed partial class CharacterSetDatabase : IDisposable
{
    private readonly string _administration;

    private CharacterSetDatabase(string name, string administration, string connection)
    {
        Name = name;
        _administration = administration;
        Connection = connection;
    }

    public string Name { get; }

    /// <summary>The connection string of this database.</summary>
    public string Connection { get; }

    public static CharacterSetDatabase Create(string backend, string characterSet)
    {
        if (characterSet is not ("latin1" or "utf8mb3" or "utf8mb4"))
            throw new ArgumentException($"Unsupported character set '{characterSet}'.", nameof(characterSet));
        var (_, _, configured) = LiveBackend.Get(backend).Configured();
        LiveDatabases.GuardDisposable(configured);
        var name = "struo_test_cs_" + Guid.NewGuid().ToString("N")[..10];
        if (!NamePattern().IsMatch(name)) throw new InvalidOperationException($"Unexpected database name '{name}'.");
        Execute(configured, $"CREATE DATABASE `{name}` CHARACTER SET {characterSet}");
        var connection = new MySqlConnectionStringBuilder(configured) { Database = name, CharacterSet = "utf8mb4" }.ConnectionString;
        return new CharacterSetDatabase(name, configured, connection);
    }

    public void Dispose()
    {
        using (var closing = new MySqlConnection(Connection)) MySqlConnection.ClearPool(closing);
        Execute(_administration, $"DROP DATABASE IF EXISTS `{Name}`");
    }

    private static void Execute(string connection, string statement)
    {
        using var open = new MySqlConnection(connection);
        open.Open();
        using var command = open.CreateCommand();
        command.CommandText = statement;
        command.ExecuteNonQuery();
    }

    [GeneratedRegex("^struo_test_cs_[0-9a-f]{10}$")]
    private static partial Regex NamePattern();
}
