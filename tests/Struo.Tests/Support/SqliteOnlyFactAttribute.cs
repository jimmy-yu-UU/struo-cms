namespace Struo.Tests.Support;

/// <summary>A <see cref="FactAttribute"/> that is skipped when a live backend is selected.</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class SqliteOnlyFactAttribute : FactAttribute
{
    public SqliteOnlyFactAttribute(string reason)
    {
        if (TestBackend.IsLive) Skip = $"SQLite-only: {reason}";
    }
}

/// <summary>A <see cref="TheoryAttribute"/> that is skipped when a live backend is selected.</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class SqliteOnlyTheoryAttribute : TheoryAttribute
{
    public SqliteOnlyTheoryAttribute(string reason)
    {
        if (TestBackend.IsLive) Skip = $"SQLite-only: {reason}";
    }
}
