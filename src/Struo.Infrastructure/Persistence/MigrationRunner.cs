using Microsoft.Extensions.Logging;
using SqlSugar;

namespace Struo.Infrastructure.Persistence;

/// <summary>
/// Lightweight, forward-only SQL migration runner. Applies the reviewed <c>*.sql</c> files under a
/// configured directory, tracking applied filenames in a <c>schema_migrations</c> table so each file
/// runs at most once.
///
/// <para>
/// Runs on <b>any</b> configured backend. Creating tables is not its job — <see
/// cref="DatabaseInitializer.CreateMissingTables"/> does that in every environment — so scripts here
/// are ALTER-only by convention, and the template ships none.
/// </para>
///
/// <para>
/// Known limits, documented in <c>db/migrations/README.md</c>: tracking is by filename with no
/// checksum; there is no advisory lock (concurrent replicas may both attempt a pending file); and the
/// per-file transaction cannot roll back DDL on MySQL / Oracle, where DDL commits implicitly.
/// </para>
///
/// <para>
/// Executing raw SQL here (via <see cref="IAdo"/>) is the accepted migration-file pattern — the
/// documented exception to the "all DB access via SqlSugar ORM, zero vendor SQL" rule, which targets
/// application query/command code, not versioned schema scripts.
/// </para>
/// </summary>
public static class MigrationRunner
{
    internal const string TrackingTable = "schema_migrations";

    /// <summary>
    /// Pure helper: keep only <c>*.sql</c> entries and order them by ordinal filename so the numeric
    /// <c>NNN-</c> prefix drives apply order deterministically across platforms.
    /// </summary>
    internal static IReadOnlyList<string> OrderSqlFiles(IEnumerable<string> fileNames) =>
        fileNames
            .Where(n => n.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Pure helper: from an already-ordered filename list, drop those already recorded as applied,
    /// preserving order.
    /// </summary>
    internal static IReadOnlyList<string> SelectPending(
        IReadOnlyList<string> orderedFileNames, ISet<string> appliedFileNames) =>
        orderedFileNames.Where(n => !appliedFileNames.Contains(n)).ToList();

    /// <summary>
    /// Applies every pending migration file under <paramref name="migrationsDirectory"/> in ordinal
    /// filename order and returns the filenames actually applied during this run (empty when nothing
    /// was pending).
    ///
    /// Each file runs inside its own transaction together with the tracking-row insert; on the first
    /// failure that file's transaction is rolled back, the run is aborted, and the exception is
    /// rethrown so the host fails loudly. Files applied before the failure stay committed and recorded.
    /// Safe to re-run — already-recorded filenames are skipped.
    /// </summary>
    public static async Task<IReadOnlyList<string>> ApplyAsync(
        ISqlSugarClient db, string migrationsDirectory, ILogger? logger, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationsDirectory);

        if (!Directory.Exists(migrationsDirectory))
        {
            throw new DirectoryNotFoundException(
                $"MigrationRunner: migrations directory not found: '{migrationsDirectory}'. " +
                "Check the Database:MigrationsPath configuration value.");
        }

        EnsureTrackingTable(db);

        var applied = new HashSet<string>(
            await db.Queryable<SchemaMigration>().Select(m => m.Filename).ToListAsync(),
            StringComparer.Ordinal);

        var fileNames = Directory.EnumerateFiles(migrationsDirectory, "*.sql")
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .Select(n => n!);

        var pending = SelectPending(OrderSqlFiles(fileNames), applied);
        if (pending.Count == 0)
        {
            logger?.LogInformation(
                "MigrationRunner: schema up to date — {AppliedCount} migration(s) already applied, none pending.",
                applied.Count);
            return [];
        }

        var justApplied = new List<string>(pending.Count);
        foreach (var fileName in pending)
        {
            ct.ThrowIfCancellationRequested();

            var sql = await File.ReadAllTextAsync(
                Path.Combine(migrationsDirectory, fileName), ct);

            try
            {
                await db.Ado.BeginTranAsync();
                await db.Ado.ExecuteCommandAsync(sql);
                await db.Insertable(new SchemaMigration
                {
                    Filename = fileName,
                    AppliedAt = DateTime.UtcNow,
                }).ExecuteCommandAsync();
                await db.Ado.CommitTranAsync();
            }
            catch (Exception ex)
            {
                await db.Ado.RollbackTranAsync();
                logger?.LogError(ex,
                    "MigrationRunner: migration '{FileName}' failed and was rolled back. " +
                    "Aborting; {AppliedCount} migration(s) applied this run before the failure.",
                    fileName, justApplied.Count);
                throw new InvalidOperationException(
                    $"Migration '{fileName}' failed and was rolled back. See inner exception.", ex);
            }

            justApplied.Add(fileName);
            logger?.LogInformation("MigrationRunner: applied migration '{FileName}'.", fileName);
        }

        logger?.LogInformation(
            "MigrationRunner: applied {Count} migration(s) this run.", justApplied.Count);
        return justApplied;
    }

    // 僅在追蹤表不存在時建立，且走 CodeFirst——既有部署（PostgreSQL 上的 text/timestamptz 版本）
    // 因此完全不受影響，也不需要任何 migration。
    private static void EnsureTrackingTable(ISqlSugarClient db)
    {
        var exists = db.DbMaintenance.GetTableInfoList(false)
            .Any(t => t.Name.Equals(TrackingTable, StringComparison.OrdinalIgnoreCase));

        if (!exists) db.CodeFirst.InitTables(typeof(SchemaMigration));
    }
}
