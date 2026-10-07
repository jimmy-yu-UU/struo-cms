using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Struo.Application.Configuration;
using Struo.Infrastructure.Metadata;

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

    /// <summary>
    /// The options the application's configuration describes: the database, the scanned assemblies and,
    /// when <paramref name="includeSeed"/> is set, the core seed built from configuration. The CLI and
    /// the startup gate both build their host from this.
    /// </summary>
    public static MigrationHostOptions FromServices(IServiceProvider services, bool includeSeed)
    {
        var db = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        return new MigrationHostOptions(
            db.DbType, db.ConnectionString, db.TablePrefix,
            services.GetRequiredService<ScannedAssemblies>().All, db.MigrationLockTimeoutSeconds)
        {
            Seed = includeSeed ? CoreSeedData.FromServices(services) : null,
        };
    }
}
