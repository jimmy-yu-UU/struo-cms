// src/Struo.Api/Http/VersionQuery.cs
using System.Globalization;
using Struo.Domain.Query;

namespace Struo.Api.Http;

internal static class VersionQuery
{
    // The optimistic-concurrency token of a request, bound as a string array so that absent (no element) is
    // distinguishable from empty (one empty element) and from repeated: absent means last-write-wins, anything present that is
    // not a single integer string (empty, "abc", "1.5", "1e2", out of range, repeated) is INVALID_VERSION,
    // the same code and the same accepted forms as an update body's `version`.
    public static long? Parse(string[]? values)
    {
        if (values is null or { Length: 0 }) return null;
        if (values.Length != 1)
            throw new InvalidVersionException("version must be supplied at most once.");
        return long.TryParse(values[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)
            ? n
            : throw new InvalidVersionException("version must be an integer.");
    }
}
