using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

// The bearer token stays permanent, but issuance and last-use are recorded so a leaked or
// stale token can be spotted. GenerateToken stamps CreatedAt (and clears LastUsedAt); a bearer
// request stamps LastUsedAt.
[Collection("ApiIntegration")]
public class AccessTokenLifecycleTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private async Task<User> LoadAdminAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        return await db.Queryable<User>().Where(u => u.Id == _factory.AdminUserId).FirstAsync();
    }

    [Fact]
    public async Task Generate_stamps_created_and_bearer_use_stamps_last_used()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync();

        var gen = await admin.PostAsync($"/api/users/{_factory.AdminUserId}/access-token", null);
        gen.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = Root(await gen.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("token").GetString()!;

        var afterGen = await LoadAdminAsync();
        afterGen.AccessTokenCreatedAt.Should().NotBeNull("generating a token records when it was issued");
        afterGen.AccessTokenLastUsedAt.Should().BeNull("a freshly issued token has not been used yet");

        var bearer = _factory.CreateClient();
        bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await bearer.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        var afterUse = await LoadAdminAsync();
        afterUse.AccessTokenLastUsedAt.Should().NotBeNull("a bearer-authenticated request records last use");
    }
}
