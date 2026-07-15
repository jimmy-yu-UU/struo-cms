using SqlSugar;

namespace Struo.Infrastructure.Revisions;

/// <summary>
/// One immutable snapshot of a revisioned item's post-write state (Phase 9c). An internal framework
/// table — NOT a <c>[CmsCollection]</c>, so it is never browsable/CRUD-able through the generic item
/// API. Append-only: rows are inserted on create/update/revert and never updated. Not
/// <see cref="Struo.Domain.Auditing.IAuditable"/> (no update path → no UpdatedAt/By); CreatedAt/By are
/// stamped explicitly at capture. Not <see cref="Struo.Domain.Auditing.ISoftDeletable"/>, so the global
/// soft-delete filter never touches it.
/// </summary>
[SugarTable("revisions")]
public sealed class Revision
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

    public string CollectionName { get; set; } = "";
    public string ItemId { get; set; } = "";
    public long RevisionNumber { get; set; }
    public string Operation { get; set; } = "";

    // MUST be `text`: SqlSugar's default varchar(255) overflows on Postgres for a realistic snapshot
    // (the recurring 7g/7g+ bug class). Explicit here rather than via a convention.
    [SugarColumn(ColumnDataType = "text")] public string Snapshot { get; set; } = "";

    public DateTime CreatedAt { get; set; }
    [SugarColumn(IsNullable = true)] public Guid? CreatedBy { get; set; }
}
