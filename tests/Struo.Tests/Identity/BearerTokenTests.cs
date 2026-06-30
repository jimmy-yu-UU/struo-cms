using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Identity;

[Collection("ApiIntegration")]
public class BearerTokenTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private async Task<Guid> SeedUser(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var id = Guid.CreateVersion7();
        await db.Insertable(new User { Id = id, Email = email, Password = hasher.Hash("pw12345678"), IsActive = true })
            .ExecuteCommandAsync();
        return id;
    }

    [Fact]
    public async Task Generated_token_authenticates_then_revoke_rejects()
    {
        var id = await SeedUser($"bearer-{Guid.NewGuid():N}@b.com");
        var admin = await _factory.CreateAuthenticatedClientAsync();

        var gen = await admin.PostAsync($"/api/users/{id}/access-token", null);
        gen.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = Root(await gen.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("token").GetString()!;

        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await c.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await admin.DeleteAsync($"/api/users/{id}/access-token")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await c.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Bad_token_is_unauthorized()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-token");
        (await c.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
