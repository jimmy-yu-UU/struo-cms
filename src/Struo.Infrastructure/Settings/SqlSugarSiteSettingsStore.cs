using System.Data.Common;
using SqlSugar;
using Struo.Application.Settings;

namespace Struo.Infrastructure.Settings;

public sealed class SqlSugarSiteSettingsStore(ISqlSugarClient db) : ISiteSettingsStore
{
    public async Task<SiteSettingsRecord?> GetAsync(CancellationToken ct = default)
    {
        var row = await db.Queryable<SiteSettings>()
            .Where(s => s.Id == SiteSettings.SingletonId).FirstAsync(ct);
        return row is null ? null : new SiteSettingsRecord(row.BrandName, row.LogoFileId);
    }

    /// <summary>
    /// Upserts the singleton settings row as UPDATE-first, INSERT-on-miss, retry-on-conflict: (1) try
    /// the UPDATE — if a row already exists this is the entire operation, and a single UPDATE statement
    /// is atomic on its own, no wrapping transaction needed; (2) if zero rows were affected, no row
    /// exists yet, so attempt the INSERT; (3) if a concurrent request won the same race and inserted
    /// first, this INSERT fails with 23505 — caught and retried as an UPDATE (the row now exists). The
    /// INSERT attempt runs inside its own transaction specifically so a caught 23505 can be cleanly
    /// rolled back before the fallback UPDATE runs: Postgres marks a transaction "aborted" after any
    /// statement error, and further statements in that same transaction fail with "current transaction
    /// is aborted" — rolling back first avoids that trap. <see cref="SiteSettings"/> has no
    /// version/rowver column, so a genuine concurrent *update* (not the first-insert race) is
    /// last-writer-wins.
    ///
    /// Considered and rejected: SqlSugar's <c>Storageable</c> upsert helper. Per SqlSugar's own docs
    /// (functional/simplified Storageable usage), it works by querying which primary keys already exist,
    /// splitting the batch into insert/update lists in memory, then executing
    /// <c>Db.Insertable(...).ExecuteCommand()</c> / <c>Db.Updateable(...).ExecuteCommand()</c> against
    /// those lists — a check-then-branch with no atomicity between the check and the act, so two
    /// concurrent first-saves could both observe no row and both attempt an insert, with the loser
    /// hitting an unmapped Postgres 23505 (unique_violation). Not a single atomic
    /// `INSERT ... ON CONFLICT DO UPDATE`, so it does not close the first-save race the sequence above
    /// handles.
    /// </summary>
    public async Task UpsertAsync(string brandName, Guid? logoFileId, Guid? updatedBy, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var updatedRows = await UpdateRowAsync(db, brandName, logoFileId, updatedBy, now, ct);
        if (updatedRows > 0) return;

        // No row existed as of the UPDATE above — attempt the insert. Scoped to its own transaction so a
        // caught duplicate-key failure can be rolled back cleanly (see method doc) — UNLESS the scoped
        // connection is already inside an ambient transaction (an outer caller's), in which case we join
        // it rather than opening — and committing/rolling back — a second one, which would end the outer
        // transaction early. Mirrors SqlSugarItemRepository.InTransactionAsync's identical nesting guard.
        // No caller nests this today; kept for consistency and to fail safe if one ever does.
        var ownsTransaction = db.Ado.Transaction is null;
        if (ownsTransaction) await db.Ado.BeginTranAsync();
        try
        {
            await db.Insertable(new SiteSettings
            {
                Id = SiteSettings.SingletonId, BrandName = brandName, LogoFileId = logoFileId,
                UpdatedAt = now, UpdatedBy = updatedBy
            }).ExecuteCommandAsync(ct);
            if (ownsTransaction) await db.Ado.CommitTranAsync();
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            if (ownsTransaction) await db.Ado.RollbackTranAsync();
            // Lost the insert race to a concurrent first save: the singleton row now exists, so the
            // update this method started with will succeed this time.
            await UpdateRowAsync(db, brandName, logoFileId, updatedBy, now, ct);
        }
        catch
        {
            // Any OTHER insert failure (not a duplicate-key race) must still roll back before
            // propagating — otherwise the scoped connection is left with an open (Postgres: aborted)
            // transaction and the next DB call on this request fails with an unrelated 25P02.
            if (ownsTransaction) await db.Ado.RollbackTranAsync();
            throw;
        }
    }

    private static Task<int> UpdateRowAsync(
        ISqlSugarClient db, string brandName, Guid? logoFileId, Guid? updatedBy, DateTime now, CancellationToken ct) =>
        // Entity-typed SetColumns so the nullable logofileid gets a typed NULL (Postgres 42804 fix).
        db.Updateable<SiteSettings>()
            .SetColumns(s => new SiteSettings
            {
                BrandName = brandName, LogoFileId = logoFileId, UpdatedAt = now, UpdatedBy = updatedBy
            })
            .Where(s => s.Id == SiteSettings.SingletonId)
            .ExecuteCommandAsync(ct);

    // Postgres error code for unique_violation (https://www.postgresql.org/docs/current/errcodes-appsummary.html).
    // Not sourced from a provider package on purpose — see IsUniqueViolation.
    private const string PostgresUniqueViolationSqlState = "23505";

    /// <summary>
    /// Unwraps the exception chain looking for a Postgres 23505 (unique_violation) via the
    /// provider-agnostic <see cref="DbException.SqlState"/> (.NET 8+, <c>System.Data.Common</c>) rather
    /// than a direct <c>Npgsql.PostgresException</c> type check. Deliberate: adding a direct
    /// <c>PackageReference</c> to Npgsql here would let NuGet resolve it independently of the version
    /// SqlSugarCore 5.1.4.216 itself depends on (Npgsql 5.0.18 transitively) and silently upgrade the
    /// whole app's Postgres driver across major versions wherever the resolver picks a newer floor — a
    /// binary-compatibility and behavior risk (e.g. Npgsql 6+ tightened <c>DateTime.Kind</c> handling for
    /// timestamp columns) this store has no business introducing. <c>Npgsql.PostgresException</c>
    /// overrides <see cref="DbException.SqlState"/>, so this check still recognizes it correctly without
    /// ever referencing the Npgsql assembly. SQLite (the test provider) never reaches this path under the
    /// test suite's single-connection, sequential execution — a real concurrent-insert race is PG-only
    /// and covered by a live-PG gate, not unit tests.
    /// </summary>
    private static bool IsUniqueViolation(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is DbException { SqlState: PostgresUniqueViolationSqlState }) return true;
        }
        return false;
    }
}
