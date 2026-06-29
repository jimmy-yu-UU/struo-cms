namespace Struo.Domain.Auditing;

/// <summary>
/// Audit-field base for entity collections (no SugarColumn attributes, so Domain stays
/// package-free — §2). Carries the four audit fields plus an abstract <see cref="Id"/>.
/// Subclasses MUST <c>override</c> <see cref="Id"/> and put <c>[SugarColumn(IsPrimaryKey = true)]</c>
/// on the override — the abstract member makes a forgotten PK a compile error rather than a runtime
/// surprise, while keeping the SqlSugar attribute out of Domain (SqlSugar requires an attributed PK;
/// an unattributed inherited "Id" is not recognised). The framework assigns the id at create time
/// (<c>Guid.CreateVersion7()</c>). Audit actors are user UUIDs (Phase 6), null until a real user
/// system exists.
/// </summary>
public abstract class AuditableEntity : IAuditable
{
    public abstract Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
