// tests/Struo.Tests/Hosting/ConfigureGraphQlTests.cs
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Struo.Domain.Metadata;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Hosting;

/// <summary>
/// <c>StruoCmsBuilder.ConfigureGraphQl</c> hands a fork HotChocolate's own builder after the core
/// schema is registered; what the fork adds is served at /graphql, and a name that clashes with the
/// core schema fails startup with the same message the collection-name check gives.
/// </summary>
[Collection("ApiIntegration")]
public class ConfigureGraphQlTests
{
    public sealed record ForkInfo(string Name);

    [ExtendObjectType("Query")]
    public sealed class ForkQuery
    {
        public ForkInfo GetForkInfo() => new("from-the-fork");
    }

    [ExtendObjectType("Query")]
    public sealed class ArticlesClashQuery
    {
        public string GetArticles() => "clash";
    }

    public sealed class RevisionClash
    {
        public string Name => "clash";
    }

    [SqliteOnlyFact("starts a full host on a SQLite temp database")]
    public async Task A_fork_query_extension_and_object_type_are_served_at_graphql()
    {
        await using var host = await ForkHost.StartAsync(cms =>
            cms.ConfigureGraphQl(gql => gql.AddTypeExtension<ForkQuery>()));

        var response = await host.CreateClient().PostAsJsonAsync(
            "/graphql", new { query = "{ forkInfo { name } _service }" });
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("errors", out _).Should().BeFalse(json);
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("forkInfo").GetProperty("name").GetString().Should().Be("from-the-fork");
        data.GetProperty("_service").GetString().Should().Be("StruoCMS GraphQL");
    }

    [SqliteOnlyFact("starts a full host on a SQLite temp database")]
    public async Task A_fork_root_field_named_like_a_generated_field_fails_startup()
    {
        var error = await ForkHost.StartupErrorAsync(cms =>
            cms.ConfigureGraphQl(gql => gql.AddTypeExtension<ArticlesClashQuery>()));

        var ex = ExceptionChainSearch.FindInner<MetadataException>(error);
        ex.Should().NotBeNull(error?.ToString());
        ex!.Message.Should().Contain("GraphQL name collision").And.Contain("'articles'");
    }

    [SqliteOnlyFact("starts a full host on a SQLite temp database")]
    public async Task A_fork_type_named_like_a_reserved_type_fails_startup()
    {
        var error = await ForkHost.StartupErrorAsync(cms =>
            cms.ConfigureGraphQl(gql => gql.AddObjectType<RevisionClash>(d => d.Name("Revision"))));

        var ex = ExceptionChainSearch.FindInner<MetadataException>(error);
        ex.Should().NotBeNull(error?.ToString());
        ex!.Message.Should().Contain("GraphQL name collision").And.Contain("'Revision'");
    }

    [SqliteOnlyFact("starts a full host on a SQLite temp database")]
    public async Task A_fork_type_named_like_a_collection_type_fails_startup()
    {
        var error = await ForkHost.StartupErrorAsync(cms =>
            cms.ConfigureGraphQl(gql => gql.AddObjectType<RevisionClash>(d => d.Name("Article"))));

        var ex = ExceptionChainSearch.FindInner<MetadataException>(error);
        ex.Should().NotBeNull(error?.ToString());
        ex!.Message.Should().Contain("GraphQL name collision").And.Contain("'Article'");
    }
}
