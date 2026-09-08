// tests/Struo.Tests/Query/JunctionPayloadWriteTests.cs
using AwesomeAssertions;
using Struo.Application.Security;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.Query;

public sealed class JunctionPayloadWriteTests
{
    [Fact]
    public async Task Mixed_array_sets_payload_only_on_object_elements()
    {
        using var h = new JunctionPayloadHarness();
        var c1 = await h.CreateChildAsync("a"); var c2 = await h.CreateChildAsync("b");
        var p = await h.CreateParentAsync(new object[] { c1, new { id = c2, note = "x", weight = 3 } });

        var links = h.Links(p);
        links.Select(l => l.JpChildId).Should().Equal(c1, c2);
        links[0].Note.Should().BeNull();
        links[1].Note.Should().Be("x"); links[1].Weight.Should().Be(3);
    }

    [Fact]
    public async Task Bare_id_update_keeps_payload_and_primary_keys()
    {
        using var h = new JunctionPayloadHarness();
        var c1 = await h.CreateChildAsync("a"); var c2 = await h.CreateChildAsync("b");
        var p = await h.CreateParentAsync(new object[] { new { id = c1, note = "keep" }, c2 });
        var before = h.Links(p);

        await h.Service.UpdateAsync("jpParent", p.ToString(), JunctionPayloadHarness.Body(new { name = "renamed", children = new[] { c1, c2 } }));

        var after = h.Links(p);
        after.Select(l => l.Id).Should().Equal(before.Select(l => l.Id));
        after[0].Note.Should().Be("keep");
    }

    [Fact]
    public async Task Object_update_merges_only_given_fields_and_dedupes_by_id_with_later_object_winning()
    {
        using var h = new JunctionPayloadHarness();
        var c1 = await h.CreateChildAsync("a");
        var p = await h.CreateParentAsync(new object[] { new { id = c1, note = "n", weight = 1 } });

        await h.Service.UpdateAsync("jpParent", p.ToString(), JunctionPayloadHarness.Body(new
        {
            name = "p",
            children = new object[] { c1, new { id = c1, weight = 7 }, new { id = c1, weight = 9 } }
        }));

        var link = h.Links(p).Should().ContainSingle().Subject;
        link.Note.Should().Be("n");
        link.Weight.Should().Be(9);
    }

    [Fact]
    public async Task Object_without_id_is_a_client_error_and_rolls_back()
    {
        using var h = new JunctionPayloadHarness();
        var c1 = await h.CreateChildAsync("a");
        var p = await h.CreateParentAsync(new object[] { c1 });

        var act = () => h.Service.UpdateAsync("jpParent", p.ToString(), JunctionPayloadHarness.Body(new { name = "p", children = new object[] { new { note = "no id" } } }));
        (await act.Should().ThrowAsync<QueryException>()).WithMessage("*'children' must carry an 'id'*");
        h.Links(p).Should().ContainSingle();
    }

    [Fact]
    public async Task Payload_validation_runs_through_the_field_rules()
    {
        using var h = new JunctionPayloadHarness();
        var c1 = await h.CreateChildAsync("a");
        var act = () => h.CreateParentAsync(new object[] { new { id = c1, note = new string('x', 21) } });
        (await act.Should().ThrowAsync<QueryException>()).WithMessage("*note*maximum length*");
    }

    [Fact]
    public async Task Payload_field_shape_error_is_prefixed_with_the_relation_and_target_id()
    {
        using var h = new JunctionPayloadHarness();
        var c1 = await h.CreateChildAsync("a");

        var act = () => h.CreateParentAsync(new object[] { new { id = c1, weight = "" } });

        (await act.Should().ThrowAsync<QueryException>())
            .WithMessage($"Relation 'children', target '{c1}': Field 'weight' has an invalid value.");
    }

    [Fact]
    public async Task Payload_MaxLength_error_is_prefixed_with_the_relation_and_target_id()
    {
        using var h = new JunctionPayloadHarness();
        var c1 = await h.CreateChildAsync("a");

        var act = () => h.CreateParentAsync(new object[] { new { id = c1, note = new string('x', 21) } });

        (await act.Should().ThrowAsync<QueryException>())
            .WithMessage($"Relation 'children', target '{c1}': Field 'note' exceeds maximum length 20.");
    }

    [Fact]
    public async Task Unknown_payload_keys_are_ignored_and_fks_cannot_be_overridden()
    {
        using var h = new JunctionPayloadHarness();
        var c1 = await h.CreateChildAsync("a");
        var p = await h.CreateParentAsync(new object[] { new { id = c1, bogus = 1, jpParentId = Guid.NewGuid(), jpChildId = Guid.NewGuid(), sort = 42 } });
        var link = h.Links(p).Should().ContainSingle().Subject;
        link.JpParentId.Should().Be(p); link.JpChildId.Should().Be(c1); link.Sort.Should().Be(0);
    }

