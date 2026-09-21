// tests/Struo.Tests/Query/QueryValidatorTests.cs
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
            // A hidden credential-style field (mirrors User.Password / User.AccessToken). It must never
            // be usable as a filter/sort/search target — otherwise meta.total becomes a blind-extraction oracle.
            new FieldMetadata { Name = "secret", Label = "Secret", Interface = FieldInterface.Text, Hidden = true, Searchable = true },
            new FieldMetadata { Name = "regions", Label = "Regions", Interface = FieldInterface.MultiSelect },
        ],
        // A OneToMany relation sharing its FK column name ("categoryId") with the ManyToOne "category"
        // relation below — mirrors the sample Category collection (M2O "parent" FK "parentId" / O2M
        // "children" whose ForeignKey is also "parentId"). Listed FIRST so a permission-target lookup
        // that naively does `Relations.FirstOrDefault(r => r.ForeignKey == head)` without a Kind guard
        // would pick this one — the wrong collection — instead of the ManyToOne "category" relation
        // the FK column actually belongs to on this root.
        Relations =
        [
            new RelationMetadata { Name = "categoryArchive", Label = "Category archive", Kind = RelationKind.OneToMany,
                TargetCollection = "categoryArchive", Interface = RelationInterface.RelatedList, ForeignKey = "categoryId" },
            new RelationMetadata { Name = "category", Label = "Category", Kind = RelationKind.ManyToOne,
                TargetCollection = "category", Interface = RelationInterface.Dropdown, ForeignKey = "categoryId" },
        ],
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
                Kind = RelationKind.ManyToMany, TargetCollection = "tag", Interface = RelationInterface.TagSelect,
                JunctionCollection = "articleTag" },
            ("category", "articles") => new RelationMetadata { Name = "articles", Label = "Articles",
                Kind = RelationKind.OneToMany, TargetCollection = "article", Interface = RelationInterface.Dropdown,
                ForeignKey = "categoryId" },
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
            "categoryArchive" => new CollectionMetadata { Name = "categoryArchive", Label = "Category archive", FieldGroups = [],
                Fields = [new FieldMetadata { Name = "name", Label = "Name", Interface = FieldInterface.Text }] },
            "tag" => new CollectionMetadata { Name = "tag", Label = "Tag", FieldGroups = [],
                Fields = [new FieldMetadata { Name = "name", Label = "Name", Interface = FieldInterface.Text }] },
            "articleTag" => new CollectionMetadata { Name = "articleTag", Label = "Article tag", FieldGroups = [],
                Fields =
                [
                    new FieldMetadata { Name = "note", Label = "Note", Interface = FieldInterface.Text },
                    new FieldMetadata { Name = "secret", Label = "Secret", Interface = FieldInterface.Text, Hidden = true },
                ] },
            "article" => Meta(),
            _ => null
        };
    }

    private static readonly IRelationshipGraph Graph = new FakeGraph();
    private static readonly IMetadataProvider Md = new FakeMeta();

    // Most cases here pin shape validation, not RBAC, so they run with a caller who can read
    // everything. The relation-permission gate has its own cases at the end of the file.
    private sealed class AllowAllPermissions : IPermissionService
    {
        public bool CanRead(string collection) => true;
        public bool CanWrite(string collection) => true;
        public bool CanDelete(string collection) => true;
        public IReadOnlyCollection<string> ReadableFields(string c, IEnumerable<string> all) => all.ToList();
    }

    private sealed class DenyReadOf(string denied) : IPermissionService
    {
        public bool CanRead(string collection) =>
            !string.Equals(collection, denied, StringComparison.OrdinalIgnoreCase);
        public bool CanWrite(string collection) => true;
        public bool CanDelete(string collection) => true;
        public IReadOnlyCollection<string> ReadableFields(string c, IEnumerable<string> all) => all.ToList();
    }

    private static readonly IPermissionService Perms = new AllowAllPermissions();

    [Fact]
    public void Unknown_filter_field_throws()
    {
        var q = new QueryModel(null, new ComparisonFilter("nope", QueryOperator.Eq, "x"), [], 0, 0, null);
        var act = () => QueryValidator.Validate(q, Meta(), Opts, Graph, Md, Perms);
        act.Should().Throw<QueryException>().WithMessage("*nope*");
    }

    [Fact]
    public void Dotted_relation_path_is_accepted_when_valid()
    {
        var q = new QueryModel(null, new ComparisonFilter("category.name", QueryOperator.Eq, "x"), [], 0, 0, null);
        var act = () => QueryValidator.Validate(q, Meta(), Opts, Graph, Md, Perms);
        act.Should().NotThrow();
    }

    [Fact]
    public void Sort_across_to_many_throws()
    {
        var q = new QueryModel(null, null, [new SortField("tags.name", false)], 0, 0, null);
        var act = () => QueryValidator.Validate(q, Meta(), Opts, Graph, Md, Perms);
        act.Should().Throw<QueryException>().WithMessage("*to-many*");
    }

    [Fact]
    public void Unknown_sort_or_field_throws()
    {
        var q1 = new QueryModel(null, null, [new SortField("ghost", false)], 0, 0, null);
        var a1 = () => QueryValidator.Validate(q1, Meta(), Opts, Graph, Md, Perms);
        a1.Should().Throw<QueryException>();

        var q2 = new QueryModel(["ghost"], null, [], 0, 0, null);
        var a2 = () => QueryValidator.Validate(q2, Meta(), Opts, Graph, Md, Perms);
        a2.Should().Throw<QueryException>();
    }

    [Fact]
    public void Limit_is_clamped_and_defaulted()
    {
        var zero = QueryValidator.Validate(new QueryModel(null, null, [], 0, 0, null), Meta(), Opts, Graph, Md, Perms);
        zero.Limit.Should().Be(Opts.DefaultLimit);

        var over = QueryValidator.Validate(new QueryModel(null, null, [], 9999, 0, null), Meta(), Opts, Graph, Md, Perms);
        over.Limit.Should().Be(Opts.MaxLimit);
    }

    [Fact]
    public void Too_many_conditions_throws()
    {
        var many = Enumerable.Range(0, Opts.MaxFilterConditions + 1)
            .Select(_ => (FilterNode)new ComparisonFilter("title", QueryOperator.Eq, "x")).ToList();
        var q = new QueryModel(null, new LogicalFilter(LogicalOperator.And, many), [], 0, 0, null);
        var act = () => QueryValidator.Validate(q, Meta(), Opts, Graph, Md, Perms);
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

    // H2: a Hidden field (credential) must be rejected as a filter target so meta.total
    // cannot be used to blind-extract the value character by character.
    [Fact]
    public void Hidden_field_filter_throws()
    {
        var q = new QueryModel(null, new ComparisonFilter("secret", QueryOperator.StartsWith, "$argon2"), [], 0, 0, null);
        var act = () => QueryValidator.Validate(q, Meta(), Opts, Graph, Md, Perms);
        act.Should().Throw<QueryException>().WithMessage("*secret*");
    }

    [Fact]
    public void Hidden_field_sort_throws()
    {
        var q = new QueryModel(null, null, [new SortField("secret", false)], 0, 0, null);
        var act = () => QueryValidator.Validate(q, Meta(), Opts, Graph, Md, Perms);
        act.Should().Throw<QueryException>().WithMessage("*secret*");
    }

    [Fact]
    public void Hidden_field_in_field_selection_throws()
    {
        var q = new QueryModel(["secret"], null, [], 0, 0, null);
        var act = () => QueryValidator.Validate(q, Meta(), Opts, Graph, Md, Perms);
        act.Should().Throw<QueryException>().WithMessage("*secret*");
    }

    // H2: search must also skip Hidden fields even if the field is (mis)marked Searchable.
    [Fact]
    public void SearchableFields_excludes_hidden()
    {
        var fields = QueryValidator.SearchableFields(Meta());
        fields.Should().NotContain("secret");
    }

    [Fact]
    public void Negative_offset_is_clamped_to_zero()
    {
        var result = QueryValidator.Validate(new QueryModel(null, null, [], 0, -5, null), Meta(), Opts, Graph, Md, Perms);
        result.Offset.Should().Be(0);
    }

    [Fact]
    public void Nested_conditions_exceeding_cap_throw()
    {
        var many = Enumerable.Range(0, Opts.MaxFilterConditions + 1)
            .Select(_ => (FilterNode)new ComparisonFilter("title", QueryOperator.Eq, "x"))
            .ToList();
        var q = new QueryModel(null, new LogicalFilter(LogicalOperator.And, many), [], 0, 0, null);
        var act = () => QueryValidator.Validate(q, Meta(), Opts, Graph, Md, Perms);
        act.Should().Throw<QueryException>().WithMessage("*conditions*");
    }

    [Fact]
    public void Nested_logical_groups_throw()
    {
        var inner = new LogicalFilter(LogicalOperator.Or,
            [new ComparisonFilter("title", QueryOperator.Eq, "x")]);
        var outer = new LogicalFilter(LogicalOperator.And, [inner]);
        var q = new QueryModel(null, outer, [], 0, 0, null);
        var act = () => QueryValidator.Validate(q, Meta(), Opts, Graph, Md, Perms);
        act.Should().Throw<QueryException>().WithMessage("*Nested*");
    }

    [Fact]
    public void Relation_path_in_fields_throws()
    {
        var q = new QueryModel(["category.name"], null, [], 0, 0, null);
        var act = () => QueryValidator.Validate(q, Meta(), Opts, Graph, Md, Perms);
        act.Should().Throw<QueryException>().WithMessage("*field selection*");
    }

    // Self-referencing relation graph (category.parent -> category), for exercising
    // recursive breadcrumb depth boundaries against opts.MaxRelationDepth.
    private sealed class SelfRefGraph : IRelationshipGraph
    {
        public IReadOnlyList<RelationMetadata> Relations(string c) => [];
        public IReadOnlyList<(string, string)> InboundRestrict(string c) => [];
        public RelationMetadata? Resolve(string collection, string rel) => (collection, rel) switch
        {
            ("category", "parent") => new RelationMetadata { Name = "parent", Label = "Parent",
                Kind = RelationKind.ManyToOne, TargetCollection = "category",
                Interface = RelationInterface.Dropdown, ForeignKey = "parentId" },
            _ => null
        };
    }

    private sealed class CategoryMeta : IMetadataProvider
    {
        public IReadOnlyList<CollectionMetadata> GetCollections() => [];
        public CollectionMetadata? GetCollection(string name) => name == "category"
            ? new CollectionMetadata { Name = "category", Label = "Category", FieldGroups = [],
                Fields = [new FieldMetadata { Name = "name", Label = "Name", Interface = FieldInterface.Text }] }
            : null;
    }

    private static CollectionMetadata CategoryRoot() => new()
    {
        Name = "category", Label = "Category", FieldGroups = [],
        Fields = [new FieldMetadata { Name = "name", Label = "Name", Interface = FieldInterface.Text }]
    };

    [Fact]
    public void Default_max_relation_depth_is_six()
    {
        new StruoQueryOptions().MaxRelationDepth.Should().Be(6);
    }

    [Fact]
    public void Self_relation_path_within_max_depth_is_accepted()
    {
        // 5-hop breadcrumb: parent.parent.parent.parent.parent.name
        var path = string.Concat(Enumerable.Repeat("parent.", 5)) + "name";
        var q = new QueryModel(null, new ComparisonFilter(path, QueryOperator.Eq, "x"), [], 0, 0, null);
        var act = () => QueryValidator.Validate(q, CategoryRoot(), new StruoQueryOptions(),
            new SelfRefGraph(), new CategoryMeta(), Perms);
        act.Should().NotThrow();
    }

    // Pins the exact accept/reject boundary the branch moves: default MaxRelationDepth is 6,
    // so a 6-hop self-relation path must still be accepted (paired with the 7-hop reject below).
    [Fact]
    public void Self_relation_path_at_max_depth_boundary_is_accepted()
    {
        // 6-hop breadcrumb: parent.parent.parent.parent.parent.parent.name
        var path = string.Concat(Enumerable.Repeat("parent.", 6)) + "name";
        var q = new QueryModel(null, new ComparisonFilter(path, QueryOperator.Eq, "x"), [], 0, 0, null);
        var act = () => QueryValidator.Validate(q, CategoryRoot(), new StruoQueryOptions(),
            new SelfRefGraph(), new CategoryMeta(), Perms);
        act.Should().NotThrow();
    }

    [Fact]
    public void Self_relation_path_exceeding_max_depth_throws()
    {
        var opts = new StruoQueryOptions { MaxRelationDepth = 6 };
        var path = string.Concat(Enumerable.Repeat("parent.", 7)) + "name"; // 7 hops > 6
        var q = new QueryModel(null, new ComparisonFilter(path, QueryOperator.Eq, "x"), [], 0, 0, null);
        var act = () => QueryValidator.Validate(q, CategoryRoot(), opts,
            new SelfRefGraph(), new CategoryMeta(), Perms);
        act.Should().Throw<QueryException>();
    }

    // A read grant on the root collection does not carry across a relation hop. Without this,
    // meta.total on a relation filter is a blind-extraction oracle over a collection the caller
    // cannot read.
    [Fact]
    public void Relation_filter_path_into_an_unreadable_collection_throws_PermissionDenied()
    {
        var q = new QueryModel(null, new ComparisonFilter("category.name", QueryOperator.Eq, "x"), [], 0, 0, null);
        var act = () => QueryValidator.Validate(q, Meta(), Opts, Graph, Md, new DenyReadOf("category"));
        act.Should().Throw<PermissionDeniedException>();
    }

    [Fact]
    public void Relation_sort_path_into_an_unreadable_collection_throws_PermissionDenied()
    {
        var q = new QueryModel(null, null, [new SortField("category.name", false)], 0, 0, null);
        var act = () => QueryValidator.Validate(q, Meta(), Opts, Graph, Md, new DenyReadOf("category"));
        act.Should().Throw<PermissionDeniedException>();
    }

    // The counterpart that keeps the gate honest: with the grant, the same path still validates.
    [Fact]
    public void Relation_path_into_a_readable_collection_is_accepted()
    {
        var q = new QueryModel(null, new ComparisonFilter("category.name", QueryOperator.Eq, "x"), [], 0, 0, null);
        var act = () => QueryValidator.Validate(q, Meta(), Opts, Graph, Md, Perms);
        act.Should().NotThrow();
    }

    // The permission check must run before the leaf is resolved. If it did not, a real field and an
    // invented one on an unreadable collection would come back with different messages, which
    // enumerates that collection's field names — reachable anonymously, since the item read
    // endpoints carry no [Authorize] and /api/schema is the authenticated-only surface.
    [Fact]
    public void Unreadable_hop_is_refused_before_the_leaf_field_is_resolved()
    {
        var denied = new DenyReadOf("category");
        var real = new QueryModel(null, new ComparisonFilter("category.name", QueryOperator.Eq, "x"), [], 0, 0, null);
        var invented = new QueryModel(null, new ComparisonFilter("category.zzz", QueryOperator.Eq, "x"), [], 0, 0, null);

        var onReal = Record.Exception(() => QueryValidator.Validate(real, Meta(), Opts, Graph, Md, denied));
        var onInvented = Record.Exception(() => QueryValidator.Validate(invented, Meta(), Opts, Graph, Md, denied));

        onReal.Should().BeOfType<PermissionDeniedException>();
        onInvented.Should().BeOfType<PermissionDeniedException>();
        onInvented!.Message.Should().Be(onReal!.Message);
        onInvented.Message.Should().NotContain("zzz");
    }

    // Same guarantee one hop further out: an invented relation name beyond an unreadable hop must
    // not be distinguishable from a real one.
    [Fact]
    public void Unreadable_hop_is_refused_before_a_deeper_relation_name_is_resolved()
    {
        var q = new QueryModel(null, new ComparisonFilter("category.ghostRel.name", QueryOperator.Eq, "x"), [], 0, 0, null);
        var act = () => QueryValidator.Validate(q, Meta(), Opts, Graph, Md, new DenyReadOf("category"));
        act.Should().Throw<PermissionDeniedException>().Which.Message.Should().NotContain("ghostRel");
    }

    // An unresolvable hop on a collection the caller CAN read keeps its original QueryException:
    // the permission walk must not swallow the existing "unknown relation" message.
    [Fact]
    public void Unknown_relation_on_a_readable_collection_still_reports_the_query_error()
    {
        var q = new QueryModel(null, new ComparisonFilter("ghostRel.name", QueryOperator.Eq, "x"), [], 0, 0, null);
        var act = () => QueryValidator.Validate(q, Meta(), Opts, Graph, Md, Perms);
        act.Should().Throw<QueryException>().WithMessage("*ghostRel*");
    }

    private static QueryModel Q(FilterNode f) => new(null, f, [], 0, 0, null);
    private static RelationPredicateFilter Some(string rel, FilterNode inner) => new(rel, RelationQuantifier.Some, inner);

    [Fact]
    public void Some_predicate_validates_inner_against_the_target_collection()
    {
        var q = Q(Some("tags", new ComparisonFilter("name", QueryOperator.Eq, "a")));
        QueryValidator.Validate(q, Meta(), Opts, Graph, Md, Perms).Filter.Should().BeOfType<RelationPredicateFilter>();
    }

    [Fact]
    public void Some_predicate_rejects_unknown_inner_field_naming_the_target()
    {
        var act = () => QueryValidator.Validate(Q(Some("tags", new ComparisonFilter("title", QueryOperator.Eq, "a"))), Meta(), Opts, Graph, Md, Perms);
        act.Should().Throw<QueryException>().WithMessage("*'title'*tag*");
    }

    [Fact]
    public void Some_predicate_rejects_a_scalar_field_as_relation_path()
    {
        var act = () => QueryValidator.Validate(Q(Some("status", new ComparisonFilter("x", QueryOperator.Eq, 1))), Meta(), Opts, Graph, Md, Perms);
        act.Should().Throw<QueryException>().WithMessage("*'status'*");
    }

    [Fact]
    public void Some_predicate_on_m2o_is_accepted()
    {
        var q = Q(new RelationPredicateFilter("category", RelationQuantifier.None, new ComparisonFilter("name", QueryOperator.Eq, "x")));
        var act = () => QueryValidator.Validate(q, Meta(), Opts, Graph, Md, Perms);
        act.Should().NotThrow();
    }

    [Fact]
    public void Inner_logical_depth_restarts_inside_a_predicate()
    {
        var inner = new LogicalFilter(LogicalOperator.Or, [new ComparisonFilter("name", QueryOperator.Eq, "a"), new ComparisonFilter("name", QueryOperator.Eq, "b")]);
        var outer = new LogicalFilter(LogicalOperator.And, [Some("tags", inner), new ComparisonFilter("status", QueryOperator.Eq, "x")]);
        var act = () => QueryValidator.Validate(Q(outer), Meta(), Opts, Graph, Md, Perms);
        act.Should().NotThrow();
    }

    [Fact]
    public void Nested_logical_inside_the_inner_still_throws()
    {
        var inner = new LogicalFilter(LogicalOperator.And, [new LogicalFilter(LogicalOperator.Or, [new ComparisonFilter("name", QueryOperator.Eq, "a")])]);
        var act = () => QueryValidator.Validate(Q(Some("tags", inner)), Meta(), Opts, Graph, Md, Perms);
        act.Should().Throw<QueryException>().WithMessage("*Nested*");
    }

    [Fact]
    public void Inner_conditions_count_toward_MaxFilterConditions()
    {
        var many = Enumerable.Range(0, Opts.MaxFilterConditions).Select(_ => (FilterNode)new ComparisonFilter("name", QueryOperator.Eq, "x")).ToList();
        var q = Q(new LogicalFilter(LogicalOperator.And, [new ComparisonFilter("status", QueryOperator.Eq, "x"), Some("tags", new LogicalFilter(LogicalOperator.And, many))]));
        var act = () => QueryValidator.Validate(q, Meta(), Opts, Graph, Md, Perms);
        act.Should().Throw<QueryException>().WithMessage("*conditions*");
    }

    [Fact]
    public void Predicate_hops_accumulate_toward_MaxRelationDepth()
    {
        var opts = new StruoQueryOptions { MaxRelationDepth = 1 };
        var q = Q(Some("category", Some("articles", new ComparisonFilter("status", QueryOperator.Eq, "x"))));
        var act = () => QueryValidator.Validate(q, Meta(), opts, Graph, Md, Perms);
        // The message must state the CONFIGURED limit (1), not the remaining budget at the point of
        // failure (0) — a nested predicate's second hop exhausts the budget, but the caller configured 1.
        act.Should().Throw<QueryException>().WithMessage("*depth of 1*");
    }

    [Fact]
    public void Predicate_into_unreadable_collection_throws_PermissionDenied()
    {
        var act = () => QueryValidator.Validate(Q(Some("tags", new ComparisonFilter("name", QueryOperator.Eq, "a"))), Meta(), Opts, Graph, Md, new DenyReadOf("tag"));
        act.Should().Throw<PermissionDeniedException>();
    }

    [Fact]
    public void Junction_field_requires_read_on_the_junction_collection()
    {
        var q = Q(new ComparisonFilter("tags._junction.note", QueryOperator.Contains, "x"));
        QueryValidator.Validate(q, Meta(), Opts, Graph, Md, Perms).Should().NotBeNull();
        var act = () => QueryValidator.Validate(q, Meta(), Opts, Graph, Md, new DenyReadOf("articleTag"));
        act.Should().Throw<PermissionDeniedException>();
    }

    [Fact]
    public void Hidden_junction_field_is_reported_as_unknown()
    {
        var act = () => QueryValidator.Validate(Q(new ComparisonFilter("tags._junction.secret", QueryOperator.Eq, "x")), Meta(), Opts, Graph, Md, Perms);
        act.Should().Throw<QueryException>().WithMessage("*Unknown field*");
    }

    // The junction collection's read grant must be checked before RelationPath.Parse resolves the
    // leaf field. Without this, a caller who can read the M2M target but not the junction collection
    // could distinguish a real payload field from an invented one by response code (400 vs 403) —
    // an oracle enumerating the junction collection's field names, mirroring
    // Unreadable_hop_is_refused_before_the_leaf_field_is_resolved above.
    [Fact]
    public void Unreadable_junction_is_refused_before_the_junction_field_is_resolved()
    {
        var q = Q(new ComparisonFilter("tags._junction.nope", QueryOperator.Eq, "x"));
        var act = () => QueryValidator.Validate(q, Meta(), Opts, Graph, Md, new DenyReadOf("articleTag"));
        act.Should().Throw<PermissionDeniedException>();
    }

    // Fix round 2: "_junction.<field>" reached directly inside a `_some`/`_none` predicate's inner
    // filter (not as an ordinary dotted field path — see QueryValidator.ValidateJunctionLeaf). The
    // predicate's own last relation segment (article.tags, an M2M with JunctionCollection "articleTag")
    // is what "_junction" resolves against here, since the inner has already consumed that hop.

    private static LogicalFilter And(params FilterNode[] children) => new(LogicalOperator.And, children);

    [Fact]
    public void Some_inner_junction_field_is_accepted()
    {
        var q = Q(Some("tags", And(new ComparisonFilter("name", QueryOperator.Eq, "a"), new ComparisonFilter("_junction.note", QueryOperator.Eq, "hero"))));
        var act = () => QueryValidator.Validate(q, Meta(), Opts, Graph, Md, Perms);
        act.Should().NotThrow();
    }

    [Fact]
    public void Some_inner_junction_field_requires_read_on_the_junction_collection()
    {
        var q = Q(Some("tags", And(new ComparisonFilter("name", QueryOperator.Eq, "a"), new ComparisonFilter("_junction.note", QueryOperator.Eq, "hero"))));
        var act = () => QueryValidator.Validate(q, Meta(), Opts, Graph, Md, new DenyReadOf("articleTag"));
        act.Should().Throw<PermissionDeniedException>();

        // The grant is checked before the field name — an invented junction field must fail the same
        // way (PermissionDenied, not Unknown field) when the caller cannot read the junction collection.
        var invented = Q(Some("tags", new ComparisonFilter("_junction.nope", QueryOperator.Eq, "x")));
        var actInvented = () => QueryValidator.Validate(invented, Meta(), Opts, Graph, Md, new DenyReadOf("articleTag"));
        actInvented.Should().Throw<PermissionDeniedException>();
    }

    [Fact]
    public void Some_inner_hidden_junction_field_is_unknown()
    {
        var act = () => QueryValidator.Validate(
            Q(Some("tags", new ComparisonFilter("_junction.secret", QueryOperator.Eq, "x"))), Meta(), Opts, Graph, Md, Perms);
        act.Should().Throw<QueryException>().WithMessage("*Unknown field*");
    }

    [Fact]
    public void Some_inner_junction_on_m2o_relation_throws()
    {
        var act = () => QueryValidator.Validate(
            Q(Some("category", new ComparisonFilter("_junction.x", QueryOperator.Eq, "y"))), Meta(), Opts, Graph, Md, Perms);
        act.Should().Throw<QueryException>();
    }

    private static QueryModel WithFacets(params string[] facets) => new QueryModel(null, null, [], 0, 0, null) { Facets = facets };
    private static QueryModel WithAggregate(AggregateOp op, params string[] fields) =>
        new QueryModel(null, null, [], 0, 0, null) { Aggregate = new AggregateSpec(new Dictionary<AggregateOp, IReadOnlyList<string>> { [op] = fields }) };

    [Fact]
    public void Facets_are_validated_and_deduplicated_preserving_first_occurrence_order()
    {
        var v = QueryValidator.Validate(WithFacets("status", "title", "status", "category.name"), Meta(), Opts, Graph, Md, Perms);
        v.Facets.Should().Equal("status", "title", "category.name");
    }

    [Fact]
    public void Facet_on_unknown_field_throws_the_same_message_as_a_filter()
    {
        var act = () => QueryValidator.Validate(WithFacets("nope"), Meta(), Opts, Graph, Md, Perms);
        act.Should().Throw<QueryException>().WithMessage("Unknown field 'nope' on collection 'article'.");
    }

    [Fact]
    public void Facet_on_a_multi_value_interface_is_rejected_with_field_and_interface()
    {
        var act = () => QueryValidator.Validate(WithFacets("regions"), Meta(), Opts, Graph, Md, Perms);
        act.Should().Throw<QueryException>().WithMessage("Field 'regions' on collection 'article' (MultiSelect) cannot be used as a facet.");
    }

    [Fact]
    public void Facet_on_a_hidden_field_is_an_unknown_field()
    {
        var act = () => QueryValidator.Validate(WithFacets("secret"), Meta(), Opts, Graph, Md, Perms);
        act.Should().Throw<QueryException>().WithMessage("Unknown field 'secret' on collection 'article'.");
    }

    [Fact]
    public void Facet_across_an_unreadable_relation_is_forbidden_before_the_leaf_is_resolved()
    {
        var act = () => QueryValidator.Validate(WithFacets("category.nope"), Meta(), Opts, Graph, Md, new DenyReadOf("category"));
        act.Should().Throw<PermissionDeniedException>();
    }

    [Fact]
    public void Facet_by_relation_name_also_needs_read_on_the_target()
    {
        var act = () => QueryValidator.Validate(WithFacets("tags"), Meta(), Opts, Graph, Md, new DenyReadOf("tag"));
        act.Should().Throw<PermissionDeniedException>();
    }

    [Fact]
    public void Empty_facet_path_is_rejected()
    {
        var act = () => QueryValidator.Validate(WithFacets(""), Meta(), Opts, Graph, Md, Perms);
        act.Should().Throw<QueryException>().WithMessage("Facet path must not be empty.");
    }

    [Fact]
    public void Too_many_facets_is_rejected_with_the_cap_in_the_message()
    {
        var opts = new StruoQueryOptions { MaxFacets = 2 };
        var act = () => QueryValidator.Validate(WithFacets("status", "categoryId", "tags"), Meta(), opts, Graph, Md, Perms);
        act.Should().Throw<QueryException>().WithMessage("Too many facets (max 2).");
    }

    // Finding B: a bare-FK facet path ("categoryId") must be permission-checked against the
    // ManyToOne relation that FK actually belongs to on THIS root — not against a OneToMany
    // relation that merely happens to share the same ForeignKey string (the child's reverse-FK
    // column, per MetadataScanner — see Meta().Relations above).
    [Fact]
    public void Fk_facet_is_checked_against_the_many_to_one_relation_even_when_an_o2m_shares_its_fk_name()
    {
        var act = () => QueryValidator.Validate(WithFacets("categoryId"), Meta(), Opts, Graph, Md, new DenyReadOf("category"));
        act.Should().Throw<PermissionDeniedException>();
    }

    [Fact]
    public void Fk_facet_is_not_checked_against_an_o2m_relation_that_merely_shares_its_fk_name()
    {
        var act = () => QueryValidator.Validate(WithFacets("categoryId"), Meta(), Opts, Graph, Md, new DenyReadOf("categoryArchive"));
        act.Should().NotThrow();
    }

    [Fact]
    public void Aggregate_count_accepts_any_known_field()
    {
        var act = () => QueryValidator.Validate(WithAggregate(AggregateOp.Count, "createdAt", "title"), Meta(), Opts, Graph, Md, Perms);
        act.Should().NotThrow();
    }

    [Fact]
    public void Aggregate_sum_on_a_text_field_is_rejected()
    {
        var act = () => QueryValidator.Validate(WithAggregate(AggregateOp.Sum, "status"), Meta(), Opts, Graph, Md, Perms);
        act.Should().Throw<QueryException>().WithMessage("Aggregate 'sum' is not supported on field 'status' (Select).");
    }

    [Fact]
    public void Aggregate_min_on_a_datetime_field_is_accepted_and_sum_is_not()
    {
        QueryValidator.Validate(WithAggregate(AggregateOp.Min, "createdAt"), Meta(), Opts, Graph, Md, Perms);
        var act = () => QueryValidator.Validate(WithAggregate(AggregateOp.Sum, "createdAt"), Meta(), Opts, Graph, Md, Perms);
        act.Should().Throw<QueryException>().WithMessage("Aggregate 'sum' is not supported on field 'createdAt' (DateTime).");
    }

    [Fact]
    public void Aggregate_on_an_unknown_or_hidden_field_is_an_unknown_field()
    {
        var act = () => QueryValidator.Validate(WithAggregate(AggregateOp.Count, "secret"), Meta(), Opts, Graph, Md, Perms);
        act.Should().Throw<QueryException>().WithMessage("Unknown field 'secret' on collection 'article'.");
    }

    [Fact]
    public void Too_many_aggregate_fields_counts_across_ops()
    {
        var opts = new StruoQueryOptions { MaxAggregates = 2 };
        var q = new QueryModel(null, null, [], 0, 0, null)
        {
            Aggregate = new AggregateSpec(new Dictionary<AggregateOp, IReadOnlyList<string>>
            {
                [AggregateOp.Count] = ["createdAt", "status"], [AggregateOp.Min] = ["createdAt"],
            }),
        };
        var act = () => QueryValidator.Validate(q, Meta(), opts, Graph, Md, Perms);
        act.Should().Throw<QueryException>().WithMessage("Too many aggregate fields (max 2).");
    }

    [Fact]
    public void ResolveFacets_returns_one_resolved_path_per_validated_facet_in_order()
    {
        var v = QueryValidator.Validate(WithFacets("status", "tags.name"), Meta(), Opts, Graph, Md, Perms);
        var resolved = QueryValidator.ResolveFacets(v, Meta(), Graph, Md);
        resolved.Select(r => r.Kind).Should().Equal(FacetPathKind.OwnField, FacetPathKind.RelationLeaf);
        resolved[1].LeafField.Should().Be("name");
    }
}
