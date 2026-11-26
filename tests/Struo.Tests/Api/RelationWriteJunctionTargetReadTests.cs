using System.Net;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;
using static Struo.Tests.Api.RelationWriteHarness;

namespace Struo.Tests.Api;

// A junction collection is itself a collection, so a write to it can carry the two keys of the many-to-many it
// joins. Each key links a row of the collection it points at (the parent key the owner, the target key the
// target) and needs read on that collection, decided before any id is parsed or looked up.
[Collection("ApiIntegration")]
public class RelationWriteJunctionTargetReadTests(ApiFactory factory)
{
    private const string ArticleTag = "fxArticleTag";
    private const string UserRoleCollection = "userRole";
    private const string Article = "fxArticle";
    private const string Tag = "fxTag";
    private const string User = "user";
    private const string Role = "role";
    private const string WriteRefusal = "Write not permitted.";

    private readonly RelationWriteHarness h = new(factory);

    private static Dictionary<string, object?> TagLink(Guid article, string tag) =>
        new() { ["articleId"] = article.ToString(), ["tagId"] = tag };

    private static Dictionary<string, object?> RoleLink(Guid user, string role) =>
        new() { ["userId"] = user.ToString(), ["roleId"] = role };

    [Theory]
    [InlineData(Rest, Post)]
    [InlineData(Rest, Put)]
    [InlineData(Gql, Post)]
    [InlineData(Gql, Put)]
    public async Task Article_tag_write_without_tag_read_is_refused_alike_for_every_id(string channel, string op)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var client = await h.CallerAsync([Article], [ArticleTag]);
        var tag = await h.SeedTagAsync();
        var kept = await h.SeedTagAsync();
        var article = await h.SeedArticleAsync(admin);
        var rowsBefore = await h.LinkRowCountAsync();
        var outcomes = new List<WriteOutcome>();
        var links = new List<Guid>();
        foreach (var kind in IdKinds)
        {
            Guid? id = null;
            if (op == Put)
            {
                id = await h.SeedLinkAsync(article, kept);
                links.Add(id.Value);
            }
            var fields = op == Post ? TagLink(article, IdOf(kind, tag)) : new() { ["tagId"] = IdOf(kind, tag) };
            outcomes.Add(await h.SendAsync(client, channel, ArticleTag, id, fields));
        }

