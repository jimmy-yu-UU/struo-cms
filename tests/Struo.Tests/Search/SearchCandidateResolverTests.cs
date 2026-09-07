using AwesomeAssertions;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Application.Search;
using Struo.Domain.Query;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Metadata;
using Struo.Sample.Blog;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Search;

public class SearchCandidateResolverTests
{
    // category: Guid PK (sample); language: long identity PK (framework) — the two PK shapes the template ships.
    private static readonly EntityRegistry Registry =
        new(MetadataScanner.ScanDescriptors([typeof(Category), typeof(Language)]));

    private static QueryModel Query(string? search) => new(null, null, [], 25, 0, search);

    private static SearchCandidateResolver Resolver(ScriptedSearchProvider provider, int max = 1000) =>
        new(provider, new StruoQueryOptions { MaxSearchCandidates = max }, Registry);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_search_never_calls_the_provider(string? search)
    {
        var provider = new ScriptedSearchProvider(_ => throw new InvalidOperationException("must not be called"));
        var q = Query(search);
        var result = await Resolver(provider).ResolveAsync("category", q, ["name"], "en", default);
        result.Should().BeSameAs(q);
        provider.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Provider_receives_collection_verbatim_term_locale_and_searchable_hint()
    {
        var provider = new ScriptedSearchProvider(_ => SearchOutcome.NotHandled);
        await Resolver(provider).ResolveAsync("category", Query("  Hello "), ["name"], "zh-TW", default);
        provider.Requests.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new SearchRequest("category", "  Hello ", "zh-TW", ["name"]),
                o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Provider_is_asked_even_when_the_collection_has_no_searchable_fields()
    {
        var provider = new ScriptedSearchProvider(_ => SearchOutcome.NotHandled);
        await Resolver(provider).ResolveAsync("category", Query("x"), [], "en", default);
        provider.Requests.Should().ContainSingle().Which.SearchableFields.Should().BeEmpty();
    }

    [Fact]
    public async Task NotHandled_returns_the_query_untouched()
    {
        var q = Query("x");
        var result = await Resolver(new ScriptedSearchProvider(_ => SearchOutcome.NotHandled))
            .ResolveAsync("category", q, ["name"], "en", default);
        result.Should().BeSameAs(q);
        result.SearchCandidates.Should().BeNull();
    }

    [Fact]
    public async Task Candidates_are_parsed_to_the_pk_type_and_deduplicated_in_order()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var provider = new ScriptedSearchProvider(_ => SearchOutcome.Candidates([a.ToString(), b.ToString(), a.ToString()]));
        var result = await Resolver(provider).ResolveAsync("category", Query("x"), ["name"], "en", default);
        result.SearchCandidates.Should().Equal(a, b);
        result.SearchCandidates![0].Should().BeOfType<Guid>();
        result.Search.Should().Be("x", "the term stays on the model for logging/diagnostics; the translator ignores it once candidates are set");
    }

    [Fact]
    public async Task Empty_candidates_are_kept_as_an_empty_set_not_null()
    {
        var result = await Resolver(new ScriptedSearchProvider(_ => SearchOutcome.Candidates([])))
            .ResolveAsync("category", Query("x"), ["name"], "en", default);
        result.SearchCandidates.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task Long_pk_collections_parse_integer_ids()
    {
        var result = await Resolver(new ScriptedSearchProvider(_ => SearchOutcome.Candidates(["7", "42"])))
            .ResolveAsync("language", Query("x"), ["name"], "en", default);
        result.SearchCandidates.Should().Equal(7L, 42L);
    }

    [Fact]
    public async Task Int_pk_collections_parse_int_ids()
    {
        var registry = new EntityRegistry(new Dictionary<string, EntityDescriptor>(StringComparer.OrdinalIgnoreCase)
        {
            ["intKeyed"] = new(typeof(IntKeyed), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["id"] = "Id" }, "Id"),
        });
        var resolver = new SearchCandidateResolver(
            new ScriptedSearchProvider(_ => SearchOutcome.Candidates(["7", "42"])), new StruoQueryOptions(), registry);
        var result = await resolver.ResolveAsync("intKeyed", Query("x"), [], "en", default);
        result.SearchCandidates.Should().Equal(7, 42);
        result.SearchCandidates![0].Should().BeOfType<int>();
    }

    [Fact]
    public async Task Short_pk_collections_parse_short_ids()
    {
        var registry = new EntityRegistry(new Dictionary<string, EntityDescriptor>(StringComparer.OrdinalIgnoreCase)
        {
            ["shortKeyed"] = new(typeof(ShortKeyed), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["id"] = "Id" }, "Id"),
        });
        var resolver = new SearchCandidateResolver(
            new ScriptedSearchProvider(_ => SearchOutcome.Candidates(["7", "42"])), new StruoQueryOptions(), registry);
        var result = await resolver.ResolveAsync("shortKeyed", Query("x"), [], "en", default);
        result.SearchCandidates.Should().Equal((short)7, (short)42);
        result.SearchCandidates![0].Should().BeOfType<short>();
    }

    [Fact]
    public async Task A_descriptor_whose_FieldToProperty_lacks_id_throws()
    {
        var registry = new EntityRegistry(new Dictionary<string, EntityDescriptor>(StringComparer.OrdinalIgnoreCase)
        {
            ["noId"] = new(typeof(IntKeyed), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), "Id"),
        });
        var resolver = new SearchCandidateResolver(
            new ScriptedSearchProvider(_ => SearchOutcome.Candidates(["7"])), new StruoQueryOptions(), registry);
        var act = () => resolver.ResolveAsync("noId", Query("x"), [], "en", default);
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*exposes no 'id' field*");
    }

    [Fact]
    public async Task More_candidates_than_MaxSearchCandidates_is_a_provider_contract_violation()
    {
        var ids = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid().ToString()).ToList();
        var act = () => Resolver(new ScriptedSearchProvider(_ => SearchOutcome.Candidates(ids)), max: 2)
            .ResolveAsync("category", Query("x"), ["name"], "en", default);
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*3 candidate ids*'category'*Query:MaxSearchCandidates (2)*");
    }

    [Fact]
    public async Task An_unparsable_id_is_a_provider_contract_violation_not_a_400()
    {
        var act = () => Resolver(new ScriptedSearchProvider(_ => SearchOutcome.Candidates(["not-a-guid"])))
            .ResolveAsync("category", Query("x"), ["name"], "en", default);
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("*'not-a-guid'*Guid*'category'*");
        ex.Which.Should().NotBeAssignableTo<QueryException>();
    }

    [Fact]
    public async Task A_string_primary_key_is_refused_because_candidates_render_as_sql_literals()
    {
        var registry = new EntityRegistry(new Dictionary<string, EntityDescriptor>(StringComparer.OrdinalIgnoreCase)
        {
            ["strKeyed"] = new(typeof(StrKeyed), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["id"] = "Id" }, "Id"),
        });
        var resolver = new SearchCandidateResolver(
            new ScriptedSearchProvider(_ => SearchOutcome.Candidates(["x"])), new StruoQueryOptions(), registry);
        var act = () => resolver.ResolveAsync("strKeyed", Query("x"), [], "en", default);
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*Guid or integer primary keys*String*");
    }

    private sealed class StrKeyed { public string Id { get; set; } = ""; }
    private sealed class IntKeyed { public int Id { get; set; } }
    private sealed class ShortKeyed { public short Id { get; set; } }
}
