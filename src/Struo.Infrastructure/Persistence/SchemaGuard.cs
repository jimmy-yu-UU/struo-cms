using SqlSugar;

namespace Struo.Infrastructure.Persistence;

/// <summary>
/// Dev-startup fail-fast. Asserts that the small set of database constraints the application
/// relies on for CORRECTNESS — not merely performance — physically exist in the connected database, and
/// throws with an actionable message if one is missing, rather than letting the app run with a silent
/// gap.
///
/// The critical constraints today are (a) the <c>revisions</c> composite UNIQUE index over
/// (collectionname, itemid, revisionnumber): the backstop that makes a lost-update race on
/// per-item revision numbers fail closed; and (b) a UNIQUE (fk, locale) index on each translation
/// sidecar the running configuration actually has, keeping per-locale overlay reads deterministic.
/// Sidecars are supplied by the CALLER as <see cref="TranslationSidecarDescriptor"/> values — Program.cs
/// derives one per collection from <c>IMetadataProvider.GetCollections()</c>'s <c>Translation</c>
/// metadata, resolving table/column names via <c>ISqlSugarClient.EntityMaintenance</c> (the same
/// resolution SqlSugar itself uses) — so this guard never hardcodes a collection or table name and a
/// fork's own sidecars are protected automatically, the same way core's <c>file_translations</c> is.
/// Indexes on core tables are created by CodeFirst (<c>InitTables</c>) from each entity's own
/// <c>SugarColumn.UniqueGroupNameList</c> — the <c>revisions</c> composite unique still comes from
/// <c>Revision</c> declaring it this way. Each translation sidecar's <c>(fk, locale)</c> unique is
/// different: it is derived, not declared — <c>SqlSugarClientFactory</c>'s <c>EntityService</c> hook
/// reads <c>[CmsTranslations]</c> metadata via a <see cref="TranslationSidecarIndexPolicy"/> (supplied
/// by <c>AddStruoInfrastructure</c>) and stamps the resolved group name onto the sidecar's fk/locale
/// columns' <c>EntityColumnInfo.UIndexGroupNameList</c> before <c>InitTables</c> reads it, so a fork's
/// own downstream sidecar gets the same treatment for tables it defines itself without declaring
/// anything on the entity — provided the client was built with the policy in place. An existing
/// database whose tables predate this guarantee needs a reviewed migration under
/// <c>db/migrations/</c> to add the missing index. Because the index NAME differs by backend and by
/// creation path, the guard detects each index by uniqueness + column coverage, never by a fixed name. A
/// sidecar table absent from the connected database is skipped rather than demanded (a fork may not use
/// every sidecar).
///
/// Deliberately NOT a general schema-diff engine (YAGNI): only correctness-critical constraints belong
/// here. The hot-path performance indexes (<c>[SugarIndex]</c>) are intentionally out of scope —
/// their absence degrades latency, it does not corrupt data. Called in Development startup only, after
/// InitTables + the migration runner (Program.cs).
/// </summary>
public static class SchemaGuard
{
    public static async Task AssertCriticalConstraintsAsync(
        ISqlSugarClient db,
        IReadOnlyList<TranslationSidecarDescriptor> translationSidecars,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var dbType = db.CurrentConnectionConfig.DbType;
        // Other backends (MySQL, SqlServer, Oracle) are type-mapped but unverified/experimental; the
        // guard cannot assert on them, so
        // it stays out of the way rather than block startup on a backend whose catalog it does not read.
        if (dbType is not (DbType.PostgreSQL or DbType.Sqlite)) return;

        // Backstop — the `revisions` composite UNIQUE (always present in the app schema; on a DB
        // that somehow lacks the table the index query returns empty and this fails, which is correct).
        await AssertUniqueCoverAsync(db, dbType, "revisions",
            ["collectionname", "itemid", "revisionnumber"], requireTableExists: true,
            "the `revisions` table has no composite UNIQUE index over " +
            "(collectionname, itemid, revisionnumber). This index is the backstop that makes a concurrent " +
            "revision-number race fail closed. If this is an existing database whose `revisions` table " +
            "predates this guarantee, add a reviewed migration under db/migrations/ to create the index; " +
            "otherwise recreate the dev schema so InitTables re-emits it from Revision's " +
            "UniqueGroupNameList.", ct);

        // Backstop — each caller-supplied translation sidecar's UNIQUE (fk, locale). Skipped when
        // the table is not present in this database (a fork may not use a given sidecar), rather than
        // demanding an index on a table that does not exist.
        foreach (var sidecar in translationSidecars)
        {
            await AssertUniqueCoverAsync(db, dbType, sidecar.TableName,
                [sidecar.ForeignKeyColumn, sidecar.LocaleColumn], requireTableExists: false,
                $"the `{sidecar.TableName}` table has no UNIQUE index over " +
                $"({sidecar.ForeignKeyColumn}, {sidecar.LocaleColumn}). This index is the backstop that " +
                "keeps per-locale overlay reads deterministic. If this is an existing database whose " +
                "table predates this guarantee (core sidecar or the fork's own), add a reviewed " +
                "migration under db/migrations/ to create the index; otherwise recreate the dev schema " +
                "with a client built through AddStruoInfrastructure (or pass " +
                "TranslationSidecarIndexPolicy.FromMetadata(...) to SqlSugarClientFactory.Create) so " +
                "InitTables emits the derived index.", ct);
        }
    }

