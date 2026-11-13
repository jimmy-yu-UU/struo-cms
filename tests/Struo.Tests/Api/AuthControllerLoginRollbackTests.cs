using System.Collections.Concurrent;
using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Struo.Api.Auth;
using Struo.Api.Controllers;
using Struo.Application.Security;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// The failure paths of <c>AuthController.Login</c> once its session is written, with fakes for every
/// collaborator: which exception reaches the caller, what is logged, and which cookies the response keeps.
/// </summary>
public class AuthControllerLoginRollbackTests
{
    private const string Email = "user@struo.test";
    private const string UnrelatedCookie = "unrelated=1; path=/";
    private static readonly string SessionCookie = SessionCookieName.Default + "=secret-cookie-value; path=/";

    private sealed class FakeAuth(Func<Task<bool>> confirm) : IAuthService
    {
        public Task<AuthResult> AuthenticateAsync(string email, string password, CancellationToken ct = default) =>
            Task.FromResult(AuthResult.Ok(Guid.CreateVersion7(), PasswordProof.From("enc:stored")));

        public Task<bool> ConfirmUnchangedAsync(AuthResult result, CancellationToken ct = default) => confirm();
    }

    private sealed class FakeThrottle : ILoginAttemptThrottle
    {
        public Task<LoginThrottleState> TryReserveAsync(string email, CancellationToken ct = default) =>
            Task.FromResult(new LoginThrottleState(false, 0));

        public Task ResetAsync(string email, CancellationToken ct = default) => Task.CompletedTask;

        public Task<LoginThrottleState> GetStateAsync(string email, CancellationToken ct = default) =>
            Task.FromResult(new LoginThrottleState(false, 0));
    }

