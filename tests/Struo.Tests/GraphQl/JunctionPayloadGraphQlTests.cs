// tests/Struo.Tests/GraphQl/JunctionPayloadGraphQlTests.cs
using System.Text.Json;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Serialization;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Struo.Api.GraphQl;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.GraphQl;

/// <summary>
/// M2M junction payload over GraphQL: for a relation with <c>HasPayload</c> (parent.children in
/// FakeMetadataFixtures), the additive <c>&lt;rel&gt;Links</c> output field plus the
/// <c>&lt;Parent&gt;&lt;Rel&gt;Link</c>/<c>&lt;Parent&gt;&lt;Rel&gt;Junction</c>/
/// <c>&lt;Parent&gt;&lt;Rel&gt;LinkInput</c> types — schema-shape assertions mirroring
/// GraphQlSchemaTests/GraphQlMutationSchemaTests, plus one execution test through the real
/// pipeline (mirrors GraphQlMutationExecutionTests' harness) proving a junction payload written via
/// <c>childrenLinks</c> on create is folded into the REST <c>children</c> shape, and read back via
/// <c>childrenLinks { node junction { note } }</c>. parent.plainChildren (no payload) is the pin
/// that nothing extra is generated for a payload-free M2M relation.
/// </summary>
public class JunctionPayloadGraphQlTests
{
    private static async Task<string> BuildSdlAsync()
    {
        var services = new ServiceCollection()
            .AddSingleton<IMetadataProvider>(FakeMetadataFixtures.Provider())
            .AddSingleton<IEntityRegistry>(FakeMetadataFixtures.Registry())
            .AddSingleton<IM2MDescriptorSource>(FakeMetadataFixtures.M2MSource())
            .AddScoped<IGraphQlDataSource, FakeGraphQlDataSource>()
            .AddSingleton<StruoTypeModule>();

        var executor = await services
            .AddGraphQLServer()
            .AddQueryType(d => d.Name("Query").Field("_service").Type<StringType>().Resolve(_ => "x"))
            .AddMutationType(d => d.Name("Mutation").Field("_service").Type<StringType>().Resolve(_ => "x"))
            .AddType<LongType>().AddType<DateTimeType>().AddType<DateType>()
            .AddType<UuidType>().AddType<AnyType>().AddJsonTypeConverter()
            .AddTypeModule<StruoTypeModule>()
            .BuildRequestExecutorAsync();

        return SchemaFormatter.FormatAsString(executor.Schema);
    }

    // Mirrors GraphQlMutationExecutionTests.ExecutorAsync verbatim (no reusable helper exists
    // there either — same "inline, don't invent a shared helper" convention as
    // NestedListArgsSchemaTests).
    private static async Task<IRequestExecutor> ExecutorAsync(FakeGraphQlDataSource ds)
    {
        var services = new ServiceCollection()
            .AddSingleton<IMetadataProvider>(FakeMetadataFixtures.Provider())
            .AddSingleton<IEntityRegistry>(FakeMetadataFixtures.Registry())
            .AddSingleton<IM2MDescriptorSource>(FakeMetadataFixtures.M2MSource())
            .AddScoped<IGraphQlDataSource>(_ => ds)
            .AddSingleton(new StruoQueryOptions())
            .AddSingleton<StruoTypeModule>()
            .AddHttpContextAccessor()
            .AddLogging();
        services.AddErrorFilter<StruoErrorFilter>();

        return await services
            .AddGraphQLServer()
            .AddQueryType(d => d.Name("Query").Field("_service").Type<StringType>().Resolve(_ => "x"))
            .AddMutationType(d => d.Name("Mutation").Field("_service").Type<StringType>().Resolve(_ => "x"))
            .AddType<LongType>().AddType<DateTimeType>().AddType<DateType>()
            .AddType<UuidType>().AddType<AnyType>().AddJsonTypeConverter()
            .AddTypeModule<StruoTypeModule>()
            .BuildRequestExecutorAsync();
    }

    // Query-side executor (list/single fields only — no Mutation-specific wiring), mirroring
    // GraphQlExecutionTests.ExecutorAsync verbatim. Used by the DeepSpec-capture tests below, which
    // need OnQuery's QueryModel.Deep, not a mutation's captured body.
    private static async Task<IRequestExecutor> QueryExecutorAsync(FakeGraphQlDataSource ds)
        => await new ServiceCollection()
            .AddSingleton<IMetadataProvider>(FakeMetadataFixtures.Provider())
            .AddSingleton<IEntityRegistry>(FakeMetadataFixtures.Registry())
            .AddSingleton<IM2MDescriptorSource>(FakeMetadataFixtures.M2MSource())
            .AddScoped<IGraphQlDataSource>(_ => ds)
            .AddSingleton(new StruoQueryOptions())
            .AddSingleton<StruoTypeModule>()
            .AddGraphQLServer()
            .AddQueryType(d => d.Name("Query").Field("_service").Type<StringType>().Resolve(_ => "x"))
            .AddMutationType(d => d.Name("Mutation").Field("_service").Type<StringType>().Resolve(_ => "x"))
            .AddType<LongType>().AddType<DateTimeType>().AddType<DateType>()
            .AddType<UuidType>().AddType<AnyType>().AddJsonTypeConverter()
            .AddTypeModule<StruoTypeModule>()
            .BuildRequestExecutorAsync();

