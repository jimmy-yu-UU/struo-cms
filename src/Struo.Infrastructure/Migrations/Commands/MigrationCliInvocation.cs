namespace Struo.Infrastructure.Migrations.Commands;

/// <summary>A parsed command line: the migration command (null when the process should run the web host), the command's own arguments, and the arguments left for host configuration.</summary>
public sealed record MigrationCliInvocation(string? Command, IReadOnlyList<string> CommandArgs, string[] HostArgs);
