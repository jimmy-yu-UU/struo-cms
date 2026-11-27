using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;
using static Struo.Tests.Api.RelationWriteHarness;

namespace Struo.Tests.Api;

// A body key that writes a link to another collection (a many-to-many array, a many-to-one foreign key) needs
// read on that target collection. The refusal comes before any id is parsed or looked up, so it is the same for
// an existing, a missing and a malformed id.
[Collection("ApiIntegration")]
public class RelationWriteTargetReadTests(ApiFactory factory)
{
    private const string TagsKey = "tags";
    private const string GlossaryKey = "glossaryId";
    private const string Article = "fxArticle";
    private const string Lexicon = "fxLexicon";

    private readonly RelationWriteHarness h = new(factory);

    private async Task<(HttpClient Client, Guid Tag)> TagCallerAsync(bool readsTag)
    {
        var read = readsTag ? new[] { Article, "fxTag" } : [Article];
        return (await h.CallerAsync(read, [Article]), await h.SeedTagAsync());
    }

    [Theory]
    [InlineData(Rest, Post)]
    [InlineData(Rest, Put)]
    [InlineData(Gql, Post)]
    [InlineData(Gql, Put)]
    public async Task Many_to_many_write_without_target_read_is_refused_alike_for_every_id(string channel, string op)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var (client, tag) = await TagCallerAsync(readsTag: false);
        var articlesBefore = await h.ArticleCountAsync();
        var outcomes = new List<WriteOutcome>();
        var seeded = 0;
        foreach (var kind in IdKinds)
        {
            Guid? id = null;
            if (op == Put)
            {
                id = await SeedArticleAsync(admin);
                seeded++;
            }
            outcomes.Add(await SendAsync(
                client, channel, Article, id, new() { [TagsKey] = new[] { IdOf(kind, tag) } }, translated: true));
        }

        AssertRefusedAlike(outcomes, channel, "fxTag");
        (await h.LinksToTagAsync(tag)).Should().Be(0);
        (await h.ArticleCountAsync()).Should().Be(articlesBefore + seeded);
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task Empty_many_to_many_array_without_target_read_is_refused_and_keeps_the_links(string channel)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var (client, tag) = await TagCallerAsync(readsTag: false);
        var article = await SeedArticleAsync(admin, tag);

        var outcome = await SendAsync(client, channel, Article, article, new() { [TagsKey] = Array.Empty<string>() });

