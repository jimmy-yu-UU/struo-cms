using SqlSugar;

namespace Struo.Infrastructure.Persistence;

/// <summary>
/// Resolves a dialect-neutral <see cref="ColumnShape"/> to the concrete column type literal for a
/// backend. This is the single place a vendor type name may appear in <c>src/</c>.
///
/// <para>
/// PostgreSQL is this repository's verified runtime target; SQLite is used for the test suite only
/// (see <c>AGENTS.md</c>). The other three mappings (MySQL, SqlServer, Oracle) are chosen to be
/// syntactically valid and semantically reasonable so that CodeFirst table creation succeeds — they
/// are not claimed to be verified against a live instance of those backends.
/// </para>
///
/// <para>
/// Two of the literals below are already parenthesised — <c>"nvarchar(max)"</c> (SqlServer,
/// <see cref="ColumnShape.LongText"/>) and <c>"datetime(6)"</c> (MySQL,
/// <see cref="ColumnShape.TimestampWithTimeZone"/>). Whether SqlSugar appends a further length
/// suffix to a <c>DbColumnInfo.DataType</c> that already contains parentheses (which would emit
/// malformed DDL such as <c>nvarchar(max)(4000)</c>) was checked by reading the upstream source at
/// <c>https://github.com/DotNetNext/SqlSugar</c> (the <c>donet5/SqlSugar</c> slug this package is
/// still sometimes referenced by is a 301 redirect to the same repository, confirmed by reading the
/// HTTP response), tag <c>5.1.4.197</c> — the closest published git tag to the version pinned in
/// <c>Directory.Packages.props</c>, which is not itself tagged upstream. The tag series stops at
/// <c>5.1.4.197</c>, with a dozen-plus untagged stable NuGet releases sitting strictly between it and
/// the pin (<c>.212</c>/<c>.213</c> only ever shipped as prerelease builds). The endpoints were
/// bounded, not every commit in between: the relevant methods below were byte-identical between the
/// <c>5.1.4.197</c> tag and <c>master</c> when diffed, so the divergence risk is low but not zero.
/// <b>This is a source-reading conclusion, not a result verified against a live SQL Server or MySQL
/// instance.</b>
/// </para>
/// <para>
/// Both <c>SqlServerDbMaintenance</c> and <c>MySqlDbMaintenance</c> resolve the length suffix
/// through a <c>GetSize(DbColumnInfo item)</c> method that reads only <c>item.Length</c> /
/// <c>item.DecimalDigits</c> — it never inspects <c>item.DataType</c> for existing parentheses.
/// The shared base implementation
/// (<c>Src/Asp.NetCore2/SqlSugar/Abstract/DbMaintenanceProvider/Methods.cs</c>, used as-is by
/// SqlServer) returns a <c>null</c> size whenever <c>Length == 0 &amp;&amp; DecimalDigits == 0</c>:
/// <c>"else if (item.Length &gt; 0 &amp;&amp; item.DecimalDigits == 0) { dataSize = ... }"</c> — none
/// of its branches match when both are zero, so <c>dataSize</c> stays <c>null</c>.
/// <c>MySqlDbMaintenance.GetSize</c> (<c>Src/Asp.NetCore2/SqlSugar/Realization/MySql/DbMaintenance/MySqlDbMaintenance.cs</c>)
/// has the same zero/zero fallthrough. A <c>null</c> <c>dataSize</c> is
/// substituted as an empty string by <c>string.Format</c>, so the composed column clause (e.g.
/// SqlServer's <c>CreateTableColumn = "{0} {1}{2} {3} {4} {5}"</c>, where <c>{1}</c> is
/// <c>DataType</c> and <c>{2}</c> is <c>dataSize</c>) renders as just <c>nvarchar(max)</c> with no
/// trailing suffix. This holds for both CREATE TABLE
/// (<c>CodeFirstProvider.NoExistLogic</c> → <c>EntityColumnToDbColumn</c> →
/// <c>DbMaintenance.CreateTable</c> → <c>GetCreateTableSql</c> → <c>GetSize</c>) and ALTER TABLE ADD
/// COLUMN (<c>CodeFirstProvider.ExistLogic</c> → the same <c>EntityColumnToDbColumn</c> →
/// <c>DbMaintenance.AddColumn</c> → <c>GetAddColumnSql</c> → <c>GetSize</c>) — both paths share the
/// identical helper, so there is no divergence between table creation and column addition here.
/// </para>
/// <para>
/// <c>item.Length</c> reaches <c>GetSize</c> as <c>0</c> because: (1) <c>DbColumnInfo.Length</c>
/// defaults to <c>0</c> (a plain C# <c>int</c>); (2) the <c>EntityService</c> hook in
/// <c>SqlSugarClientFactory.cs</c> sets only <c>column.DataType</c> for a
/// <see cref="ColumnShapeAttribute"/>-marked property and never touches <c>column.Length</c>; (3)
/// <c>CodeFirstProvider.Execute</c>'s own default-length injection
/// (<c>"if (item.PropertyInfo.PropertyType == UtilConstants.StringType &amp;&amp;
/// item.DataType.IsNullOrEmpty() &amp;&amp; item.Length == 0) { item.Length = DefultLength; }"</c>)
/// is guarded on <c>DataType.IsNullOrEmpty()</c>, which is false once <c>ColumnTypeMap.For</c> has
/// already assigned a literal — the <c>EntityService</c> hook that assigns it runs earlier in the
/// same pipeline, at
/// <c>Src/Asp.NetCore2/SqlSugar/Abstract/EntityMaintenance/EntityMaintenance.cs:424-430</c>. That
/// call site is lexically inside the private <c>SetColumns</c> method (<c>:307</c>), which
/// <c>GetEntityInfoNoCache</c> (<c>:58</c>) calls at <c>:89</c> while building the <c>EntityInfo</c>
/// that <c>CodeFirstProvider.Execute</c> subsequently injects the default length into — so the hook's
/// assignment is already in place by the time the guard is checked; and (4)
/// <c>EntityColumnToDbColumn</c> copies <c>Length</c> straight through
/// (<c>"Length = item.Length,"</c>) without ever re-deriving it from the <c>DataType</c> string. Net
/// result: for these two shaped, pre-parenthesised mappings, length stays unset and no suffix is
/// appended on either backend.
/// </para>
/// <para>
/// A third length-touching mechanism exists and is also a no-op for these two literals: both
/// providers run a <c>ConvertCreateColumnInfo(DbColumnInfo x)</c> pre-pass before <c>GetSize</c> — not
/// only in the CREATE path (SqlServer's <c>CreateTable</c> calls it per column at
/// <c>Realization/SqlServer/DbMaintenance/SqlServerDbMaintenance.cs:721</c>; MySQL's
/// <c>GetCreateTableSql</c> does the same at
/// <c>Realization/MySql/DbMaintenance/MySqlDbMaintenance.cs:537</c>) but also in the modify path
/// (SqlServer's <c>UpdateColumn</c> calls it at <c>SqlServerDbMaintenance.cs:498</c>; MySQL's
/// <c>UpdateColumn</c> at <c>MySqlDbMaintenance.cs:612</c>) — both providers' method is defined once,
/// at <c>SqlServerDbMaintenance.cs:756-771</c> and <c>MySqlDbMaintenance.cs:792-808</c> respectively,
/// and reused by every caller. SqlServer's version only rewrites <c>DataType</c> when it
/// case-insensitively equals <c>"nvarchar"</c> or <c>"varchar"</c> and <c>Length &lt; 1</c> —
/// <c>"nvarchar(max)".EqualCase("nvarchar")</c> is <c>false</c>, so the already-parenthesised
/// literal never matches that branch. MySQL's version checks the same two names plus a
/// <c>{"longtext", "date"}</c> array — <c>"datetime(6)"</c> matches neither, so it also passes
/// through untouched. Both are no-ops here for the same underlying reason <c>GetSize</c> is: the
/// condition that would trigger a rewrite never holds for a value that already contains its own
/// parentheses.
/// </para>
/// <para>
/// The <see cref="ColumnShape.LongText"/> conclusion above is not limited to properties carrying
/// <see cref="ColumnShapeAttribute"/>: a multi-value <c>[CmsField]</c> property (a JSON column)
/// resolves to the identical <c>ColumnTypeMap.For(ColumnShape.LongText, dbType)</c> call inside the
/// same <c>EntityService</c> hook's JSON-column branch, so <c>"nvarchar(max)"</c> reaches SQL Server
/// through this route too. The reasoning holds there for the same reason: that branch sets
/// <c>column.IsJson = true</c> and <c>column.DataType</c> only, never <c>column.Length</c>, and
/// <c>CodeFirstProvider.EntityColumnToDbColumn</c> does not copy <c>IsJson</c> into the resulting
/// <c>DbColumnInfo</c> at all — it is not one of the fields its object initializer sets — so nothing
/// about the JSON route changes the zero-length conclusion. A fork relying on JSON columns on SQL
/// Server is covered by the same no-op finding as the shaped properties above.
/// </para>
/// </summary>
internal static class ColumnTypeMap
{
    public static string For(ColumnShape shape, DbType dbType) => shape switch
    {
        ColumnShape.LongText => dbType switch
        {
            DbType.PostgreSQL or DbType.Sqlite => "text",
            // MySQL's TEXT caps at 65,535 bytes — not unbounded, unlike every other mapping here.
            // A full item Revision.Snapshot or a realistic RichText/Markdown/Json body can exceed
            // that (error 1406 in strict mode, silent truncation otherwise). LONGTEXT (up to 4 GiB)
            // is the conventional MySQL choice for unbounded text.
            DbType.MySql => "longtext",
            DbType.SqlServer => "nvarchar(max)",
            DbType.Oracle => "clob",
            _ => throw new ArgumentOutOfRangeException(nameof(dbType), dbType, "Unmapped DbType for ColumnShape.LongText."),
        },
        ColumnShape.TimestampWithTimeZone => dbType switch
        {
            DbType.PostgreSQL => "timestamptz",
            // MySQL's TIMESTAMP is range-limited (1970-2038); DATETIME(6) storing UTC is the
            // conventional choice for an instant.
            DbType.MySql => "datetime(6)",
            DbType.SqlServer => "datetimeoffset",
            DbType.Oracle => "timestamp with time zone",
            // SQLite does not validate declared type names (it derives a storage affinity from
            // substring matches), so this literal is accepted as-is — preserving exactly the
            // column type the existing test suite has always created.
            DbType.Sqlite => "timestamptz",
            _ => throw new ArgumentOutOfRangeException(nameof(dbType), dbType, "Unmapped DbType for ColumnShape.TimestampWithTimeZone."),
        },
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unmapped ColumnShape."),
    };
}
