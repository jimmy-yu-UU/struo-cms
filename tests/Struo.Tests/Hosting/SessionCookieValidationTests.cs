// tests/Struo.Tests/Hosting/SessionCookieValidationTests.cs
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Hosting;

/// <summary>
/// The session cookie name must be a valid cookie token, and a <c>__Host-</c> or <c>__Secure-</c> name
/// only starts on a cookie the host really sends as Secure (<c>__Host-</c> also needs Path=/ and no
/// Domain). The check runs against the effective cookie options, so it fails startup with a message naming
/// <c>Auth:Cookie:Name</c>.
/// </summary>
[Collection("ApiIntegration")]
public class SessionCookieValidationTests
{
    private static async Task<string?> FailureAsync(string name, string? requireSecure)
    {
        var error = await ForkHost.StartupErrorAsync(mutate: s =>
        {
            s["Auth:Cookie:Name"] = name;
            if (requireSecure is not null) s["Auth:Cookie:RequireSecure"] = requireSecure;
        });
        if (error is null) return null;
        var ove = ExceptionChainSearch.FindInner<OptionsValidationException>(error);
        ove.Should().NotBeNull(error.ToString());
        return string.Join(" ", ove!.Failures);
    }

    [SqliteOnlyTheory("starts a full host on a SQLite temp database")]
    [InlineData("bad name")]
    [InlineData("bad;name")]
    [InlineData("bad=name")]
    [InlineData("naïve")]
    [InlineData("acme\n")]
    public async Task A_name_that_is_not_a_cookie_token_fails_startup(string name)
    {
        var failure = await FailureAsync(name, "true");

        failure.Should().NotBeNull().And.Contain("Auth:Cookie:Name");
    }

    [SqliteOnlyTheory("starts a full host on a SQLite temp database")]
    [InlineData("__Host-acme")]
    [InlineData("__Secure-acme")]
    public async Task A_prefixed_name_without_a_secure_cookie_fails_startup(string name)
    {
        // The test host runs as Development, where an unset RequireSecure means SameAsRequest.
        var failure = await FailureAsync(name, requireSecure: null);

        failure.Should().NotBeNull().And.Contain("Auth:Cookie:Name").And.Contain("Secure");
    }

    [SqliteOnlyTheory("starts a full host on a SQLite temp database")]
    [InlineData("__Host-acme")]
    [InlineData("__Secure-acme")]
    [InlineData("acme.session")]
    public async Task A_valid_name_starts(string name)
    {
        var failure = await FailureAsync(name, "true");

        failure.Should().BeNull();
    }

    [SqliteOnlyTheory("starts a full host on a SQLite temp database")]
    [InlineData("Domain")]
    [InlineData("Path")]
    public async Task A_host_prefixed_name_on_a_cookie_with_a_domain_or_another_path_fails_startup(string changed)
    {
        var error = await ForkHost.StartupErrorAsync(
            mutate: s =>
            {
                s["Auth:Cookie:Name"] = "__Host-acme";
                s["Auth:Cookie:RequireSecure"] = "true";
            },
            afterAddStruoCms: services => services.PostConfigure<CookieAuthenticationOptions>(
                Struo.Api.Auth.AuthSchemes.Cookie, o =>
                {
                    if (changed == "Domain") o.Cookie.Domain = "example.test";
                    else o.Cookie.Path = "/admin";
                }));

        var ove = ExceptionChainSearch.FindInner<OptionsValidationException>(error);
        ove.Should().NotBeNull(error?.ToString());
        string.Join(" ", ove!.Failures).Should().Contain("Auth:Cookie:Name").And.Contain("Path=/");
    }

    [SqliteOnlyFact("starts a full host on a SQLite temp database")]
    public async Task A_host_prefixed_name_pins_the_cookie_path_to_the_root()
    {
        await using var host = await ForkHost.StartAsync(mutate: s =>
        {
            s["Auth:Cookie:Name"] = "__Host-acme";
            s["Auth:Cookie:RequireSecure"] = "true";
        });

        host.App.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(Struo.Api.Auth.AuthSchemes.Cookie).Cookie.Path.Should().Be("/");
    }
}
