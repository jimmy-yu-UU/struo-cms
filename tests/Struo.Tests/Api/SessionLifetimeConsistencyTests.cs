using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Struo.Api.Auth;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// The cookie handler's own sliding-expiration window (<c>CookieAuthenticationOptions.ExpireTimeSpan</c>,
/// wired in <c>AuthWiring</c>) and <c>DistributedCacheTicketStore</c>'s idea of a session's lifetime
/// (the cache entry's sliding expiration, and what it stamps into a <c>UserSession</c> row's
/// <c>ExpiresAt</c>) MUST agree — <c>DistributedCacheTicketStore.IndexNewSessionAsync</c>'s login-time
/// sweep deletes a user's own index rows past their <c>ExpiresAt</c>, which is only safe if that really
/// is how long the cookie stays alive. Both now read the one constant, <see cref="AuthSchemes.SessionLifetime"/>.
/// </summary>
[Collection("ApiIntegration")]
public class SessionLifetimeConsistencyTests(ApiFactory factory)
{
    [Fact]
    public void Cookie_ExpireTimeSpan_equals_the_shared_session_lifetime_constant()
    {
        var monitor = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();

        monitor.Get(AuthSchemes.Cookie).ExpireTimeSpan.Should().Be(AuthSchemes.SessionLifetime);
    }
}
