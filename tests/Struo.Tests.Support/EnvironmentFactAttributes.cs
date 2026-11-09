using Xunit.Abstractions;
using Xunit.Sdk;

namespace Struo.Tests.Support;

/// <summary>A <see cref="FactAttribute"/> that is skipped everywhere except Windows.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute(string reason)
    {
        if (!OperatingSystem.IsWindows()) Skip = $"Windows-only: {reason}";
    }
}

/// <summary>A <see cref="FactAttribute"/> that is skipped unless the backend keeps a zone with its instants
/// (PostgreSQL and SQL Server).</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ZoneAwareOnlyFactAttribute : FactAttribute
{
    public ZoneAwareOnlyFactAttribute(string reason)
    {
        if (TestBackend.Current is not (TestBackendKind.PostgreSQL or TestBackendKind.SqlServer))
            Skip = $"Zone-aware backends only (PostgreSQL, SQL Server): {reason}";
    }
}

/// <summary>A <see cref="FactAttribute"/> that is skipped on the listed backends.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class NotOnBackendFactAttribute : FactAttribute
{
    public NotOnBackendFactAttribute(string reason, params TestBackendKind[] excluded)
    {
        if (Array.IndexOf(excluded, TestBackend.Current) >= 0)
            Skip = $"Not on {TestBackend.Current}: {reason}";
    }
}

/// <summary>
/// Skip reasons for the opt-in live tests: which environment variable or configuration key supplies the
/// missing connection.
/// </summary>
public static class LiveSkip
{
    /// <summary>The reason a backend's live tests cannot run here, or null when its connection is set.</summary>
    public static string? ForBackend(string backend)
    {
        if (LiveBackend.Get(backend).Connection is not null) return null;
        var (envVar, configKey) = backend switch
        {
            "PostgreSQL" => (LiveDatabases.PostgresEnvVar, LiveDatabases.PostgresConfigKey),
            "SqlServer" => (LiveDatabases.SqlServerEnvVar, LiveDatabases.SqlServerConfigKey),
            "MySql" => (LiveDatabases.MySqlEnvVar, LiveDatabases.MySqlConfigKey),
            "MariaDb" => (LiveDatabases.MariaDbEnvVar, LiveDatabases.MariaDbConfigKey),
            _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, "Not a live backend."),
        };
        return $"{backend} is not configured: set {envVar} or {configKey}.";
    }

    /// <summary>The reason a test cannot run when one of the variables is unset or blank, or null when all are set.</summary>
    public static string? ForEnvironment(params string[] variables) =>
        variables.FirstOrDefault(v => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(v))) is { } missing
            ? $"Environment variable {missing} is not set."
            : null;
}

/// <summary>A <see cref="FactAttribute"/> that is skipped unless every listed environment variable is set.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class EnvironmentFactAttribute : FactAttribute
{
    public EnvironmentFactAttribute(params string[] variables) => Skip = LiveSkip.ForEnvironment(variables);
}

/// <summary>
/// A <see cref="FactAttribute"/> that is skipped unless the live backend is configured and every listed
/// environment variable is set.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LiveBackendFactAttribute : FactAttribute
{
    public LiveBackendFactAttribute(string backend, params string[] variables) =>
        Skip = LiveSkip.ForBackend(backend) ?? LiveSkip.ForEnvironment(variables);
}

/// <summary>
/// A <see cref="TheoryAttribute"/> whose rows name live backends. A row that names a backend without a
/// configured connection is reported as skipped, with the variable that would enable it, and the others
/// run. A row's backend is any argument that is a backend name or a non-SQLite <see cref="TestBackendKind"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
[XunitTestCaseDiscoverer("Struo.Tests.Support.LiveBackendTheoryDiscoverer", "Struo.Tests.Support")]
public sealed class LiveBackendTheoryAttribute : TheoryAttribute;

/// <summary>Discovers <see cref="LiveBackendTheoryAttribute"/> rows and skips those whose backend is not configured.</summary>
public sealed class LiveBackendTheoryDiscoverer(IMessageSink diagnosticMessageSink) : TheoryDiscoverer(diagnosticMessageSink)
{
    protected override IEnumerable<IXunitTestCase> CreateTestCasesForDataRow(
        ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo theoryAttribute, object[] dataRow)
    {
        var reason = dataRow.Select(BackendOf).Where(b => b is not null).Select(b => LiveSkip.ForBackend(b!))
            .FirstOrDefault(r => r is not null);
        return reason is null
            ? base.CreateTestCasesForDataRow(discoveryOptions, testMethod, theoryAttribute, dataRow)
            : CreateTestCasesForSkippedDataRow(discoveryOptions, testMethod, theoryAttribute, dataRow, reason);
    }

    private static string? BackendOf(object? argument) => argument switch
    {
        string name when LiveBackend.All.Any(b => b.Name == name) => name,
        TestBackendKind kind when kind != TestBackendKind.Sqlite => kind.ToString(),
        _ => null,
    };
}
