using System.Reflection;
using Struo.Application.Configuration;

namespace Struo.Infrastructure.Migrations;

public sealed record MigrationHostOptions(
    StruoDbType DbType, string ConnectionString, string TablePrefix,
    IReadOnlyList<Assembly> Assemblies, int LockTimeoutSeconds)
{
    /// <summary>Restricts scanning to one namespace and its children; tests use it to isolate probe migrations.</summary>
    internal string? NamespaceFilter { get; init; }

    /// <summary>Lets tests replace services of the runner container, for example to inject a failure.</summary>
    internal Action<Microsoft.Extensions.DependencyInjection.IServiceCollection>? ConfigureRunnerServices { get; init; }

    /// <summary>Inputs of the core seed migration; the seed migration fails when this is null.</summary>
    public CoreSeedData? Seed { get; init; }
}
