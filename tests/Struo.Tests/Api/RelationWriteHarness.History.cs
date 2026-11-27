using System.Net.Http.Json;
using System.Text.Json;
using SqlSugar;
using Struo.Infrastructure.Files;
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

    /// <summary>An article inserted directly whose category, hero image, gallery and Open Graph image are all <paramref name="dangling"/>.</summary>
    public async Task<Guid> SeedDanglingArticleAsync(Guid dangling) => await WithDbAsync(async db =>
    {
        var id = Guid.CreateVersion7();
        await db.Insertable(new FxArticle
        {
            Id = id, CategoryId = dangling, HeroImageId = dangling, Gallery = [dangling],
        }).ExecuteCommandAsync();
        await db.Insertable(new FxArticleTranslation
        {
            FxArticleId = id, Locale = "en", Title = "dangling", SeoOgImageId = dangling,
        }).ExecuteCommandAsync();
        return id;
    });

    /// <summary>A category inserted directly, under <paramref name="parent"/> when given (the row need not exist).</summary>
    public async Task<Guid> SeedCategoryAsync(Guid? parent) => await WithDbAsync(async db =>
    {
        var id = Guid.CreateVersion7();
        await db.Insertable(new FxCategory { Id = id, Name = "category", ParentId = parent }).ExecuteCommandAsync();
        return id;
    });

    public Task<FxCategory> CategoryAsync(Guid id) =>
        WithDbAsync(db => db.Queryable<FxCategory>().Where(c => c.Id == id).FirstAsync());

    public Task<int> CategoryCountAsync() => WithDbAsync(db => db.Queryable<FxCategory>().CountAsync());

    public Task<MediaFolder> FolderAsync(Guid id) =>
        WithDbAsync(db => db.Queryable<MediaFolder>().Where(f => f.Id == id).FirstAsync());

    public Task<int> FolderCountAsync() => WithDbAsync(db => db.Queryable<MediaFolder>().CountAsync());

    /// <summary>Removes the rows from the database, as a purge does.</summary>
    public Task PurgeAsync(Guid? category = null, Guid? file = null, Guid? tag = null) => WithDbAsync(async db =>
    {
        if (category is { } c) await db.Deleteable<FxCategory>().Where(x => x.Id == c).ExecuteCommandAsync();
        if (file is { } f) await db.Deleteable<Struo.Infrastructure.Files.File>().Where(x => x.Id == f).ExecuteCommandAsync();
        if (tag is { } t) await db.Deleteable<FxTag>().Where(x => x.Id == t).ExecuteCommandAsync();
        return 0;
    });

    /// <summary>How many translation rows of the article hold <paramref name="locale"/>.</summary>
    public Task<int> TranslationRowsAsync(Guid article, string locale) => WithDbAsync(db =>
        db.Queryable<FxArticleTranslation>().Where(t => t.FxArticleId == article && t.Locale == locale).CountAsync());

    /// <summary>Moves a row of a soft-deletable collection to the trash.</summary>
    public static async Task TrashAsync(HttpClient admin, string collection, Guid id) =>
        (await admin.DeleteAsync($"/api/items/{collection}/{id}")).EnsureSuccessStatusCode();

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

    /// <summary>The <c>data</c> of an item read over REST.</summary>
    public async Task<JsonElement> ReadItemAsync(HttpClient client, string collection, Guid id)
    {
        var root = await client.GetFromJsonAsync<JsonElement>($"/api/items/{collection}/{id}");
        return root.GetProperty("data");
    }

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
