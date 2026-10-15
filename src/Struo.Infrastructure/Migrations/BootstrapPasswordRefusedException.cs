namespace Struo.Infrastructure.Migrations;

/// <summary>The configured bootstrap admin password is too weak to seed.</summary>
public sealed class BootstrapPasswordRefusedException(string message) : Exception(message);
