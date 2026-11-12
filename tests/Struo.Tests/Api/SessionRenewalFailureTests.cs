using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Struo.Api.Auth;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// When the cookie handler renews a session past half its sliding window and the cache fails during the
/// renewal, the request that triggered it still gets its normal response.
/// </summary>
[Collection("ApiIntegration")]
public class SessionRenewalFailureTests(ApiFactory factory)
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task A_cache_failure_during_renewal_does_not_change_the_response(bool onGet, bool onSet)
    {
        var (email, password, _) = await factory.SeedEditorAsync([], []);
        var cache = new ThrowingDistributedCache();
        var clock = new FakeTimeProvider();
        var logs = new ListLogger<DistributedCacheTicketStore>();
        using var host = factory.WithWebHostBuilder(b =>
        {
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton<ILogger<DistributedCacheTicketStore>>(logs);
                s.AddSingleton<IDistributedCache>(cache);
                s.PostConfigure<CookieAuthenticationOptions>(AuthSchemes.Cookie, o => o.TimeProvider = clock);
            });
        });
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        var prefix = SessionCookieName.Default + "=";
        var cookie = login.Headers.GetValues("Set-Cookie")
            .Last(v => v.StartsWith(prefix, StringComparison.Ordinal))[prefix.Length..].Split(';')[0];

        clock.Advance(TimeSpan.FromHours(5));
        // The cookie handler reads the ticket first, then the renewal checks it is still there: the second read fails.
        cache.ThrowOnGetNumber = onGet ? 2 : 0;
        cache.ThrowOnSet = onSet;
        cache.Armed = true;
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add("Cookie", $"{SessionCookieName.Default}={cookie}");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        logs.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("renew"));
    }
}
