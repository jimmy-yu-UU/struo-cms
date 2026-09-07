// tests/Struo.Tests/Query/QueryValidatorSearchCandidatesTests.cs
using AwesomeAssertions;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Application.Security;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.Query;

/// <summary>
/// Defence in depth (final-review item 1): <see cref="QueryValidator.Validate"/> must clear any
/// inbound <see cref="QueryModel.SearchCandidates"/> so <see cref="Struo.Application.Search.SearchCandidateResolver"/>
/// stays the ONLY writer of the one field FilterTranslator renders as SQL literals — no parser sets
/// it today, but a future JSON-bound request surface must not be able to smuggle one in.
/// Uses minimal fakes (same fixture style as <see cref="QueryValidatorTests"/>): the happy path this
/// exercises (null filter, empty sort, no fields/facets/aggregate) never calls the graph/metadata/
/// permission collaborators, so the fakes below need not implement anything beyond the interface shape.
/// </summary>
public class QueryValidatorSearchCandidatesTests
{
    private static CollectionMetadata Meta() => new()
    {
        Name = "article", Label = "Article", FieldGroups = [],
        Fields = [new FieldMetadata { Name = "title", Label = "Title", Interface = FieldInterface.Text }],
    };

    private sealed class UnusedGraph : IRelationshipGraph
    {
        public IReadOnlyList<RelationMetadata> Relations(string c) => [];
        public IReadOnlyList<(string, string)> InboundRestrict(string c) => [];
        public RelationMetadata? Resolve(string collection, string rel) => null;
    }

    private sealed class UnusedMetadata : IMetadataProvider
    {
        public IReadOnlyList<CollectionMetadata> GetCollections() => [];
        public CollectionMetadata? GetCollection(string name) => null;
    }

    private sealed class AllowAllPermissions : IPermissionService
    {
        public bool CanRead(string collection) => true;
        public bool CanWrite(string collection) => true;
        public bool CanDelete(string collection) => true;
        public IReadOnlyCollection<string> ReadableFields(string c, IEnumerable<string> all) => all.ToList();
    }

    private static readonly StruoQueryOptions Opts = new();
    private static readonly IRelationshipGraph Graph = new UnusedGraph();
    private static readonly IMetadataProvider Md = new UnusedMetadata();
    private static readonly IPermissionService Perms = new AllowAllPermissions();

    [Fact]
    public void Validate_clears_an_inbound_non_null_SearchCandidates_list()
    {
        var q = new QueryModel(null, null, [], 25, 0, null) { SearchCandidates = [Guid.NewGuid()] };
        var result = QueryValidator.Validate(q, Meta(), Opts, Graph, Md, Perms);
        result.SearchCandidates.Should().BeNull();
    }

    [Fact]
    public void Validate_leaves_a_null_SearchCandidates_null()
    {
        var q = new QueryModel(null, null, [], 25, 0, null);
        var result = QueryValidator.Validate(q, Meta(), Opts, Graph, Md, Perms);
        result.SearchCandidates.Should().BeNull();
    }
}