    private sealed class NoJunctionWrite : IPermissionService
    {
        public bool CanRead(string c) => true;
        public bool CanWrite(string c) => c != "jpLink";
        public bool CanDelete(string c) => true;
        public IReadOnlyCollection<string> ReadableFields(string c, IEnumerable<string> all) => all.ToList();
    }

    [Fact]
    public async Task Payload_write_requires_write_grant_on_the_junction_collection_but_bare_ids_do_not()
    {
        using var h = new JunctionPayloadHarness(new NoJunctionWrite());
        var c1 = await h.CreateChildAsync("a");
        var p = await h.CreateParentAsync(new object[] { c1 }); // bare id: allowed

        var act = () => h.Service.UpdateAsync("jpParent", p.ToString(), JunctionPayloadHarness.Body(new { name = "p", children = new object[] { new { id = c1, note = "x" } } }));
        (await act.Should().ThrowAsync<PermissionDeniedException>()).WithMessage("*jpLink*");
    }

    [Fact]
    public async Task Plain_junction_relation_still_rejects_object_elements()
    {
        using var h = new JunctionPayloadHarness();
        var c1 = await h.CreateChildAsync("a");
        var act = () => h.Service.CreateAsync("jpParent", JunctionPayloadHarness.Body(new { name = "p", plainChildren = new object[] { new { id = c1, note = "x" } } }));
        (await act.Should().ThrowAsync<QueryException>()).WithMessage("*'plainChildren'*not valid*");
    }

    [Fact]
    public async Task Grant_check_runs_before_payload_field_validation()
    {
        // The junction write grant is denied AND the payload would separately fail MaxLength — the
        // caller must see the 403, not a 400 that would leak that the field rule was even evaluated.
        using var h = new JunctionPayloadHarness(new NoJunctionWrite());
        var c1 = await h.CreateChildAsync("a");

        var act = () => h.CreateParentAsync(new object[] { new { id = c1, note = new string('x', 21) } });
        (await act.Should().ThrowAsync<PermissionDeniedException>()).WithMessage("*jpLink*");
    }

    [Fact]
    public async Task Object_outranks_a_bare_id_appearing_before_or_after_it_in_the_same_request()
    {
        using var h = new JunctionPayloadHarness();
        var c1 = await h.CreateChildAsync("a");
        var p = await h.CreateParentAsync(new object[] { c1, new { id = c1, note = "x" }, c1 });

        var link = h.Links(p).Should().ContainSingle().Subject;
        link.Note.Should().Be("x");
    }

    [Fact]
    public async Task Two_objects_for_the_same_id_do_not_merge_payload_across_each_other()
    {
        using var h = new JunctionPayloadHarness();
        var c1 = await h.CreateChildAsync("a");
        var p = await h.CreateParentAsync(new object[] { new { id = c1, note = "n" } });

        // The later object (weight only) replaces the earlier one entirely — it does not inherit "note"
        // from the earlier object in this same request, and the stored note is untouched from before.
        await h.Service.UpdateAsync("jpParent", p.ToString(), JunctionPayloadHarness.Body(new
        {
            name = "p",
            children = new object[] { new { id = c1, note = "x" }, new { id = c1, weight = 9 } }
        }));

        var link = h.Links(p).Should().ContainSingle().Subject;
        link.Note.Should().Be("n");
        link.Weight.Should().Be(9);
    }

    private sealed class WriteEverywhereNotSuperAdmin : IPermissionService
    {
        public bool CanRead(string c) => true;
        public bool CanWrite(string c) => true;
        public bool CanDelete(string c) => true;
        public bool IsSuperAdmin => false;
        public IReadOnlyCollection<string> ReadableFields(string c, IEnumerable<string> all) => all.ToList();
    }

    [Fact]
    public async Task AdminOnly_junction_payload_requires_super_admin_but_bare_ids_do_not()
    {
        using var h = new JunctionPayloadHarness(new WriteEverywhereNotSuperAdmin());
        var c1 = await h.CreateChildAsync("a");

        // Bare id: membership-only — no payload write, so the ordinary per-collection grant suffices
        // even though the caller is not super-admin.
        var created = await h.Service.CreateAsync("jpParent",
            JunctionPayloadHarness.Body(new { name = "p", adminChildren = new object[] { c1 } }));
        var p = Guid.Parse(created["id"]!.ToString()!);
        h.AdminLinks(p).Should().ContainSingle();

        var act = () => h.Service.UpdateAsync("jpParent", p.ToString(), JunctionPayloadHarness.Body(new
        {
            name = "p",
            adminChildren = new object[] { new { id = c1, note = "x" } }
        }));
        (await act.Should().ThrowAsync<PermissionDeniedException>()).WithMessage("*super-admin*");
    }
}
