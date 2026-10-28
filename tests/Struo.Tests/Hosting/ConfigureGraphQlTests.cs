// tests/Struo.Tests/Hosting/ConfigureGraphQlTests.cs
using System.Net.Http.Json;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Types;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Struo.Api.Hosting;
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
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "HotChocolate resolves only instance members of a type extension.")]
    public sealed class ForkQuery
    {
        public ForkInfo GetForkInfo() => new("from-the-fork");
    }

    [ExtendObjectType("Query")]
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "HotChocolate resolves only instance members of a type extension.")]
    public sealed class FxArticlesClashQuery
    {
        public string GetFxArticles() => "clash";
    }

    [ExtendObjectType("Mutation")]
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "HotChocolate resolves only instance members of a type extension.")]
    public sealed class MutationClash
    {
        public string CreateFxArticle() => "clash";
    }

    [ExtendObjectType("Mutation")]
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "HotChocolate resolves only instance members of a type extension.")]
    public sealed class ForkMutation
    {
        public string ForkOpen() => "open";

        public string ForkGuarded([Service] IHttpContextAccessor accessor) =>
            accessor.HttpContext?.User.Identity?.IsAuthenticated == true
                ? "guarded"
                : throw new GraphQLException("Sign in to run this mutation.");
    }

    [ExtendObjectType("FxArticle")]
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "HotChocolate resolves only instance members of a type extension.")]
    public sealed class FxArticleExtension
    {
        public string GetForkNote() => "note";
    }

    public sealed class EmptyThing;

    [ExtendObjectType("Query")]
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "HotChocolate resolves only instance members of a type extension.")]
    public sealed class EmptyTypeQuery
    {
        public EmptyThing GetEmpty() => new();
    }

    public sealed class RevisionClash
    {
        public static string Name => "clash";
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
            cms.ConfigureGraphQl(gql => gql.AddTypeExtension<FxArticlesClashQuery>()));

        var ex = ExceptionChainSearch.FindInner<MetadataException>(error);
        ex.Should().NotBeNull(error?.ToString());
        ex!.Message.Should().Contain("GraphQL name collision").And.Contain("'fxArticles'");
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
            cms.ConfigureGraphQl(gql => gql.AddObjectType<RevisionClash>(d => d.Name("FxArticle"))));

        var ex = ExceptionChainSearch.FindInner<MetadataException>(error);
        ex.Should().NotBeNull(error?.ToString());
        ex!.Message.Should().Contain("GraphQL name collision").And.Contain("'FxArticle'");
    }

    [SqliteOnlyFact("starts a full host on a SQLite temp database")]
    public async Task A_fork_mutation_field_is_served_and_carries_no_automatic_authentication()
    {
        await using var host = await ForkHost.StartAsync(cms =>
            cms.ConfigureGraphQl(gql => gql.AddTypeExtension<ForkMutation>()));
        var client = host.CreateClient();

        var open = await client.PostAsJsonAsync("/graphql", new { query = "mutation { forkOpen }" });
        var openJson = await open.Content.ReadAsStringAsync();
        var guarded = await client.PostAsJsonAsync("/graphql", new { query = "mutation { forkGuarded }" });
        var guardedJson = await guarded.Content.ReadAsStringAsync();

        using var openDoc = JsonDocument.Parse(openJson);
        openDoc.RootElement.TryGetProperty("errors", out _).Should().BeFalse(openJson);
        openDoc.RootElement.GetProperty("data").GetProperty("forkOpen").GetString().Should().Be("open");
        using var guardedDoc = JsonDocument.Parse(guardedJson);
        guardedDoc.RootElement.TryGetProperty("errors", out _).Should().BeTrue(
            "the fork field checks the caller itself; " + guardedJson);
    }

    [SqliteOnlyFact("starts a full host on a SQLite temp database")]
    public async Task A_fork_mutation_field_named_like_a_generated_field_fails_startup()
    {
        var error = await ForkHost.StartupErrorAsync(cms =>
            cms.ConfigureGraphQl(gql => gql.AddTypeExtension<MutationClash>()));

        var ex = ExceptionChainSearch.FindInner<MetadataException>(error);
        ex.Should().NotBeNull(error?.ToString());
        ex!.Message.Should().Contain("GraphQL name collision").And.Contain("Mutation field name 'createFxArticle'");
    }

    [SqliteOnlyFact("starts a full host on a SQLite temp database")]
    public async Task A_fork_field_on_a_generated_type_is_a_legitimate_extension()
    {
        await using var host = await ForkHost.StartAsync(cms =>
            cms.ConfigureGraphQl(gql => gql.AddTypeExtension<FxArticleExtension>()));

        var response = await host.CreateClient().PostAsJsonAsync(
            "/graphql", new { query = "{ __type(name: \"FxArticle\") { fields { name } } }" });
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("errors", out _).Should().BeFalse(json);
        doc.RootElement.GetProperty("data").GetProperty("__type").GetProperty("fields").EnumerateArray()
            .Select(f => f.GetProperty("name").GetString()).Should().Contain("forkNote");
    }

    [SqliteOnlyFact("starts a full host on a SQLite temp database")]
    public async Task A_schema_error_that_is_not_a_name_collision_is_not_labelled_as_one()
    {
        var error = await ForkHost.StartupErrorAsync(cms =>
            cms.ConfigureGraphQl(gql => gql.AddTypeExtension<EmptyTypeQuery>()));

        error.Should().NotBeNull();
        ExceptionChainSearch.FindInner<HotChocolate.SchemaException>(error).Should().NotBeNull(error!.ToString());
        error!.ToString().Should().NotContain("name collision");
    }

    [Fact]
    public void ConfigureGraphQl_after_AddStruoCms_has_applied_the_configuration_throws()
    {
        StruoCmsBuilder? kept = null;
        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
        Struo.Api.Hosting.StruoCmsHostingExtensions.AddStruoCms(builder, cms => kept = cms);

        var late = () => kept!.ConfigureGraphQl(_ => { });

        late.Should().Throw<InvalidOperationException>().WithMessage("*AddStruoCms*");
    }
}
