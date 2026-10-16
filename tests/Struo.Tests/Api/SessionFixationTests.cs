using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SqlSugar;
using Struo.Api.Auth;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// Logging in while the request already carries a valid session cookie must issue a fresh session key
/// and revoke the presented one; the presented key must never end up resolving to the new user.
/// </summary>
[Collection("ApiIntegration")]
public class SessionFixationTests(ApiFactory factory)
{
    private const string Password = "fixation-pw-123";

    private sealed record TestUser(Guid Id, string Email);

    private async Task<TestUser> SeedUserAsync()
    {
        var id = Guid.CreateVersion7();
        var email = $"fixation-{id:N}@struo.test";
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        await db.Insertable(new User { Id = id, Email = email, Password = hasher.Hash(Password), IsActive = true })
            .ExecuteCommandAsync();
        return new TestUser(id, email);
    }

    private HttpClient NewClient()
    {
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
        });
        client.DefaultRequestHeaders.Add(CsrfProtectionMiddleware.HeaderName, "1");
        return client;
    }

    private static string? SessionCookieFrom(HttpResponseMessage resp)
    {
        if (!resp.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        var prefix = AuthSchemes.SessionCookieName + "=";
        var line = values.LastOrDefault(v => v.StartsWith(prefix, StringComparison.Ordinal));
        if (line is null) return null;
        var value = line[prefix.Length..].Split(';')[0];
        return value.Length == 0 ? null : value;
    }

    private static HttpRequestMessage WithCookie(HttpMethod method, string url, string? cookie)
    {
        var req = new HttpRequestMessage(method, url);
        if (cookie is not null) req.Headers.Add("Cookie", $"{AuthSchemes.SessionCookieName}={cookie}");
        return req;
    }

    private static async Task<HttpResponseMessage> LoginAsync(
        HttpClient client, TestUser user, string? presentedCookie)
    {
        var req = WithCookie(HttpMethod.Post, "/api/auth/login", presentedCookie);
        req.Content = JsonContent.Create(new { email = user.Email, password = Password });
        return await client.SendAsync(req);
    }

    private static async Task<(HttpStatusCode Status, string? Id)> MeAsync(HttpClient client, string? cookie)
    {
        var resp = await client.SendAsync(WithCookie(HttpMethod.Get, "/api/auth/me", cookie));
        if (resp.StatusCode != HttpStatusCode.OK) return (resp.StatusCode, null);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        if (root.TryGetProperty("data", out var data)) root = data;
        return (resp.StatusCode, root.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Login_over_existing_session_closes_the_fixation_window()
    {
        var client = NewClient();
        var userA = await SeedUserAsync();
        var userB = await SeedUserAsync();

        var loginA = await LoginAsync(client, userA, null);
        loginA.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookieA = SessionCookieFrom(loginA);
        cookieA.Should().NotBeNull();

        var loginB = await LoginAsync(client, userB, cookieA);
        loginB.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookieB = SessionCookieFrom(loginB);
        cookieB.Should().NotBeNull().And.NotBe(cookieA);

        // The new cookie authenticates B.
        (await MeAsync(client, cookieB)).Id.Should().Be(userB.Id.ToString());

        // The presented cookie resolves to nobody - in particular never to B.
        var presented = await MeAsync(client, cookieA);
        presented.Id.Should().NotBe(userB.Id.ToString());
        presented.Status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Failed_login_over_existing_session_leaves_that_session_alive()
    {
        var client = NewClient();
        var userA = await SeedUserAsync();
        var cookieA = SessionCookieFrom(await LoginAsync(client, userA, null));

        var req = WithCookie(HttpMethod.Post, "/api/auth/login", cookieA);
        req.Content = JsonContent.Create(new { email = userA.Email, password = "wrong-password" });
        (await client.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await MeAsync(client, cookieA)).Id.Should().Be(userA.Id.ToString());
    }

    [Fact]
    public async Task Login_over_existing_session_still_requires_the_csrf_header()
    {
        var client = NewClient();
        var userA = await SeedUserAsync();
        var cookieA = SessionCookieFrom(await LoginAsync(client, userA, null));

        var req = WithCookie(HttpMethod.Post, "/api/auth/login", cookieA);
        req.Headers.Remove(CsrfProtectionMiddleware.HeaderName);
        client.DefaultRequestHeaders.Remove(CsrfProtectionMiddleware.HeaderName);
        req.Content = JsonContent.Create(new { email = userA.Email, password = Password });
        (await client.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static Func<HttpContext, Task> Middleware(OidcOptions oidc) =>
        new SessionRotationMiddleware(_ => Task.CompletedTask, Options.Create(oidc)).InvokeAsync;

    private static DefaultHttpContext Request(string method, string path, string cookieHeader)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = method;
        ctx.Request.Path = path;
        ctx.Request.Headers.Cookie = cookieHeader;
        return ctx;
    }

    [Theory]
    [InlineData("POST", "/api/auth/login", true)]
    [InlineData("GET", "/api/auth/login", false)]
    [InlineData("GET", "/signin-oidc", true)]
    [InlineData("GET", "/api/auth/me", false)]
    public async Task Middleware_hides_the_session_cookie_only_on_sign_in_requests(string method, string path, bool hidden)
    {
        var invoke = Middleware(new OidcOptions { Enabled = true, CallbackPath = "/signin-oidc" });
        var ctx = Request(method, path, $"other=1; {AuthSchemes.SessionCookieName}=abc; more=2");

        await invoke(ctx);

        if (hidden)
        {
            ctx.Request.Headers.Cookie.ToString().Should().Be("other=1; more=2");
            SessionRotation.PresentedCookie(ctx).Should().Be("abc");
        }
        else
        {
            ctx.Request.Headers.Cookie.ToString().Should().Contain(AuthSchemes.SessionCookieName);
            SessionRotation.PresentedCookie(ctx).Should().BeNull();
        }
    }

    [Fact]
    public async Task Middleware_ignores_the_oidc_callback_path_when_oidc_is_disabled()
    {
        var invoke = Middleware(new OidcOptions { Enabled = false, CallbackPath = "/signin-oidc" });
        var ctx = Request("GET", "/signin-oidc", $"{AuthSchemes.SessionCookieName}=abc");

        await invoke(ctx);

        SessionRotation.PresentedCookie(ctx).Should().BeNull();
    }

    private sealed class FixedExternalLogin(Guid userId) : IExternalLoginService
    {
        public Task<ExternalLoginResult> ResolveOrProvisionAsync(
            ExternalIdentity identity, ExternalLoginPolicy policy, CancellationToken ct = default) =>
            Task.FromResult(ExternalLoginResult.Ok(userId, false));
    }

    // Runs the callback request through the real middleware, then the production OnTokenValidated logic.
    private async Task<ClaimsPrincipal?> OidcCallbackAsync(TestUser user, string? presentedCookie)
    {
        var oidc = new OidcOptions { Enabled = true, CallbackPath = "/signin-oidc" };
        var services = new ServiceCollection()
            .AddSingleton<IExternalLoginService>(new FixedExternalLogin(user.Id))
            .AddSingleton(Options.Create(oidc))
            .AddSingleton(factory.Services.GetRequiredService<SessionRotation>())
            .BuildServiceProvider();
        var ctx = Request("GET", "/signin-oidc", $"{AuthSchemes.SessionCookieName}={presentedCookie}");
        ctx.RequestServices = services;
        await Middleware(oidc)(ctx);
        return (await OidcWiring.ResolveLocalPrincipalAsync(ctx, new ClaimsPrincipal(new ClaimsIdentity()))).Principal;
    }

    [Fact]
    public async Task Oidc_callback_over_existing_session_revokes_the_presented_session()
    {
        var client = NewClient();
        var userA = await SeedUserAsync();
        var userB = await SeedUserAsync();
        var cookieA = SessionCookieFrom(await LoginAsync(client, userA, null));
        using var scope = factory.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IUserSessionStore>();
        (await sessions.ListTicketKeysForUserAsync(userA.Id)).Should().HaveCount(1);

        var principal = await OidcCallbackAsync(userB, cookieA);

        principal!.FindFirstValue(ClaimTypes.NameIdentifier).Should().Be(userB.Id.ToString());
        var presented = await MeAsync(client, cookieA);
        presented.Id.Should().NotBe(userB.Id.ToString());
        presented.Status.Should().Be(HttpStatusCode.Unauthorized);
        (await sessions.ListTicketKeysForUserAsync(userA.Id)).Should().BeEmpty();
    }
}
