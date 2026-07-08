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
            OnDelete = (c, id) => { seenCollection = c; seenId = id; return true; }
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
        var ds = new FakeGraphQlDataSource { OnDelete = (_, _) => false };

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

        // The input reached the service as a JSON body carrying exactly the sent keys.
        capturedBody!.Value.GetProperty("status").GetString().Should().Be("published");
        capturedBody.Value.GetProperty("categoryId").GetString().Should().Be("9");
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

    [Fact]
    public async Task Create_permission_denied_maps_to_FORBIDDEN()
    {
        var ds = new FakeGraphQlDataSource
        {
            OnCreate = (_, _) => throw new PermissionDeniedException("Write not permitted.")
        };

        var json = (await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { createArticle(input: { status: \"x\" }) { id } }")).ToJson();

        json.Should().Contain("FORBIDDEN");
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
    public async Task Update_version_conflict_maps_to_CONFLICT()
    {
        var ds = new FakeGraphQlDataSource
        {
            OnUpdate = (_, _, _) => throw new ConcurrencyConflictException("Row was modified by another writer.")
        };

        var json = (await (await ExecutorAsync(ds)).ExecuteAsync(
            "mutation { updateArticle(id: \"5\", input: { status: \"x\", version: 1 }) { id } }")).ToJson();

        json.Should().Contain("CONFLICT");
    }
}
