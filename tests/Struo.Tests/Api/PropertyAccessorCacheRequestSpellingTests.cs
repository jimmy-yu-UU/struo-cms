using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Application.Query;
using Struo.Tests.Fixtures;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// <see cref="PropertyAccessorCache"/> is process-wide and unbounded, so a name that comes from a request
/// must never become a key. A translation field name on a write and a nested sort field name on a read
/// are validated without regard to case; both reach the cache in the metadata's spelling.
/// </summary>
[Collection("ApiIntegration")]
public class PropertyAccessorCacheRequestSpellingTests(ApiFactory factory)
{
    // Spellings no metadata or property uses; the cache may hold "title" and "Title" for legitimate callers.
    private static readonly string[] TitleSpellings = ["tITLE", "tiTle", "titLE", "TItle", "TITLE"];
    private static readonly string[] StatusSpellings = ["sTATUS", "staTUS", "-STAtus", "STATUS"];
    private static readonly int[] InvalidTitleValue = [1, 2];
    private static readonly string[] UnknownSortField = ["NoSuchField"];
    private static readonly string[] PublishedThenDraft = ["published", "draft"];
    private static readonly string[] DraftThenPublished = ["draft", "published"];
    private static readonly string[] GraphqlSortSpellings = ["sTATuS", "-STAtUs"];
    private static readonly string[] GraphqlSortNames = ["sTATuS", "STAtUs"];

    [Fact]
    public async Task Translation_field_names_in_any_case_add_no_cache_key()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        foreach (var spelling in TitleSpellings)
        {
            var response = await admin.PostAsJsonAsync("/api/items/fxArticle", new Dictionary<string, object>
            {
                ["status"] = "draft",
                ["translations"] = new Dictionary<string, object>
                {
                    ["en"] = new Dictionary<string, object> { [spelling] = "t-" + spelling },
                },
            });
            response.StatusCode.Should().Be(HttpStatusCode.Created, spelling);
        }

        PropertyAccessorCache.NamesFor(typeof(FxArticleTranslation)).Should().NotIntersectWith(TitleSpellings);
    }

    [Fact]
    public async Task Translation_values_written_under_any_spelling_read_back_unchanged()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var created = await admin.PostAsJsonAsync("/api/items/fxArticle", new Dictionary<string, object>
        {
            ["status"] = "draft",
            ["translations"] = new Dictionary<string, object>
            {
                ["en"] = new Dictionary<string, object> { ["tItLE"] = "mixed-case title" },
            },
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("id").GetString();

        var read = await admin.GetFromJsonAsync<JsonElement>($"/api/items/fxArticle/{id}");

        read.GetProperty("data").GetProperty("translations").GetProperty("en").GetProperty("title").GetString().Should().Be("mixed-case title");
    }

    [Fact]
    public async Task An_invalid_translation_value_names_the_field_by_its_metadata_spelling()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();

        var response = await admin.PostAsJsonAsync("/api/items/fxArticle", new Dictionary<string, object>
        {
            ["status"] = "draft",
            ["translations"] = new Dictionary<string, object>
            {
                ["en"] = new Dictionary<string, object> { ["tITLE"] = InvalidTitleValue },
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error");
        error.GetProperty("message").GetString().Should().Contain("'title'").And.NotContain("tITLE");
        var detail = error.GetProperty("details")[0];
        detail.GetProperty("field").GetString().Should().Be("title");
        detail.GetProperty("message").GetString().Should().Contain("'title'").And.NotContain("tITLE");
    }

    private static async Task<string> SeedCategoryWithArticlesAsync(HttpClient admin)
    {
        var category = await admin.PostAsJsonAsync("/api/items/fxCategory", new { name = "cat-" + Guid.NewGuid().ToString("N")[..6] });
        var categoryId = (await category.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("id").GetString();
        foreach (var status in PublishedThenDraft)
            await admin.PostAsJsonAsync("/api/items/fxArticle", new Dictionary<string, object>
            {
                ["status"] = status,
                ["categoryId"] = categoryId!,
                ["translations"] = new Dictionary<string, object>
                {
                    ["en"] = new Dictionary<string, object> { ["title"] = "n-" + status },
                },
            });

        return categoryId!;
    }

    [Fact]
    public async Task Nested_sort_field_names_in_any_case_add_no_cache_key_and_sort()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var categoryId = await SeedCategoryWithArticlesAsync(admin);

        foreach (var spelling in StatusSpellings)
        {
            var response = await admin.PostAsJsonAsync("/api/items/fxCategory/query", new Dictionary<string, object>
            {
                ["filter"] = new Dictionary<string, object> { ["id"] = new Dictionary<string, object> { ["_eq"] = categoryId! } },
                ["deep"] = new Dictionary<string, object>
                {
                    ["articles"] = new Dictionary<string, object> { ["sort"] = new[] { spelling } },
                },
            });
            response.StatusCode.Should().Be(HttpStatusCode.OK, spelling);
            var data = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
            var statuses = data[0].GetProperty("articles").EnumerateArray()
                .Select(a => a.GetProperty("status").GetString()).ToList();
            var expected = spelling.StartsWith('-') ? PublishedThenDraft : DraftThenPublished;
            statuses.Should().Equal(expected, spelling);
        }

        PropertyAccessorCache.NamesFor(typeof(FxArticle))
            .Should().NotIntersectWith(StatusSpellings.Select(s => s.TrimStart('-')));
    }

    [Fact]
    public async Task A_graphql_nested_sort_in_any_case_adds_no_cache_key()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var categoryId = await SeedCategoryWithArticlesAsync(admin);

        foreach (var spelling in GraphqlSortSpellings)
        {
            var response = await admin.PostAsJsonAsync("/graphql", new
            {
                query = $"{{ fxCategories(filter: {{ id: {{ eq: \"{categoryId}\" }} }}) {{ items {{ articles(sort: [\"{spelling}\"]) {{ id }} }} }} }}",
            });
            var raw = await response.Content.ReadAsStringAsync();
            raw.Should().NotContain("\"errors\"", spelling);
        }

        PropertyAccessorCache.NamesFor(typeof(FxArticle)).Should().NotContain(GraphqlSortNames);
    }

    [Fact]
    public async Task An_unknown_nested_sort_field_is_rejected()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();

        var response = await admin.PostAsJsonAsync("/api/items/fxCategory/query", new Dictionary<string, object>
        {
            ["deep"] = new Dictionary<string, object>
            {
                ["articles"] = new Dictionary<string, object> { ["sort"] = UnknownSortField },
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
