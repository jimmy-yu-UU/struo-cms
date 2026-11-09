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
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

// POST /api/login-locks/query and DELETE /api/login-locks?email=: query and clear the per-account login lock. Every test runs on
// its own derived host (isolated in-memory throttle) with a small PermitLimit, seeding through the
// shared base factory, like LoginAccountThrottleTests.
[Collection("ApiIntegration")]
public class LoginLocksEndpointTests(ApiFactory factory)
{
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

    private static Task<HttpResponseMessage> QueryAsync(HttpClient client, params string[] emails) =>
        client.PostAsJsonAsync($"{Url}/query", new { emails });

    private static async Task<List<JsonElement>> EntriesAsync(HttpClient client, params string[] emails)
    {
        var resp = await QueryAsync(client, emails);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        return (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").EnumerateArray().ToList();
    }

    private static async Task<(bool locked, int retryAfter)> StateAsync(HttpClient client, string email)
    {
        var entry = (await EntriesAsync(client, email)).Single();
        return (entry.GetProperty("locked").GetBoolean(), entry.GetProperty("retryAfterSeconds").GetInt32());
    }

    private static async Task LockAsync(WebApplicationFactory<Program> host, string email, int failures = 3)
    {
        using var client = NewClient(host);
        for (var i = 0; i < failures; i++)
            (await LoginAsync(client, email, WrongPassword)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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
    public async Task A_caller_with_write_on_user_can_query_and_clear()
    {
        using var host = Host();
        var (victim, _, _) = await factory.SeedEditorAsync([], []);
        using var helper = await HelperAsync(host);
        await LockAsync(host, victim);

        (await StateAsync(helper, victim)).locked.Should().BeTrue();
        (await helper.DeleteAsync(Q(victim))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await StateAsync(helper, victim)).locked.Should().BeFalse();
    }

    [Fact]
    public async Task A_read_only_caller_can_query_but_not_clear()
    {
        using var host = Host();
        var (victim, _, _) = await factory.SeedEditorAsync([], []);
        var (email, password, _) = await factory.SeedEditorAsync(["user"], []);
        using var reader = await LoggedInAsync(host, email, password);
        using var admin = await AdminAsync(host);
        await LockAsync(host, victim);

        (await StateAsync(reader, victim)).locked.Should().BeTrue();
        (await reader.DeleteAsync(Q(victim))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await StateAsync(admin, victim)).locked.Should().BeTrue();
    }

    [Fact]
    public async Task A_caller_without_a_grant_on_user_is_403_on_both_verbs_and_the_lock_stays()
    {
        using var host = Host();
        var (victim, _, _) = await factory.SeedEditorAsync([], []);
        var (otherEmail, otherPassword, _) = await factory.SeedEditorAsync([], []);
        using var other = await LoggedInAsync(host, otherEmail, otherPassword);
        using var admin = await AdminAsync(host);
        await LockAsync(host, victim);

        (await QueryAsync(other, victim)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await other.DeleteAsync(Q(victim))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await StateAsync(admin, victim)).locked.Should().BeTrue();
    }

    [Fact]
    public async Task Anonymous_is_401()
    {
        using var host = Host();
        using var anonymous = NewClient(host);

        (await QueryAsync(anonymous, "a@struo.test")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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

        var knownEntry = (await EntriesAsync(admin, known)).Single();
        var unknownEntry = (await EntriesAsync(admin, unknown)).Single();
        unknownEntry.GetProperty("locked").GetBoolean().Should().Be(knownEntry.GetProperty("locked").GetBoolean());
        unknownEntry.GetProperty("retryAfterSeconds").GetInt32().Should().Be(knownEntry.GetProperty("retryAfterSeconds").GetInt32());
        unknownEntry.GetProperty("clearable").GetBoolean().Should().Be(knownEntry.GetProperty("clearable").GetBoolean());

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

        (await QueryAsync(admin, email)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.DeleteAsync($"{Url}?email={Uri.EscapeDataString(email)}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.DeleteAsync(Url)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_over_long_email_is_400()
    {
        using var host = Host();
        using var admin = await AdminAsync(host);
        var tooLong = new string('a', 256) + "@struo.test";

        (await QueryAsync(admin, tooLong)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
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

    // A second super-admin account (active or not), seeded through the base factory.
    private async Task<(string Email, string Password, Guid Id)> SeedSuperAdminAsync(bool active = true)
    {
        await factory.CreateAuthenticatedClientAsync(); // seeds the "admin" super-admin role
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var hasher = scope.ServiceProvider.GetRequiredService<Struo.Application.Security.IPasswordHasher>();
        var id = Guid.CreateVersion7();
        var email = $"root-{id:N}@struo.test";
        const string password = "root-pw-123";
        var role = await db.Queryable<Role>().Where(r => r.Name == "admin").FirstAsync();
        await db.Insertable(new User
        {
            Id = id, Email = email, Password = hasher.Hash(password), Name = "Root", IsActive = active,
        }).ExecuteCommandAsync();
        await db.Insertable(new UserRole { Id = Guid.CreateVersion7(), UserId = id, RoleId = role.Id })
            .ExecuteCommandAsync();
        return (email, password, id);
    }

    private async Task<HttpClient> HelperAsync(WebApplicationFactory<Program> host)
    {
        var (email, password, _) = await factory.SeedEditorAsync(["user"], ["user"]);
        return await LoggedInAsync(host, email, password);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_user_manager_cannot_clear_the_lock_on_a_super_admin_account(bool targetActive)
    {
        var logs = new CapturingProvider();
        using var host = Host(logs);
        var root = await SeedSuperAdminAsync(targetActive);
        using var helper = await HelperAsync(host);
        using var admin = await AdminAsync(host);
        await LockAsync(host, root.Email);

        var refused = await helper.DeleteAsync(Q(root.Email));

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await refused.Content.ReadAsStringAsync();
        body.Should().Contain("FORBIDDEN").And.Contain("super-admin");
        (await StateAsync(admin, root.Email)).locked.Should().BeTrue();

        string[] all;
        lock (logs.Messages) all = logs.Messages.ToArray();
        var refusals = all.Where(m => m.Contains("clear refused")).ToArray();
        refusals.Should().ContainSingle().Which.Should().Contain(root.Id.ToString());
        all.Should().NotContain(m => m.Contains(root.Email, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_super_admin_can_clear_the_lock_on_a_super_admin_account()
    {
        using var host = Host();
        var root = await SeedSuperAdminAsync();
        using var admin = await AdminAsync(host);
        await LockAsync(host, root.Email);

        (await admin.DeleteAsync(Q(root.Email))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await StateAsync(admin, root.Email)).locked.Should().BeFalse();
    }

    [Fact]
    public async Task A_user_manager_can_still_clear_a_normal_account_and_an_unknown_email()
    {
        using var host = Host();
        var (victim, _, _) = await factory.SeedEditorAsync([], []);
        var unknown = $"nobody-{Guid.NewGuid():N}@struo.test";
        using var helper = await HelperAsync(host);
        using var admin = await AdminAsync(host);
        await LockAsync(host, victim);

        (await helper.DeleteAsync(Q(victim))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await helper.DeleteAsync(Q(unknown))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await StateAsync(admin, victim)).locked.Should().BeFalse();
    }

    // ----------------------------------------------------------------- batch query

    [Fact]
    public async Task The_query_answers_in_request_order_with_one_entry_per_occurrence()
    {
        using var host = Host();
        var (locked, _, _) = await factory.SeedEditorAsync([], []);
        var (free, _, _) = await factory.SeedEditorAsync([], []);
        var unknown = $"nobody-{Guid.NewGuid():N}@struo.test";
        using var admin = await AdminAsync(host);
        await LockAsync(host, locked);

        var entries = await EntriesAsync(admin, free, locked, unknown, locked.ToUpperInvariant(), free);

        entries.Select(e => e.GetProperty("email").GetString()).Should()
            .Equal(free, locked, unknown, locked.ToUpperInvariant(), free);
        entries.Select(e => e.GetProperty("locked").GetBoolean()).Should().Equal(false, true, false, true, false);
        entries[1].GetProperty("retryAfterSeconds").GetInt32().Should().BeInRange(1, 300);
        entries[0].GetProperty("retryAfterSeconds").GetInt32().Should().Be(0);
        entries.Should().OnlyContain(e => e.GetProperty("clearable").GetBoolean());
    }

    [Fact]
    public async Task Clearable_is_false_for_a_super_admin_account_unless_the_caller_is_a_super_admin()
    {
        using var host = Host();
        var root = await SeedSuperAdminAsync();
        var (plain, _, _) = await factory.SeedEditorAsync([], []);
        using var helper = await HelperAsync(host);
        using var admin = await AdminAsync(host);

        var asManager = await EntriesAsync(helper, root.Email, plain);
        asManager.Select(e => e.GetProperty("clearable").GetBoolean()).Should().Equal(false, true);

        var asAdmin = await EntriesAsync(admin, root.Email, plain);
        asAdmin.Select(e => e.GetProperty("clearable").GetBoolean()).Should().Equal(true, true);
    }

    [Fact]
    public async Task A_query_looks_the_accounts_up_once_whatever_the_number_of_emails()
    {
        var counter = new DirectoryCalls();
        using var host = Host().WithWebHostBuilder(b => b.ConfigureServices(services =>
            services.AddScoped<ISuperAdminDirectory>(sp => new CountingDirectory(
                new SqlSugarSuperAdminDirectory(sp.GetRequiredService<ISqlSugarClient>()), counter))));
        using var helper = await HelperAsync(host);
        var emails = Enumerable.Range(0, 100).Select(i => $"batch-{i}-{Guid.NewGuid():N}@struo.test").ToArray();

        (await EntriesAsync(helper, emails)).Should().HaveCount(100);

        counter.EmailLookups.Should().Be(1);
    }

    private sealed class DirectoryCalls { public int EmailLookups; }

    private sealed class CountingDirectory(ISuperAdminDirectory inner, DirectoryCalls counter) : ISuperAdminDirectory
    {
        public Task<IReadOnlySet<Guid>> SuperAdminUserIdsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct = default) =>
            inner.SuperAdminUserIdsAsync(userIds, ct);

        public Task<IReadOnlySet<string>> SuperAdminEmailsAsync(IReadOnlyCollection<string> emails, CancellationToken ct = default)
        {
            Interlocked.Increment(ref counter.EmailLookups);
            return inner.SuperAdminEmailsAsync(emails, ct);
        }

        public Task<IReadOnlySet<Guid>> SuperAdminRoleIdsAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken ct = default) =>
            inner.SuperAdminRoleIdsAsync(roleIds, ct);

        public Task<IReadOnlySet<Guid>> PublicRoleIdsAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken ct = default) =>
            inner.PublicRoleIdsAsync(roleIds, ct);

        public bool IsPublicRoleName(string? name) => inner.IsPublicRoleName(name);

        public Task LockSuperAdminRolesAsync(CancellationToken ct = default) => inner.LockSuperAdminRolesAsync(ct);

        public Task<IReadOnlySet<Guid>> ActiveSuperAdminUserIdsAsync(CancellationToken ct = default) =>
            inner.ActiveSuperAdminUserIdsAsync(ct);
    }

    [Fact]
    public async Task A_query_for_a_hundred_and_one_emails_is_400()
    {
        using var host = Host();
        using var admin = await AdminAsync(host);
        var emails = Enumerable.Range(0, 101).Select(i => $"e{i}@struo.test").ToArray();

        var resp = await QueryAsync(admin, emails);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("BAD_USER_INPUT");
    }

    [Fact]
    public async Task An_empty_or_missing_email_list_is_400()
    {
        using var host = Host();
        using var admin = await AdminAsync(host);

        (await QueryAsync(admin)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync($"{Url}/query", new { })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await QueryAsync(admin, "ok@struo.test", "  ")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

}
