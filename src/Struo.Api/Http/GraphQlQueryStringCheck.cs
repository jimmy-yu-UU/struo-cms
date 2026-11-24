using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Struo.Api.Http;

/// <summary>
/// Refuses a GET to <c>/graphql</c> whose <c>variables</c> or <c>extensions</c> query-string value GraphQL
/// cannot accept: a value that is not JSON (a value repeated in the query string is read as its values
/// joined by commas), JSON nested deeper than <see cref="MaxJsonDepth"/>, JSON of the wrong kind, or a
/// <c>variables</c> array holding anything but objects and nulls. An empty value counts as absent.
/// </summary>
internal static class GraphQlQueryStringCheck
{
    private const int MaxJsonDepth = 64;
    private const string InvalidJson = "invalid JSON";

    private static readonly JsonDocumentOptions ParseOptions = new() { MaxDepth = MaxJsonDepth };

    private static readonly JsonValueKind[] ElementKinds = [JsonValueKind.Object, JsonValueKind.Null];

    private sealed record Rule(string Name, string Expected, JsonValueKind[] Allowed, bool CheckElements = false);

    private static readonly Rule[] Rules =
    [
        new("variables", "object, array, or null",
            [JsonValueKind.Object, JsonValueKind.Array, JsonValueKind.Null], CheckElements: true),
        new("extensions", "object", [JsonValueKind.Object]),
    ];

    /// <summary>The message of the first value of <paramref name="request"/> that is refused, or null.</summary>
    internal static string? Problem(HttpRequest request) =>
        HttpMethods.IsGet(request.Method)
            ? Rules
                .Select(rule => ProblemOf(rule, request.Query[rule.Name].ToString()))
                .FirstOrDefault(problem => problem is not null)
            : null;

    private static string? ProblemOf(Rule rule, string value)
    {
        if (value.Length == 0) return null;
        using var document = Parse(value);
        if (document is null) return KindMessage(rule, InvalidJson);

        var root = document.RootElement;
        if (!rule.Allowed.Contains(root.ValueKind)) return KindMessage(rule, KindName(root.ValueKind));
        return rule.CheckElements && root.ValueKind == JsonValueKind.Array ? ElementProblem(rule, root) : null;
    }

    private static string? ElementProblem(Rule rule, JsonElement array)
    {
        var offending = array.EnumerateArray()
            .Select(element => element.ValueKind)
            .Where(kind => !ElementKinds.Contains(kind))
            .Select(kind => (JsonValueKind?)kind)
            .FirstOrDefault();
        return offending is { } found
            ? $"Invalid '{rule.Name}' value. Expected array elements to be object or null, but got {KindName(found)}."
            : null;
    }

    private static string KindMessage(Rule rule, string found) =>
        $"Invalid '{rule.Name}' value. Expected {rule.Expected}, but got {found}.";

    private static JsonDocument? Parse(string value)
    {
        try
        {
            return JsonDocument.Parse(value, ParseOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string KindName(JsonValueKind kind) =>
        kind is JsonValueKind.True or JsonValueKind.False ? "Boolean" : kind.ToString();
}
