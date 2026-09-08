using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using SqlSugar;
using Struo.Application.Changes;
using Struo.Application.Configuration;
using Struo.Infrastructure.Changes;
using Struo.Infrastructure.Persistence;
using Struo.Sample.Blog;
using Struo.Tests.Query;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Changes;

/// <summary>Spec §3.4 R1–R7 through the real ItemService on SQLite (PurgeIntegrityHarness fixtures: cascadeNode = Cascade, category.parent = SetNull).</summary>
public sealed class ItemServiceChangeNotificationTests : IDisposable
{
    private readonly RecordingItemChangeListener _listener = new();
    private readonly ListLogger<ItemChangeNotifier> _log = new();
    private readonly PurgeIntegrityHarness _h;

    public ItemServiceChangeNotificationTests()
    {
        _h = PurgeIntegrityHarness.Create(notifier: new ItemChangeNotifier([_listener], _log));
    }

    public void Dispose() => _h.Dispose();

    private static JsonElement Body(string json) { using var d = JsonDocument.Parse(json); return d.RootElement.Clone(); }

    // cascadeNode declares a translation sidecar with a Required Title field, so a create needs a
    // translations payload for the default locale ("en") even though `name` itself is an own field —
    // adapted from the brief's bare {"name":"…"} bodies to avoid the "translation required" 400
    // (matches the shape PurgeIntegrityHarness.CreateCascadeNodeAsync already uses).
    private static string CascadeNodeBody(string name, string? parentId = null)
    {
        var parentPart = parentId is null ? "" : $",\"parentId\":\"{parentId}\"";
        return $"{{\"name\":\"{name}\",\"translations\":{{\"en\":{{\"title\":\"{name}\"}}}}{parentPart}}}";
    }

    // Plain interpolation (not a raw string) — the JSON's own "}}" runs around "translations"/"en"
    // repeatedly collide with raw-interpolated-string brace-counting rules (see the CS9007 note on
    // Create_with_relations_and_translations_is_still_one_call below), so this sidesteps that class
    // of literal-vs-interpolation ambiguity entirely.
    private static string ArticleBody(string title, string tagId) =>
        $"{{\"status\":\"draft\",\"translations\":{{\"en\":{{\"title\":\"{title}\"}}}},\"tags\":[\"{tagId}\"]}}";

    private async Task<string> CreateAsync(string collection, string json)
    {
        var created = await _h.Service.CreateAsync(collection, Body(json));
        _listener.Calls.Clear();
        return created["id"]!.ToString()!;
    }

    private (string Collection, string Id, ItemChangeKind Kind) Only()
    {
        var call = _listener.Calls.Should().ContainSingle().Subject;
        var c = call.Should().ContainSingle().Subject;
        return (c.Collection, c.Id, c.Kind);
    }

    [Fact]
    public async Task Create_raises_one_Created_with_the_canonical_collection_and_lowercase_id()
    {
        var created = await _h.Service.CreateAsync("Category", Body("""{"name":"c"}"""));
        var id = created["id"]!.ToString()!;
        Only().Should().Be(("category", id.ToLowerInvariant(), ItemChangeKind.Created));
    }

    [Fact]
    public async Task Update_and_revert_raise_Updated()
    {
        var id = await CreateAsync("category", """{"name":"c"}""");
        await _h.Service.UpdateAsync("category", id, Body("""{"name":"c2"}"""));
        Only().Should().Be(("category", id, ItemChangeKind.Updated));

        _listener.Calls.Clear();
        var nodeId = await CreateAsync("cascadeNode", CascadeNodeBody("n"));
        await _h.Service.UpdateAsync("cascadeNode", nodeId, Body("""{"name":"n2"}"""));
        _listener.Calls.Clear();
        await _h.Service.RevertAsync("cascadeNode", nodeId, 1);
        Only().Should().Be(("cascadeNode", nodeId, ItemChangeKind.Updated));
    }

