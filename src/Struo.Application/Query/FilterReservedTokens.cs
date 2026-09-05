// src/Struo.Application/Query/FilterReservedTokens.cs
using Struo.Domain.Query;

namespace Struo.Application.Query;

public static class FilterReservedTokens
{
    public const string And = "_and";
    public const string Or = "_or";
    public const string Some = "_some";
    public const string None = "_none";
    public const string Junction = "_junction";

    /// <summary>REST and GraphQL spellings; a field or relation may not be named any of these.</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { And, Or, Some, None, Junction, "and", "or", "some", "none", "junction" };

    public static bool TryQuantifier(string segment, out RelationQuantifier quantifier)
    {
        switch (segment)
        {
            case Some: quantifier = RelationQuantifier.Some; return true;
            case None: quantifier = RelationQuantifier.None; return true;
            default: quantifier = default; return false;
        }
    }
}