    /// <summary>Queues the session cookie on sign-in, as the cookie handler does; sign-out can be made to throw.</summary>
    private sealed class FakeAuthentication(bool signOutThrows) : IAuthenticationService
    {
        public int SignOuts;

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal,
            AuthenticationProperties? properties)
        {
            context.Response.Headers.SetCookie = StringValues.Concat(
                StringValues.Concat(context.Response.Headers.SetCookie, SessionCookie), UnrelatedCookie);
            return Task.CompletedTask;
        }

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            SignOuts++;
            return signOutThrows ? throw new InvalidOperationException("discard failed") : Task.CompletedTask;
        }
    }

    private sealed class WarningLog : ILogger<AuthController>
    {
        public readonly ConcurrentBag<(string Message, Exception? Error)> Warnings = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Warnings.Add((formatter(state, exception), exception));
        }
    }

    private sealed class ThrowingRemoveCache : IDistributedCache
    {
        public byte[]? Get(string key) => null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult<byte[]?>(null);
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => Task.CompletedTask;
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) => throw new InvalidOperationException("session cache unavailable");
        public Task RemoveAsync(string key, CancellationToken token = default) =>
            throw new InvalidOperationException("session cache unavailable");
    }

    private sealed class FixedTicketFormat(AuthenticationTicket ticket) : ISecureDataFormat<AuthenticationTicket>
    {
        public string Protect(AuthenticationTicket data) => "protected";
        public string Protect(AuthenticationTicket data, string? purpose) => "protected";
        public AuthenticationTicket? Unprotect(string? protectedText) => ticket;
        public AuthenticationTicket? Unprotect(string? protectedText, string? purpose) => ticket;
    }

    private sealed class FixedOptionsMonitor(CookieAuthenticationOptions options) : IOptionsMonitor<CookieAuthenticationOptions>
    {
        public CookieAuthenticationOptions CurrentValue => options;
        public CookieAuthenticationOptions Get(string? name) => options;
        public IDisposable? OnChange(Action<CookieAuthenticationOptions, string?> listener) => null;
    }

    private static SessionRotation NewRotation(bool presentedSessionRemovalThrows)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("Microsoft.AspNetCore.Authentication.Cookies-SessionId", "auth-ticket:presented")], "test"));
        var options = new CookieAuthenticationOptions
        {
            TicketDataFormat = new FixedTicketFormat(new AuthenticationTicket(principal, AuthSchemes.Cookie)),
        };
        IDistributedCache cache = presentedSessionRemovalThrows ? new ThrowingRemoveCache() : new NoOpCache();
        var store = new DistributedCacheTicketStore(cache, null!, NullLogger<DistributedCacheTicketStore>.Instance);
        return new SessionRotation(new FixedOptionsMonitor(options), store);
    }

    private sealed class NoOpCache : IDistributedCache
    {
        public byte[]? Get(string key) => null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult<byte[]?>(null);
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => Task.CompletedTask;
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) { }
        public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
    }

    private sealed record Rig(AuthController Controller, DefaultHttpContext Http, FakeAuthentication Authentication, WarningLog Log);

    private static Rig NewRig(Func<Task<bool>> confirm, bool signOutThrows, bool presented = false,
        bool presentedSessionRemovalThrows = false)
    {
        var authentication = new FakeAuthentication(signOutThrows);
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton<IAuthenticationService>(authentication)
                .BuildServiceProvider(),
        };
        if (presented) SessionRotation.RecordPresentedCookie(http, "presented-cookie");
        var log = new WarningLog();
        var controller = new AuthController(
            new FakeAuth(confirm), new FakeThrottle(), NewRotation(presentedSessionRemovalThrows),
            SessionCookieName.Fixed(), log)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
        return new Rig(controller, http, authentication, log);
    }

    private static string[] SetCookies(Rig rig) => rig.Http.Response.Headers.SetCookie.OfType<string>().ToArray();

    private static void AssertNoSessionCookie(Rig rig)
    {
        SetCookies(rig).Should().Equal(UnrelatedCookie);
    }

    [Fact]
    public async Task A_confirmation_that_throws_surfaces_its_own_exception_when_the_discard_throws_too()
    {
        var rig = NewRig(() => throw new InvalidOperationException("confirmation unavailable"), signOutThrows: true);

        var act = () => rig.Controller.Login(new AuthController.LoginRequest(Email, "pw"), default);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("confirmation unavailable");
        rig.Authentication.SignOuts.Should().Be(1);
        rig.Log.Warnings.Should().ContainSingle(w => w.Error!.Message == "discard failed");
        AssertNoSessionCookie(rig);
    }

    [Fact]
    public async Task A_failed_confirmation_answers_401_when_the_discard_throws_and_sends_no_session_cookie()
    {
        var rig = NewRig(() => Task.FromResult(false), signOutThrows: true);

        var result = await rig.Controller.Login(new AuthController.LoginRequest(Email, "pw"), default);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        rig.Log.Warnings.Should().ContainSingle(w => w.Error!.Message == "discard failed");
        rig.Log.Warnings.Should().NotContain(w => w.Message.Contains("secret-cookie-value"));
        AssertNoSessionCookie(rig);
    }

    [Fact]
    public async Task A_failed_confirmation_answers_401_and_discards_once_when_the_discard_works()
    {
        var rig = NewRig(() => Task.FromResult(false), signOutThrows: false);

        var result = await rig.Controller.Login(new AuthController.LoginRequest(Email, "pw"), default);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        rig.Authentication.SignOuts.Should().Be(1);
        rig.Log.Warnings.Should().BeEmpty();
        AssertNoSessionCookie(rig);
    }

    [Fact]
    public async Task A_failure_while_revoking_the_presented_session_surfaces_and_discards_the_new_session()
    {
        var rig = NewRig(() => Task.FromResult(true), signOutThrows: false, presented: true,
            presentedSessionRemovalThrows: true);

        var act = () => rig.Controller.Login(new AuthController.LoginRequest(Email, "pw"), default);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("session cache unavailable");
        rig.Authentication.SignOuts.Should().Be(1, "the session this login wrote is discarded");
        AssertNoSessionCookie(rig);
    }

    [Fact]
    public async Task A_confirmed_login_keeps_its_cookies_and_discards_nothing()
    {
        var rig = NewRig(() => Task.FromResult(true), signOutThrows: false);

        var result = await rig.Controller.Login(new AuthController.LoginRequest(Email, "pw"), default);

        result.Should().BeOfType<OkObjectResult>();
        rig.Authentication.SignOuts.Should().Be(0);
        SetCookies(rig).Should().Equal(SessionCookie, UnrelatedCookie);
    }
}
