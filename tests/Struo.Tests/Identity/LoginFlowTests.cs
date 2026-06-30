using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Identity;

[Collection("ApiIntegration")]
public class LoginFlowTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private async Task SeedUser(string email, string password, bool active = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        if (await db.Queryable<User>().Where(u => u.Email == email).AnyAsync()) return;
        await db.Insertable(new User
        {
            Id = Guid.CreateVersion7(), Email = email, Password = hasher.Hash(password), IsActive = active
        }).ExecuteCommandAsync();
    }

    [Fact]
    public async Task Login_valid_sets_cookie_and_me_returns_user()
    {
        await SeedUser("login@b.com", "pw12345678");
        var c = _factory.CreateClient();
        var resp = await c.PostAsJsonAsync("/api/auth/login", new { email = "login@b.com", password = "pw12345678" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Headers.Contains("Set-Cookie").Should().BeTrue();

        var me = await c.GetAsync("/api/auth/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_wrong_password_returns_401()
    {
        await SeedUser("login2@b.com", "pw12345678");
        var c = _factory.CreateClient();
        var resp = await c.PostAsJsonAsync("/api/auth/login", new { email = "login2@b.com", password = "WRONG" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_when_anonymous_returns_401()
    {
        (await _factory.CreateClient().GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
