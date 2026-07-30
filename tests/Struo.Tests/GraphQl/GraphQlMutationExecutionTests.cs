// tests/Struo.Tests/GraphQl/GraphQlMutationExecutionTests.cs
using System.Text.Json;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Struo.Api.GraphQl;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.GraphQl;

public class GraphQlMutationExecutionTests
{
    // Mirrors GraphQlExecutionTests.ExecutorAsync but adds the Mutation root anchor and registers
    // the error filter (mutations must surface domain-exception codes).
    private static async Task<IRequestExecutor> ExecutorAsync(FakeGraphQlDataSource ds)
    {
        var services = new ServiceCollection()
            .AddSingleton<IMetadataProvider>(FakeMetadataFixtures.Provider())
            .AddSingleton<IEntityRegistry>(FakeMetadataFixtures.Registry())
            .AddScoped<IGraphQlDataSource>(_ => ds)
            .AddSingleton(new StruoQueryOptions())
            .AddSingleton<StruoTypeModule>()
            .AddHttpContextAccessor() // StruoErrorFilter now injects IHttpContextAccessor
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

    private static JsonElement ParseData(IExecutionResult result)
    {
        var json = result.ToJson();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("errors", out var errors))
            throw new InvalidOperationException($"GraphQL errors: {errors}\n{json}");
        return doc.RootElement.GetProperty("data").Clone();
    }

