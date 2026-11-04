using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SqlSugar;
using Struo.Api.Auth;
using Struo.Api.Controllers;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

// GET/DELETE /api/login-locks?email=: query and clear the per-account login lock. Every test runs on
// its own derived host (isolated in-memory throttle) with a small PermitLimit, seeding through the
// shared base factory, like LoginAccountThrottleTests.
[Collection("ApiIntegration")]
public class LoginLocksEndpointTests(ApiFactory factory)
{
    private const string Unlock = "login.unlock";
    private const string WrongPassword = "wrong-password";
    private const string Url = "/api/login-locks";

    // Registered as the controller's ILogger<T> directly: the host's logging pipeline is Serilog, which
    // does not forward to providers added through ConfigureLogging.
    private sealed class CapturingProvider : ILogger<LoginLocksController>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (Messages) Messages.Add($"{logLevel}|{formatter(state, exception)}");
        }
    }

    private WebApplicationFactory<Program> Host(CapturingProvider? logs = null, int permitLimit = 3) =>
        factory.WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:LoginAccount:Enabled"] = "true",
                ["RateLimiting:LoginAccount:PermitLimit"] = permitLimit.ToString(),
                ["RateLimiting:LoginAccount:WindowSeconds"] = "300",
                ["RateLimiting:Login:Enabled"] = "false",
            }));
            if (logs is not null) b.ConfigureServices(services => services.AddSingleton<ILogger<LoginLocksController>>(logs));
        });

    private static HttpClient NewClient(WebApplicationFactory<Program> host, bool csrf = true)
    {
        var client = host.CreateClient();
        if (csrf) client.DefaultRequestHeaders.Add(CsrfProtectionMiddleware.HeaderName, "1");
        return client;
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password });

    private static async Task<HttpResponseMessage> LoginOnceAsync(
        WebApplicationFactory<Program> host, string email, string password)
    {
        using var client = NewClient(host);
        return await LoginAsync(client, email, password);
    }

    private static async Task<HttpClient> LoggedInAsync(WebApplicationFactory<Program> host, string email, string password)
    {
        var client = NewClient(host);
        (await LoginAsync(client, email, password)).EnsureSuccessStatusCode();
        return client;
    }

    private async Task<HttpClient> AdminAsync(WebApplicationFactory<Program> host)
    {
        await factory.CreateAuthenticatedClientAsync(); // seeds the admin
        return await LoggedInAsync(host, ApiFactory.AdminEmail, ApiFactory.AdminPassword);
    }

    private static string Q(string email) => $"{Url}?email={Uri.EscapeDataString(email)}";

    private static async Task<(bool locked, int retryAfter)> StateAsync(HttpClient client, string email)
    {
        var resp = await client.GetAsync(Q(email));
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        var data = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        return (data.GetProperty("locked").GetBoolean(), data.GetProperty("retryAfterSeconds").GetInt32());
    }

    private static async Task LockAsync(WebApplicationFactory<Program> host, string email, int failures = 3)
    {
        using var client = NewClient(host);
        for (var i = 0; i < failures; i++)
            (await LoginAsync(client, email, WrongPassword)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task GrantUnlockAsync(Guid userId)
    {
        Guid roleId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            roleId = (await db.Queryable<UserRole>().Where(ur => ur.UserId == userId).FirstAsync()).RoleId;
        }
        using var admin = await factory.CreateAuthenticatedClientAsync();
        (await admin.PutAsJsonAsync($"/api/roles/{roleId}/capabilities", new[] { Unlock })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_locked_account_reports_locked_until_deleted_then_logs_in_with_the_right_password()
    {
        using var host = Host();
        var (email, password, _) = await factory.SeedEditorAsync([], []);
        using var admin = await AdminAsync(host);
        (await StateAsync(admin, email)).locked.Should().BeFalse();

        await LockAsync(host, email);

        var locked = await StateAsync(admin, email);
        locked.locked.Should().BeTrue();
        locked.retryAfter.Should().BeInRange(1, 300);
        (await LoginOnceAsync(host, email, password)).StatusCode.Should().Be((HttpStatusCode)429);

        (await admin.DeleteAsync(Q(email))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await StateAsync(admin, email)).Should().Be((false, 0));
        (await LoginOnceAsync(host, email, password)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Querying_does_not_spend_an_attempt()
    {
        using var host = Host(permitLimit: 3);
        var (email, password, _) = await factory.SeedEditorAsync([], []);
        using var admin = await AdminAsync(host);
        await LockAsync(host, email, failures: 2);

        for (var i = 0; i < 5; i++) (await StateAsync(admin, email)).locked.Should().BeFalse();

        (await LoginOnceAsync(host, email, password)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_email_is_matched_case_and_whitespace_insensitively()
    {
        using var host = Host();
        var (email, _, _) = await factory.SeedEditorAsync([], []);
        using var admin = await AdminAsync(host);
        await LockAsync(host, email);

        (await StateAsync(admin, $"  {email.ToUpperInvariant()} ")).locked.Should().BeTrue();
        (await admin.DeleteAsync(Q($"  {email.ToUpperInvariant()} "))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await StateAsync(admin, email)).locked.Should().BeFalse();
    }

    [Fact]
    public async Task A_holder_of_login_unlock_can_query_and_clear()
    {
        using var host = Host();
        var (victim, _, _) = await factory.SeedEditorAsync([], []);
        var (helperEmail, helperPassword, helperId) = await factory.SeedEditorAsync([], []);
        await GrantUnlockAsync(helperId);
        using var helper = await LoggedInAsync(host, helperEmail, helperPassword);
        await LockAsync(host, victim);

        (await StateAsync(helper, victim)).locked.Should().BeTrue();
        (await helper.DeleteAsync(Q(victim))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await StateAsync(helper, victim)).locked.Should().BeFalse();
    }

    [Fact]
    public async Task A_caller_without_login_unlock_is_403_on_both_verbs_and_the_lock_stays()
    {
        using var host = Host();
        var (victim, _, _) = await factory.SeedEditorAsync([], []);
        var (otherEmail, otherPassword, _) = await factory.SeedEditorAsync([], []);
        using var other = await LoggedInAsync(host, otherEmail, otherPassword);
        using var admin = await AdminAsync(host);
        await LockAsync(host, victim);

        (await other.GetAsync(Q(victim))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await other.DeleteAsync(Q(victim))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await StateAsync(admin, victim)).locked.Should().BeTrue();
    }

    [Fact]
    public async Task Anonymous_is_401()
    {
        using var host = Host();
        using var anonymous = NewClient(host);

        (await anonymous.GetAsync(Q("a@struo.test"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.DeleteAsync(Q("a@struo.test"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_cookie_delete_without_the_csrf_header_is_403_and_keeps_the_lock()
    {
        using var host = Host();
        var (email, _, _) = await factory.SeedEditorAsync([], []);
        await factory.CreateAuthenticatedClientAsync();
        using var bare = NewClient(host, csrf: false);
        (await LoginAsync(bare, ApiFactory.AdminEmail, ApiFactory.AdminPassword)).EnsureSuccessStatusCode();
        await LockAsync(host, email);

        var refused = await bare.DeleteAsync(Q(email));
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await refused.Content.ReadAsStringAsync()).Should().Contain(CsrfProtectionMiddleware.HeaderName);

        using var admin = await AdminAsync(host);
        (await StateAsync(admin, email)).locked.Should().BeTrue();
    }

    [Fact]
    public async Task An_unknown_email_gets_the_same_shape_and_status_as_a_known_one()
    {
        using var host = Host();
        var (known, _, _) = await factory.SeedEditorAsync([], []);
        var unknown = $"nobody-{Guid.NewGuid():N}@struo.test";
        using var admin = await AdminAsync(host);

        var knownResp = await admin.GetAsync(Q(known));
        var unknownResp = await admin.GetAsync(Q(unknown));
        unknownResp.StatusCode.Should().Be(knownResp.StatusCode);
        (await unknownResp.Content.ReadAsStringAsync()).Should().Be(await knownResp.Content.ReadAsStringAsync());

        (await admin.DeleteAsync(Q(unknown))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // A lock on an address with no account is still a lock: it reports like any other.
        await LockAsync(host, unknown);
        (await StateAsync(admin, unknown)).locked.Should().BeTrue();
        (await admin.DeleteAsync(Q(unknown))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await StateAsync(admin, unknown)).locked.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_missing_or_blank_email_is_400(string email)
    {
        using var host = Host();
        using var admin = await AdminAsync(host);

        (await admin.GetAsync($"{Url}?email={Uri.EscapeDataString(email)}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.DeleteAsync($"{Url}?email={Uri.EscapeDataString(email)}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.DeleteAsync(Url)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_over_long_email_is_400()
    {
        using var host = Host();
        using var admin = await AdminAsync(host);
        var tooLong = new string('a', 320) + "@struo.test";

        (await admin.GetAsync(Q(tooLong))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.DeleteAsync(Q(tooLong))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // An account can hold any non-blank email and the lock is keyed on the string alone, so an address
    // without the usual shape must stay readable and clearable.
    [Fact]
    public async Task An_email_of_any_shape_can_be_read_and_cleared()
    {
        using var host = Host();
        using var admin = await AdminAsync(host);
        const string odd = "no-at-sign";
        await LockAsync(host, odd);

        (await StateAsync(admin, odd)).locked.Should().BeTrue();
        (await admin.DeleteAsync(Q(odd))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await StateAsync(admin, odd)).locked.Should().BeFalse();
    }

    [Fact]
    public async Task Clearing_logs_the_actor_and_target_ids_and_never_the_email()
    {
        var logs = new CapturingProvider();
        using var host = Host(logs);
        var (email, _, targetId) = await factory.SeedEditorAsync([], []);
        var unknown = $"ghost-{Guid.NewGuid():N}@struo.test";
        using var admin = await AdminAsync(host);

        (await admin.DeleteAsync(Q(email))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.DeleteAsync(Q(unknown))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        string[] all;
        lock (logs.Messages) all = logs.Messages.ToArray();
        var entries = all.Where(m => m.Contains("Login lock cleared")).ToArray();
        entries.Should().HaveCount(2);
        entries.Should().OnlyContain(m => m.StartsWith("Information|"));
        entries.Should().OnlyContain(m => m.Contains(factory.AdminUserId.ToString()));
        entries.Should().ContainSingle(m => m.Contains(targetId.ToString()));
        entries.Should().ContainSingle(m => m.Contains("does not exist"));
        all.Should().NotContain(m => m.Contains(email, StringComparison.OrdinalIgnoreCase)
                                     || m.Contains(unknown, StringComparison.OrdinalIgnoreCase));
    }
}
