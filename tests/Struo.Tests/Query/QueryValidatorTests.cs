// tests/Struo.Tests/Query/QueryValidatorTests.cs
using AwesomeAssertions;
using Struo.Application.Configuration;
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

    [Fact]
    public void Unknown_filter_field_throws()
    {
        var q = new QueryModel(null, new ComparisonFilter("nope", QueryOperator.Eq, "x"), [], 0, 0, null);
        var act = () => QueryValidator.Validate(q, Meta(), Opts);
        act.Should().Throw<QueryException>().WithMessage("*nope*");
    }

    [Fact]
    public void Dotted_relation_path_throws()
    {
        var q = new QueryModel(null, new ComparisonFilter("category.name", QueryOperator.Eq, "x"), [], 0, 0, null);
        var act = () => QueryValidator.Validate(q, Meta(), Opts);
        act.Should().Throw<QueryException>().WithMessage("*relation*");
    }

    [Fact]
    public void Unknown_sort_or_field_throws()
    {
        var q1 = new QueryModel(null, null, [new SortField("ghost", false)], 0, 0, null);
        var a1 = () => QueryValidator.Validate(q1, Meta(), Opts);
        a1.Should().Throw<QueryException>();

        var q2 = new QueryModel(["ghost"], null, [], 0, 0, null);
        var a2 = () => QueryValidator.Validate(q2, Meta(), Opts);
        a2.Should().Throw<QueryException>();
    }

    [Fact]
    public void Limit_is_clamped_and_defaulted()
    {
        var zero = QueryValidator.Validate(new QueryModel(null, null, [], 0, 0, null), Meta(), Opts);
        zero.Limit.Should().Be(Opts.DefaultLimit);

        var over = QueryValidator.Validate(new QueryModel(null, null, [], 9999, 0, null), Meta(), Opts);
        over.Limit.Should().Be(Opts.MaxLimit);
    }

    [Fact]
    public void Too_many_conditions_throws()
    {
        var many = Enumerable.Range(0, Opts.MaxFilterConditions + 1)
            .Select(_ => (FilterNode)new ComparisonFilter("title", QueryOperator.Eq, "x")).ToList();
        var q = new QueryModel(null, new LogicalFilter(LogicalOperator.And, many), [], 0, 0, null);
        var act = () => QueryValidator.Validate(q, Meta(), Opts);
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
        var result = QueryValidator.Validate(new QueryModel(null, null, [], 0, -5, null), Meta(), Opts);
        result.Offset.Should().Be(0);
    }

    [Fact]
    public void Nested_conditions_exceeding_cap_throw()
    {
        // Build groups of ComparisonFilters, each wrapped in a LogicalFilter(And, [...]),
        // summing to MaxFilterConditions + 1 leaves total across nested levels.
        var groupSize = Opts.MaxFilterConditions / 2 + 1;
        var group1 = new LogicalFilter(LogicalOperator.And,
            Enumerable.Range(0, groupSize)
                .Select(_ => (FilterNode)new ComparisonFilter("title", QueryOperator.Eq, "x"))
                .ToList());
        var group2 = new LogicalFilter(LogicalOperator.And,
            Enumerable.Range(0, groupSize)
                .Select(_ => (FilterNode)new ComparisonFilter("title", QueryOperator.Eq, "x"))
                .ToList());
        var outer = new LogicalFilter(LogicalOperator.Or, [group1, group2]);
        var q = new QueryModel(null, outer, [], 0, 0, null);
        var act = () => QueryValidator.Validate(q, Meta(), Opts);
        act.Should().Throw<QueryException>().WithMessage("*conditions*");
    }
}
