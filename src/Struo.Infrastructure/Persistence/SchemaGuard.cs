using SqlSugar;

namespace Struo.Infrastructure.Persistence;

/// <summary>
/// Dev-startup fail-fast (DB-5). Asserts that the small set of database constraints the application
/// relies on for CORRECTNESS — not merely performance — physically exist in the connected database, and
/// throws with an actionable message if one is missing, rather than letting the app run with a silent
/// gap.
///
/// The one critical constraint today is the <c>revisions</c> composite UNIQUE index over
/// (collectionname, itemid, revisionnumber): the DB-4 (=CS-6) backstop that makes a lost-update race on
/// per-item revision numbers fail closed. On live PostgreSQL it is created by
/// <c>db/migrations/010-revisions-unique-number.sql</c> (name <c>ux_revisions_item_no</c>); on a
/// CodeFirst dev/test database it is created by <c>InitTables</c> from <see cref="Revisions.Revision"/>'s
/// <c>UniqueGroupNameList</c> (name <c>Index_revisions_…_Unique</c>). Because the NAME differs by backend
/// and by creation path, the guard detects the index by uniqueness + column coverage, never by a fixed
/// name.
///
/// Deliberately NOT a general schema-diff engine (YAGNI): only correctness-critical constraints belong
/// here. The hot-path performance indexes (009 / <c>[SugarIndex]</c>) are intentionally out of scope —
/// their absence degrades latency, it does not corrupt data. Called in Development startup only, after
/// InitTables + the migration runner (Program.cs).
/// </summary>
public static class SchemaGuard
{
    private static readonly string[] RequiredRevisionsColumns = ["collectionname", "itemid", "revisionnumber"];

    public static async Task AssertCriticalConstraintsAsync(ISqlSugarClient db, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var covered = db.CurrentConnectionConfig.DbType switch
        {
            DbType.PostgreSQL => await HasRevisionsUniqueAsync(db,
                "SELECT indexdef FROM pg_indexes WHERE tablename = 'revisions'"),
            // sqlite_master.sql holds the CREATE UNIQUE INDEX text for explicitly-created unique indexes
            // (the CodeFirst composite). Auto-indexes (PK / column UNIQUE) have NULL sql and are excluded
            // — correct, since the PK's unique index does not cover the three revision columns.
            DbType.Sqlite => await HasRevisionsUniqueAsync(db,
                "SELECT sql FROM sqlite_master WHERE type='index' AND tbl_name='revisions' AND sql IS NOT NULL"),
            // Other backends are experimental/unverified (CLAUDE.md §1); the guard cannot assert on them,
            // so it stays out of the way rather than block startup on a backend it does not cover.
            _ => true,
        };

        if (!covered)
        {
            throw new InvalidOperationException(
                "Critical schema constraint missing: the `revisions` table has no composite UNIQUE index " +
                "over (collectionname, itemid, revisionnumber). This is the DB-4 backstop that makes a " +
                "concurrent revision-number race fail closed. Apply " +
                "db/migrations/010-revisions-unique-number.sql (live PostgreSQL), or recreate the dev " +
                "schema so InitTables re-emits it from Revision's UniqueGroupNameList.");
        }
    }

    // Reading index metadata is the one place a raw catalog query is unavoidable: SqlSugar's ORM surface
    // does not expose "is there a UNIQUE index covering these columns". Scoped to a single read-only
    // catalog query per backend; no user input is interpolated.
    private static async Task<bool> HasRevisionsUniqueAsync(ISqlSugarClient db, string catalogQuery)
    {
        var defs = await db.Ado.SqlQueryAsync<string>(catalogQuery);
        return defs.Any(IsRevisionsUniqueCover);
    }

    private static bool IsRevisionsUniqueCover(string? indexDef)
    {
        if (string.IsNullOrEmpty(indexDef)) return false;
        var d = indexDef.ToLowerInvariant();
        return d.Contains("unique") && RequiredRevisionsColumns.All(d.Contains);
    }
}
