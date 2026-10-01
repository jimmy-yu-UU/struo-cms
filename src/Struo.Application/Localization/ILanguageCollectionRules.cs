namespace Struo.Application.Localization;

/// <summary>
/// Invariants of the <c>language</c> collection, checked by <c>ItemService</c> inside the write
/// transaction after a create, update or purge of a language row: codes unique (case-insensitive), at
/// least one enabled row, exactly one enabled row marked default. A violation throws
/// <see cref="Struo.Domain.Query.QueryException"/>, so the transaction rolls back and the API answers 400.
/// </summary>
public interface ILanguageCollectionRules
{
    Task EnsureInvariantsAsync(CancellationToken ct = default);
}
