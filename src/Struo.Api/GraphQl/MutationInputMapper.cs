// src/Struo.Api/GraphQl/MutationInputMapper.cs
using System.Text.Json;

namespace Struo.Api.GraphQl;

/// <summary>
/// Converts the HotChocolate typed-input dictionary (keys = camelCase input field names, values =
/// CLR scalars produced by the scalar bindings) into the <see cref="JsonElement"/> that
/// <c>ItemService.CreateAsync</c>/<c>UpdateAsync</c> already accept. Uses the same web JSON options
/// as ItemService so number/date/string shapes match the REST write path. Because the dictionary
/// contains ONLY the keys the client supplied, the resulting element carries exactly the sent fields
/// — which is what drives ItemService's partial merge-update.
/// </summary>
public static class MutationInputMapper
{
    private static readonly JsonSerializerOptions Opts = new(JsonSerializerDefaults.Web);

    public static JsonElement ToJsonElement(IReadOnlyDictionary<string, object?>? input)
        => JsonSerializer.SerializeToElement(
            input ?? new Dictionary<string, object?>(), Opts);
}
