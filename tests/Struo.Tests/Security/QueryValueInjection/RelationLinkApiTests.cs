// tests/Struo.Tests/Security/QueryValueInjection/RelationLinkApiTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Application.Query;
using Struo.Infrastructure.Localization;
using Struo.Tests.Fixtures;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Security.QueryValueInjection;

/// <summary>The ids of a many-to-many write become the key type before any query, and there are never too many.</summary>
[Collection("ApiIntegration")]
public sealed class RelationLinkApiTests(ApiFactory factory) : IDisposable
{
    private SqlCapture? _capture;

    private SqlCapture Capture => _capture ??= SqlCapture.Attach(factory.Services);

    public void Dispose() => _capture?.Dispose();

    private ISqlSugarClient Db => factory.Services.GetRequiredService<ISqlSugarClient>();

    private static async Task<string> CreateAsync(HttpClient c, string collection, object body)
    {
        var r = await c.PostAsJsonAsync($"/api/items/{collection}", body);
        var text = await r.Content.ReadAsStringAsync();
        r.StatusCode.Should().Be(HttpStatusCode.Created, text);
        return JsonDocument.Parse(text).RootElement.GetProperty("data").GetProperty("id").GetString()!;
    }

    private static async Task<(HttpStatusCode Status, string Body)> PutTagsAsync(HttpClient c, string article, string tagsJson)
    {
        var r = await c.PutAsync($"/api/items/fxArticle/{article}",
            new StringContent($$"""{"tags":{{tagsJson}}}""", Encoding.UTF8, "application/json"));
        return (r.StatusCode, await r.Content.ReadAsStringAsync());
    }

    private async Task<List<Guid>> LinkedTagsAsync(string article) =>
        (await Db.Queryable<FxArticleTag>().Where(j => j.ArticleId == Guid.Parse(article)).ToListAsync())
        .Select(j => j.TagId).OrderBy(g => g).ToList();

