using Struo.Application.Configuration;

namespace Struo.Tests.Support;

/// <summary>
/// One live-database case: a display name, the dialect, and the resolved connection string (null when
/// the backend is not configured). MariaDB runs on <see cref="StruoDbType.MySql"/> but is its own case so
/// test output names it separately.
/// </summary>
public sealed record LiveBackend(string Name, StruoDbType Db, string? Connection)
{
    public static IReadOnlyList<LiveBackend> All { get; } =
    [
        new("PostgreSQL", StruoDbType.PostgreSQL, LiveDatabases.Postgres),
        new("SqlServer", StruoDbType.SqlServer, LiveDatabases.SqlServer),
        new("MySql", StruoDbType.MySql, LiveDatabases.MySql),
        new("MariaDb", StruoDbType.MySql, LiveDatabases.MariaDb),
    ];

    public static LiveBackend Get(string name) => All.Single(b => b.Name == name);

    /// <summary>The resolved case of a backend whose connection is configured; throws when it is not.</summary>
    /// <remarks>
    /// Tests carry <c>LiveBackendTheory</c> or <c>LiveBackendFact</c>, which skip an unconfigured backend
    /// before the body runs, so this is the body's way to read the connection as non-null.
    /// </remarks>
    public (string Name, StruoDbType Db, string Connection) Configured() =>
        Connection is { Length: > 0 } connection
            ? (Name, Db, connection)
            : throw new InvalidOperationException($"{Name} is not configured; the test should have been skipped.");

    /// <summary>Theory data of backend names, restricted to <paramref name="names"/> when given.</summary>
    public static TheoryData<string> Names(params string[] names) =>
        new(All.Where(b => names.Length == 0 || names.Contains(b.Name)).Select(b => b.Name));

    public override string ToString() => Name;
}
