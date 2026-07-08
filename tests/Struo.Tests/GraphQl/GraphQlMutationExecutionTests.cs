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
}
