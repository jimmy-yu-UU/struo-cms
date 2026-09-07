namespace Struo.Application.Search;

/// <summary>Either "not handled" (core runs its LIKE search) or a candidate id set (an empty set is a handled zero-hit search, never a fallback).</summary>
public sealed class SearchOutcome
{
    private SearchOutcome(IReadOnlyList<string>? ids) => Ids = ids;

    public static SearchOutcome NotHandled { get; } = new(null);

    public static SearchOutcome Candidates(IReadOnlyList<string> ids) =>
        new(ids ?? throw new ArgumentNullException(nameof(ids)));

    public bool Handled => Ids is not null;

    /// <summary>Candidate root ids as strings; non-null exactly when <see cref="Handled"/>.</summary>
    public IReadOnlyList<string>? Ids { get; }
}
