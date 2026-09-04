// tests/Struo.Tests/Query/JunctionPayloadReadTests.cs
using AwesomeAssertions;
using Struo.Application.Security;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.Query;

public sealed class JunctionPayloadReadTests
{
    private static DeepSpec Deep(params string[] relations) =>
        new(relations.ToDictionary(
            r => r, _ => new DeepRelationSpec(null, null), StringComparer.OrdinalIgnoreCase));

    [Fact]
    public async Task Deep_adds_a_junction_sub_object_with_payload_but_without_hidden_fields()
    {
        using var h = new JunctionPayloadHarness();
        var c1 = await h.CreateChildAsync("a");
        var p = await h.CreateParentAsync(new object[] { new { id = c1, note = "x", weight = 2, secret = "s" } });

        var item = await h.Service.GetAsync("jpParent", p.ToString(), Deep("children"));
        var child = ((IReadOnlyList<object?>)item!["children"]!).Cast<IReadOnlyDictionary<string, object?>>().Single();
        child["name"].Should().Be("a");
        var junction = (IReadOnlyDictionary<string, object?>)child["_junction"]!;
        junction.Keys.Should().BeEquivalentTo(["note", "weight"]);
        junction["note"].Should().Be("x");
        junction["weight"].Should().Be(2);
    }

    [Fact]
    public async Task Plain_junction_relation_has_no_junction_key()
    {
        using var h = new JunctionPayloadHarness();
        var c1 = await h.CreateChildAsync("a");
        var p = await h.CreateParentAsync(new object[] { c1 });
        await h.Service.UpdateAsync(
            "jpParent", p.ToString(), JunctionPayloadHarness.Body(new { name = "p", plainChildren = new[] { c1 } }));

        var item = await h.Service.GetAsync("jpParent", p.ToString(), Deep("plainChildren"));
        var child = ((IReadOnlyList<object?>)item!["plainChildren"]!).Cast<IReadOnlyDictionary<string, object?>>().Single();
        child.Should().NotContainKey("_junction");
    }

    private sealed class NoJunctionRead : IPermissionService
    {
        public bool CanRead(string c) => c != "jpLink";
        public bool CanWrite(string c) => true;
        public bool CanDelete(string c) => true;
        public IReadOnlyCollection<string> ReadableFields(string c, IEnumerable<string> all) => all.ToList();
    }

    [Fact]
    public async Task Junction_is_omitted_when_the_caller_cannot_read_the_junction_collection()
    {
        using var h = new JunctionPayloadHarness(new NoJunctionRead());
        var c1 = await h.CreateChildAsync("a");
        var p = await h.CreateParentAsync(new object[] { new { id = c1, note = "x" } });

        var item = await h.Service.GetAsync("jpParent", p.ToString(), Deep("children"));
        var child = ((IReadOnlyList<object?>)item!["children"]!).Cast<IReadOnlyDictionary<string, object?>>().Single();
        child["name"].Should().Be("a", "the target itself is still readable");
        child.Should().NotContainKey("_junction");
    }

    [Fact]
    public async Task List_query_with_deep_carries_junction_per_parent()
    {
        using var h = new JunctionPayloadHarness();
        var c1 = await h.CreateChildAsync("a");
        var p1 = await h.CreateParentAsync(new object[] { new { id = c1, note = "one" } });
        var p2 = await h.CreateParentAsync(new object[] { new { id = c1, note = "two" } });

        var page = await h.Service.QueryAsync(
            "jpParent", new QueryModel(null, null, [], 10, 0, null) { Deep = Deep("children") });
        var notes = page.Data.ToDictionary(r => Guid.Parse(r["id"]!.ToString()!),
            r => ((IReadOnlyDictionary<string, object?>)((IReadOnlyList<object?>)r["children"]!).Cast<IReadOnlyDictionary<string, object?>>().Single()["_junction"]!)["note"]);
        notes[p1].Should().Be("one"); notes[p2].Should().Be("two");
    }
}
