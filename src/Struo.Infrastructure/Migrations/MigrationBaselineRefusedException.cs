namespace Struo.Infrastructure.Migrations;

/// <summary>The database is not in a state that <c>migrate:baseline</c> can adopt.</summary>
public sealed class MigrationBaselineRefusedException(string message) : Exception(message);
