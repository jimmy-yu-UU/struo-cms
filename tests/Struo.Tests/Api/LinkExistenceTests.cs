using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;
using static Struo.Tests.Api.RelationWriteHarness;

namespace Struo.Tests.Api;

// A many-to-one foreign key, and the two keys of a junction collection written directly, must name an existing
// row when they are newly assigned. An id equal to the stored one is not checked, a trashed row is not a valid
// new assignment, and null clears. The read refusal still comes first.
[Collection("ApiIntegration")]
public class LinkExistenceTests(ApiFactory factory)
{
    private const string Lexicon = "fxLexicon";
    private const string Category = "fxCategory";
    private const string GlossaryKey = "glossaryId";
    private const string GlossaryMissing = "The id in 'glossaryId' does not exist in 'fxGlossary'.";
    private const string ParentMissing = "The id in 'parentId' does not exist in 'fxCategory'.";

    private readonly RelationWriteHarness h = new(factory);

    private static string MissingId => IdOf(Missing, Guid.Empty);

    private static void AssertRefused(WriteOutcome outcome, string message)
    {
        outcome.Code.Should().Be("BAD_USER_INPUT", outcome.Body);
        outcome.Message.Should().Be(message);
    }

    [Theory]
    [InlineData(Rest, Post)]
    [InlineData(Rest, Put)]
    [InlineData(Gql, Post)]
    [InlineData(Gql, Put)]
    public async Task A_many_to_one_key_naming_a_missing_row_is_refused(string channel, string op)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        Guid? id = op == Put ? await h.SeedLexiconAsync() : null;
        var lexicons = await h.LexiconCountAsync();
        var fields = new Dictionary<string, object?> { [GlossaryKey] = MissingId };
        if (op == Post) fields["name"] = "L";

        var outcome = await h.SendAsync(admin, channel, Lexicon, id, fields);