        AssertRefusedAlike([outcome], channel, "fxTag");
        (await h.LinkCountAsync(article)).Should().Be(1);
    }

    [Theory]
    [InlineData(Rest, TagsKey)]
    [InlineData(Gql, "tagsLinks")]
    public async Task Junction_payload_write_without_target_read_is_refused_on_the_target(string channel, string key)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var client = await h.CallerAsync([Article], [Article, "fxArticleTag"]);
        var tag = await h.SeedTagAsync();
        var article = await SeedArticleAsync(admin);

        var outcome = await SendAsync(
            client, channel, Article, article, new() { [key] = new object[] { new { id = tag, note = "n" } } });

        AssertRefusedAlike([outcome], channel, "fxTag");
        (await h.LinkCountAsync(article)).Should().Be(0);
    }

    [Theory]
    [InlineData(Rest, Post)]
    [InlineData(Rest, Put)]
    [InlineData(Gql, Post)]
    [InlineData(Gql, Put)]
    public async Task Many_to_many_write_with_target_read_links_and_still_rejects_a_missing_id(string channel, string op)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var (client, tag) = await TagCallerAsync(readsTag: true);
        Guid? id = op == Put ? await SeedArticleAsync(admin) : null;

        var linked = await SendAsync(client, channel, Article, id, new() { [TagsKey] = new[] { tag.ToString() } }, translated: true);
        var missing = await SendAsync(
            client, channel, Article, id, new() { [TagsKey] = new[] { IdOf(Missing, tag) } }, translated: true);

        AssertWritten(linked);
        (await h.LinksToTagAsync(tag)).Should().Be(1);
        missing.Code.Should().Be("BAD_USER_INPUT");
        missing.Message.Should().Be("One or more ids in 'tags' do not exist in 'fxTag'.");
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task Update_without_the_relation_key_needs_no_target_read_and_keeps_the_links(string channel)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var (client, tag) = await TagCallerAsync(readsTag: false);
        var article = await SeedArticleAsync(admin, tag);

        var outcome = await SendAsync(client, channel, Article, article, new() { ["status"] = "published" });

        AssertWritten(outcome);
        (await h.LinkCountAsync(article)).Should().Be(1);
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task Super_admin_writes_a_many_to_many_without_a_role_grant(string channel)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var tag = await h.SeedTagAsync();
        var article = await SeedArticleAsync(admin);

        var linked = await SendAsync(admin, channel, Article, article, new() { [TagsKey] = new[] { tag.ToString() } });
        var missing = await SendAsync(
            admin, channel, Article, article, new() { [TagsKey] = new[] { IdOf(Missing, tag) } });

        AssertWritten(linked);
        missing.Code.Should().Be("BAD_USER_INPUT");
    }

    [Theory]
    [InlineData(Rest, Post)]
    [InlineData(Rest, Put)]
    [InlineData(Gql, Post)]
    [InlineData(Gql, Put)]
    public async Task Many_to_one_write_without_target_read_is_refused_alike_for_every_id(string channel, string op)
    {
        var client = await h.CallerAsync([Lexicon], [Lexicon]);
        var glossary = await h.SeedGlossaryAsync();
        var lexicons = await h.LexiconCountAsync();
        var outcomes = new List<WriteOutcome>();
        var seeded = new List<Guid>();
        foreach (var kind in IdKinds.Append("null"))
        {
            Guid? id = null;
            if (op == Put)
            {
                id = await h.SeedLexiconAsync(glossary);
                seeded.Add(id.Value);
            }
            var value = kind == "null" ? null : IdOf(kind, glossary);
            var fields = new Dictionary<string, object?> { [GlossaryKey] = value };
            if (op == Post) fields["name"] = "L";
            outcomes.Add(await SendAsync(client, channel, Lexicon, id, fields));
        }

        AssertRefusedAlike(outcomes, channel, "fxGlossary");
        if (op == Post) (await h.LexiconCountAsync()).Should().Be(lexicons);
        foreach (var id in seeded) (await h.LexiconAsync(id)).GlossaryId.Should().Be(glossary);
    }

    [Fact]
    public async Task Many_to_one_key_is_matched_ignoring_case()
    {
        var client = await h.CallerAsync([Lexicon], [Lexicon]);
        var lexicon = await h.SeedLexiconAsync();
        var glossary = await h.SeedGlossaryAsync();

        var outcome = await SendAsync(client, Rest, Lexicon, lexicon, new() { ["GlossaryId"] = glossary.ToString() });

        AssertRefusedAlike([outcome], Rest, "fxGlossary");
        (await h.LexiconAsync(lexicon)).GlossaryId.Should().BeNull();
    }

    [Theory]
    [InlineData(Rest, Post)]
    [InlineData(Rest, Put)]
    [InlineData(Gql, Post)]
    [InlineData(Gql, Put)]
    public async Task Many_to_one_write_with_target_read_links(string channel, string op)
    {
        var client = await h.CallerAsync([Lexicon, "fxGlossary"], [Lexicon]);
        var glossary = await h.SeedGlossaryAsync();
        Guid? id = op == Put ? await h.SeedLexiconAsync() : null;
        var fields = new Dictionary<string, object?> { [GlossaryKey] = glossary.ToString() };
        if (op == Post) fields["name"] = "L";

        var outcome = await SendAsync(client, channel, Lexicon, id, fields);

        AssertWritten(outcome);
        if (id is { } stored) (await h.LexiconAsync(stored)).GlossaryId.Should().Be(glossary);
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task Many_to_one_update_without_the_key_needs_no_target_read(string channel)
    {
        var client = await h.CallerAsync([Lexicon], [Lexicon]);
        var glossary = await h.SeedGlossaryAsync();
        var lexicon = await h.SeedLexiconAsync(glossary);

        var outcome = await SendAsync(client, channel, Lexicon, lexicon, new() { ["name"] = "renamed" });

        AssertWritten(outcome);
        (await h.LexiconAsync(lexicon)).GlossaryId.Should().Be(glossary);
    }

    [Fact]
    public async Task Super_admin_writes_a_many_to_one_without_a_role_grant()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var glossary = await h.SeedGlossaryAsync();
        var lexicon = await h.SeedLexiconAsync();

        var outcome = await SendAsync(admin, Rest, Lexicon, lexicon, new() { [GlossaryKey] = glossary.ToString() });

        AssertWritten(outcome);
        (await h.LexiconAsync(lexicon)).GlossaryId.Should().Be(glossary);
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task Assigning_roles_without_role_read_is_refused(string channel)
    {
        var client = await h.CallerAsync(["user"], ["user"]);
        var user = await h.SeedPlainUserAsync();
        var role = await h.SeedPlainRoleAsync();

        var outcome = await SendAsync(client, channel, "user", user, new() { ["roles"] = new[] { role.ToString() } });

        AssertRefusedAlike([outcome], channel, "role");
        (await h.RoleLinkCountAsync(user)).Should().Be(0);
    }

    [Fact]
    public async Task Assigning_a_plain_role_with_role_read_is_allowed()
    {
        var client = await h.CallerAsync(["user", "role"], ["user"]);
        var user = await h.SeedPlainUserAsync();
        var role = await h.SeedPlainRoleAsync();

        var outcome = await SendAsync(client, Rest, "user", user, new() { ["roles"] = new[] { role.ToString() } });

        AssertWritten(outcome);
        (await h.RoleLinkCountAsync(user)).Should().Be(1);
    }

    [Fact]
    public async Task Moving_a_file_into_a_folder_without_folder_read_is_refused()
    {
        var client = await h.CallerAsync(["file"], ["file"]);
        var folder = await h.SeedFolderAsync();
        var file = await h.SeedFileAsync();

        var outcome = await SendAsync(client, Rest, "file", file, new() { ["folderId"] = folder.ToString() });

        AssertRefusedAlike([outcome], Rest, "mediaFolder");
        (await h.FileAsync(file)).FolderId.Should().BeNull();
    }

    [Fact]
    public async Task Moving_a_file_into_a_folder_with_folder_read_succeeds()
    {
        var client = await h.CallerAsync(["file", "mediaFolder"], ["file"]);
        var folder = await h.SeedFolderAsync();
        var file = await h.SeedFileAsync();

        var outcome = await SendAsync(client, Rest, "file", file, new() { ["folderId"] = folder.ToString() });

        AssertWritten(outcome);
        (await h.FileAsync(file)).FolderId.Should().Be(folder);
    }
}
