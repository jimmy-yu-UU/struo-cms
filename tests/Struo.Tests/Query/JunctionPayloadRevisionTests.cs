// tests/Struo.Tests/Query/JunctionPayloadRevisionTests.cs
using System.Text.Json;
using AwesomeAssertions;
using Struo.Application.Security;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.Query;

public sealed class JunctionPayloadRevisionTests
{
    /// <summary>
    /// A permission fake whose write grant on the "jpLink" junction collection can be flipped at
    /// runtime (everything else stays allowed) — lets a single harness instance create data under a
    /// permissive grant, then have that grant withdrawn before exercising <c>RevertAsync</c>, without
    /// needing to construct a second harness/database.
    /// </summary>
    private sealed class ToggleableJunctionWritePermissions : IPermissionService
    {
        public bool AllowJunctionWrite { get; set; } = true;
        public bool CanRead(string collection) => true;
        public bool CanWrite(string collection) => collection != "jpLink" || AllowJunctionWrite;
        public bool CanDelete(string collection) => true;
        public IReadOnlyCollection<string> ReadableFields(string collection, IEnumerable<string> allFieldNames) =>
            allFieldNames.ToList();
    }

    [Fact]
    public async Task Revert_of_a_payload_link_requires_the_junctions_write_grant()
    {
        var perms = new ToggleableJunctionWritePermissions();
        using var h = new JunctionPayloadHarness(perms);
        var c1 = await h.CreateChildAsync("a");
        var p = await h.CreateParentAsync(new object[] { new { id = c1, note = "v1" } });
        await h.Service.UpdateAsync("jpParent", p.ToString(),
            JunctionPayloadHarness.Body(new { name = "p", children = new object[] { new { id = c1, note = "v2" } } }));
        h.Links(p).Single().Note.Should().Be("v2");
        var first = (await h.Service.ListRevisionsAsync("jpParent", p.ToString()))[^1].RevisionNumber;

        // I1: write on the parent ("jpParent") is granted throughout; only the junction's own write
        // grant is withdrawn. Revert must still be refused — a role that may write the parent but not
        // the junction must not be able to smuggle a junction-payload change through revert.
        perms.AllowJunctionWrite = false;
        var act = () => h.Service.RevertAsync("jpParent", p.ToString(), first);
        (await act.Should().ThrowAsync<PermissionDeniedException>()).WithMessage("*jpLink*");
        h.Links(p).Single().Note.Should().Be("v2", "the refused revert must not have changed the DB row");

        // Control: the identical revert succeeds once the junction grant is restored — proves the
        // 403 above came from the missing junction grant, not from some other defect on this path.
        perms.AllowJunctionWrite = true;
        await h.Service.RevertAsync("jpParent", p.ToString(), first);
        h.Links(p).Single().Note.Should().Be("v1");
    }

    [Fact]
    public async Task Revert_restores_a_guid_payload_field()
    {
        using var h = new JunctionPayloadHarness();
        var c1 = await h.CreateChildAsync("a");
        var refV1 = Guid.NewGuid();
        var refV2 = Guid.NewGuid();
        var p = await h.CreateParentAsync(new object[] { new { id = c1, @ref = refV1 } });
        await h.Service.UpdateAsync("jpParent", p.ToString(),
            JunctionPayloadHarness.Body(new { name = "p", children = new object[] { new { id = c1, @ref = refV2 } } }));
        h.Links(p).Single().Ref.Should().Be(refV2);

        var first = (await h.Service.ListRevisionsAsync("jpParent", p.ToString()))[^1].RevisionNumber;
        await h.Service.RevertAsync("jpParent", p.ToString(), first);

        h.Links(p).Single().Ref.Should().Be(refV1, "the Guid payload field must round-trip through snapshot -> revert -> DB");
    }

    [Fact]
    public async Task Snapshot_stores_payload_objects_for_payload_relations_and_bare_ids_for_plain_ones()
    {
        using var h = new JunctionPayloadHarness();
        var c1 = await h.CreateChildAsync("a");
        var p = await h.CreateParentAsync(new object[] { new { id = c1, note = "x", secret = "s" } });
        await h.Service.UpdateAsync("jpParent", p.ToString(), JunctionPayloadHarness.Body(new { name = "p", plainChildren = new[] { c1 } }));

        var revisions = await h.Service.ListRevisionsAsync("jpParent", p.ToString());
        var rec = await h.RevisionStore.GetAsync("jpParent", p.ToString(), revisions[0].RevisionNumber);
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
        var rec = await h.Service.GetRevisionAsync("jpParent", p.ToString(), revisions[0].RevisionNumber);
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

        var first = (await h.Service.ListRevisionsAsync("jpParent", p.ToString()))[^1].RevisionNumber;
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

        var legacy = (await h.Service.ListRevisionsAsync("jpParent", p.ToString()))[0].RevisionNumber;
        await h.Service.RevertAsync("jpParent", p.ToString(), legacy);

        var links = h.Links(p);
        links.Select(l => l.JpChildId).Should().Equal(c1, c2);
        links[0].Note.Should().Be("keep", "bare ids in a snapshot leave payload untouched");
    }
}