        AssertRefused(outcome, GlossaryMissing);
        if (id is { } stored) (await h.LexiconAsync(stored)).GlossaryId.Should().BeNull();
        else (await h.LexiconCountAsync()).Should().Be(lexicons);
    }

    [Theory]
    [InlineData(Rest, Post)]
    [InlineData(Rest, Put)]
    [InlineData(Gql, Post)]
    [InlineData(Gql, Put)]
    public async Task A_many_to_one_key_naming_an_existing_row_is_stored(string channel, string op)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var glossary = await h.SeedGlossaryAsync();
        Guid? id = op == Put ? await h.SeedLexiconAsync() : null;
        var fields = new Dictionary<string, object?> { [GlossaryKey] = glossary.ToString() };
        if (op == Post) fields["name"] = "L";

        AssertWritten(await h.SendAsync(admin, channel, Lexicon, id, fields));

        if (id is { } stored) (await h.LexiconAsync(stored)).GlossaryId.Should().Be(glossary);
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task An_unchanged_dangling_many_to_one_key_is_accepted_when_another_field_changes(string channel)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var lexicon = await h.SeedLexiconAsync(Guid.Parse(MissingId));

        var outcome = await h.SendAsync(
            admin, channel, Lexicon, lexicon, new() { [GlossaryKey] = MissingId, ["name"] = "renamed" });

        AssertWritten(outcome);
        var stored = await h.LexiconAsync(lexicon);
        stored.Name.Should().Be("renamed");
        stored.GlossaryId.Should().Be(Guid.Parse(MissingId));
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task A_dangling_many_to_one_key_is_replaced_or_cleared_but_not_by_another_missing_row(string channel)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var glossary = await h.SeedGlossaryAsync();
        var lexicon = await h.SeedLexiconAsync(Guid.Parse(MissingId));
        var other = IdOf(Missing, Guid.Empty).Replace("11111111", "99999999");

        var refused = await h.SendAsync(admin, channel, Lexicon, lexicon, new() { [GlossaryKey] = other });
        var replaced = await h.SendAsync(admin, channel, Lexicon, lexicon, new() { [GlossaryKey] = glossary.ToString() });
        var cleared = await h.SendAsync(admin, channel, Lexicon, lexicon, new() { [GlossaryKey] = null });

        AssertRefused(refused, GlossaryMissing);
        AssertWritten(replaced);
        AssertWritten(cleared);
        (await h.LexiconAsync(lexicon)).GlossaryId.Should().BeNull();
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task A_trashed_row_is_not_a_valid_new_assignment_but_an_unchanged_one_is_accepted(string channel)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var parent = await h.SeedCategoryAsync();
        var child = await h.SeedCategoryAsync(parent);
        var other = await h.SeedCategoryAsync();
        await TrashAsync(admin, Category, parent);

        var assigned = await h.SendAsync(admin, channel, Category, other, new() { ["parentId"] = parent.ToString() });
        var unchanged = await h.SendAsync(
            admin, channel, Category, child, new() { ["parentId"] = parent.ToString(), ["name"] = "renamed" });

        AssertRefused(assigned, ParentMissing);
        AssertWritten(unchanged);
        (await h.CategoryAsync(other)).ParentId.Should().BeNull();
        (await h.CategoryAsync(child)).ParentId.Should().Be(parent);
    }

    [Theory]
    [InlineData(Rest, Post)]
    [InlineData(Rest, Put)]
    [InlineData(Gql, Post)]
    [InlineData(Gql, Put)]
    public async Task A_self_referencing_key_naming_a_missing_row_is_refused(string channel, string op)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        Guid? id = op == Put ? await h.SeedCategoryAsync() : null;
        var before = await h.CategoryCountAsync();
        var fields = new Dictionary<string, object?> { ["parentId"] = MissingId };
        if (op == Post) fields["name"] = "C";

        var outcome = await h.SendAsync(admin, channel, Category, id, fields);

        AssertRefused(outcome, ParentMissing);
        if (id is { } stored) (await h.CategoryAsync(stored)).ParentId.Should().BeNull();
        else (await h.CategoryCountAsync()).Should().Be(before);
    }

    [Theory]
    [InlineData(Post)]
    [InlineData(Put)]
    public async Task A_media_folder_parent_naming_a_missing_row_is_refused(string op)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        Guid? id = op == Put ? await h.SeedFolderAsync() : null;
        var before = await h.FolderCountAsync();
        var fields = new Dictionary<string, object?> { ["parentId"] = MissingId };
        if (op == Post) fields["name"] = "F";

        var outcome = await h.SendAsync(admin, Rest, "mediaFolder", id, fields);

        AssertRefused(outcome, "The id in 'parentId' does not exist in 'mediaFolder'.");
        if (id is { } stored) (await h.FolderAsync(stored)).ParentId.Should().BeNull();
        else (await h.FolderCountAsync()).Should().Be(before);
    }

    [Fact]
    public async Task A_file_moved_into_a_missing_folder_is_refused_and_into_an_existing_one_is_stored()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var folder = await h.SeedFolderAsync();
        var file = await h.SeedFileAsync();

        var missing = await h.SendAsync(admin, Rest, "file", file, new() { ["folderId"] = MissingId });
        var existing = await h.SendAsync(admin, Rest, "file", file, new() { ["folderId"] = folder.ToString() });

        AssertRefused(missing, "The id in 'folderId' does not exist in 'mediaFolder'.");
        AssertWritten(existing);
        (await h.FileAsync(file)).FolderId.Should().Be(folder);
    }

    [Theory]
    [InlineData(Rest, "articleId", "fxArticle")]
    [InlineData(Rest, "tagId", "fxTag")]
    [InlineData(Gql, "articleId", "fxArticle")]
    [InlineData(Gql, "tagId", "fxTag")]
    public async Task A_junction_key_naming_a_missing_row_is_refused_on_create_and_update(
        string channel, string key, string target)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var tag = await h.SeedTagAsync();
        var article = await h.SeedArticleAsync(admin);
        var link = await h.SeedLinkAsync(article, tag);
        var rows = await h.LinkRowCountAsync();
        var keys = new Dictionary<string, object?> { ["articleId"] = article.ToString(), ["tagId"] = tag.ToString() };
        var message = $"The id in '{key}' does not exist in '{target}'.";

        keys[key] = MissingId;
        var created = await h.SendAsync(admin, channel, "fxArticleTag", null, new(keys));
        var updated = await h.SendAsync(admin, channel, "fxArticleTag", link, new() { [key] = MissingId });

        AssertRefused(created, message);
        AssertRefused(updated, message);
        (await h.LinkRowCountAsync()).Should().Be(rows);
        var stored = await h.LinkAsync(link);
        (stored.ArticleId, stored.TagId).Should().Be((article, tag));
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task A_junction_row_with_existing_keys_is_stored(string channel)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var (tag, article) = (await h.SeedTagAsync(), await h.SeedArticleAsync(admin));

        var outcome = await h.SendAsync(
            admin, channel, "fxArticleTag", null, new() { ["articleId"] = article.ToString(), ["tagId"] = tag.ToString() });

        AssertWritten(outcome);
        (await h.LinksToTagAsync(tag)).Should().Be(1);
    }

    [Fact]
    public async Task An_unchanged_dangling_junction_key_is_accepted_when_another_field_changes()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var article = await h.SeedArticleAsync(admin);
        var missing = Guid.Parse(MissingId);
        var link = await h.SeedLinkAsync(article, missing);

        var outcome = await h.SendAsync(
            admin, Rest, "fxArticleTag", link, new() { ["tagId"] = MissingId, ["note"] = "kept" });

        AssertWritten(outcome);
        (await h.LinkAsync(link)).Note.Should().Be("kept");
    }

    [Theory]
    [InlineData("userId", "user")]
    [InlineData("roleId", "role")]
    public async Task A_user_role_key_naming_a_missing_row_is_refused(string key, string target)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var (user, role) = (await h.SeedPlainUserAsync(), await h.SeedPlainRoleAsync());
        var rows = await h.UserRoleRowCountAsync();
        var keys = new Dictionary<string, object?> { ["userId"] = user.ToString(), ["roleId"] = role.ToString() };
        keys[key] = MissingId;

        var outcome = await h.SendAsync(admin, Rest, "userRole", null, keys);

        AssertRefused(outcome, $"The id in '{key}' does not exist in '{target}'.");
        (await h.UserRoleRowCountAsync()).Should().Be(rows);
    }

    [Fact]
    public async Task The_read_refusal_comes_before_the_existence_check()
    {
        var reader = await h.CallerAsync([Lexicon, "fxGlossary"], [Lexicon]);
        var blind = await h.CallerAsync([Lexicon], [Lexicon]);
        var lexicon = await h.SeedLexiconAsync();
        var outcomes = new List<WriteOutcome>();
        foreach (var kind in IdKinds)
            outcomes.Add(await h.SendAsync(blind, Rest, Lexicon, lexicon, new() { [GlossaryKey] = IdOf(kind, Guid.NewGuid()) }));

        var seen = await h.SendAsync(reader, Rest, Lexicon, lexicon, new() { [GlossaryKey] = MissingId });

        AssertRefusedAlike(outcomes, Rest, "fxGlossary");
        AssertRefused(seen, GlossaryMissing);
    }
}
