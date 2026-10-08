using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using SqlSugar;

namespace Struo.Tests.Support;

/// <summary>
/// Create, drop and list databases on a live test server. The DROP statements below are the only raw
/// SQL in the test-database lifecycle; every interpolated name passes <see cref="ValidateName"/> first.
/// </summary>
internal static partial class LiveDatabaseDdl
{
    public static string DatabaseOf(string connectionString) => WithDatabase(connectionString, kind: null, name: null);

    /// <summary>Creates <paramref name="name"/> next to the configured test database.</summary>
    public static LiveTestDatabase Create(TestBackendKind kind, string name, string configuredConnection)
    {
        ValidateName(name);
        var connection = WithDatabase(configuredConnection, kind, name);
        using (var client = OpenClient(kind, connection))
        {
            if (!client.DbMaintenance.CreateDatabase())
                throw new InvalidOperationException($"{kind}: SqlSugar did not create database '{name}'.");
        }

        var database = new LiveTestDatabase(kind, name, connection);
        if (!Exists(kind, name))
        {
            database.Dispose();
            throw new InvalidOperationException($"{kind}: database '{name}' is missing after creation.");
        }
        return database;
    }

    /// <summary>Creates a database with a caller-chosen name; disposing drops it.</summary>
    public static IDisposable CreateNamed(TestBackendKind kind, string name) =>
        Create(kind, name, TestBackend.ConfiguredConnection(kind));

    public static bool Exists(TestBackendKind kind, string name) =>
        List(kind).Contains(name, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> List(TestBackendKind kind)
    {
        using var client = OpenClient(kind, TestBackend.ConfiguredConnection(kind));
        return client.DbMaintenance.GetDataBaseList(client);
    }

    /// <summary>Drops the database, severing open connections where the server allows it. Never throws.</summary>
    public static void Drop(TestBackendKind kind, string name)
    {
        try
        {
            ValidateName(name);
            ClearPools(kind);
            using var client = OpenClient(kind, TestBackend.ConfiguredConnection(kind));
            client.Ado.ExecuteCommand(DropStatement(kind, name));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"WARNING: could not drop test database '{name}' on {kind}: {ex.Message}");
        }
    }

    public static SqlSugarClient OpenClient(TestBackendKind kind, string connectionString, bool keepOpen = false) =>
        new(new ConnectionConfig
        {
            ConnectionString = connectionString,
            DbType = SqlSugarDbType(kind),
            IsAutoCloseConnection = !keepOpen,
            MoreSettings = new ConnMoreSettings { SqlServerCodeFirstNvarchar = kind == TestBackendKind.SqlServer },
        });

    public static void ValidateName(string name)
    {
        if (!ValidNamePattern().IsMatch(name))
            throw new ArgumentException($"Test database name '{name}' must match ^[a-z0-9_]+$.", nameof(name));
    }

    private static string DropStatement(TestBackendKind kind, string name) => kind switch
    {
        TestBackendKind.PostgreSQL => $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)",
        TestBackendKind.SqlServer =>
            $"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END",
        TestBackendKind.MySql or TestBackendKind.MariaDb => $"DROP DATABASE IF EXISTS `{name}`",
        _ => throw new ArgumentException("SQLite databases are files, not server databases.", nameof(kind)),
    };

    private static DbType SqlSugarDbType(TestBackendKind kind) => kind switch
    {
        TestBackendKind.PostgreSQL => DbType.PostgreSQL,
        TestBackendKind.SqlServer => DbType.SqlServer,
        TestBackendKind.MySql or TestBackendKind.MariaDb => DbType.MySql,
        _ => throw new ArgumentException("SQLite databases are files, not server databases.", nameof(kind)),
    };

    private static void ClearPools(TestBackendKind kind)
    {
        switch (kind)
        {
            case TestBackendKind.PostgreSQL: NpgsqlConnection.ClearAllPools(); break;
            case TestBackendKind.SqlServer: SqlConnection.ClearAllPools(); break;
            default: MySqlConnection.ClearAllPools(); break;
        }
    }

    // With kind and name null, only reads the database name back.
    private static string WithDatabase(string connectionString, TestBackendKind? kind, string? name)
    {
        switch (kind)
        {
            case TestBackendKind.PostgreSQL:
                return Rewrite(new NpgsqlConnectionStringBuilder(connectionString), b => b.Database, (b, v) => b.Database = v, name);
            case TestBackendKind.SqlServer:
                return Rewrite(new SqlConnectionStringBuilder(connectionString), b => b.InitialCatalog, (b, v) => b.InitialCatalog = v, name);
            case TestBackendKind.MySql or TestBackendKind.MariaDb:
                return Rewrite(new MySqlConnectionStringBuilder(connectionString), b => b.Database, (b, v) => b.Database = v, name);
            default:
                return ReadDatabase(connectionString);
        }
    }

    private static string Rewrite<T>(T builder, Func<T, string?> get, Action<T, string> set, string? name)
        where T : System.Data.Common.DbConnectionStringBuilder
    {
        if (name is null) return get(builder) ?? "";
        set(builder, name);
        return builder.ConnectionString;
    }

    private static string ReadDatabase(string connectionString)
    {
        var builder = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = connectionString };
        foreach (var key in new[] { "Database", "Initial Catalog" })
            if (builder.TryGetValue(key, out var value)) return value?.ToString() ?? "";
        throw new InvalidOperationException("The test connection string names no database.");
    }

    [GeneratedRegex("^[a-z0-9_]+$")]
    private static partial Regex ValidNamePattern();
}