        AssertRefusedAlike(outcomes, channel, Tag);
        (await h.LinksToTagAsync(tag)).Should().Be(0);
        (await h.LinkRowCountAsync()).Should().Be(rowsBefore + links.Count);
        foreach (var link in links) (await h.LinkAsync(link)).TagId.Should().Be(kept);
    }

    [Theory]
    [InlineData(Rest, Post)]
    [InlineData(Rest, Put)]
    [InlineData(Gql, Post)]
    [InlineData(Gql, Put)]
    public async Task User_role_write_without_role_read_is_refused_alike_for_every_id(string channel, string op)
    {
        var client = await h.CallerAsync([User], [User]);
        var user = await h.SeedPlainUserAsync();
        var role = await h.SeedPlainRoleAsync();
        var kept = await h.SeedPlainRoleAsync();
        var rowsBefore = await h.UserRoleRowCountAsync();
        var outcomes = new List<WriteOutcome>();
        var links = new List<Guid>();
        foreach (var kind in IdKinds)
        {
            Guid? id = null;
            if (op == Put)
            {
                id = await h.SeedUserRoleAsync(await h.SeedPlainUserAsync(), kept);
                links.Add(id.Value);
            }
            var fields = op == Post ? RoleLink(user, IdOf(kind, role)) : new() { ["roleId"] = IdOf(kind, role) };
            outcomes.Add(await h.SendAsync(client, channel, UserRoleCollection, id, fields));
        }

        AssertRefusedAlike(outcomes, channel, Role);
        (await h.UserRoleRowCountAsync()).Should().Be(rowsBefore + links.Count);
        foreach (var link in links) (await h.UserRoleAsync(link)).RoleId.Should().Be(kept);
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task User_role_write_without_user_read_is_refused_on_the_user(string channel)
    {
        var client = await h.CallerAsync([Role], [User]);
        var user = await h.SeedPlainUserAsync();
        var role = await h.SeedPlainRoleAsync();

        var outcome = await h.SendAsync(client, channel, UserRoleCollection, null, RoleLink(user, role.ToString()));

        AssertRefusedAlike([outcome], channel, User);
        (await h.RoleLinkCountAsync(user)).Should().Be(0);
    }

    [Fact]
    public async Task Article_tag_key_is_matched_ignoring_case()
    {
        var client = await h.CallerAsync([Article], [ArticleTag]);
        var article = await h.SeedArticleAsync(await factory.CreateAuthenticatedClientAsync());
        var tag = await h.SeedTagAsync();
        var link = await h.SeedLinkAsync(article, await h.SeedTagAsync());

        var outcome = await h.SendAsync(client, Rest, ArticleTag, link, new() { ["TagId"] = tag.ToString() });

        AssertRefusedAlike([outcome], Rest, Tag);
        (await h.LinksToTagAsync(tag)).Should().Be(0);
    }

    [Theory]
    [InlineData(Rest, Post)]
    [InlineData(Rest, Put)]
    [InlineData(Gql, Post)]
    [InlineData(Gql, Put)]
    public async Task Article_tag_write_with_both_reads_stores_the_link(string channel, string op)
    {
        var client = await h.CallerAsync([Article, Tag], [ArticleTag]);
        var article = await h.SeedArticleAsync(await factory.CreateAuthenticatedClientAsync());
        var tag = await h.SeedTagAsync();
        Guid? id = op == Put ? await h.SeedLinkAsync(article, await h.SeedTagAsync()) : null;

        var outcome = await h.SendAsync(
            client, channel, ArticleTag, id, op == Post ? TagLink(article, tag.ToString()) : new() { ["tagId"] = tag.ToString() });

        AssertWritten(outcome);
        (await h.LinksToTagAsync(tag)).Should().Be(1);
    }

    [Theory]
    [InlineData(Rest, Post)]
    [InlineData(Rest, Put)]
    [InlineData(Gql, Post)]
    [InlineData(Gql, Put)]
    public async Task User_role_write_with_both_reads_stores_the_link(string channel, string op)
    {
        var client = await h.CallerAsync([User, Role], [User]);
        var user = await h.SeedPlainUserAsync();
        var role = await h.SeedPlainRoleAsync();
        Guid? id = op == Put ? await h.SeedUserRoleAsync(user, await h.SeedPlainRoleAsync()) : null;

        var outcome = await h.SendAsync(
            client, channel, UserRoleCollection, id, op == Post ? RoleLink(user, role.ToString()) : new() { ["roleId"] = role.ToString() });

        AssertWritten(outcome);
        if (id is { } stored) (await h.UserRoleAsync(stored)).RoleId.Should().Be(role);
        else (await h.RoleLinkCountAsync(user)).Should().Be(1);
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task Article_tag_write_without_a_key_needs_no_target_read(string channel)
    {
        var client = await h.CallerAsync([Article], [ArticleTag]);
        var article = await h.SeedArticleAsync(await factory.CreateAuthenticatedClientAsync());
        var tag = await h.SeedTagAsync();
        var link = await h.SeedLinkAsync(article, tag);

        var outcome = await h.SendAsync(client, channel, ArticleTag, link, new() { ["note"] = "n" });

        AssertWritten(outcome);
        (await h.LinkAsync(link)).TagId.Should().Be(tag);
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task Article_tag_write_without_junction_write_is_refused_on_write_first(string channel)
    {
        var client = await h.CallerAsync([Article], []);
        var article = await h.SeedArticleAsync(await factory.CreateAuthenticatedClientAsync());
        var tag = await h.SeedTagAsync();

        var outcome = await h.SendAsync(client, channel, ArticleTag, null, TagLink(article, tag.ToString()));

        outcome.Code.Should().Be("FORBIDDEN");
        outcome.Message.Should().Be(WriteRefusal);
        (await h.LinksToTagAsync(tag)).Should().Be(0);
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task User_role_write_without_user_write_is_refused_on_write_first(string channel)
    {
        var client = await h.CallerAsync([User], []);
        var user = await h.SeedPlainUserAsync();
        var role = await h.SeedPlainRoleAsync();

        var outcome = await h.SendAsync(client, channel, UserRoleCollection, null, RoleLink(user, role.ToString()));

        outcome.Code.Should().Be("FORBIDDEN");
        outcome.Message.Should().Be(WriteRefusal);
        (await h.RoleLinkCountAsync(user)).Should().Be(0);
    }

    [Fact]
    public async Task Super_admin_writes_a_junction_without_a_role_grant()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var article = await h.SeedArticleAsync(admin);
        var tag = await h.SeedTagAsync();

        var outcome = await h.SendAsync(admin, Rest, ArticleTag, null, TagLink(article, tag.ToString()));

        AssertWritten(outcome);
        (await h.LinksToTagAsync(tag)).Should().Be(1);
        outcome.Status.Should().Be(HttpStatusCode.Created);
    }
}
