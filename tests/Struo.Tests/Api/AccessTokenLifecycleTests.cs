using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Application.Security;
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

    private static async Task<(JsonElement Data, string Raw)> ReadInfoWithRawAsync(HttpClient client, Guid userId)
    {
        var res = await client.GetAsync($"/api/users/{userId}/access-token");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await res.Content.ReadAsStringAsync();
        return (Root(raw).GetProperty("data"), raw);
    }

    private static async Task<JsonElement> ReadInfoAsync(HttpClient client, Guid userId) =>
        (await ReadInfoWithRawAsync(client, userId)).Data;

    [Fact]
    public async Task Info_reports_issue_and_last_use_in_utc_and_never_the_hash()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var target = Guid.CreateVersion7();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            await db.Insertable(new User { Id = target, Email = $"tok-info-{target:N}@struo.test", IsActive = true })
                .ExecuteCommandAsync();
        }

        var none = await ReadInfoAsync(admin, target);
        none.GetProperty("hasToken").GetBoolean().Should().BeFalse();
        none.GetProperty("createdAt").ValueKind.Should().Be(JsonValueKind.Null);
        none.GetProperty("lastUsedAt").ValueKind.Should().Be(JsonValueKind.Null);

        var gen = await admin.PostAsync($"/api/users/{target}/access-token", null);
        var token = Root(await gen.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("token").GetString()!;

        var (issued, raw) = await ReadInfoWithRawAsync(admin, target);
        issued.GetProperty("hasToken").GetBoolean().Should().BeTrue();
        issued.GetProperty("createdAt").GetString().Should().EndWith("Z");
        issued.GetProperty("lastUsedAt").ValueKind.Should().Be(JsonValueKind.Null, "a fresh token has not been used");

        raw.Should().NotContain(AccessTokenHasher.Hash(token), "the stored hash is never serialized");
        raw.Should().NotContain(token);
        issued.EnumerateObject().Select(p => p.Name).Should()
            .BeEquivalentTo("hasToken", "createdAt", "lastUsedAt");

        var bearer = _factory.CreateClient();
        bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // The token belongs to a role-less user; any authenticated endpoint is enough to stamp last use.
        (await bearer.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        var used = await ReadInfoAsync(admin, target);
        used.GetProperty("lastUsedAt").GetString().Should().EndWith("Z");

        (await admin.DeleteAsync($"/api/users/{target}/access-token")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var revoked = await ReadInfoAsync(admin, target);
        revoked.GetProperty("hasToken").GetBoolean().Should().BeFalse();
        revoked.GetProperty("createdAt").ValueKind.Should().Be(JsonValueKind.Null, "a revoked token reports no timestamps");
        revoked.GetProperty("lastUsedAt").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Info_is_admin_only_and_404_for_unknown_user()
    {
        var (editor, editorId) = await _factory.CreateEditorClientAsync(["user"], []);
        (await _factory.CreateClient().GetAsync($"/api/users/{editorId}/access-token")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "an anonymous caller is rejected before any admin check");
        (await editor.GetAsync($"/api/users/{editorId}/access-token")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "even a user with read access on `user` is not an admin");

        var admin = await _factory.CreateAuthenticatedClientAsync();
        (await admin.GetAsync($"/api/users/{Guid.CreateVersion7()}/access-token")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }
}
