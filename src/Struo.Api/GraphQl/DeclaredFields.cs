// src/Struo.Api/GraphQl/DeclaredFields.cs
using HotChocolate.Types.Descriptors.Configurations;
using Struo.Domain.Metadata;

namespace Struo.Api.GraphQl;

/// <summary>
/// The fields declared on the GraphQL types of one collection, with the declaration each came from. The schema
/// builder adds every field through <see cref="Add(ObjectTypeConfiguration, ObjectFieldConfiguration, string?)"/>,
/// and <see cref="Inspect(ObjectTypeConfiguration)"/> reads the finished type, so a field added by any other
/// route is still compared: only its origin text is then the generic one. <see cref="Require"/> stops the host
/// with a <see cref="MetadataException"/> that names the collection, the type, the field name and both
/// declarations, where the GraphQL library would report only the type and the name. Names compare as the
/// GraphQL library compares them, case-sensitively.
/// </summary>
internal sealed class DeclaredFields
{
    private readonly Dictionary<object, string> _origins = new(ReferenceEqualityComparer.Instance);
    private readonly List<string> _problems = [];

    /// <summary>Adds <paramref name="field"/> to <paramref name="type"/>; <paramref name="origin"/> says which declaration it stands for.</summary>
    internal void Add(ObjectTypeConfiguration type, ObjectFieldConfiguration field, string? origin = null)
    {
        Record(field, origin);
        type.Fields.Add(field);
    }

    /// <summary>Adds <paramref name="field"/> to <paramref name="type"/>; <paramref name="origin"/> says which declaration it stands for.</summary>
    internal void Add(InputObjectTypeConfiguration type, InputFieldConfiguration field, string? origin = null)
    {
        Record(field, origin);
        type.Fields.Add(field);
    }

    /// <summary>Records the fields of <paramref name="type"/> that share a name.</summary>
    internal void Inspect(ObjectTypeConfiguration type) =>
        Inspect(type.Name, type.Fields.Select(f => (f.Name, Origin: OriginOf(f, f.Name))));

    /// <summary>Records the fields of <paramref name="type"/> that share a name.</summary>
    internal void Inspect(InputObjectTypeConfiguration type) =>
        Inspect(type.Name, type.Fields.Select(f => (f.Name, Origin: OriginOf(f, f.Name))));

    /// <summary>Stops the host when a type of <paramref name="collection"/> declares a field name twice.</summary>
    internal void Require(string collection)
    {
        if (_problems.Count == 0) return;

        throw new MetadataException(
            $"GraphQL field name collision in collection '{collection}': " + string.Join(" ", _problems) +
            " Rename one of the declarations: a GraphQL field is named after its property in camelCase, so the " +
            "schema cannot hold both.");
    }

    internal void Clear() => _problems.Clear();

    private void Record(object field, string? origin)
    {
        if (origin is not null) _origins[field] = origin;
    }

    // A field without a recorded origin is one the CMS adds itself, such as id, version or an operator.
    private string OriginOf(object field, string name) =>
        _origins.GetValueOrDefault(field) ?? $"the '{name}' field the CMS adds";

    private void Inspect(string typeName, IEnumerable<(string Name, string Origin)> fields)
    {
        foreach (var group in fields.GroupBy(f => f.Name, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            var origins = group.Select(f => f.Origin).ToList();
            _problems.Add(
                $"The type '{typeName}' declares the field '{group.Key}' more than once: {Join(origins)}.");
        }
    }

    private static string Join(IReadOnlyList<string> origins) =>
        origins.Count == 2 ? $"{origins[0]} and {origins[1]}" : $"{string.Join(", ", origins.Take(origins.Count - 1))} and {origins[^1]}";
}
