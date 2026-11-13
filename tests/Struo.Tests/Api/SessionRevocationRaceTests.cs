using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Distributed;
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
/// "Revoke every session" against a login that is in flight. Each interleaving is forced with a
/// barrier at an exact point, never with a delay: the login paused after the password was verified, and
/// the revocation paused between listing the ticket keys and removing them.
/// </summary>
[Collection("ApiIntegration")]
public class SessionRevocationRaceTests(ApiFactory factory)
{
    private const string Pw = "old-password-123";
    private const string NewPw = "new-password-456";
    private const string SessionIdClaim = "Microsoft.AspNetCore.Authentication.Cookies-SessionId";
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);

    /// <summary>Holds the first caller for the armed user until released; everyone else passes.</summary>
    private sealed class Gate
    {
        private Guid? _armed;
        public readonly TaskCompletionSource Reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Arm(Guid userId) => _armed = userId;

        public async Task PassAsync(Guid userId)
        {
            if (_armed != userId) return;
            _armed = null;
            Reached.TrySetResult();
            await Release.Task.WaitAsync(Wait);
        }
    }

    private sealed class GatedAuth(IAuthService inner, Gate gate) : IAuthService
    {
        public async Task<AuthResult> AuthenticateAsync(string email, string password, CancellationToken ct = default)
        {
            var result = await inner.AuthenticateAsync(email, password, ct);
            if (result.Succeeded) await gate.PassAsync(result.UserId!.Value);
            return result;
        }

        public Task<bool> ConfirmUnchangedAsync(AuthResult result, CancellationToken ct = default) =>
            inner.ConfirmUnchangedAsync(result, ct);
    }

    /// <summary>Pauses a listing after it read the keys and records every key ever indexed.</summary>
    private sealed class GatedSessions(IUserSessionStore inner, Gate listGate, ConcurrentBag<string> recorded)
        : IUserSessionStore
    {
        public Task RecordAsync(Guid userId, string ticketKey, DateTime c, DateTime e, CancellationToken ct = default)
        {
            recorded.Add(ticketKey);
            return inner.RecordAsync(userId, ticketKey, c, e, ct);
        }

        public Task<bool> RenewAsync(string ticketKey, Guid userId, DateTime e, CancellationToken ct = default) =>
            inner.RenewAsync(ticketKey, userId, e, ct);

        public Task RemoveByTicketKeyAsync(string ticketKey, CancellationToken ct = default) =>
            inner.RemoveByTicketKeyAsync(ticketKey, ct);

        public Task RemoveExpiredForUserAsync(Guid userId, DateTime now, CancellationToken ct = default) =>
            inner.RemoveExpiredForUserAsync(userId, now, ct);

        public async Task<IReadOnlyList<string>> ListTicketKeysForUserAsync(Guid userId, CancellationToken ct = default)
        {
            var keys = await inner.ListTicketKeysForUserAsync(userId, ct);
            await listGate.PassAsync(userId);
            return keys;
        }

        public Task RemoveByTicketKeysAsync(IReadOnlyCollection<string> ticketKeys, CancellationToken ct = default) =>
            inner.RemoveByTicketKeysAsync(ticketKeys, ct);
    }

    /// <summary>Delegates to the real throttle and counts how often a login cleared it.</summary>
    private sealed class CountingThrottle(ILoginAttemptThrottle inner) : ILoginAttemptThrottle
    {
        public int Resets;

        public Task<LoginThrottleState> TryReserveAsync(string email, CancellationToken ct = default) =>
            inner.TryReserveAsync(email, ct);

        public Task ResetAsync(string email, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Resets);
            return inner.ResetAsync(email, ct);
        }

        public Task<LoginThrottleState> GetStateAsync(string email, CancellationToken ct = default) =>
            inner.GetStateAsync(email, ct);
    }

    private sealed record Rig(
        WebApplicationFactory<Program> Host, HttpClient Client, Gate AuthGate, Gate ListGate,
        ConcurrentBag<string> Recorded, CountingThrottle Throttle, string Email, Guid UserId) : IDisposable
    {
        public void Dispose()
        {
            Client.Dispose();
            Host.Dispose();
        }
    }

    private async Task<Rig> NewRigAsync()
    {
        var authGate = new Gate();
        var listGate = new Gate();
        var recorded = new ConcurrentBag<string>();
        CountingThrottle? throttle = null;
        var host = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<ILoginAttemptThrottle>(sp => throttle = new CountingThrottle(
                ActivatorUtilities.CreateInstance<DistributedCacheLoginAttemptThrottle>(sp)));
            s.AddScoped<IAuthService>(sp => new GatedAuth(ActivatorUtilities.CreateInstance<AuthService>(sp), authGate));
            s.AddScoped<IUserSessionStore>(sp =>
                new GatedSessions(ActivatorUtilities.CreateInstance<SqlSugarUserSessionStore>(sp), listGate, recorded));
        }));
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add(CsrfProtectionMiddleware.HeaderName, "1");

        var id = Guid.CreateVersion7();
        var email = $"race-{id:N}@struo.test";
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        await db.Insertable(new User { Id = id, Email = email, Password = hasher.Hash(Pw), IsActive = true })
            .ExecuteCommandAsync();
        host.Services.GetRequiredService<ILoginAttemptThrottle>();
        return new Rig(host, client, authGate, listGate, recorded, throttle!, email, id);
    }

    private static string? SessionCookieOf(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        var prefix = SessionCookieName.Default + "=";
        var line = values.LastOrDefault(v => v.StartsWith(prefix, StringComparison.Ordinal));
        var value = line?[prefix.Length..].Split(';')[0];
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static HttpRequestMessage Request(HttpMethod method, string url, string? cookie, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (cookie is not null) request.Headers.Add("Cookie", $"{SessionCookieName.Default}={cookie}");
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private static Task<HttpResponseMessage> LoginAsync(Rig rig, string password) =>
        rig.Client.SendAsync(Request(HttpMethod.Post, "/api/auth/login", null,
            new { email = rig.Email, password }));

    private static async Task<HttpStatusCode> MeAsync(Rig rig, string? cookie) =>
        (await rig.Client.SendAsync(Request(HttpMethod.Get, "/api/auth/me", cookie))).StatusCode;

    private static Task<HttpResponseMessage> ChangePasswordAsync(Rig rig, string ownerCookie) =>
        rig.Client.SendAsync(Request(HttpMethod.Put, $"/api/users/{rig.UserId}/password", ownerCookie,
            new { newPassword = NewPw, currentPassword = Pw }));

    private static async Task<List<string>> IndexedKeysAsync(Rig rig)
    {
        using var scope = rig.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        return await db.Queryable<UserSession>().Where(s => s.UserId == rig.UserId)
            .Select(s => s.TicketKey).ToListAsync();
    }

    private static string TicketKeyOf(Rig rig, string cookie)
    {
        var options = rig.Host.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(AuthSchemes.Cookie);
        return options.TicketDataFormat.Unprotect(cookie)!.Principal.FindFirst(SessionIdClaim)!.Value;
    }

    private static async Task<List<string>> LiveTicketKeysAsync(Rig rig)
    {
        var cache = rig.Host.Services.GetRequiredService<IDistributedCache>();
        var live = new List<string>();
        foreach (var key in rig.Recorded.Distinct())
            if (await cache.GetAsync(key) is not null) live.Add(key);
        return live;
    }

    private static async Task RevokeAllAsync(Rig rig)
    {
        using var scope = rig.Host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IUserSessionRevocationService>()
            .RevokeAllForUserAsync(rig.UserId);
    }

    [Fact]
    public async Task A_login_paused_after_verification_fails_when_a_password_change_and_revoke_all_finish_first()
    {
        using var rig = await NewRigAsync();
        var ownerCookie = SessionCookieOf(await LoginAsync(rig, Pw))!;

        var resetsBefore = rig.Throttle.Resets;
        rig.AuthGate.Arm(rig.UserId);
        var pausedLogin = LoginAsync(rig, Pw);
        await rig.AuthGate.Reached.Task.WaitAsync(Wait);

        (await ChangePasswordAsync(rig, ownerCookie)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        rig.AuthGate.Release.SetResult();
        var response = await pausedLogin;

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        SessionCookieOf(response).Should().BeNull("a failed login hands out no session cookie");
        response.Headers.Contains("Set-Cookie").Should().BeFalse();
        (await LiveTicketKeysAsync(rig)).Should().BeEmpty("no ticket survives, the first session included");
        (await IndexedKeysAsync(rig)).Should().BeEmpty();
        rig.Throttle.Resets.Should().Be(resetsBefore, "a failed login keeps its attempt counted");
        (await LoginAsync(rig, NewPw)).StatusCode.Should().Be(HttpStatusCode.OK, "the new password still logs in");
    }

    [Fact]
    public async Task A_login_paused_after_verification_fails_when_the_revocation_lists_keys_before_the_session_exists()
    {
        using var rig = await NewRigAsync();
        var ownerCookie = SessionCookieOf(await LoginAsync(rig, Pw))!;

        rig.AuthGate.Arm(rig.UserId);
        var pausedLogin = LoginAsync(rig, Pw);
        await rig.AuthGate.Reached.Task.WaitAsync(Wait);

        rig.ListGate.Arm(rig.UserId);
        var passwordChange = ChangePasswordAsync(rig, ownerCookie);
        await rig.ListGate.Reached.Task.WaitAsync(Wait);

        rig.AuthGate.Release.SetResult();
        var response = await pausedLogin;
        rig.ListGate.Release.SetResult();
        (await passwordChange).StatusCode.Should().Be(HttpStatusCode.NoContent);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        SessionCookieOf(response).Should().BeNull();
        (await LiveTicketKeysAsync(rig)).Should().BeEmpty();
        (await IndexedKeysAsync(rig)).Should().BeEmpty();
    }

    [Fact]
    public async Task B_a_session_indexed_after_the_listing_keeps_its_row_and_a_second_revoke_all_ends_it()
    {
        using var rig = await NewRigAsync();
        var firstCookie = SessionCookieOf(await LoginAsync(rig, Pw))!;

        rig.ListGate.Arm(rig.UserId);
        var revoke = RevokeAllAsync(rig);
        await rig.ListGate.Reached.Task.WaitAsync(Wait);

        var lateLogin = await LoginAsync(rig, Pw);
        lateLogin.StatusCode.Should().Be(HttpStatusCode.OK);
        var lateCookie = SessionCookieOf(lateLogin)!;
        var lateKey = TicketKeyOf(rig, lateCookie);

        rig.ListGate.Release.SetResult();
        await revoke;

        (await MeAsync(rig, firstCookie)).Should().Be(HttpStatusCode.Unauthorized);
        (await IndexedKeysAsync(rig)).Should().Equal(lateKey);
        (await MeAsync(rig, lateCookie)).Should().Be(HttpStatusCode.OK);

        await RevokeAllAsync(rig);

        (await MeAsync(rig, lateCookie)).Should().Be(HttpStatusCode.Unauthorized);
        (await IndexedKeysAsync(rig)).Should().BeEmpty();
    }

    [Fact]
    public async Task Normal_login_keeps_its_session_and_changing_the_password_revokes_existing_sessions()
    {
        using var rig = await NewRigAsync();
        var first = SessionCookieOf(await LoginAsync(rig, Pw))!;
        var second = SessionCookieOf(await LoginAsync(rig, Pw))!;
        (await MeAsync(rig, first)).Should().Be(HttpStatusCode.OK);
        (await MeAsync(rig, second)).Should().Be(HttpStatusCode.OK);
        (await IndexedKeysAsync(rig)).Should().HaveCount(2);

        (await ChangePasswordAsync(rig, first)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await MeAsync(rig, first)).Should().Be(HttpStatusCode.Unauthorized);
        (await MeAsync(rig, second)).Should().Be(HttpStatusCode.Unauthorized);
        (await IndexedKeysAsync(rig)).Should().BeEmpty();
    }
}
