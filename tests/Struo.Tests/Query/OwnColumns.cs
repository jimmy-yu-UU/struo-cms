// tests/Struo.Tests/Query/OwnColumns.cs
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Domain.Query;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Query;

namespace Struo.Tests.Query;

/// <summary>Runs the production <see cref="FilterTranslator"/> over one entity whose fields are all own columns.</summary>
internal static class OwnColumns
{
    private const string Collection = "subject";

    public static List<IConditionalModel> Translate(
        ISqlSugarClient db, EntityDescriptor d, FilterNode? filter,
        string? search = null, IReadOnlyList<string>? searchableFields = null)
    {
        var registry = new EntityRegistry(new Dictionary<string, EntityDescriptor>(StringComparer.OrdinalIgnoreCase)
        {
            [Collection] = d
        });
        var graph = new RelationshipGraph([], new Dictionary<string, Type>());
        var metadata = new CachedMetadataProvider([]);
        return new FilterTranslator(db, graph, metadata, registry, new StruoQueryOptions())
            .Translate(Collection, filter, search, null, searchableFields ?? [], null, d);
    }
}
