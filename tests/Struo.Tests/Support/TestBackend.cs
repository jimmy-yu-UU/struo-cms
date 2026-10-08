using System.Text.RegularExpressions;
using Struo.Application.Configuration;

namespace Struo.Tests.Support;

public enum TestBackendKind { Sqlite, PostgreSQL, SqlServer, MySql, MariaDb }

/// <summary>
/// The backend API test hosts run on, chosen by <c>STRUO_TEST_BACKEND</c> (default SQLite). A live
/// backend gets one fresh, uniquely named database per host, dropped on dispose.
/// </summary>
internal static partial class TestBackend
{
    public const string EnvVar = "STRUO_TEST_BACKEND";

    private const int MaxNameLength = 63;
    private const int RunIdLength = 8;
    private const string TestMarker = "_test";

    private static readonly Lazy<TestBackendKind> CurrentKind = new(
        () => Parse(Environment.GetEnvironmentVariable(EnvVar)));

    public static readonly string RunId = Guid.NewGuid().ToString("N")[..RunIdLength];

    public static TestBackendKind Current => CurrentKind.Value;

    public static bool IsLive => Current != TestBackendKind.Sqlite;

    public static StruoDbType DbType => DbTypeOf(Current);

    public static TestBackendKind Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return TestBackendKind.Sqlite;
        var trimmed = value.Trim();
        var match = Enum.GetValues<TestBackendKind>()
            .Cast<TestBackendKind?>()
            .FirstOrDefault(k => string.Equals(k.ToString(), trimmed, StringComparison.OrdinalIgnoreCase));
        return match ?? throw new InvalidOperationException(
            $"{EnvVar}='{value}' is not a valid backend. Valid values: " +
            string.Join(", ", Enum.GetNames<TestBackendKind>()) + ".");
    }

    public static StruoDbType DbTypeOf(TestBackendKind kind) => kind switch
    {
        TestBackendKind.Sqlite => StruoDbType.Sqlite,
        TestBackendKind.PostgreSQL => StruoDbType.PostgreSQL,
        TestBackendKind.SqlServer => StruoDbType.SqlServer,
        TestBackendKind.MySql or TestBackendKind.MariaDb => StruoDbType.MySql,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static TestBackendKind KindOf(LiveBackend backend) => Parse(backend.Name);

    public static ITestDatabase CreateDatabase(string purpose) =>
        IsLive ? CreateLiveDatabase(Current, purpose) : CreateSqliteDatabase();

    public static ITestDatabase CreateSqliteDatabase() => new SqliteTempTestDatabase();

    public static ITestDatabase CreateLiveDatabase(TestBackendKind kind, string purpose) =>
        CreateLiveDatabase(kind, purpose, RunId);

    public static ITestDatabase CreateLiveDatabase(TestBackendKind kind, string purpose, string runId)
    {
        var configured = ConfiguredConnection(kind);
        LiveDatabases.GuardDisposable(configured);
        var name = DatabaseNameFor(LiveDatabaseDdl.DatabaseOf(configured), purpose, runId);
        return LiveDatabaseDdl.Create(kind, name, configured);
    }

    /// <summary>Returns <paramref name="connection"/>, or throws naming where to configure it.</summary>
    public static string RequireConnection(TestBackendKind kind, string? connection)
    {
        if (!string.IsNullOrWhiteSpace(connection)) return connection;
        var (envVar, configKey) = SettingsFor(kind);
        throw new InvalidOperationException(
            $"{EnvVar}={kind} needs a test connection string: set the {envVar} environment variable " +
            $"or '{configKey}' in the API project's appsettings.Development.json.");
    }

    public static string ConfiguredConnection(TestBackendKind kind)
    {
        if (kind == TestBackendKind.Sqlite)
            throw new ArgumentException("SQLite has no live server connection.", nameof(kind));
        return RequireConnection(kind, LiveBackend.Get(kind.ToString()).Connection);
    }

    public static string ConfiguredDatabaseName(TestBackendKind kind) =>
        LiveDatabaseDdl.DatabaseOf(ConfiguredConnection(kind));

    public static string DatabaseNameFor(string configuredName, string purpose, string runId)
    {
        var stem = Stem(configuredName);
        var purposePart = Sanitize(purpose);
        var combined = purposePart.Length == 0 ? stem : $"{stem}_{purposePart}";
        var room = MaxNameLength - 1 - runId.Length;
        if (combined.Length > room) combined = $"{combined[..(room - TestMarker.Length)].TrimEnd('_')}{TestMarker}";
        return $"{combined}_{runId}";
    }

    /// <summary>True for names <see cref="DatabaseNameFor"/> can produce for this configured database.</summary>
    public static bool IsLeftoverName(string configuredName, string name)
    {
        var match = LeftoverShape().Match(name);
        if (!match.Success) return false;
        var stemPart = match.Groups["stem"].Value;
        var prefix = Stem(configuredName) + "_";
        if (stemPart.StartsWith(prefix, StringComparison.Ordinal)) return true;

        // Truncated form: a prefix of "<stem>_<purpose>" followed by the test marker.
        var room = MaxNameLength - 1 - RunIdLength - TestMarker.Length;
        if (!stemPart.EndsWith(TestMarker, StringComparison.Ordinal)) return false;
        var head = stemPart[..^TestMarker.Length];
        return head.Length >= room - 1 && head.Length <= room &&
               (prefix.StartsWith(head, StringComparison.Ordinal) ||
                head.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <summary>Leftover names among <paramref name="names"/>, never including the current run's databases.</summary>
    public static IReadOnlyList<string> SelectLeftovers(
        string configuredName, IEnumerable<string> names, string? runId = null) =>
        names
            .Where(n => IsLeftoverName(configuredName, n) &&
                        !n.EndsWith($"_{RunId}", StringComparison.Ordinal) &&
                        (runId is null || n.EndsWith($"_{runId}", StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToList();

    public static IReadOnlyList<string> FindLeftovers(TestBackendKind kind, string? runId = null) =>
        SelectLeftovers(ConfiguredDatabaseName(kind), LiveDatabaseDdl.List(kind), runId);

    public static void DropLeftovers(TestBackendKind kind, string? runId = null)
    {
        foreach (var name in FindLeftovers(kind, runId)) LiveDatabaseDdl.Drop(kind, name);
    }

    private static (string EnvVar, string ConfigKey) SettingsFor(TestBackendKind kind) => kind switch
    {
        TestBackendKind.PostgreSQL => (LiveDatabases.PostgresEnvVar, LiveDatabases.PostgresConfigKey),
        TestBackendKind.SqlServer => (LiveDatabases.SqlServerEnvVar, LiveDatabases.SqlServerConfigKey),
        TestBackendKind.MySql => (LiveDatabases.MySqlEnvVar, LiveDatabases.MySqlConfigKey),
        TestBackendKind.MariaDb => (LiveDatabases.MariaDbEnvVar, LiveDatabases.MariaDbConfigKey),
        _ => throw new ArgumentException("SQLite has no live server connection.", nameof(kind)),
    };

    private static string Stem(string configuredName)
    {
        var stem = Sanitize(configuredName);
        return stem.Contains("test", StringComparison.Ordinal) ? stem : $"{stem}_test";
    }

    private static string Sanitize(string value) =>
        NonNameChars().Replace(value.ToLowerInvariant(), "_").Trim('_');

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonNameChars();

    [GeneratedRegex("^(?<stem>[a-z0-9_]+)_[0-9a-f]{8}$")]
    private static partial Regex LeftoverShape();
}
