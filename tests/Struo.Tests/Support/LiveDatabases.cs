using Microsoft.Extensions.Configuration;

namespace Struo.Tests.Support;

/// <summary>
/// Connection strings for the opt-in live-database tests. Each resolves from an environment variable
/// first, then from <c>Testing:*</c> in the API project's appsettings. Null makes live tests return
/// early, and makes the API hosts fail when that backend is selected via <c>STRUO_TEST_BACKEND</c>.
/// </summary>
internal static class LiveDatabases
{
    public const string PostgresEnvVar = "STRUO_TEST_PG_CONNECTION";
    public const string PostgresConfigKey = "Testing:PostgresConnection";
    public const string SqlServerEnvVar = "STRUO_TEST_SQLSERVER_CONNECTION";
    public const string SqlServerConfigKey = "Testing:SqlServerConnection";
    public const string MySqlEnvVar = "STRUO_TEST_MYSQL_CONNECTION";
    public const string MySqlConfigKey = "Testing:MySqlConnection";
    public const string MariaDbEnvVar = "STRUO_TEST_MARIADB_CONNECTION";
    public const string MariaDbConfigKey = "Testing:MariaDbConnection";

    public static string? Postgres { get; } =
        Resolve(PostgresEnvVar, PostgresConfigKey) is { } raw
            ? PgTestConnectionString.DisablePooling(raw)
            : null;

    public static string? SqlServer { get; } =
        Resolve(SqlServerEnvVar, SqlServerConfigKey);

    public static string? MySql { get; } =
        Resolve(MySqlEnvVar, MySqlConfigKey);

    public static string? MariaDb { get; } =
        Resolve(MariaDbEnvVar, MariaDbConfigKey);

    /// <summary>Throws unless the connection's database name contains "test".</summary>
    public static void GuardDisposable(string connectionString)
    {
        var dbName = connectionString.Split(';')
            .Select(p => p.Trim())
            .Select(p => p.Split('=', 2))
            .Where(kv => kv.Length == 2 &&
                         (kv[0].Equals("Database", StringComparison.OrdinalIgnoreCase) ||
                          kv[0].Equals("Initial Catalog", StringComparison.OrdinalIgnoreCase)))
            .Select(kv => kv[1].Trim())
            .FirstOrDefault() ?? "";
        if (!dbName.Contains("test", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Refusing destructive live tests against database '{dbName}': its name must contain 'test'.");
    }

    private static string? Resolve(string envVar, string configKey)
    {
        var raw = Environment.GetEnvironmentVariable(envVar);
        if (string.IsNullOrWhiteSpace(raw))
        {
            var apiDir = FindApiDir();
            if (apiDir is null) return null;
            raw = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(apiDir, "appsettings.json"), optional: true)
                .AddJsonFile(Path.Combine(apiDir, "appsettings.Development.json"), optional: true)
                .Build()[configKey];
        }
        return string.IsNullOrWhiteSpace(raw) ? null : raw;
    }

    private static string? FindApiDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Struo.Api");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }
}