    [Fact]
    public async Task Trash_raises_Trashed_once_and_a_repeat_trash_raises_nothing()
    {
        var id = await CreateAsync("category", """{"name":"c"}""");
        await _h.Service.DeleteAsync("category", id.ToUpperInvariant());
        Only().Should().Be(("category", id, ItemChangeKind.Trashed));
        _listener.Calls.Clear();
        await _h.Service.DeleteAsync("category", id);
        _listener.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Restore_raises_Restored_once_and_a_repeat_restore_raises_nothing()
    {
        var id = await CreateAsync("category", """{"name":"c"}""");
        await _h.Service.DeleteAsync("category", id);
        _listener.Calls.Clear();
        await _h.Service.RestoreAsync("category", id);
        Only().Should().Be(("category", id, ItemChangeKind.Restored));
        _listener.Calls.Clear();
        await _h.Service.RestoreAsync("category", id);
        _listener.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Purge_raises_Purged_for_the_row_and_every_cascaded_child_in_one_call()
    {
        var root = await CreateAsync("cascadeNode", CascadeNodeBody("root"));
        var child = await CreateAsync("cascadeNode", CascadeNodeBody("child", root));
        var grandchild = await CreateAsync("cascadeNode", CascadeNodeBody("gc", child));
        await _h.Service.DeleteAsync("cascadeNode", root, purge: true);
        var call = _listener.Calls.Should().ContainSingle().Subject;
        call.Should().OnlyContain(c => c.Collection == "cascadeNode" && c.Kind == ItemChangeKind.Purged);
        call.Select(c => c.Id).Should().BeEquivalentTo([root, child, grandchild]);
    }

    [Fact]
    public async Task Purge_raises_Updated_for_rows_whose_foreign_key_was_set_to_null()
    {
        var parent = await CreateAsync("category", """{"name":"p"}""");
        var childA = await CreateAsync("category", $$$"""{"name":"a","parentId":"{{{parent}}}"}""");
        var childB = await CreateAsync("category", $$$"""{"name":"b","parentId":"{{{parent}}}"}""");
        await _h.Service.DeleteAsync("category", parent, purge: true);
        var call = _listener.Calls.Should().ContainSingle().Subject;
        call.Select(c => (c.Id, c.Kind)).Should().BeEquivalentTo(
            [(parent, ItemChangeKind.Purged), (childA, ItemChangeKind.Updated), (childB, ItemChangeKind.Updated)]);
    }

    [Fact]
    public async Task Purge_raises_Updated_for_live_parents_that_lose_an_inbound_M2M_link()
    {
        // Fix round 1 (spec R4 extension): purging a Tag deletes the INBOUND article_tags junction
        // rows (Article declares the M2M, Tag does not), so every LIVE article that carried the tag
        // must itself get an Updated. a3 is linked too but trashed before the purge — mirroring
        // CollectSetNullUpdatesAsync's live-only semantics, a trashed parent must NOT be raised.
        var tag = await CreateAsync("tag", """{"name":"t"}""");
        var a1 = await CreateAsync("article", ArticleBody("A1", tag));
        var a2 = await CreateAsync("article", ArticleBody("A2", tag));
        var a3 = await CreateAsync("article", ArticleBody("A3", tag));
        await _h.Service.DeleteAsync("article", a3); // trash a3 — must be excluded below
        _listener.Calls.Clear();

        await _h.Service.DeleteAsync("tag", tag, purge: true);

        var call = _listener.Calls.Should().ContainSingle().Subject;
        call.Select(c => (c.Collection, c.Id, c.Kind)).Should().BeEquivalentTo(
            [("tag", tag, ItemChangeKind.Purged), ("article", a1, ItemChangeKind.Updated), ("article", a2, ItemChangeKind.Updated)]);
    }

    [Fact]
    public async Task Purge_of_an_unknown_id_raises_nothing()
    {
        await _h.Service.DeleteAsync("category", Guid.NewGuid().ToString(), purge: true);
        _listener.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Listener_runs_after_commit_and_can_read_the_new_row()
    {
        // Fix round 1: reads through a SECOND ISqlSugarClient opened on the same SQLite file, not
        // through the harness's own repository/connection. A same-connection read is not a real
        // discriminator — SQLite's own connection would see its own uncommitted row too, so that
        // would "pass" even if NotifyAsync ran BEFORE the commit. A genuinely separate connection can
        // only see the row once the write has actually committed, which is the property this test
        // means to prove.
        Category? seen = null;
        PurgeIntegrityHarness h = null!;
        var probe = new RecordingItemChangeListener
        {
            OnCall = async changes =>
            {
                using var db = SqlSugarClientFactory.Create(
                    new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = h.ConnectionString },
                    new TestCurrentUserAccessor(Guid.Empty));
                seen = await db.Queryable<Category>().In(Guid.Parse(changes[0].Id)).FirstAsync();
            },
        };
        h = PurgeIntegrityHarness.Create(notifier: new ItemChangeNotifier([probe], _log));
        using (h)
        {
            await h.Service.CreateAsync("category", Body("""{"name":"c"}"""));
        }
        seen.Should().NotBeNull();
    }

    [Fact]
    public async Task A_throwing_listener_does_not_fail_the_write_and_is_logged()
    {
        var bad = new RecordingItemChangeListener { OnCall = _ => throw new InvalidOperationException("index down") };
        using var h = PurgeIntegrityHarness.Create(notifier: new ItemChangeNotifier([bad], _log));
        var created = await h.Service.CreateAsync("category", Body("""{"name":"c"}"""));
        (await h.Repository.GetByIdAsync("category", created["id"]!.ToString()!)).Should().NotBeNull();
        _log.Entries.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Error);
    }

    [Fact]
    public async Task Create_with_relations_and_translations_is_still_one_call()
    {
        var cat = await CreateAsync("category", """{"name":"c"}""");
        var tag = await CreateAsync("tag", """{"name":"t"}""");
        // $$$$ (not the brief's $$$): the JSON literally ends in three consecutive closing braces
        // ("...{"title":"T"}}}"), which a 3-dollar raw interpolated string cannot disambiguate from an
        // interpolation-close (CS9007) — bumping to 4 dollars/braces resolves it without changing the JSON.
        await _h.Service.CreateAsync("article", Body($$$$"""{"status":"draft","categoryId":"{{{{cat}}}}","tags":["{{{{tag}}}}"],"translations":{"en":{"title":"T"}}}"""));
        _listener.Calls.Should().ContainSingle().Which.Should().ContainSingle().Which.Kind.Should().Be(ItemChangeKind.Created);
    }
}
