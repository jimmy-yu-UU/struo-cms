// tests/Struo.Tests/Query/JunctionPayloadRevisionTests.cs
using System.Text.Json;
using AwesomeAssertions;
using Xunit;

namespace Struo.Tests.Query;

public sealed class JunctionPayloadRevisionTests
{
    [Fact]
    public async Task Snapshot_stores_payload_objects_for_payload_relations_and_bare_ids_for_plain_ones()
    {
        using var h = new JunctionPayloadHarness();
        var c1 = await h.CreateChildAsync("a");
        var p = await h.CreateParentAsync(new object[] { new { id = c1, note = "x", secret = "s" } });
        await h.Service.UpdateAsync("jpParent", p.ToString(), JunctionPayloadHarness.Body(new { name = "p", plainChildren = new[] { c1 } }));

        var revisions = await h.Service.ListRevisionsAsync("jpParent", p.ToString());
        var rec = await h.RevisionStore.GetAsync("jpParent", p.ToString(), revisions.First().RevisionNumber);
        using var doc = JsonDocument.Parse(rec!.Snapshot);
        var children = doc.RootElement.GetProperty("children");
        children[0].GetProperty("id").GetString().Should().Be(c1.ToString());
        children[0].GetProperty("note").GetString().Should().Be("x");
        children[0].GetProperty("secret").GetString().Should().Be("s", "the stored snapshot keeps hidden payload for revert");
        doc.RootElement.GetProperty("plainChildren")[0].GetString().Should().Be(c1.ToString());
    }

    [Fact]
    public async Task Revision_read_redacts_hidden_payload_fields()
    {
        using var h = new JunctionPayloadHarness();
        var c1 = await h.CreateChildAsync("a");
        var p = await h.CreateParentAsync(new object[] { new { id = c1, note = "x", secret = "s" } });

        var revisions = await h.Service.ListRevisionsAsync("jpParent", p.ToString());
        var rec = await h.Service.GetRevisionAsync("jpParent", p.ToString(), revisions.First().RevisionNumber);
        using var doc = JsonDocument.Parse(rec!.Snapshot);
        var el = doc.RootElement.GetProperty("children")[0];
        el.TryGetProperty("secret", out _).Should().BeFalse();
        el.GetProperty("note").GetString().Should().Be("x");
    }

    [Fact]
    public async Task Revert_restores_payload()
    {
        using var h = new JunctionPayloadHarness();
        var c1 = await h.CreateChildAsync("a");
        var p = await h.CreateParentAsync(new object[] { new { id = c1, note = "v1" } });
        await h.Service.UpdateAsync("jpParent", p.ToString(), JunctionPayloadHarness.Body(new { name = "p", children = new object[] { new { id = c1, note = "v2" } } }));
        h.Links(p).Single().Note.Should().Be("v2");

        var first = (await h.Service.ListRevisionsAsync("jpParent", p.ToString())).Last().RevisionNumber;
        await h.Service.RevertAsync("jpParent", p.ToString(), first);

        h.Links(p).Single().Note.Should().Be("v1");
    }

    [Fact]
    public async Task Legacy_bare_id_snapshot_still_reverts()
    {
        using var h = new JunctionPayloadHarness();
        var c1 = await h.CreateChildAsync("a"); var c2 = await h.CreateChildAsync("b");
        var p = await h.CreateParentAsync(new object[] { new { id = c1, note = "keep" } });
        // Simulate a pre-U2 snapshot: bare ids only.
        await h.RevisionStore.CaptureAsync("jpParent", p.ToString(), "update",
            JsonSerializer.Serialize(new { id = p, name = "p", children = new[] { c1, c2 } }));

        var legacy = (await h.Service.ListRevisionsAsync("jpParent", p.ToString())).First().RevisionNumber;
        await h.Service.RevertAsync("jpParent", p.ToString(), legacy);

        var links = h.Links(p);
        links.Select(l => l.JpChildId).Should().Equal(c1, c2);
        links[0].Note.Should().Be("keep", "bare ids in a snapshot leave payload untouched");
    }
}
