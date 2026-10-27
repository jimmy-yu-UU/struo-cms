using FluentMigrator;
using FluentMigrator.Infrastructure;
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
        await using var gate = await AcquireLockAsync(logger, ct);
        RefuseUnrecordedSchema();
        var before = GetStatus();
        if (before.All(m => m.State != MigrationState.Pending)) return [];
        if (MigrationInfo.OutOfOrderWarning(before) is { } outOfOrder) logger.LogWarning("{Warning}", outOfOrder);
        try
        {
            using var sp = BuildRunner(preview: false, new SqlSuppressingLoggerFactory(loggerFactory));
            using var scope = sp.CreateScope();
            scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Each migration commits on its own, so the seed can be recorded before a later one fails.
            TryLogGeneratedPassword(before, logger);
            throw new MigrationFailedException($"Migration run failed; lowest unrecorded version: {FirstUnrecordedVersion()}. {Innermost(ex).Message}", ex);
        }
        var applied = AppliedSince(before);
        LogGeneratedPassword(applied, logger);
        return applied;
    }

    private List<MigrationInfo> AppliedSince(IReadOnlyList<MigrationInfo> before)
    {
        var pendingBefore = before.Where(m => m.State == MigrationState.Pending).Select(m => m.Version).ToHashSet();
        return GetStatus().Where(m => m.State == MigrationState.Applied && pendingBefore.Contains(m.Version)).ToList();
    }

    // Never throws, so the original failure is always preserved.
    private void TryLogGeneratedPassword(IReadOnlyList<MigrationInfo> before, ILogger logger)
    {
        try
        {
            LogGeneratedPassword(AppliedSince(before), logger);
        }
        catch (Exception)
        {
            // The status read failed too; the migration failure the caller rethrows is the useful one.
        }
    }

    // The seed migration inserts the admin only on the run that applies it, so this is the one place
    // the generated password is ever printed.
    private void LogGeneratedPassword(IReadOnlyList<MigrationInfo> applied, ILogger logger)
    {
        var seed = options.Seed;
        if (seed?.GeneratedPassword is null || applied.All(m => m.Version != Core.SeedCoreData.Version)) return;
        logger.LogWarning(
            "Bootstrap admin created: {Email} with the generated password {Password}. " +
            "It is shown only once; change it after the first login.",
            seed.AdminEmail, seed.GeneratedPassword);
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
            RecordAll(scope.ServiceProvider.GetRequiredService<IMigrationProcessor>(), loader, known, logger);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new MigrationFailedException($"Baseline failed: {Innermost(ex).Message}", ex);
        }
        return GetStatus().Where(m => m.State == MigrationState.Applied).ToList();
    }

    // The version loader writes through the scope's processor, so one transaction makes the rows all-or-nothing.
    private static void RecordAll(
        IMigrationProcessor processor, IVersionLoader loader, IDictionary<long, IMigrationInfo> known, ILogger logger)
    {
        processor.BeginTransaction();
        try
        {
            foreach (var (version, migration) in known.OrderBy(kv => kv.Key))
                loader.UpdateVersionInfo(version, migration.Description ?? "");
            processor.CommitTransaction();
        }
        catch
        {
            TryRollback(processor, logger);
            throw;
        }
    }

    // A failed rollback must not mask the failure that caused it.
    private static void TryRollback(IMigrationProcessor processor, ILogger logger)
    {
        try
        {
            processor.RollbackTransaction();
        }
        catch (Exception rollbackFailure)
        {
            logger.LogError(rollbackFailure, "Rolling back the baseline records failed.");
        }
    }

    /// <summary>A database with the core tables but no version table was built by an earlier schema source;
    /// running the migrations on it would fail on the existing tables, so it must be baselined first.</summary>
    private void RefuseUnrecordedSchema()
    {
        try
        {
            using var db = OpenClient();
            var versionTable = StruoVersionTableMetaData.TableNameFor(options.TablePrefix);
            if (!db.DbMaintenance.IsAnyTable(versionTable, false) && CoreTables.Exist(db, options.TablePrefix))
                throw new MigrationBaselineRefusedException(
                    "the database has the framework tables but no migration history; run the 'migrate:baseline' command once.");
        }
        catch (Exception ex) when (ex is not (MigrationBaselineRefusedException or OperationCanceledException))
        {
            throw new MigrationFailedException($"Could not inspect the database: {Innermost(ex).Message}", ex);
        }
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

    private SqlSugarClient OpenClient() => OpenClient(options.DbType, options.ConnectionString);

    // The version-table read maps SchemaVersionRow, so the reflection cache is scoped per provider
    // the same way SqlSugarClientFactory scopes it.
    internal static SqlSugarClient OpenClient(StruoDbType dbType, string connectionString)
    {
        var sugarDbType = DbTypeMapper.Map(dbType);
        return new SqlSugarClient(new ConnectionConfig
        {
            DbType = sugarDbType,
            ConnectionString = connectionString,
            IsAutoCloseConnection = true,
            ConfigureExternalServices = new ConfigureExternalServices
            {
                ReflectionInoCacheService = new ProviderScopedReflectionCache(sugarDbType),
            },
        });
    }

    private async Task<IAsyncDisposable> AcquireLockAsync(ILogger logger, CancellationToken ct)
    {
        if (options.DbType == StruoDbType.Oracle)
            logger.LogWarning(
                "Oracle takes no migration lock; running migrate concurrently is unsupported.");
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

        // A pending migration below the highest recorded version was merged after a newer one was applied.
        var highestRecorded = applied.Count == 0 ? long.MinValue : applied.Keys.Max();
        return known.Keys.Union(applied.Keys).Order()
            .Select(v => new MigrationInfo(
                v,
                known.TryGetValue(v, out var d) ? d : applied[v].Description ?? "",
                StateOf(known.ContainsKey(v), applied.ContainsKey(v)),
                applied.TryGetValue(v, out var row) ? row.AppliedOn : null,
                OutOfOrder: !applied.ContainsKey(v) && v < highestRecorded))
            .ToList();
    }

    private static MigrationState StateOf(bool known, bool applied)
    {
        if (!known) return MigrationState.Orphaned;
        return applied ? MigrationState.Applied : MigrationState.Pending;
    }

    /// <summary>Pending migrations and their SQL, written to <paramref name="sqlOut"/>. Executes nothing.
    /// The SQL includes the seed as given, so callers pass a redacted seed (the CLI does).</summary>
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
        options.ConfigureRunnerServices?.Invoke(services);
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
