namespace Struo.Domain.Auditing;

/// <summary>
/// Audit-field base for entity collections (no SugarColumn attributes, so Domain stays
/// package-free — §2). Carries the four audit fields plus an abstract <see cref="Id"/>.
/// Subclasses MUST <c>override</c> <see cref="Id"/> and put <c>[SugarColumn(IsPrimaryKey = true)]</c>
/// on the override — the abstract member makes a forgotten PK a compile error rather than a runtime
/// surprise, while keeping the SqlSugar attribute out of Domain (SqlSugar requires an attributed PK;
/// an unattributed inherited "Id" is not recognised). The framework assigns the id at create time
/// (<c>Guid.CreateVersion7()</c>). Audit actors are user UUIDs, null until a real user
/// system exists.
/// </summary>
public abstract class AuditableEntity : IAuditable
{
    public abstract Guid Id { get; set; }
    public virtual DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public virtual DateTime UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    /// <summary>
    /// Optimistic-concurrency token (D2). Incremented on every update; an update guards on the value
    /// the caller last read (<c>WHERE version = expected</c>) so a concurrent writer that already moved
    /// the row on causes a conflict (409) instead of a silent lost update. Plain column — no SugarColumn
    /// attribute, so Domain stays package-free (§2).
    /// </summary>
    public long Version { get; set; }
}