    private async Task<(HttpClient Admin, string Article, string Tag)> SeedAsync()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var tag = await CreateAsync(admin, "fxTag", new { name = "qv-" + Guid.NewGuid().ToString("N")[..8] });
        var article = await CreateAsync(admin, "fxArticle", new { status = "draft", translations = new { en = new { title = "qv" } } });
        (await PutTagsAsync(admin, article, $"[\"{tag}\"]")).Status.Should().Be(HttpStatusCode.OK);
        return (admin, article, tag);
    }

    [Fact]
    public async Task A_mixed_array_is_a_400_and_the_stored_links_stay()
    {
        var (admin, article, tag) = await SeedAsync();
        var (editor, _) = await factory.CreateEditorClientAsync(["fxArticle"], ["fxArticle"]);
        const string marker = "struo_qv_marker_m2m";

        foreach (var (who, client) in new[] { ("admin", admin), ("editor", editor) })
        {
            foreach (var payload in new[] { "0) OR (1=1", "0); CREATE TABLE " + marker + " (x int); -- " })
            {
                Capture.Clear();
                var (status, _) = await PutTagsAsync(client, article, JsonSerializer.Serialize(new object[] { 1, payload }));
                status.Should().Be(HttpStatusCode.BadRequest, who);
                Capture.Matching("OR (1=1").Should().BeEmpty();
                Capture.Matching(marker).Should().BeEmpty();
            }
        }
        Db.DbMaintenance.IsAnyTable(marker, false).Should().BeFalse();
        (await LinkedTagsAsync(article)).Should().Equal(Guid.Parse(tag));
    }

    [Theory]
    [InlineData("[1]")]
    [InlineData("[\"not-a-guid\"]")]
    [InlineData("[1.5]")]
    [InlineData("[true]")]
    [InlineData("[null]")]
    [InlineData("[[\"x\"]]")]
    [InlineData("[{\"id\":\"0) OR (1=1\"}]")]
    public async Task An_id_that_is_not_a_key_of_the_target_is_a_400_before_any_lookup(string tags)
    {
        var (admin, article, tag) = await SeedAsync();
        Capture.Clear();
        var (status, body) = await PutTagsAsync(admin, article, tags);
        status.Should().Be(HttpStatusCode.BadRequest, body);
        Capture.Matching("fx_tags").Where(s => s.Contains(" IN ", StringComparison.OrdinalIgnoreCase)).Should().BeEmpty();
        (await LinkedTagsAsync(article)).Should().Equal(Guid.Parse(tag));
    }

    [Fact]
    public async Task The_same_key_written_two_ways_is_one_link()
    {
        var (admin, article, tag) = await SeedAsync();
        var guid = Guid.Parse(tag);
        var (status, body) = await PutTagsAsync(admin, article,
            JsonSerializer.Serialize(new[] { guid.ToString("N").ToUpperInvariant(), guid.ToString("D"), guid.ToString("B") }));
        status.Should().Be(HttpStatusCode.OK, body);
        (await LinkedTagsAsync(article)).Should().Equal(guid);
    }

    [Fact]
    public async Task Too_many_ids_are_a_400_naming_the_setting_and_the_limit_itself_is_allowed()
    {
        var (admin, article, tag) = await SeedAsync();
        var tooMany = Enumerable.Range(0, 1001).Select(_ => Guid.NewGuid().ToString()).ToArray();
        var (status, body) = await PutTagsAsync(admin, article, JsonSerializer.Serialize(tooMany));
        status.Should().Be(HttpStatusCode.BadRequest);
        body.Should().Contain("Query:MaxRelationLinks").And.Contain("1000");

        var atLimit = Enumerable.Range(0, 1000).Select(_ => Guid.NewGuid().ToString()).ToArray();
        var (limitStatus, limitBody) = await PutTagsAsync(admin, article, JsonSerializer.Serialize(atLimit));
        limitStatus.Should().Be(HttpStatusCode.BadRequest);
        limitBody.Should().Contain("do not exist");
        (await LinkedTagsAsync(article)).Should().Equal(Guid.Parse(tag));
    }

    [Fact]
    public async Task The_lookup_sites_refuse_a_value_of_the_wrong_type_without_running_a_statement()
    {
        using var scope = factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IItemRepository>();
        object[] mixed = [1L, "0) OR (1=1"];
        Capture.Clear();

        var a = () => repo.QueryWhereInAsync("language", "id", mixed);
        var b = () => repo.QueryWhereInWithDeletedAsync("language", "id", mixed);
        var c = () => repo.QueryWhereInFilteredAsync("language", "id", mixed, null, null);
        await a.Should().ThrowAsync<InvalidOperationException>();
        await b.Should().ThrowAsync<InvalidOperationException>();
        await c.Should().ThrowAsync<InvalidOperationException>();
        Capture.Matching("OR (1=1").Should().BeEmpty();

        var languages = await Db.Queryable<Language>().ToListAsync();
        object[] ids = languages.Select(l => (object)l.Id).ToArray();
        (await repo.QueryWhereInAsync("language", "id", ids)).Should().HaveCount(languages.Count);
        (await repo.QueryWhereInWithDeletedAsync("language", "id", ids)).Should().HaveCount(languages.Count);
        (await repo.QueryWhereInFilteredAsync("language", "id", ids, null, null)).Should().HaveCount(languages.Count);
    }

    [Fact]
    public async Task The_lookup_sites_refuse_text_that_would_parse_as_the_column_type()
    {
        using var scope = factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IItemRepository>();
        var id = (await Db.Queryable<Language>().FirstAsync()).Id;
        object[] asText = [id.ToString()];
        Capture.Clear();

        var a = () => repo.QueryWhereInAsync("language", "id", asText);
        var b = () => repo.QueryWhereInWithDeletedAsync("language", "id", asText);
        var c = () => repo.QueryWhereInFilteredAsync("language", "id", asText, null, null);
        await a.Should().ThrowAsync<InvalidOperationException>();
        await b.Should().ThrowAsync<InvalidOperationException>();
        await c.Should().ThrowAsync<InvalidOperationException>();
        Capture.Matching(id.ToString()).Should().BeEmpty();
    }
}
