using System.Reflection;
using Struo.Application.Configuration;

namespace Struo.Infrastructure.Migrations;

public sealed record MigrationHostOptions(
    StruoDbType DbType, string ConnectionString, string TablePrefix,
    IReadOnlyList<Assembly> Assemblies, int LockTimeoutSeconds)
{
    /// <summary>Restricts scanning to one namespace and its children; tests use it to isolate probe migrations.</summary>
    internal string? NamespaceFilter { get; init; }

    /// <summary>Inputs of the core seed migration; the seed migration fails when this is null.</summary>
    public CoreSeedData? Seed { get; init; }
}
