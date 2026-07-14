namespace Struo.Domain.Query;

/// <summary>How a read treats soft-deleted rows (Phase 9b). Default is <see cref="Exclude"/>.</summary>
public enum DeletedFilter { Exclude, Only, With }