    [Fact]
    public async Task Delete_returns_true_when_found()
    {
        string? seenCollection = null, seenId = null;
        var ds = new FakeGraphQlDataSource
        {
            OnDelete = (c, id, _) => { seenCollection = c; seenId = id; return true; }
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { deleteArticle(id: \"7\") }");
        var data = ParseData(result);

        data.GetProperty("deleteArticle").GetBoolean().Should().BeTrue();
        seenCollection.Should().Be("article");
        seenId.Should().Be("7");
    }

    [Fact]
    public async Task Delete_returns_false_when_not_found()
    {
        var ds = new FakeGraphQlDataSource { OnDelete = (_, _, _) => false };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { deleteArticle(id: \"missing\") }");
        var data = ParseData(result);

        data.GetProperty("deleteArticle").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Create_returns_node_with_selected_fields_and_reread_relation()
    {
        JsonElement? capturedBody = null;
        var ds = new FakeGraphQlDataSource
        {
            OnCreate = (c, body) => { capturedBody = body.Clone(); return new Dictionary<string, object?> { ["id"] = "42" }; },
            // Re-read (GetAsync) returns the fully-projected node incl. the M2O relation.
            OnGet = (_, id, _, _) => new Dictionary<string, object?>
            {
                ["id"] = id,
                ["status"] = "published",
                ["category"] = new Dictionary<string, object?> { ["id"] = "9", ["name"] = "News" },
            },
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { createArticle(input: { status: \"published\", categoryId: \"9\" }) { id status category { name } } }");
        var data = ParseData(result);

        var node = data.GetProperty("createArticle");
        node.GetProperty("id").GetString().Should().Be("42");
        node.GetProperty("status").GetString().Should().Be("published");
        node.GetProperty("category").GetProperty("name").GetString().Should().Be("News");

        // The input reached the service as a JSON body carrying exactly the sent keys — NOT
        // backfilled with every other optional ArticleCreateInput field (publishedAt, heroImageId,
        // regions, keywords, attributes, gallery, faqs) as nulls.
        capturedBody!.Value.EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(["status", "categoryId"]);
        capturedBody.Value.GetProperty("status").GetString().Should().Be("published");
        capturedBody.Value.GetProperty("categoryId").GetString().Should().Be("9");
    }

    [Fact]
    public async Task Create_sends_only_provided_keys_not_backfilled_defaults()
    {
        // Regression for the v16 null-backfill bug: HotChocolate's coerced input dictionary contains
        // EVERY declared optional field, backfilled with null when the client didn't send it. Without
        // routing create through SentFieldsOnly (like update already does), omitting a field with a
        // CLR default (e.g. Article.Status = "draft") would send status: null and clobber the
        // default instead of leaving it untouched.
        JsonElement? capturedBody = null;
        var ds = new FakeGraphQlDataSource
        {
            OnCreate = (_, body) => { capturedBody = body.Clone(); return new Dictionary<string, object?> { ["id"] = "1" }; },
            OnGet = (_, id, _, _) => new Dictionary<string, object?> { ["id"] = id, ["status"] = "published" },
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { createArticle(input: { status: \"published\" }) { id } }");
        ParseData(result);

        capturedBody!.Value.EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(["status"]);
    }

    [Fact]
    public async Task Create_via_variables_sends_only_provided_keys()
    {
        // Real clients send $input as a GraphQL variable, not an inline literal. SentFieldsOnly
        // (MutationResolvers) recovers the sent-fields set from the argument LITERAL, which must
        // reflect only the variable's own JSON keys post-substitution — guard against a fix that
        // happens to work for inline literals but not variables.
        JsonElement? capturedBody = null;
        var ds = new FakeGraphQlDataSource
        {
            OnCreate = (_, body) => { capturedBody = body.Clone(); return new Dictionary<string, object?> { ["id"] = "1" }; },
            OnGet = (_, id, _, _) => new Dictionary<string, object?> { ["id"] = id, ["status"] = "published" },
        };

        var request = OperationRequestBuilder.New()
            .SetDocument("mutation($input: ArticleCreateInput!) { createArticle(input: $input) { id } }")
            .SetVariableValues(new Dictionary<string, object?>
            {
                ["input"] = new Dictionary<string, object?> { ["status"] = "published" },
            })
            .Build();

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(request);
        ParseData(result);

        capturedBody!.Value.EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(["status"]);
    }

    [Fact]
    public async Task Create_validation_error_maps_to_BAD_USER_INPUT()
    {
        var ds = new FakeGraphQlDataSource
        {
            OnCreate = (_, _) => throw new QueryException("Field 'status' is required.")
        };

        var json = (await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { createArticle(input: { }) { id } }")).ToJson();

        json.Should().Contain("BAD_USER_INPUT");
        json.Should().Contain("required");
    }

    // No HttpContext in this in-process executor ⇒ unauthenticated ⇒ UNAUTHORIZED
    // (REST-parity via the shared DomainErrorMap), not the old unconditional FORBIDDEN.
    [Fact]
    public async Task Create_permission_denied_without_http_context_maps_to_UNAUTHORIZED()
    {
        var ds = new FakeGraphQlDataSource
        {
            OnCreate = (_, _) => throw new PermissionDeniedException("Write not permitted.")
        };

        var json = (await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { createArticle(input: { status: \"x\" }) { id } }")).ToJson();

        json.Should().Contain("UNAUTHORIZED");
    }

    [Fact]
    public async Task Update_sends_only_provided_keys_and_returns_reread_node()
    {
        JsonElement? capturedBody = null;
        string? capturedId = null;
        var ds = new FakeGraphQlDataSource
        {
            OnUpdate = (_, id, body) => { capturedId = id; capturedBody = body.Clone(); return new Dictionary<string, object?> { ["id"] = id }; },
            OnGet = (_, id, _, _) => new Dictionary<string, object?> { ["id"] = id, ["status"] = "archived" },
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { updateArticle(id: \"5\", input: { status: \"archived\", version: 3 }) { id status } }");
        var data = ParseData(result);

        capturedId.Should().Be("5");
        // Partial merge: only the sent keys are present in the body.
        capturedBody!.Value.EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(["status", "version"]);
        capturedBody.Value.GetProperty("version").GetInt64().Should().Be(3);

        data.GetProperty("updateArticle").GetProperty("status").GetString().Should().Be("archived");
    }

    [Fact]
    public async Task Update_via_variables_sends_only_provided_keys()
    {
        // Real clients send $input as a GraphQL variable, not an inline literal. SentFieldsOnly
        // (MutationResolvers) recovers the partial-merge key set from the argument LITERAL, which
        // must reflect only the variable's own JSON keys post-substitution — guard against a fix
        // that happens to work for inline literals but not variables.
        JsonElement? capturedBody = null;
        var ds = new FakeGraphQlDataSource
        {
            OnUpdate = (_, id, body) => { capturedBody = body.Clone(); return new Dictionary<string, object?> { ["id"] = id }; },
            OnGet = (_, id, _, _) => new Dictionary<string, object?> { ["id"] = id, ["status"] = "archived" },
        };

        var request = OperationRequestBuilder.New()
            .SetDocument("mutation($input: ArticleUpdateInput!) { updateArticle(id: \"5\", input: $input) { id } }")
            .SetVariableValues(new Dictionary<string, object?>
            {
                ["input"] = new Dictionary<string, object?> { ["status"] = "archived", ["version"] = 3 },
            })
            .Build();

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(request);
        ParseData(result);

        capturedBody!.Value.EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(["status", "version"]);
    }

    [Fact]
    public async Task Update_unknown_id_returns_null()
    {
        var ds = new FakeGraphQlDataSource { OnUpdate = (_, _, _) => null };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { updateArticle(id: \"nope\", input: { status: \"x\" }) { id } }");
        var data = ParseData(result);

        data.GetProperty("updateArticle").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Create_returns_write_result_when_reread_is_permission_denied()
    {
        // The create already committed. If the caller's role can write but not read the
        // collection, the post-create re-read (GetAsync) throws PermissionDeniedException — that
        // must NOT surface as a FORBIDDEN error hiding a successful write (which would invite
        // duplicate-create retries). The resolver falls back to the write result instead.
        var ds = new FakeGraphQlDataSource
        {
            OnCreate = (_, _) => new Dictionary<string, object?> { ["id"] = "42", ["status"] = "published" },
            OnGet = (_, _, _, _) => throw new PermissionDeniedException("no read"),
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { createArticle(input: { status: \"published\" }) { id status } }");
        var data = ParseData(result);

        var node = data.GetProperty("createArticle");
        node.GetProperty("id").GetString().Should().Be("42");
        node.GetProperty("status").GetString().Should().Be("published");
    }

    [Fact]
    public async Task Update_returns_write_result_when_reread_is_permission_denied()
    {
        // Same best-effort fallback as create: the update committed, so a read-denied re-read
        // must return the write result rather than FORBIDDEN.
        var ds = new FakeGraphQlDataSource
        {
            OnUpdate = (_, id, _) => new Dictionary<string, object?> { ["id"] = id, ["status"] = "archived" },
            OnGet = (_, _, _, _) => throw new PermissionDeniedException("no read"),
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { updateArticle(id: \"5\", input: { status: \"archived\" }) { id status } }");
        var data = ParseData(result);

        var node = data.GetProperty("updateArticle");
        node.GetProperty("id").GetString().Should().Be("5");
        node.GetProperty("status").GetString().Should().Be("archived");
    }

    // Optimistic-lock CAS miss surfaces over the GraphQL pipeline as extensions.code ==
    // VERSION_CONFLICT (not the generic CONFLICT). Asserted on the parsed extensions.code exactly —
    // a substring Contains("CONFLICT") would falsely pass since VERSION_CONFLICT contains it.
    [Fact]
    public async Task Update_version_conflict_maps_to_VERSION_CONFLICT()
    {
        var ds = new FakeGraphQlDataSource
        {
            OnUpdate = (_, _, _) => throw new ConcurrencyConflictException("Row was modified by another writer.")
        };

        var json = (await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { updateArticle(id: \"5\", input: { status: \"x\", version: 1 }) { id } }")).ToJson();

        using var doc = JsonDocument.Parse(json);
        var code = doc.RootElement.GetProperty("errors")[0]
            .GetProperty("extensions").GetProperty("code").GetString();
        code.Should().Be("VERSION_CONFLICT");
    }

    [Fact]
    public async Task Create_with_tags_and_multivalue_passes_structured_body()
    {
        JsonElement? body = null;
        var ds = new FakeGraphQlDataSource
        {
            OnCreate = (_, b) => { body = b.Clone(); return new Dictionary<string, object?> { ["id"] = "1" }; },
            OnGet = (_, id, _, _) => new Dictionary<string, object?> { ["id"] = id },
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { createArticle(input: { " +
            "regions: [\"apac\", \"emea\"], " +
            "keywords: [{ value: \"ai\", label: \"AI\" }, { value: \"ml\" }], " +
            "gallery: [\"3f2504e0-4f89-11d3-9a0c-0305e82c3301\"], " +
            "tags: [\"9\"] " +
            "}) { id } }");
        ParseData(result);

        body!.Value.GetProperty("regions").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(["apac", "emea"]);
        var kw = body.Value.GetProperty("keywords");
        kw[0].GetProperty("value").GetString().Should().Be("ai");
        kw[0].GetProperty("label").GetString().Should().Be("AI");
        // second tag omitted its label -> pruned (no "label" key), NOT backfilled null
        kw[1].EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["value"]);
        body.Value.GetProperty("gallery").GetArrayLength().Should().Be(1);
        body.Value.GetProperty("tags").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Create_with_repeater_prunes_unsent_sub_fields()
    {
        JsonElement? body = null;
        var ds = new FakeGraphQlDataSource
        {
            OnCreate = (_, b) => { body = b.Clone(); return new Dictionary<string, object?> { ["id"] = "1" }; },
            OnGet = (_, id, _, _) => new Dictionary<string, object?> { ["id"] = id },
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { createArticle(input: { faqs: [{ question: \"Q1\" }] }) { id } }");
        ParseData(result);

        var row = body!.Value.GetProperty("faqs")[0];
        // Only the sent sub-field survives: "answer" was NOT backfilled as null.
        row.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["question"]);
        row.GetProperty("question").GetString().Should().Be("Q1");
    }

    [Fact]
    public async Task Create_with_repeater_via_variables_prunes_unsent_sub_fields()
    {
        JsonElement? body = null;
        var ds = new FakeGraphQlDataSource
        {
            OnCreate = (_, b) => { body = b.Clone(); return new Dictionary<string, object?> { ["id"] = "1" }; },
            OnGet = (_, id, _, _) => new Dictionary<string, object?> { ["id"] = id },
        };

        var request = OperationRequestBuilder.New()
            .SetDocument("mutation($input: ArticleCreateInput!) { createArticle(input: $input) { id } }")
            .SetVariableValues(new Dictionary<string, object?>
            {
                ["input"] = new Dictionary<string, object?>
                {
                    ["faqs"] = new object[] { new Dictionary<string, object?> { ["question"] = "Q1" } },
                },
            })
            .Build();

        ParseData(await (await ExecutorAsync(ds)).ExecuteAsync(request));

        var row = body!.Value.GetProperty("faqs")[0];
        row.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["question"]);
    }

    [Fact]
    public async Task Create_with_json_any_preserves_full_object()
    {
        JsonElement? body = null;
        var ds = new FakeGraphQlDataSource
        {
            OnCreate = (_, b) => { body = b.Clone(); return new Dictionary<string, object?> { ["id"] = "1" }; },
            OnGet = (_, id, _, _) => new Dictionary<string, object?> { ["id"] = id },
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { createArticle(input: { attributes: { a: 1, b: { c: \"x\" } } }) { id } }");
        ParseData(result);

        // Any is opaque: every key the client wrote survives at every depth (no pruning damage).
        var attrs = body!.Value.GetProperty("attributes");
        attrs.GetProperty("a").GetInt32().Should().Be(1);
        attrs.GetProperty("b").GetProperty("c").GetString().Should().Be("x");
    }

    [Fact]
    public async Task Update_with_empty_m2m_array_clears_junction()
    {
        JsonElement? body = null;
        var ds = new FakeGraphQlDataSource
        {
            OnUpdate = (_, id, b) => { body = b.Clone(); return new Dictionary<string, object?> { ["id"] = id }; },
            OnGet = (_, id, _, _) => new Dictionary<string, object?> { ["id"] = id },
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { updateArticle(id: \"5\", input: { tags: [] }) { id } }");
        ParseData(result);

        // Explicit empty array is sent (present + empty) -> ItemService.SyncM2MAsync clears the junction.
        body!.Value.GetProperty("tags").GetArrayLength().Should().Be(0);
        body.Value.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["tags"]);
    }

    [Fact]
    public async Task Create_folds_translations_list_into_locale_keyed_object()
    {
        JsonElement? body = null;
        var ds = new FakeGraphQlDataSource
        {
            OnCreate = (_, b) => { body = b.Clone(); return new Dictionary<string, object?> { ["id"] = "1" }; },
            OnGet = (_, id, _, _) => new Dictionary<string, object?> { ["id"] = id },
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { createArticle(input: { status: \"published\", translations: [" +
            "{ locale: \"en\", fields: { title: \"Hello\" } }, " +
            "{ locale: \"zh-TW\", fields: { title: \"你好\", seoOgImageId: \"3f2504e0-4f89-11d3-9a0c-0305e82c3301\" } } " +
            "] }) { id } }");
        ParseData(result);

        // translations reached ItemService as a locale-KEYED OBJECT (not a list): { en: {...}, zh-TW: {...} }.
        var tr = body!.Value.GetProperty("translations");
        tr.ValueKind.Should().Be(JsonValueKind.Object);
        tr.GetProperty("en").GetProperty("title").GetString().Should().Be("Hello");
        tr.GetProperty("zh-TW").GetProperty("title").GetString().Should().Be("你好");
        tr.GetProperty("zh-TW").GetProperty("seoOgImageId").GetString()
            .Should().Be("3f2504e0-4f89-11d3-9a0c-0305e82c3301");
        // Locale codes and field keys are verbatim (NOT camelCased) — zh-TW stays zh-TW.
        tr.TryGetProperty("zhTw", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Create_translations_prunes_unsent_fields_inline()
    {
        JsonElement? body = null;
        var ds = new FakeGraphQlDataSource
        {
            OnCreate = (_, b) => { body = b.Clone(); return new Dictionary<string, object?> { ["id"] = "1" }; },
            OnGet = (_, id, _, _) => new Dictionary<string, object?> { ["id"] = id },
        };

        // Send only `title` in the en entry — `body`/`seoOgImageId` are NOT backfilled as null.
        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { createArticle(input: { translations: [ { locale: \"en\", fields: { title: \"T\" } } ] }) { id } }");
        ParseData(result);

        var en = body!.Value.GetProperty("translations").GetProperty("en");
        en.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["title"]);
        en.GetProperty("title").GetString().Should().Be("T");
    }

    [Fact]
    public async Task Create_translations_prunes_unsent_fields_via_variables()
    {
        // $variable form: SentFieldsOnly recovers sent keys from the post-substitution literal, so
        // the nested fields object prunes the same way as an inline literal (guard for both forms).
        JsonElement? body = null;
        var ds = new FakeGraphQlDataSource
        {
            OnCreate = (_, b) => { body = b.Clone(); return new Dictionary<string, object?> { ["id"] = "1" }; },
            OnGet = (_, id, _, _) => new Dictionary<string, object?> { ["id"] = id },
        };

        var request = OperationRequestBuilder.New()
            .SetDocument("mutation($input: ArticleCreateInput!) { createArticle(input: $input) { id } }")
            .SetVariableValues(new Dictionary<string, object?>
            {
                ["input"] = new Dictionary<string, object?>
                {
                    ["translations"] = new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["locale"] = "en",
                            ["fields"] = new Dictionary<string, object?> { ["title"] = "T" },
                        },
                    },
                },
            })
            .Build();

        ParseData(await (await ExecutorAsync(ds)).ExecuteAsync(request));

        var en = body!.Value.GetProperty("translations").GetProperty("en");
        en.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["title"]);
    }

    [Fact]
    public async Task Update_adds_locale_and_partial_merges_other_fields()
    {
        JsonElement? body = null;
        var ds = new FakeGraphQlDataSource
        {
            OnUpdate = (_, id, b) => { body = b.Clone(); return new Dictionary<string, object?> { ["id"] = id }; },
            OnGet = (_, id, _, _) => new Dictionary<string, object?> { ["id"] = id },
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { updateArticle(id: \"5\", input: { version: 2, translations: [ { locale: \"zh-TW\", fields: { title: \"新\" } } ] }) { id } }");
        ParseData(result);

        // Only version + translations were sent; translations folded to a locale-keyed object.
        body!.Value.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["version", "translations"]);
        body.Value.GetProperty("translations").GetProperty("zh-TW").GetProperty("title").GetString().Should().Be("新");
    }

    [Fact]
    public async Task Update_without_translations_leaves_them_untouched()
    {
        JsonElement? body = null;
        var ds = new FakeGraphQlDataSource
        {
            OnUpdate = (_, id, b) => { body = b.Clone(); return new Dictionary<string, object?> { ["id"] = id }; },
            OnGet = (_, id, _, _) => new Dictionary<string, object?> { ["id"] = id },
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { updateArticle(id: \"5\", input: { status: \"published\" }) { id } }");
        ParseData(result);

        // No `translations` key sent -> fold is a no-op -> body has no translations key (partial merge).
        body!.Value.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["status"]);
    }

    [Fact]
    public async Task Duplicate_locale_last_writer_wins()
    {
        JsonElement? body = null;
        var ds = new FakeGraphQlDataSource
        {
            OnCreate = (_, b) => { body = b.Clone(); return new Dictionary<string, object?> { ["id"] = "1" }; },
            OnGet = (_, id, _, _) => new Dictionary<string, object?> { ["id"] = id },
        };

        var result = await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { createArticle(input: { translations: [ " +
            "{ locale: \"en\", fields: { title: \"first\" } }, " +
            "{ locale: \"en\", fields: { title: \"second\" } } ] }) { id } }");
        ParseData(result);

        body!.Value.GetProperty("translations").GetProperty("en").GetProperty("title").GetString().Should().Be("second");
    }

    [Fact]
    public async Task Create_missing_required_translation_field_maps_to_BAD_USER_INPUT()
    {
        // ItemService validates translations verbatim; the resolver adds no new validation. Simulate
        // the domain rejection and assert the error filter maps it to BAD_USER_INPUT (parity with REST).
        var ds = new FakeGraphQlDataSource
        {
            OnCreate = (_, _) => throw new QueryException(
                "Required translation field 'title' is missing for locale 'en'."),
        };

        var json = (await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { createArticle(input: { translations: [ { locale: \"en\", fields: { } } ] }) { id } }")).ToJson();

        json.Should().Contain("BAD_USER_INPUT");
        json.Should().Contain("Required translation field");
    }
}
