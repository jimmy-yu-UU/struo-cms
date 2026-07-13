using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Struo.Application.Configuration;
using Struo.Application.Query;
using Struo.Domain.Query;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Query;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

[Collection("ApiIntegration")]
public class DeepNestingBatchingTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private async Task<string> Post(HttpClient c, string col, object body) =>
        Root(await (await c.PostAsJsonAsync($"/api/items/{col}", body)).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetString()!;

    private static readonly System.Reflection.BindingFlags Flags =
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.IgnoreCase;

    private static object? ReadProp(object e, string name) => e.GetType().GetProperty(name, Flags)?.GetValue(e);

    // Expands `category -> articles (O2M) -> category (M2O)` and returns how many WhereIn queries
    // the expander issued for a category that has `articleCount` articles.
    private async Task<int> ExpandCountForCategoryWith(int articleCount, string label)
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var catId = await Post(c, "category", new { name = $"Batch-{label}" });
        for (var i = 0; i < articleCount; i++)
            await Post(c, "article", new
            {
                status = "draft", categoryId = catId,
                translations = new { en = new { title = $"Batch-{label}-{i}" } }
            });

        using var scope = _factory.Services.CreateScope();
        var real = scope.ServiceProvider.GetRequiredService<IItemRepository>();
        var graph = scope.ServiceProvider.GetRequiredService<RelationshipGraph>();
        var counter = new CountingItemRepository(real);
        var relFilter = scope.ServiceProvider.GetRequiredService<IRelationFilterResolver>();
        var opts = scope.ServiceProvider.GetRequiredService<StruoQueryOptions>();
        var expander = new RelationExpander(counter, graph, relFilter, opts);

        var parents = await real.QueryWhereInAsync("category", "id", new object[] { System.Guid.Parse(catId) });

        var deep = new DeepSpec(new Dictionary<string, DeepRelationSpec>
        {
            ["articles"] = new DeepRelationSpec(null, null,
                new DeepSpec(new Dictionary<string, DeepRelationSpec>
                {
                    ["category"] = new DeepRelationSpec(null, null)
                }))
        });

        counter.ResetCount();
        await expander.ExpandAsync(
            "category", parents, deep,
            projectTarget: (_, entity, _) => new Dictionary<string, object?> { ["id"] = ReadProp(entity, "id") },
            parentId: entity => ReadProp(entity, "id")!,
            readProp: ReadProp);
        return counter.WhereInCalls;
    }

    [Fact]
    public async Task Depth2_expansion_query_count_is_constant_in_row_count()
    {
        var few = await ExpandCountForCategoryWith(2, "few");
        var many = await ExpandCountForCategoryWith(8, "many");

        // Batched (breadth-first per level): 1 query for `articles` + 1 for the nested `category`
        // = 2, regardless of how many articles exist. A per-parent (N+1) recursion would make the
        // nested-category query count grow with the article count, so `many > few`.
        few.Should().Be(many);
        many.Should().Be(2);
    }

    private async Task<int> ExpandCountWithArgs(int articleCount, string label)
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var catId = await Post(c, "category", new { name = $"ArgBatch-{label}" });
        for (var i = 0; i < articleCount; i++)
            await Post(c, "article", new
            {
                status = $"s{i}", categoryId = catId,
                translations = new { en = new { title = $"ArgBatch-{label}-{i}" } }
            });

        using var scope = _factory.Services.CreateScope();
        var real = scope.ServiceProvider.GetRequiredService<IItemRepository>();
        var graph = scope.ServiceProvider.GetRequiredService<RelationshipGraph>();
        var relFilter = scope.ServiceProvider.GetRequiredService<IRelationFilterResolver>();
        var opts = scope.ServiceProvider.GetRequiredService<StruoQueryOptions>();
        var counter = new CountingItemRepository(real);
        var expander = new RelationExpander(counter, graph, relFilter, opts);

        var parents = await real.QueryWhereInAsync("category", "id", new object[] { System.Guid.Parse(catId) });

        // articles(O2M) with filter+sort+limit, nested category(M2O). Args must NOT add queries.
        var deep = new DeepSpec(new Dictionary<string, DeepRelationSpec>
        {
            ["articles"] = new DeepRelationSpec(null, 1,
                new DeepSpec(new Dictionary<string, DeepRelationSpec> { ["category"] = new DeepRelationSpec(null, null) }))
            {
                Filter = new ComparisonFilter("status", QueryOperator.Neq, "zzz"),
                Sort = new List<SortField> { new("status", false) }
            }
        });

        counter.ResetCount();
        await expander.ExpandAsync(
            "category", parents, deep,
            projectTarget: (_, entity, _) => new Dictionary<string, object?> { ["id"] = ReadProp(entity, "id") },
            parentId: entity => ReadProp(entity, "id")!,
            readProp: ReadProp);
        return counter.WhereInCalls;
    }

    [Fact]
    public async Task Query_count_with_args_is_constant_in_row_count()
    {
        var few = await ExpandCountWithArgs(2, "few");
        var many = await ExpandCountWithArgs(8, "many");
        few.Should().Be(many);
        many.Should().Be(2); // 1 for articles + 1 for nested category, regardless of args or row count
    }
}
