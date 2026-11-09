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
