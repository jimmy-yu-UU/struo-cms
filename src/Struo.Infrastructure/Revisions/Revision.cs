using SqlSugar;
using Struo.Infrastructure.Persistence;

namespace Struo.Infrastructure.Revisions;

/// <summary>
/// One immutable snapshot of a revisioned item's post-write state. An internal framework
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

    // Composite UNIQUE (collectionname, itemid, revisionnumber). CaptureAsync assigns the
    // per-item number as max()+1 inside ItemService's write transaction; this index is the backstop that
    // makes a concurrent lost-update race fail closed (unique violation -> the capture's transaction
    // rolls back with the item write) instead of silently duplicating a revision number. The three
    // columns share one group name so SqlSugar CodeFirst emits a single composite unique index
    // (ux_revisions_item_no) — the same mechanism the Identity entities use (see UserRole). The
    // matching physical DDL for live/existing databases lives in db/migrations/001-core-baseline.sql.
    [SugarColumn(UniqueGroupNameList = ["ux_revisions_item_no"])]
    public string CollectionName { get; set; } = "";
    [SugarColumn(UniqueGroupNameList = ["ux_revisions_item_no"])]
    public string ItemId { get; set; } = "";
    [SugarColumn(UniqueGroupNameList = ["ux_revisions_item_no"])]
    public long RevisionNumber { get; set; }
    public string Operation { get; set; } = "";

    // MUST be unbounded: SqlSugar's default varchar(255) overflows for a realistic snapshot (the same
    // content-column-widening problem the EntityService hook solves for [CmsField] content
    // interfaces). Declared explicitly rather than by convention, because Snapshot is a plain
    // framework column with no [CmsField] interface to key off of.
    [ColumnShape(ColumnShape.LongText)] public string Snapshot { get; set; } = "";

    public DateTime CreatedAt { get; set; }
    [SugarColumn(IsNullable = true)] public Guid? CreatedBy { get; set; }
}