    private static async Task AssertUniqueCoverAsync(
        ISqlSugarClient db, DbType dbType, string table, string[] requiredColumns,
        bool requireTableExists, string missingMessage, CancellationToken ct)
    {
        if (!requireTableExists && !await TableExistsAsync(db, dbType, table, ct)) return;

        if (!await HasUniqueCoverAsync(db, dbType, table, requiredColumns, ct))
            throw new InvalidOperationException("Critical schema constraint missing: " + missingMessage);
    }

    private static async Task<bool> TableExistsAsync(
        ISqlSugarClient db, DbType dbType, string table, CancellationToken ct)
    {
        // Table name comes from scanned entity metadata or a fixed literal, never request input;
        // scoped to one read-only catalog query.
        var query = dbType == DbType.PostgreSQL
            ? $"SELECT count(*) FROM pg_tables WHERE tablename = '{table}'"
            : $"SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = '{table}'";
        var counts = await db.Ado.SqlQueryAsync<int>(query, null, ct);
        return counts.FirstOrDefault() > 0;
    }

    // Reading index metadata is the one place a raw catalog query is unavoidable: SqlSugar's ORM surface
    // does not expose "is there a UNIQUE index covering these columns". Scoped to a single read-only
    // catalog query per table; the table name comes from scanned entity metadata or a fixed literal,
    // never from request input.
    private static async Task<bool> HasUniqueCoverAsync(
        ISqlSugarClient db, DbType dbType, string table, string[] requiredColumns, CancellationToken ct)
    {
        var query = dbType == DbType.PostgreSQL
            ? $"SELECT indexdef FROM pg_indexes WHERE tablename = '{table}'"
            // sqlite_master.sql holds the CREATE UNIQUE INDEX text for explicitly-created unique indexes
            // (the CodeFirst composite). Auto-indexes (PK / column UNIQUE) have NULL sql and are excluded
            // — correct, since a PK's unique index does not cover the composite columns asserted here.
            : $"SELECT sql FROM sqlite_master WHERE type='index' AND tbl_name='{table}' AND sql IS NOT NULL";
        var defs = await db.Ado.SqlQueryAsync<string>(query, null, ct);
        return defs.Any(indexDef => IsUniqueCover(indexDef, requiredColumns));
    }

    private static bool IsUniqueCover(string? indexDef, string[] requiredColumns)
    {
        if (string.IsNullOrEmpty(indexDef)) return false;
        var d = indexDef.ToLowerInvariant();
        // Required columns are compared case-insensitively: Postgres folds unquoted identifiers to
        // lowercase in the catalog, but a column name resolved via EntityMaintenance.GetDbColumnName
        // reflects the CLR property's declared case (e.g. "FileId"), which SQLite's sqlite_master.sql
        // preserves verbatim. Lowercasing both sides keeps the comparison correct on both backends.
        return d.Contains("unique") && requiredColumns.All(c => d.Contains(c.ToLowerInvariant()));
    }
}
