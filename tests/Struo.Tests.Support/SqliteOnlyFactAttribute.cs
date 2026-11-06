namespace Struo.Tests.Support;

/// <summary>A <see cref="FactAttribute"/> that is skipped when a live backend is selected.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SqliteOnlyFactAttribute : FactAttribute
{
    public SqliteOnlyFactAttribute(string reason)
    {
        if (TestBackend.IsLive) Skip = $"SQLite-only: {reason}";
    }
}

/// <summary>A <see cref="TheoryAttribute"/> that is skipped when a live backend is selected.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SqliteOnlyTheoryAttribute : TheoryAttribute
{
    public SqliteOnlyTheoryAttribute(string reason)
    {
        if (TestBackend.IsLive) Skip = $"SQLite-only: {reason}";
    }
}

/// <summary>A <see cref="FactAttribute"/> that is skipped on SQLite, for a test that needs two concurrent
/// transactions a single-writer database cannot offer.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LiveOnlyFactAttribute : FactAttribute
{
    public LiveOnlyFactAttribute(string reason)
    {
        if (!TestBackend.IsLive) Skip = $"Live-only: {reason}";
    }
}
