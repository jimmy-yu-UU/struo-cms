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
[SugarIndex("ux_{table}_item_no", nameof(CollectionName), OrderByType.Asc, nameof(ItemId), OrderByType.Asc, nameof(RevisionNumber), OrderByType.Asc, true)]
public sealed class Revision
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

    // Composite UNIQUE (collectionname, itemid, revisionnumber). CaptureAsync assigns the
    // per-item number as max()+1 inside ItemService's write transaction; this index is the backstop that
    // makes a concurrent lost-update race fail closed (unique violation -> the capture's transaction
    // rolls back with the item write) instead of silently duplicating a revision number. Declared as the
    // class-level [SugarIndex("ux_{table}_item_no", ..., IsUnique)] above (the same declaration pattern
    // UserRole uses for its two-column key), so CodeFirst emits it as a single composite unique index
    // named after the resolved (prefixed) table. CodeFirst creates this index on any backend where the
    // table does not yet exist; it is never retrofitted onto an already-existing table.
    public string CollectionName { get; set; } = "";
    public string ItemId { get; set; } = "";
    public long RevisionNumber { get; set; }
    public string Operation { get; set; } = "";

    /// <summary>For an <c>Operation == "revert"</c> row, the revision number whose snapshot was
    /// re-applied. NULL on every other operation, and on revert rows written before this column
    /// existed — the history UI falls back to the plain operation label when it is null.</summary>
    [SugarColumn(IsNullable = true)] public long? SourceRevisionNumber { get; set; }

    // MUST be unbounded: SqlSugar's default varchar(255) overflows for a realistic snapshot (the same
    // content-column-widening problem the EntityService hook solves for [CmsField] content
    // interfaces). Declared explicitly rather than by convention, because Snapshot is a plain
    // framework column with no [CmsField] interface to key off of.
    [ColumnShape(ColumnShape.LongText)] public string Snapshot { get; set; } = "";

    public DateTime CreatedAt { get; set; }
    [SugarColumn(IsNullable = true)] public Guid? CreatedBy { get; set; }
}
