namespace Struo.Domain.Auditing;

/// <summary>
/// Audit-field base for entity collections (no SugarColumn attributes, so Domain stays
/// package-free — §2). Carries the four audit fields only. Each concrete subclass declares its
/// own primary key explicitly, e.g. <c>[SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }</c>,
/// because SqlSugar requires an attributed PK (an unattributed inherited "Id" is not recognised).
/// The framework assigns the id at create time (<c>Guid.CreateVersion7()</c>). Audit actors are
/// user UUIDs (Phase 6), null until a real user system exists.
/// </summary>
public abstract class AuditableEntity : IAuditable
{
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
