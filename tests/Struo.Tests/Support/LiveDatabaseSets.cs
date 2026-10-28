namespace Struo.Tests.Support;

/// <summary>Databases for <c>LiveRepositoryTests</c>.</summary>
public sealed class RepositoryDatabases() : LiveDatabaseSet("repo");

/// <summary>Databases for <c>PostgresOnlyTests</c>.</summary>
public sealed class PostgresOnlyDatabases() : LiveDatabaseSet("pgonly");
