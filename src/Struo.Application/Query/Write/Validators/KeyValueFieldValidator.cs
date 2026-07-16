// src/Struo.Application/Query/Write/Validators/KeyValueFieldValidator.cs
using System.Reflection;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// KeyValue fields (Dictionary&lt;string,string&gt;) live on the parent entity. Reject blank keys and
/// enforce Required as a non-empty map. Values may be empty; duplicate keys are impossible
/// (System.Text.Json last-wins bind). Non-translatable only.
/// </summary>
public sealed class KeyValueFieldValidator : IFieldValidator
{
    public void ValidateAndNormalize(FieldMetadata field, PropertyInfo pi, object entity)
    {
        var map = pi.GetValue(entity) as IDictionary<string, string>;
        if (map is not null)
        {
            foreach (var key in map.Keys)
                if (string.IsNullOrWhiteSpace(key))
                    throw new QueryException($"Field '{field.Name}' has an entry with an empty key.");
        }
        if (field.Required && (map is null || map.Count == 0))
            throw new QueryException($"Field '{field.Name}' is required.");
    }
}
