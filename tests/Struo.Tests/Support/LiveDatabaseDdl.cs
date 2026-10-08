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
    /// <summary>The database a connection string names, read with the provider's own builder.</summary>
    public static string DatabaseOf(TestBackendKind kind, string connectionString) => kind switch
    {
        TestBackendKind.PostgreSQL => new NpgsqlConnectionStringBuilder(connectionString).Database ?? "",
        TestBackendKind.SqlServer => new SqlConnectionStringBuilder(connectionString).InitialCatalog,
        TestBackendKind.MySql or TestBackendKind.MariaDb => new MySqlConnectionStringBuilder(connectionString).Database,
        _ => throw new ArgumentException("SQLite databases are files, not server databases.", nameof(kind)),
    };

    /// <summary>Creates <paramref name="name"/> next to the configured test database.</summary>
    public static LiveTestDatabase Create(TestBackendKind kind, string name, string configuredConnection)
    {
        ValidateName(name);
        if (Exists(kind, name, configuredConnection))
            throw new InvalidOperationException(
                $"{kind}: database '{name}' already exists; refusing to adopt a database this run did not create.");
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
        Exists(kind, name, TestBackend.ConfiguredConnection(kind));

    public static IReadOnlyList<string> List(TestBackendKind kind) =>
        List(kind, TestBackend.ConfiguredConnection(kind));

    private static bool Exists(TestBackendKind kind, string name, string configuredConnection) =>
        List(kind, configuredConnection).Contains(name, StringComparer.OrdinalIgnoreCase);

    private static List<string> List(TestBackendKind kind, string configuredConnection)
    {
        using var client = OpenClient(kind, configuredConnection);
        return client.DbMaintenance.GetDataBaseList(client);
    }

    /// <summary>Drops the database, severing open connections where the server allows it. Never throws.</summary>
    public static void Drop(TestBackendKind kind, string name)
    {
        try
        {
            ValidateName(name);
            var configured = TestBackend.ConfiguredConnection(kind);
            ClearPool(kind, WithDatabase(configured, kind, name));
            using var client = OpenClient(kind, configured);
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

    // Clears only the pool of the connection being dropped. Never throws.
    private static void ClearPool(TestBackendKind kind, string connectionString)
    {
        try
        {
            switch (kind)
            {
                case TestBackendKind.PostgreSQL:
                    using (var pg = new NpgsqlConnection(connectionString)) NpgsqlConnection.ClearPool(pg);
                    break;
                case TestBackendKind.SqlServer:
                    using (var mssql = new SqlConnection(connectionString)) SqlConnection.ClearPool(mssql);
                    break;
                default:
                    using (var mysql = new MySqlConnection(connectionString)) MySqlConnection.ClearPool(mysql);
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"WARNING: could not clear the connection pool on {kind}: {ex.Message}");
        }
    }

    private static string WithDatabase(string connectionString, TestBackendKind kind, string name)
    {
        switch (kind)
        {
            case TestBackendKind.PostgreSQL:
                return new NpgsqlConnectionStringBuilder(connectionString) { Database = name }.ConnectionString;
            case TestBackendKind.SqlServer:
                return new SqlConnectionStringBuilder(connectionString) { InitialCatalog = name }.ConnectionString;
            case TestBackendKind.MySql or TestBackendKind.MariaDb:
                return new MySqlConnectionStringBuilder(connectionString) { Database = name }.ConnectionString;
            default:
                throw new ArgumentException("SQLite databases are files, not server databases.", nameof(kind));
        }
    }

    [GeneratedRegex("^[a-z0-9_]+$")]
    private static partial Regex ValidNamePattern();
}
