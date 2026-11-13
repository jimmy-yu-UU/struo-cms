namespace Struo.Application.Localization;

/// <summary>
/// Invariants of the <c>language</c> collection, checked by <c>LanguageWriteInterceptor</c> inside the write
/// transaction after a create, update, revert or purge of a language row: codes unique (case-insensitive), at
/// least one enabled row, exactly one enabled row marked default. A duplicate code throws
/// <see cref="Struo.Domain.Query.ConflictException"/> (409); any other violation throws
/// <see cref="Struo.Domain.Query.QueryException"/> (400). Either way the transaction rolls back.
/// </summary>
public interface ILanguageCollectionRules
{
    Task EnsureInvariantsAsync(CancellationToken ct = default);
}
