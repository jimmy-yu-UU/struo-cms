using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

/// <summary>
/// Requests must not leave state behind in SqlSugar's static registries. A per-request scope adds one
/// key per request (and, on the synchronous readiness path, a whole inner client) that nothing frees.
/// </summary>
[Collection(SqlSugarStaticStateCollection.Name)]
public sealed class SqlSugarScopeRetentionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const int Requests = 2000;
    private const string MultiRootQuery =
        "{ articles { items { id } } categories { items { id } } tags { items { id } } }";

    [Fact]
    public void ISqlSugarClient_is_one_instance_for_the_whole_host()
    {
        using var first = factory.Services.CreateScope();
        using var second = factory.Services.CreateScope();

        var a = first.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var b = second.ServiceProvider.GetRequiredService<ISqlSugarClient>();

        a.Should().BeSameAs(b);
    }

    [Fact]
    public async Task A_mix_of_requests_adds_no_key_to_SqlSugars_static_registries()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync();
        using var anon = factory.CreateClient();
        var requests = new Func<Task<HttpResponseMessage>>[]
        {
            () => admin.GetAsync("/api/items/category"),
            () => admin.PostAsJsonAsync("/graphql", new { query = MultiRootQuery }),
            () => anon.GetAsync("/health/ready"),
            () => anon.GetAsync("/api/items/article"),
        };
        await Drive(requests, 40);
        var (asyncBefore, threadBefore) = (SqlSugarStaticContext.AsyncKeys, SqlSugarStaticContext.ThreadKeys);

        await Drive(requests, Requests);

        (SqlSugarStaticContext.AsyncKeys - asyncBefore).Should().Be(0);
        (SqlSugarStaticContext.ThreadKeys - threadBefore).Should().Be(0);
    }

    // A fresh host, baselined before its first readiness call: a singleton scope owns at most one
    // thread-local registry key, so a shared host that already served readiness would hide a
    // readiness check that goes back to touching the scope from a sync lambda.
    [Fact]
    public async Task The_readiness_check_never_registers_a_client_with_the_singleton_scope()
    {
        await using var fresh = new ApiFactory();
        using var anon = fresh.CreateClient();
        var (asyncBefore, threadBefore) = (SqlSugarStaticContext.AsyncKeys, SqlSugarStaticContext.ThreadKeys);

        await Drive([() => anon.GetAsync("/health/ready")], 200);

        (SqlSugarStaticContext.AsyncKeys - asyncBefore).Should().Be(0);
        (SqlSugarStaticContext.ThreadKeys - threadBefore).Should().Be(0);
    }

    private static async Task Drive(IReadOnlyList<Func<Task<HttpResponseMessage>>> requests, int count)
    {
        for (var i = 0; i < count; i++)
        {
            using var response = await requests[i % requests.Count]();
            var body = await response.Content.ReadAsStringAsync();
            response.IsSuccessStatusCode.Should().BeTrue(body);
            if (response.RequestMessage?.RequestUri?.AbsolutePath == "/graphql")
                JsonDocument.Parse(body).RootElement.TryGetProperty("errors", out _).Should().BeFalse(body);
        }
    }
}