    private static JsonElement ParseData(IExecutionResult result)
    {
        var json = result.ToJson();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("errors", out var errors))
            throw new InvalidOperationException($"GraphQL errors: {errors}\n{json}");
        return doc.RootElement.GetProperty("data").Clone();
    }

    // Returns the SDL text of a single `type X { ... }` block (mirrors GraphQlMutationSchemaTests'
    // InputBlock, for object types instead of input types).
    private static string TypeBlock(string sdl, string typeName)
    {
        var start = sdl.IndexOf("type " + typeName, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"schema should declare type {typeName}");
        var open = sdl.IndexOf('{', start);
        var close = sdl.IndexOf('}', open);
        return sdl[open..close];
    }

    private static string InputBlock(string sdl, string typeName)
    {
        var start = sdl.IndexOf("input " + typeName, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"schema should declare input {typeName}");
        var open = sdl.IndexOf('{', start);
        var close = sdl.IndexOf('}', open);
        return sdl[open..close];
    }

    [Fact]
    public async Task Payload_relation_gains_Links_field_and_Link_Junction_types()
    {
        var sdl = await BuildSdlAsync();

        sdl.Should().Contain("childrenLinks: [ParentChildrenLink!]");

        var link = TypeBlock(sdl, "ParentChildrenLink");
        link.Should().Contain("node: Child!");
        // junction is nullable (no "!") — absent "_junction" (RBAC-omitted) must resolve to null.
        link.Should().MatchRegex(@"junction:\s*ParentChildrenJunction\r?\n");

        var junction = TypeBlock(sdl, "ParentChildrenJunction");
        junction.Should().Contain("note: String");
    }

    [Fact]
    public async Task Hidden_payload_field_is_excluded_from_Junction_type()
    {
        var sdl = await BuildSdlAsync();

        var junction = TypeBlock(sdl, "ParentChildrenJunction");
        junction.Should().NotContain("secret");
    }

    [Fact]
    public async Task LinkInput_carries_id_and_writable_payload_fields_only()
    {
        var sdl = await BuildSdlAsync();

        var input = InputBlock(sdl, "ParentChildrenLinkInput");
        input.Should().Contain("id: ID!");
        input.Should().Contain("note: String");
        // Hidden payload field mirrors AddWritableFields' rule -> excluded from the input too.
        input.Should().NotContain("secret");
    }

    [Fact]
    public async Task Create_and_update_inputs_carry_the_Links_field_alongside_the_plain_id_array()
    {
        var sdl = await BuildSdlAsync();

        var create = InputBlock(sdl, "ParentCreateInput");
        create.Should().Contain("children: [ID!]");                      // untouched plain field
        create.Should().Contain("childrenLinks: [ParentChildrenLinkInput!]");

        var update = InputBlock(sdl, "ParentUpdateInput");
        update.Should().Contain("children: [ID!]");
        update.Should().Contain("childrenLinks: [ParentChildrenLinkInput!]");
    }

    [Fact]
    public async Task Payload_free_M2M_relation_gets_no_Links_artefacts()
    {
        var sdl = await BuildSdlAsync();

        // parent.plainChildren carries no junction payload (FakeM2MDescriptorSource returns no
        // descriptor for it) -> none of the additive shapes are generated.
        sdl.Should().NotContain("plainChildrenLinks");
        sdl.Should().NotContain("ParentPlainChildrenLink");
        sdl.Should().NotContain("ParentPlainChildrenJunction");
        sdl.Should().NotContain("ParentPlainChildrenLinkInput");

        var create = InputBlock(sdl, "ParentCreateInput");
        create.Should().Contain("plainChildren: [ID!]"); // the ordinary M2M id-array field stays
        create.Should().NotContain("plainChildrenLinks");
    }

    // CRITICAL regression: "secretChildren" HasPayload=true (its JunctionPayload has one entry),
    // but that entry is Hidden — so ExposableJunctionFields(descriptor) is empty for it. Before the
    // fix, CollectionSchemaBuilder generated a zero-field ParentSecretChildrenJunction anyway, which
    // HotChocolate rejects at schema-build time ("has to at least define one field"), taking the
    // WHOLE schema build down — BuildSdlAsync() throwing at all (not any specific assertion below)
    // is what the original bug looked like. Getting an SDL string back at all is therefore already
    // most of the proof; the assertions pin the rest of the required behaviour (no half-generated
    // artefacts, and the ordinary bare relation + [ID!] input untouched).
    [Fact]
    public async Task Relation_with_only_hidden_payload_field_builds_schema_with_no_Links_artefacts()
    {
        var sdl = await BuildSdlAsync(); // must not throw SchemaException

        sdl.Should().NotContain("secretChildrenLinks");
        sdl.Should().NotContain("ParentSecretChildrenLink");
        sdl.Should().NotContain("ParentSecretChildrenJunction");
        sdl.Should().NotContain("ParentSecretChildrenLinkInput");

        // The bare relation field and its input still exist, exactly like a payload-free relation.
        // SchemaFormatter wraps a field's argument list onto its own lines once the field name is
        // long enough (as "secretChildren" is) — so the separators between args are whitespace
        // only, not necessarily a comma; `,?\s*` tolerates either layout.
        sdl.Should().MatchRegex(
            @"secretChildren\(\s*filter:\s*ChildFilterInput,?\s*sort:\s*\[String!\],?\s*limit:\s*Int,?\s*offset:\s*Int,?\s*\):\s*\[Child!\]");
        InputBlock(sdl, "ParentCreateInput").Should().Contain("secretChildren: [ID!]");
    }

    // IMPORTANT regression: `<rel>Links` must drive the SAME DeepSpec entry as `<rel>` itself, or
    // ItemService/DeepExpansionCoordinator never expands the relation and the field resolves to
    // null in production for a client that selects only `childrenLinks` (not also `children`).
    [Fact]
    public async Task ChildrenLinks_selection_alone_produces_a_children_DeepSpec_entry()
    {
        DeepSpec? deepSeen = null;
        var ds = new FakeGraphQlDataSource
        {
            OnQuery = (_, q, _, _) =>
            {
                deepSeen = q.Deep;
                var row = new Dictionary<string, object?> { ["id"] = "1" };
                return new PagedResult(new IReadOnlyDictionary<string, object?>[] { row }, 1, q.Limit, q.Offset);
            }
        };

        var result = await (await QueryExecutorAsync(ds)).ExecuteAsync(
            "{ parents { items { id childrenLinks { node { id } junction { note } } } } }");
        ParseData(result);

        deepSeen.Should().NotBeNull();
        deepSeen!.Relations.Should().ContainKey("children");
    }

    // A nested relation under `node` (not under the top-level `childrenLinks` field itself) must
    // still produce a nested DeepSpec — proving BuildDeep recurses into node's own sub-selection,
    // not the Link type's `{ node junction }` shape.
    [Fact]
    public async Task Nested_relation_under_node_produces_a_nested_DeepSpec_entry()
    {
        DeepSpec? deepSeen = null;
        var ds = new FakeGraphQlDataSource
        {
            OnQuery = (_, q, _, _) =>
            {
                deepSeen = q.Deep;
                var row = new Dictionary<string, object?> { ["id"] = "1" };
                return new PagedResult(new IReadOnlyDictionary<string, object?>[] { row }, 1, q.Limit, q.Offset);
            }
        };

        var result = await (await QueryExecutorAsync(ds)).ExecuteAsync(
            "{ parents { items { id childrenLinks { node { id tags { name } } } } } }");
        ParseData(result);

        deepSeen.Should().NotBeNull();
        deepSeen!.Relations.Should().ContainKey("children");
        var childrenDeep = deepSeen.Relations["children"].Deep;
        childrenDeep.Should().NotBeNull("a relation nested under `node` must still produce a nested Deep tree");
        childrenDeep!.Relations.Should().ContainKey("tags");
    }

    // M1: an EXPLICIT `childrenLinks: null` still overwrites/discards a simultaneously-sent
    // `children` array — FoldLinks keys off ContainsKey, which is true for a present-but-null value.
    [Fact]
    public async Task Explicit_null_childrenLinks_discards_a_simultaneous_children_array()
    {
        JsonElement? capturedBody = null;
        var ds = new FakeGraphQlDataSource
        {
            OnCreate = (_, body) => { capturedBody = body.Clone(); return new Dictionary<string, object?> { ["id"] = "1" }; },
            OnGet = (_, id, _, _) => new Dictionary<string, object?> { ["id"] = id },
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { createParent(input: { name: \"p\", children: [\"c1\"], childrenLinks: null }) { id } }");
        ParseData(result);

        capturedBody!.Value.EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(["name", "children"]);
        capturedBody.Value.GetProperty("children").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // M2: the UPDATE mutation path folds childrenLinks the same way create does.
    [Fact]
    public async Task Update_with_childrenLinks_folds_into_children()
    {
        JsonElement? capturedBody = null;
        var ds = new FakeGraphQlDataSource
        {
            OnUpdate = (_, id, body) => { capturedBody = body.Clone(); return new Dictionary<string, object?> { ["id"] = id }; },
            OnGet = (_, id, _, _) => new Dictionary<string, object?> { ["id"] = id },
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { updateParent(id: \"5\", input: { childrenLinks: [{ id: \"c1\", note: \"y\" }] }) { id } }");
        ParseData(result);

        capturedBody!.Value.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["children"]);
        var sent = capturedBody.Value.GetProperty("children");
        sent.GetArrayLength().Should().Be(1);
        sent[0].GetProperty("id").GetString().Should().Be("c1");
        sent[0].GetProperty("note").GetString().Should().Be("y");
    }

    [Fact]
    public async Task Create_with_childrenLinks_writes_payload_and_reads_it_back_via_junction()
    {
        JsonElement? capturedBody = null;
        var ds = new FakeGraphQlDataSource
        {
            OnCreate = (_, body) => { capturedBody = body.Clone(); return new Dictionary<string, object?> { ["id"] = "1" }; },
            // Re-read: "children" (not "childrenLinks") carries the deep-expanded target rows, each
            // with a "_junction" dict when the relation has payload — exactly what ItemService's
            // deep expansion projects in production.
            OnGet = (_, id, _, _) => new Dictionary<string, object?>
            {
                ["id"] = id,
                ["children"] = new List<object?>
                {
                    new Dictionary<string, object?>
                    {
                        ["id"] = "c1",
                        ["name"] = "Child One",
                        ["_junction"] = new Dictionary<string, object?> { ["note"] = "x" },
                    },
                },
            },
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { createParent(input: { name: \"p\", childrenLinks: [{ id: \"c1\", note: \"x\" }] }) " +
            "{ id childrenLinks { node { id } junction { note } } } }");
        var data = ParseData(result);

        // FoldLinks folded childrenLinks -> children (the REST mixed-array shape SyncM2MAsync
        // accepts: a list of { id, ...payload } objects) and dropped the childrenLinks key.
        capturedBody!.Value.EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(["name", "children"]);
        var sentChildren = capturedBody.Value.GetProperty("children");
        sentChildren.GetArrayLength().Should().Be(1);
        sentChildren[0].GetProperty("id").GetString().Should().Be("c1");
        sentChildren[0].GetProperty("note").GetString().Should().Be("x");

        var node = data.GetProperty("createParent");
        var link = node.GetProperty("childrenLinks")[0];
        link.GetProperty("node").GetProperty("id").GetString().Should().Be("c1");
        link.GetProperty("junction").GetProperty("note").GetString().Should().Be("x");
    }

    [Fact]
    public async Task Element_without_junction_key_resolves_junction_to_null()
    {
        // RBAC-omitted case: the deep expansion leaves "_junction" off the element entirely when
        // the caller cannot read the junction collection. `junction` must resolve to null (its SDL
        // type is nullable, not "!") rather than error.
        var ds = new FakeGraphQlDataSource
        {
            OnCreate = (_, _) => new Dictionary<string, object?> { ["id"] = "1" },
            OnGet = (_, id, _, _) => new Dictionary<string, object?>
            {
                ["id"] = id,
                ["children"] = new List<object?>
                {
                    new Dictionary<string, object?> { ["id"] = "c1", ["name"] = "Child One" },
                },
            },
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { createParent(input: { name: \"p\" }) { id childrenLinks { node { id } junction { note } } } }");
        var data = ParseData(result);

        var link = data.GetProperty("createParent").GetProperty("childrenLinks")[0];
        link.GetProperty("junction").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task ChildrenLinks_wins_when_both_children_and_childrenLinks_are_sent()
    {
        JsonElement? capturedBody = null;
        var ds = new FakeGraphQlDataSource
        {
            OnCreate = (_, body) => { capturedBody = body.Clone(); return new Dictionary<string, object?> { ["id"] = "1" }; },
            OnGet = (_, id, _, _) => new Dictionary<string, object?> { ["id"] = id },
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { createParent(input: { name: \"p\", children: [\"stale\"], " +
            "childrenLinks: [{ id: \"c1\", note: \"x\" }] }) { id } }");
        ParseData(result);

        capturedBody!.Value.EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(["name", "children"]);
        var sent = capturedBody.Value.GetProperty("children");
        sent.GetArrayLength().Should().Be(1);
        sent[0].GetProperty("id").GetString().Should().Be("c1");
    }
}
