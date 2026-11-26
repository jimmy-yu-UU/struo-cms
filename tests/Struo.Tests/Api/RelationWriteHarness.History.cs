using System.Net.Http.Json;
using System.Text.Json;
using SqlSugar;
using Struo.Tests.Fixtures;

namespace Struo.Tests.Api;

/// <summary>The relations and fields of an article that revisions and reads are measured on.</summary>
internal sealed record ArticleLinks(
    string Status, string Title, Guid? Category, Guid[] Tags, Guid? Hero, Guid[] Gallery, Guid? OgImage);

/// <summary>An article with two revisions, and the rows its two states link.</summary>
internal sealed record ArticleHistory(
    Guid Article, Guid Category1, Guid Category2, Guid Tag1, Guid Tag2, Guid File1, Guid File2)
{
    /// <summary>The state revision 1 holds.</summary>
    public ArticleLinks First => new("draft", "v1", Category1, [Tag1], File1, [File1], File1);

    /// <summary>The state revision 2 holds.</summary>
    public ArticleLinks Second => new("published", "v2", Category2, [Tag2], File2, [File2], File2);
}

internal sealed partial class RelationWriteHarness
{
    public const string RevisionOne = "1";

    /// <summary>An article created in the state <see cref="ArticleHistory.First"/> and updated to <see cref="ArticleHistory.Second"/>.</summary>
    public async Task<ArticleHistory> SeedHistoryAsync(HttpClient admin)
    {
        var category1 = await SeedCategoryAsync();
        var category2 = await SeedCategoryAsync();
        var (tag1, tag2) = (await SeedTagAsync(), await SeedTagAsync());
        var (file1, file2) = (await SeedFileAsync(), await SeedFileAsync());
        var history = new ArticleHistory(Guid.Empty, category1, category2, tag1, tag2, file1, file2);

        var created = await admin.PostAsJsonAsync("/api/items/fxArticle", Body(history.First));
        created.EnsureSuccessStatusCode();
        var root = await created.Content.ReadFromJsonAsync<JsonElement>();
        var article = Guid.Parse(root.GetProperty("data").GetProperty("id").GetString()!);
        var updated = await admin.PutAsJsonAsync($"/api/items/fxArticle/{article}", Body(history.Second));
        updated.EnsureSuccessStatusCode();
        return history with { Article = article };
    }

    private static object Body(ArticleLinks links) => new
    {
        status = links.Status,
        categoryId = links.Category,
        tags = links.Tags,
        heroImageId = links.Hero,
        gallery = links.Gallery,
        translations = new { en = new { title = links.Title, seoOgImageId = links.OgImage } },
    };

    public async Task<Guid> SeedCategoryAsync() => await WithDbAsync(async db =>
    {
        var id = Guid.CreateVersion7();
        await db.Insertable(new FxCategory { Id = id, Name = "category" }).ExecuteCommandAsync();
        return id;
    });

    /// <summary>The relations and fields an article stores, read from the database.</summary>
    public Task<ArticleLinks> StoredLinksAsync(Guid article) => WithDbAsync(async db =>
    {
        var row = await db.Queryable<FxArticle>().Where(a => a.Id == article).FirstAsync();
        var tags = await db.Queryable<FxArticleTag>().Where(l => l.ArticleId == article).Select(l => l.TagId).ToListAsync();
        var en = await db.Queryable<FxArticleTranslation>()
            .Where(t => t.FxArticleId == article && t.Locale == "en").FirstAsync();
        return new ArticleLinks(
            row.Status, en.Title, row.CategoryId, [.. tags], row.HeroImageId, [.. row.Gallery], en.SeoOgImageId);
    });

    /// <summary>Reverts the article to <paramref name="revision"/>.</summary>
    public async Task<WriteOutcome> RevertAsync(HttpClient client, string channel, Guid article, string revision)
    {
        if (channel == Rest)
            return await OutcomeAsync(
                await client.PostAsync($"/api/items/fxArticle/{article}/revisions/{revision}/revert", null),
                restEnvelope: true);
        var query = $"mutation {{ revertFxArticle(id: \"{article}\", revisionNumber: {revision}) {{ id }} }}";
        return await OutcomeAsync(await client.PostAsJsonAsync("/graphql", new { query }), restEnvelope: false);
    }

    /// <summary>The snapshot of a revision as the channel returns it.</summary>
    public async Task<JsonElement> SnapshotAsync(HttpClient client, string channel, Guid article, string revision)
    {
        if (channel == Rest)
        {
            var rest = await client.GetFromJsonAsync<JsonElement>($"/api/items/fxArticle/{article}/revisions/{revision}");
            return rest.GetProperty("data").GetProperty("snapshot");
        }
        var query = $"{{ fxArticleRevision(id: \"{article}\", revisionNumber: {revision}) {{ snapshot }} }}";
        var resp = await client.PostAsJsonAsync("/graphql", new { query });
        var root = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return root.GetProperty("data").GetProperty("fxArticleRevision").GetProperty("snapshot");
    }
}
