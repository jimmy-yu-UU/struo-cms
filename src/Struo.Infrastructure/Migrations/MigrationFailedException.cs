namespace Struo.Infrastructure.Migrations;

public sealed class MigrationFailedException(string message, Exception? inner = null) : Exception(message, inner);
