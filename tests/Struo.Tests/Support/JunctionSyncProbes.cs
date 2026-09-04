using SqlSugar;

namespace Struo.Tests.Support;

/// <summary>
/// Junction-table probe for the diff-and-patch M2M sync (<c>SyncManyToManyAsync</c> /
/// <c>ManyToManySync</c>) on real PostgreSQL. Mirrors ManyToManySyncTests.MmsLink (SQLite) — same
/// shape, including the renamed <see cref="Alias"/> column — but lives here so
/// PostgresIntegrationTests does not depend on another test class's nested type. Named distinctly
/// from any other probe table in this suite.
/// </summary>
[SugarTable("junction_sync_probe")]
public sealed class JunctionSyncProbe
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    public Guid ParentId { get; set; }
    public Guid ChildId { get; set; }
    [SugarColumn(IsNullable = true)] public string? Note { get; set; }
    [SugarColumn(IsNullable = true)] public int? Weight { get; set; }
    // Renamed column: pins that a payload write reaches the aliased column, not the CLR name.
    [SugarColumn(IsNullable = true, ColumnName = "note_text")] public string? Alias { get; set; }
    public int Sort { get; set; }
}
