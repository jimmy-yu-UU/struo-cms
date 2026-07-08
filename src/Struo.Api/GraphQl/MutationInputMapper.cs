// src/Struo.Api/GraphQl/MutationInputMapper.cs
using System.Text.Json;

namespace Struo.Api.GraphQl;

/// <summary>
/// Converts the HotChocolate typed-input dictionary (keys = camelCase input field names, values =
/// CLR scalars produced by the scalar bindings) into the <see cref="JsonElement"/> that
/// <c>ItemService.CreateAsync</c>/<c>UpdateAsync</c> already accept. Uses the same web JSON options
/// as ItemService so number/date/string shapes match the REST write path. This mapper trusts the
/// dictionary it is given verbatim — it does NOT filter unsent fields itself. HotChocolate's coerced
/// argument dictionary actually backfills every optional input field the client didn't send with a
/// null entry, so the CALLER (both create's and update's resolvers in
/// <see cref="MutationResolvers"/>, via its <c>SentFieldsOnly</c> helper) is responsible for passing
/// in only the keys the client actually supplied before calling <see cref="ToJsonElement"/>.
/// </summary>
public static class MutationInputMapper
{
    private static readonly JsonSerializerOptions Opts = new(JsonSerializerDefaults.Web);

    public static JsonElement ToJsonElement(IReadOnlyDictionary<string, object?>? input)
        => JsonSerializer.SerializeToElement(
            input ?? new Dictionary<string, object?>(), Opts);
}
