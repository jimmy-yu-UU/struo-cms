// tests/Struo.Tests/Query/QueryValidatorTests.cs
using AwesomeAssertions;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.Query;

public class QueryValidatorTests
{
    private static CollectionMetadata Meta() => new()
    {
        Name = "article", Label = "Article",
        FieldGroups = [],
        Fields =
        [
            new FieldMetadata { Name = "title", Label = "Title", Interface = FieldInterface.Text, Searchable = true },
            new FieldMetadata { Name = "status", Label = "Status", Interface = FieldInterface.Select },
            new FieldMetadata { Name = "createdAt", Label = "CreatedAt", Interface = FieldInterface.DateTime, IsSystem = true, ReadOnly = true },
        ]
    };

    private static readonly StruoQueryOptions Opts = new();

    // Fakes that satisfy the article→category (M2O) and article→tags (M2M) graph.
    private sealed class FakeGraph : IRelationshipGraph
    {
        public IReadOnlyList<RelationMetadata> Relations(string c) => [];
        public IReadOnlyList<(string, string)> InboundRestrict(string c) => [];
        public RelationMetadata? Resolve(string collection, string rel) => (collection, rel) switch
        {
            ("article", "category") => new RelationMetadata { Name = "category", Label = "Category",
                Kind = RelationKind.ManyToOne, TargetCollection = "category", Interface = RelationInterface.Dropdown, ForeignKey = "categoryId" },
            ("article", "tags") => new RelationMetadata { Name = "tags", Label = "Tags",
                Kind = RelationKind.ManyToMany, TargetCollection = "tag", Interface = RelationInterface.TagSelect },
            _ => null
        };
    }

    private sealed class FakeMeta : IMetadataProvider
    {
        public IReadOnlyList<CollectionMetadata> GetCollections() => [];
        public CollectionMetadata? GetCollection(string name) => name switch
        {
            "category" => new CollectionMetadata { Name = "category", Label = "Category", FieldGroups = [],
                Fields = [new FieldMetadata { Name = "name", Label = "Name", Interface = FieldInterface.Text }] },
            "tag" => new CollectionMetadata { Name = "tag", Label = "Tag", FieldGroups = [],
                Fields = [new FieldMetadata { Name = "name", Label = "Name", Interface = FieldInterface.Text }] },
            _ => null
        };
    }

    private static readonly IRelationshipGraph Graph = new FakeGraph();
    private static readonly IMetadataProvider Md = new FakeMeta();

    [Fact]
    public void Unknown_filter_field_throws()
    {
        var q = new QueryModel(null, new ComparisonFilter("nope", QueryOperator.Eq, "x"), [], 0, 0, null);
        var act = () => QueryValidator.Validate(q, Meta(), Opts, Graph, Md);
        act.Should().Throw<QueryException>().WithMessage("*nope*");
    }

    [Fact]
    public void Dotted_relation_path_is_accepted_when_valid()
    {
        var q = new QueryModel(null, new ComparisonFilter("category.name", QueryOperator.Eq, "x"), [], 0, 0, null);
        var act = () => QueryValidator.Validate(q, Meta(), Opts, Graph, Md);
        act.Should().NotThrow();
    }

    [Fact]
    public void Sort_across_to_many_throws()
    {
        var q = new QueryModel(null, null, [new SortField("tags.name", false)], 0, 0, null);
        var act = () => QueryValidator.Validate(q, Meta(), Opts, Graph, Md);
        act.Should().Throw<QueryException>().WithMessage("*to-many*");
    }

    [Fact]
    public void Unknown_sort_or_field_throws()
    {
        var q1 = new QueryModel(null, null, [new SortField("ghost", false)], 0, 0, null);
        var a1 = () => QueryValidator.Validate(q1, Meta(), Opts, Graph, Md);
        a1.Should().Throw<QueryException>();

        var q2 = new QueryModel(["ghost"], null, [], 0, 0, null);
        var a2 = () => QueryValidator.Validate(q2, Meta(), Opts, Graph, Md);
        a2.Should().Throw<QueryException>();
    }

    [Fact]
    public void Limit_is_clamped_and_defaulted()
    {
        var zero = QueryValidator.Validate(new QueryModel(null, null, [], 0, 0, null), Meta(), Opts, Graph, Md);
        zero.Limit.Should().Be(Opts.DefaultLimit);

        var over = QueryValidator.Validate(new QueryModel(null, null, [], 9999, 0, null), Meta(), Opts, Graph, Md);
        over.Limit.Should().Be(Opts.MaxLimit);
    }

    [Fact]
    public void Too_many_conditions_throws()
    {
        var many = Enumerable.Range(0, Opts.MaxFilterConditions + 1)
            .Select(_ => (FilterNode)new ComparisonFilter("title", QueryOperator.Eq, "x")).ToList();
        var q = new QueryModel(null, new LogicalFilter(LogicalOperator.And, many), [], 0, 0, null);
        var act = () => QueryValidator.Validate(q, Meta(), Opts, Graph, Md);
        act.Should().Throw<QueryException>().WithMessage("*conditions*");
    }

    [Fact]
    public void SearchableFields_returns_only_searchable()
    {
        var fields = QueryValidator.SearchableFields(Meta());
        fields.Should().Contain("title");
        fields.Should().NotContain("status");
        fields.Should().NotContain("createdAt");
    }

    [Fact]
    public void Negative_offset_is_clamped_to_zero()
    {
        var result = QueryValidator.Validate(new QueryModel(null, null, [], 0, -5, null), Meta(), Opts, Graph, Md);
        result.Offset.Should().Be(0);
    }

    [Fact]
    public void Nested_conditions_exceeding_cap_throw()
    {
        var many = Enumerable.Range(0, Opts.MaxFilterConditions + 1)
            .Select(_ => (FilterNode)new ComparisonFilter("title", QueryOperator.Eq, "x"))
            .ToList();
        var q = new QueryModel(null, new LogicalFilter(LogicalOperator.And, many), [], 0, 0, null);
        var act = () => QueryValidator.Validate(q, Meta(), Opts, Graph, Md);
        act.Should().Throw<QueryException>().WithMessage("*conditions*");
    }

    [Fact]
    public void Nested_logical_groups_throw()
    {
        var inner = new LogicalFilter(LogicalOperator.Or,
            [new ComparisonFilter("title", QueryOperator.Eq, "x")]);
        var outer = new LogicalFilter(LogicalOperator.And, [inner]);
        var q = new QueryModel(null, outer, [], 0, 0, null);
        var act = () => QueryValidator.Validate(q, Meta(), Opts, Graph, Md);
        act.Should().Throw<QueryException>().WithMessage("*Nested*");
    }

    [Fact]
    public void Relation_path_in_fields_throws()
    {
        var q = new QueryModel(["category.name"], null, [], 0, 0, null);
        var act = () => QueryValidator.Validate(q, Meta(), Opts, Graph, Md);
        act.Should().Throw<QueryException>().WithMessage("*field selection*");
    }
}
