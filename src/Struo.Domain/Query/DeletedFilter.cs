namespace Struo.Domain.Query;

/// <summary>How a read treats soft-deleted rows. Default is <see cref="Exclude"/>.</summary>
public enum DeletedFilter { Exclude, Only, With }
