namespace Struo.Infrastructure.Migrations.Commands;

/// <summary>A parsed command line: the <c>migrate*</c> command (null when the process should run the web host) and the arguments left for host configuration.</summary>
public sealed record MigrationCliInvocation(string? Command, string[] HostArgs);
