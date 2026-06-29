namespace Struo.Domain.Auditing;

/// <summary>
/// Base for entity collections. PK is a UUIDv7 <see cref="Guid"/> recognised by the "Id" naming
/// convention (no SqlSugar attribute, so Domain stays package-free — §2). The framework assigns
/// the id at create time (<c>Guid.CreateVersion7()</c>). Audit actors are user UUIDs (Phase 6),
/// null until a real user system exists.
/// </summary>
public abstract class AuditableEntity : IAuditable
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
