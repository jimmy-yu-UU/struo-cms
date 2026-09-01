// tests/Struo.Tests/GraphQl/GraphQlUserDeleteSessionRevocationTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Api.Auth;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.GraphQl;

/// <summary>
/// GraphQL exposes its own <c>deleteUser</c> mutation (<c>MutationResolvers.DeleteField</c> ->
/// <c>IGraphQlDataSource.DeleteAsync</c> -> <c>ItemService.DeleteAsync</c> directly — it never touches
/// <c>ItemsController</c>). Proactive session revocation on user delete therefore has to live in
/// <c>ItemService</c>, not the REST controller, or this exact path leaves orphaned <c>user_sessions</c>
/// rows. <see cref="Struo.Tests.Api.UserDeleteSessionRevocationTests"/> pins the same behavior over
/// REST; this pins it over GraphQL.
/// </summary>
[Collection("ApiIntegration")]
public class GraphQlUserDeleteSessionRevocationTests(ApiFactory factory)
{
    private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement;

    private static async Task<JsonElement> PostGraphQlAsync(HttpClient client, string query)
    {
        var response = await client.PostAsJsonAsync("/graphql", new { query });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return Root(await response.Content.ReadAsStringAsync());
    }

    private async Task<(string email, string password, Guid userId)> SeedUserAsync()
    {
        var userId = Guid.CreateVersion7();
        const string password = "gql-delete-pw-123";
        var email = $"gql-delete-{userId:N}@struo.test";
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        await db.Insertable(new User
        {
            Id = userId, Email = email, Password = hasher.Hash(password), IsActive = true,
        }).ExecuteCommandAsync();
        return (email, password, userId);
    }

    private async Task LoginAsync(string email, string password)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CsrfProtectionMiddleware.HeaderName, "1");
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        resp.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task DeleteUser_mutation_immediately_removes_the_users_index_rows()
    {
        var (email, password, userId) = await SeedUserAsync();
        await LoginAsync(email, password); // creates a user_sessions row over REST; unrelated to the GraphQL call below

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            (await db.Queryable<UserSession>().Where(s => s.UserId == userId).CountAsync())
                .Should().Be(1, "the login above must have indexed the session");
        }

        var admin = await factory.CreateAuthenticatedClientAsync();
        var result = await PostGraphQlAsync(admin, $"mutation {{ deleteUser(id: \"{userId}\") }}");
        result.TryGetProperty("errors", out var errors).Should().BeFalse($"unexpected errors: {errors}");
        result.GetProperty("data").GetProperty("deleteUser").GetBoolean().Should().BeTrue();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            (await db.Queryable<UserSession>().Where(s => s.UserId == userId).CountAsync())
                .Should().Be(0, "the GraphQL delete mutation goes straight through ItemService, bypassing " +
                                "ItemsController entirely, so revocation must live in ItemService itself");
        }
    }
}
