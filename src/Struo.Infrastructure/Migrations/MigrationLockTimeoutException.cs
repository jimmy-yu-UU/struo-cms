namespace Struo.Infrastructure.Migrations;

/// <summary>The migration lock stayed held by another session until the timeout elapsed.</summary>
public sealed class MigrationLockTimeoutException(string message) : Exception(message);
