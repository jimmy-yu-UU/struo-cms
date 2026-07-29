namespace Struo.Domain.Auditing;

/// <summary>
/// Opt-in marker for soft-deletable collections. An entity that implements this
/// interface is soft-deleted (its <see cref="DeletedAt"/> is stamped) instead of being removed;
/// reads exclude it by default. Mirrors <see cref="IAuditable"/>: package-free, no SqlSugar
/// attributes (§2). A null <see cref="DeletedAt"/> means the row is live. The framework assigns
/// <see cref="DeletedAt"/>/<see cref="DeletedBy"/> at delete time.
/// </summary>
public interface ISoftDeletable
{
    DateTime? DeletedAt { get; set; }
    Guid? DeletedBy { get; set; }
}
