// tests/Struo.Tests/Query/RelationWriteTests.cs
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

// NOTE: Tests that exercised Author (Restrict delete), Tag M2M, and ArticleFile gallery
// were deleted in Phase 5.5 when those entities were removed from the sample domain.
// Remaining: category SetNull relation (still present on Article.CategoryId).

[Collection("ApiIntegration")]
public class RelationWriteTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;
    private static async Task<string> Id(System.Net.Http.HttpResponseMessage r) =>
        Root(await r.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;

    [Fact]
    public async Task Delete_category_referenced_by_setnull_relation_is_allowed()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        // Article.categoryId is OnDelete.SetNull (not Restrict), so deleting a referenced category is permitted.
        var categoryId = await Id(await c.PostAsJsonAsync("/api/items/category", new { name = "Doomed" }));
        await c.PostAsJsonAsync("/api/items/article", new
        {
            status = "draft",
            categoryId,
            translations = new { en = new { title = "InCat" } }
        });

        (await c.DeleteAsync($"/api/items/category/{categoryId}")).StatusCode
            .Should().Be(System.Net.HttpStatusCode.NoContent);
    }
}
