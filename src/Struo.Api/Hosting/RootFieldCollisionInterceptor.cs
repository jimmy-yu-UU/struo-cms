// src/Struo.Api/Hosting/RootFieldCollisionInterceptor.cs
using HotChocolate.Configuration;
using HotChocolate.Types.Descriptors.Configurations;
using Struo.Domain.Metadata;

namespace Struo.Api.Hosting;

/// <summary>
/// Records the fields each registration contributes to the <c>Query</c> and <c>Mutation</c> roots before
/// HotChocolate merges the type extensions, which would otherwise fold two fields of one name into one
/// without complaint, and fails the schema build when a name is contributed more than once. HotChocolate
/// creates one instance per schema build.
/// </summary>
internal sealed class RootFieldCollisionInterceptor : TypeInterceptor
{
    private readonly Dictionary<string, Dictionary<string, int>> _counts = new(StringComparer.Ordinal);

    public override void OnBeforeRegisterDependencies(
        ITypeDiscoveryContext discoveryContext, TypeSystemConfiguration configuration)
    {
        if (configuration is not ObjectTypeConfiguration { Name: "Query" or "Mutation" } root) return;

        if (!_counts.TryGetValue(root.Name, out var fields))
            _counts[root.Name] = fields = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var field in root.Fields)
            fields[field.Name] = fields.GetValueOrDefault(field.Name) + 1;
    }

    public override void OnTypesInitialized()
    {
        var problems = _counts
            .OrderBy(r => r.Key, StringComparer.Ordinal)
            .SelectMany(r => r.Value
                .Where(f => f.Value > 1)
                .OrderBy(f => f.Key, StringComparer.Ordinal)
                .Select(f => $"The {r.Key} field name '{f.Key}' is defined more than once."))
            .ToList();
        if (problems.Count == 0) return;

        throw new MetadataException(
            "GraphQL name collision: a field added through ConfigureGraphQl clashes with a root field the schema already has. "
            + string.Join(" ", problems)
            + " Rename it so the name is unique.");
    }
}
