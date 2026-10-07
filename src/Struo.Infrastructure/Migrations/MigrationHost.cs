using FluentMigrator.Runner;
using FluentMigrator.Runner.Exceptions;
using FluentMigrator.Runner.Initialization;
using FluentMigrator.Runner.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Persistence;

namespace Struo.Infrastructure.Migrations;

/// <summary>Applies pending migrations and reports status against the prefixed version table.</summary>
public sealed class MigrationHost(MigrationHostOptions options, ILoggerFactory loggerFactory)
{
    /// <summary>Runs every pending migration in version order; returns the ones applied by this run.</summary>
    public async Task<IReadOnlyList<MigrationInfo>> ApplyAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var logger = loggerFactory.CreateLogger<MigrationHost>();
        if (options.DbType == StruoDbType.Oracle)
            logger.LogWarning(
                "Oracle takes no migration lock; running migrate concurrently is unsupported.");
        await using var gate = await AcquireLockAsync(logger, ct);
        var before = GetStatus();
        if (before.All(m => m.State != MigrationState.Pending)) return [];
        try
        {
            using var sp = BuildRunner(preview: false, loggerFactory);
            using var scope = sp.CreateScope();
            scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new MigrationFailedException($"Migration run failed; lowest unrecorded version: {FirstUnrecordedVersion()}. {Innermost(ex).Message}", ex);
        }
        var pendingBefore = before.Where(m => m.State == MigrationState.Pending).Select(m => m.Version).ToHashSet();
        IReadOnlyList<MigrationInfo> applied = GetStatus()
            .Where(m => m.State == MigrationState.Applied && pendingBefore.Contains(m.Version)).ToList();
        return applied;
    }

    /// <summary>Records every known migration as applied without executing it, so a database built by an
    /// earlier schema source joins the migration history. Refuses a database that already has recorded
    /// migrations or lacks the core tables; in both cases nothing is written.</summary>
    public async Task<IReadOnlyList<MigrationInfo>> BaselineAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var logger = loggerFactory.CreateLogger<MigrationHost>();
        await using var gate = await AcquireLockAsync(logger, ct);
        EnsureBaselineable();
        try
        {
            using var sp = BuildRunner(preview: false, loggerFactory);
            using var scope = sp.CreateScope();
            var loader = scope.ServiceProvider.GetRequiredService<IVersionLoader>();
            loader.LoadVersionInfo();
            var known = scope.ServiceProvider.GetRequiredService<IMigrationInformationLoader>().LoadMigrations();
            foreach (var (version, migration) in known.OrderBy(kv => kv.Key))
                loader.UpdateVersionInfo(version, migration.Description ?? "");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new MigrationFailedException($"Baseline failed: {Innermost(ex).Message}", ex);
        }
        return GetStatus().Where(m => m.State == MigrationState.Applied).ToList();
    }

    private void EnsureBaselineable()
    {
        try
        {
            if (ReadAppliedRows().Count > 0)
                throw new MigrationBaselineRefusedException(
                    "migrations are already recorded in this database; baseline only applies to a database with none.");
            using var db = OpenClient();
            if (!CoreTables.Exist(db, options.TablePrefix))
                throw new MigrationBaselineRefusedException(
                    "the core tables do not exist; run migrate on a new database instead.");
        }
        catch (Exception ex) when (ex is not (MigrationBaselineRefusedException or OperationCanceledException))
        {
            throw new MigrationFailedException($"Could not inspect the database: {Innermost(ex).Message}", ex);
        }
    }

    private SqlSugarClient OpenClient() => new(new ConnectionConfig
    {
        DbType = DbTypeMapper.Map(options.DbType),
        ConnectionString = options.ConnectionString,
        IsAutoCloseConnection = true,
    });

    private async Task<IAsyncDisposable> AcquireLockAsync(ILogger logger, CancellationToken ct)
    {
        try
        {
            return await MigrationLock.AcquireAsync(
                options.DbType, options.ConnectionString, options.TablePrefix,
                TimeSpan.FromSeconds(options.LockTimeoutSeconds), ct, logger);
        }
        catch (Exception ex) when (ex is not (MigrationLockTimeoutException or OperationCanceledException))
        {
            throw new MigrationFailedException($"Could not acquire the migration lock: {Innermost(ex).Message}", ex);
        }
    }

    /// <summary>Known and recorded migrations merged by version. Writes nothing to the database.</summary>
    public IReadOnlyList<MigrationInfo> GetStatus()
    {
        IDictionary<long, string> known;
        try
        {
            using var sp = BuildRunner(preview: true, NullLoggerFactory.Instance);
            using var scope = sp.CreateScope();
            known = scope.ServiceProvider.GetRequiredService<IMigrationInformationLoader>()
                .LoadMigrations().ToDictionary(kv => kv.Key, kv => kv.Value.Description ?? "");
        }
        catch (MissingMigrationsException)
        {
            known = new Dictionary<long, string>();
        }
        catch (Exception ex)
        {
            throw new MigrationFailedException($"Could not load migrations: {Innermost(ex).Message}", ex);
        }

        Dictionary<long, SchemaVersionRow> applied;
        try
        {
            applied = ReadAppliedRows();
        }
        catch (Exception ex)
        {
            throw new MigrationFailedException($"Could not read applied migrations: {Innermost(ex).Message}", ex);
        }

        return known.Keys.Union(applied.Keys).Order()
            .Select(v => new MigrationInfo(
                v,
                known.TryGetValue(v, out var d) ? d : applied[v].Description ?? "",
                StateOf(known.ContainsKey(v), applied.ContainsKey(v)),
                applied.TryGetValue(v, out var row) ? row.AppliedOn : null))
            .ToList();
    }

    private static MigrationState StateOf(bool known, bool applied)
    {
        if (!known) return MigrationState.Orphaned;
        return applied ? MigrationState.Applied : MigrationState.Pending;
    }

    /// <summary>Pending migrations and their SQL, written to <paramref name="sqlOut"/>. Executes nothing.</summary>
    public IReadOnlyList<MigrationInfo> Preview(TextWriter sqlOut)
    {
        var pending = GetStatus().Where(m => m.State == MigrationState.Pending).ToList();
        if (pending.Count == 0) return pending;
        using var sqlLogging = LoggerFactory.Create(b => b.AddProvider(
            new SqlScriptFluentMigratorLoggerProvider(sqlOut,
                new SqlScriptFluentMigratorLoggerOptions { ShowSql = true }, disposeWriter: false)));
        try
        {
            using var sp = BuildRunner(preview: true, sqlLogging);
            using var scope = sp.CreateScope();
            scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
        }
        catch (Exception ex)
        {
            throw new MigrationFailedException($"Preview failed: {Innermost(ex).Message}", ex);
        }
        return pending;
    }

    internal ServiceProvider BuildRunner(bool preview, ILoggerFactory logging)
    {
        var services = new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(rb =>
            {
                MigrationDialect.Register(rb, options.DbType);
                rb.WithGlobalConnectionString(options.ConnectionString)
                  .WithVersionTable(new StruoVersionTableMetaData(options.TablePrefix))
                  .AsGlobalPreview(preview)
                  .ScanIn(options.Assemblies.ToArray()).For.Migrations();
            })
            .AddSingleton(new StruoMigrationContext(options.DbType, options.TablePrefix, options.Seed))
            .Configure<TypeFilterOptions>(f => { f.Namespace = options.NamespaceFilter; f.NestedNamespaces = true; })
            .AddLogging();
        services.Replace(ServiceDescriptor.Singleton(logging));
        return services.BuildServiceProvider(validateScopes: false);
    }

    private Dictionary<long, SchemaVersionRow> ReadAppliedRows()
    {
        var table = StruoVersionTableMetaData.TableNameFor(options.TablePrefix);
        using var db = OpenClient();
        if (!db.DbMaintenance.IsAnyTable(table, false)) return [];
        return db.Queryable<SchemaVersionRow>().AS(table).ToList().ToDictionary(r => r.Version);
    }

    /// <summary>The lowest version still unrecorded after a failed run, a pointer for diagnosis rather than a
    /// guarantee of which migration threw. Never throws, so the original failure is always preserved.</summary>
    private string FirstUnrecordedVersion()
    {
        try
        {
            return GetStatus().FirstOrDefault(m => m.State == MigrationState.Pending)?.Version.ToString() ?? "(none)";
        }
        catch (Exception)
        {
            return "(unknown)";
        }
    }

    private static Exception Innermost(Exception ex)
    {
        while (ex.InnerException is not null) ex = ex.InnerException;
        return ex;
    }
}
