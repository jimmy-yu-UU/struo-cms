// tests/Struo.Tests/GraphQl/MutationAuthenticationTests.cs
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Infrastructure.Identity;
using Struo.Sample.Blog;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.GraphQl;

/// <summary>
/// Every GraphQL mutation needs an authenticated caller, even when the <c>public</c> role holds a
/// write grant on the collection (REST write endpoints require sign-in the same way). Queries stay
/// open to anonymous callers.
/// </summary>
[Collection("ApiIntegration")]
public sealed class MutationAuthenticationTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Collection = "category";
    private const string CreatedName = "signed-in";
    private Guid _publicRoleId;

    // The shared test database seeds `public` with read on this collection; the tests add write.
    public async Task InitializeAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        _publicRoleId = (await db.Queryable<Role>().Where(r => r.Name == "public").FirstAsync()).Id;
        await SetPublicWriteAsync(db, canWrite: true);
    }

    public async Task DisposeAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        await SetPublicWriteAsync(db, canWrite: false);
        await db.Deleteable<Category>().Where(c => c.Name == CreatedName).ExecuteCommandAsync();
    }

    private async Task SetPublicWriteAsync(ISqlSugarClient db, bool canWrite) =>
        await db.Updateable<Permission>()
            .SetColumns(p => new Permission { CanWrite = canWrite, CanDelete = canWrite })
            .Where(p => p.RoleId == _publicRoleId && p.Collection == Collection)
            .ExecuteCommandAsync();

    private static async Task<JsonElement> PostAsync(HttpClient client, string query)
    {
        var response = await client.PostAsJsonAsync("/graphql", new { query });
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static string ErrorCode(JsonElement root) =>
        root.GetProperty("errors")[0].GetProperty("extensions").GetProperty("code").GetString()!;

    [Theory]
    [InlineData("mutation { createCategory(input: { name: \"anon\" }) { id } }")]
    [InlineData("mutation { updateCategory(id: \"1\", input: { name: \"anon\" }) { id } }")]
    [InlineData("mutation { deleteCategory(id: \"1\") }")]
    [InlineData("mutation { restoreCategory(id: \"1\") { id } }")]
    [InlineData("mutation { _service }")]
    public async Task Anonymous_mutation_is_unauthorized_even_with_public_write_grant(string mutation)
    {
        var root = await PostAsync(factory.CreateClient(), mutation);

        ErrorCode(root).Should().Be("UNAUTHORIZED");
        root.TryGetProperty("data", out var data).Should().BeTrue();
        data.ValueKind.Should().BeOneOf(JsonValueKind.Null, JsonValueKind.Object);
    }

    [Fact]
    public async Task Authenticated_caller_with_public_write_grant_can_mutate()
    {
        var (client, _) = await factory.CreateRolelessClientAsync();

        var root = await PostAsync(client,
            $"mutation {{ createCategory(input: {{ name: \"{CreatedName}\" }}) {{ id name }} }}");

        root.TryGetProperty("errors", out var errors).Should().BeFalse($"unexpected errors: {errors}");
        root.GetProperty("data").GetProperty("createCategory").GetProperty("name").GetString()
            .Should().Be(CreatedName);
    }

    [Fact]
    public async Task Anonymous_query_still_succeeds()
    {
        var root = await PostAsync(factory.CreateClient(), "{ categories { items { id } } }");

        root.TryGetProperty("errors", out var errors).Should().BeFalse($"unexpected errors: {errors}");
        root.GetProperty("data").GetProperty("categories").GetProperty("items").ValueKind
            .Should().Be(JsonValueKind.Array);
    }
}
