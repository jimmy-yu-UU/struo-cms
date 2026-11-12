// tests/Struo.Tests/GraphQl/GraphQlItemUseCasesSeamTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Struo.Application.Query;
using Struo.Application.Revisions;
using Struo.Domain.Query;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.GraphQl;

/// <summary>
/// GraphQL reads and writes reach the data through whatever <see cref="IItemUseCases"/> is registered:
/// a recording decorator registered on a derived host sees every call a query and a mutation make.
/// </summary>
[Collection("ApiIntegration")]
public class GraphQlItemUseCasesSeamTests(ApiFactory factory)
{
    private sealed class RecordingUseCases(IItemUseCases inner, List<string> calls) : IItemUseCases
    {
        public Task<PagedResult> QueryAsync(string collection, QueryModel raw, string? locale = null,
            DeletedFilter deleted = DeletedFilter.Exclude, CancellationToken ct = default)
        {
            calls.Add(nameof(QueryAsync));
            return inner.QueryAsync(collection, raw, locale, deleted, ct);
        }

        public Task<IReadOnlyDictionary<string, object?>?> GetAsync(string collection, string id, DeepSpec? deep = null,
            string? locale = null, DeletedFilter deleted = DeletedFilter.Exclude, CancellationToken ct = default)
        {
            calls.Add(nameof(GetAsync));
            return inner.GetAsync(collection, id, deep, locale, deleted, ct);
        }

        public Task<IReadOnlyDictionary<string, object?>> CreateAsync(string collection, JsonElement body, CancellationToken ct = default)
        {
            calls.Add(nameof(CreateAsync));
            return inner.CreateAsync(collection, body, ct);
        }

        public Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(string collection, string id, JsonElement body, CancellationToken ct = default)
        {
            calls.Add(nameof(UpdateAsync));
            return inner.UpdateAsync(collection, id, body, ct);
        }

        public Task<bool> DeleteAsync(string collection, string id, bool purge = false, CancellationToken ct = default)
        {
            calls.Add(nameof(DeleteAsync));
            return inner.DeleteAsync(collection, id, purge, ct);
        }

        public Task<IReadOnlyDictionary<string, object?>?> RestoreAsync(string collection, string id, CancellationToken ct = default)
        {
            calls.Add(nameof(RestoreAsync));
            return inner.RestoreAsync(collection, id, ct);
        }

        public Task<IReadOnlyList<RevisionInfo>?> ListRevisionsAsync(string collection, string id, CancellationToken ct = default)
        {
            calls.Add(nameof(ListRevisionsAsync));
            return inner.ListRevisionsAsync(collection, id, ct);
        }

        public Task<RevisionRecord?> GetRevisionAsync(string collection, string id, long revisionNumber, CancellationToken ct = default)
        {
            calls.Add(nameof(GetRevisionAsync));
            return inner.GetRevisionAsync(collection, id, revisionNumber, ct);
        }

        public Task<IReadOnlyDictionary<string, object?>?> RevertAsync(string collection, string id, long revisionNumber,
            long? expectedVersion = null, CancellationToken ct = default)
        {
            calls.Add(nameof(RevertAsync));
            return inner.RevertAsync(collection, id, revisionNumber, expectedVersion, ct);
        }
    }

    private static async Task<JsonElement> PostAsync(HttpClient client, string query)
    {
        var response = await client.PostAsJsonAsync("/graphql", new { query });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        root.TryGetProperty("errors", out var errors).Should().BeFalse($"unexpected errors: {errors}");
        return root;
    }

    [Fact]
    public async Task A_graphql_read_and_a_graphql_mutation_both_pass_through_the_registered_use_cases()
    {
        var calls = new List<string>();
        using var host = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.AddScoped<IItemUseCases>(sp => new RecordingUseCases(sp.GetRequiredService<ItemService>(), calls))));
        var client = host.CreateClient();
        await factory.EnsureAdminSeededAsync();
        client.DefaultRequestHeaders.Add("X-Struo-CSRF", "1");
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword });
        login.StatusCode.Should().Be(HttpStatusCode.OK, await login.Content.ReadAsStringAsync());

        await PostAsync(client, "{ fxCategories(limit: 1) { total } }");
        calls.Should().Contain(nameof(IItemUseCases.QueryAsync));

        calls.Clear();
        await PostAsync(client, "mutation { createFxCategory(input: { name: \"GqlSeam\" }) { id } }");
        calls.Should().Contain(nameof(IItemUseCases.CreateAsync));
    }
}
