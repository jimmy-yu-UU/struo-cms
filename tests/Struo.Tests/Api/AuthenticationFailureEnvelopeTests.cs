// tests/Struo.Tests/Api/AuthenticationFailureEnvelopeTests.cs
using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Struo.Application.Security;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// The unified response envelope must wrap EVERY REST response, including the failure modes that
/// originate inside the authentication middleware itself rather than in MVC — a Redis-backed ticket
/// store that is unreachable, or (as modeled here) the bearer handler's credential lookup failing.
/// <para>
/// This is a middleware-ORDER guard: <c>UseExceptionHandler</c> only catches exceptions thrown
/// DOWNSTREAM of where it sits in the pipeline. While it was registered after
/// <c>UseAuthentication</c>/<c>UseAuthorization</c>, an exception raised during authentication
/// escaped <see cref="Struo.Api.Http.StruoExceptionHandler"/> entirely and went out as a bare 500
/// with an empty body — no <c>success</c>/<c>error</c> shape at all, and no masked-and-logged
/// treatment of the internal detail.
/// </para>
/// Own derived host (throwing <see cref="IUserCredentialStore"/>) so the shared ApiFactory fixture's
/// ~55 other classes keep a working credential store.
/// </summary>
[Collection("ApiIntegration")]
public class AuthenticationFailureEnvelopeTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    /// <summary>Stands in for any infrastructure failure inside the bearer authentication handler.</summary>
    private sealed class ThrowingCredentialStore : IUserCredentialStore
    {
        public Task<UserCredential?> FindByEmailAsync(string email, CancellationToken ct = default) =>
            throw new InvalidOperationException("credential store unreachable");

        public Task<UserCredential?> FindByAccessTokenAsync(string tokenHash, CancellationToken ct = default) =>
            throw new InvalidOperationException("credential store unreachable");

        public Task TouchAccessTokenLastUsedAsync(Guid userId, DateTime nowUtc, CancellationToken ct = default) =>
            throw new InvalidOperationException("credential store unreachable");
    }

    private HttpClient CreateClientWithFailingAuthentication() =>
        _factory
            .WithWebHostBuilder(b => b.ConfigureServices(s =>
                // Last registration wins for the single-service resolve the bearer handler performs.
                s.AddScoped<IUserCredentialStore, ThrowingCredentialStore>()))
            .CreateClient();

    [Fact]
    public async Task Exception_during_authentication_is_wrapped_in_the_error_envelope()
    {
        var client = CreateClientWithFailingAuthentication();
        // The Adaptive policy scheme forwards to the Bearer handler on the strength of this header
        // alone, so the throwing store is reached inside UseAuthentication — before routing or MVC.
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/ping");
        request.Headers.Add("Authorization", "Bearer any-token-value");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotBeEmpty("an authentication-stage failure must still carry the error envelope");

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        var error = doc.RootElement.GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("INTERNAL_SERVER_ERROR");
        // Masked: the unmapped exception's own message never reaches the client.
        error.GetProperty("message").GetString().Should().Be("An internal error occurred.");
        body.Should().NotContain("credential store unreachable");
    }

    [Fact]
    public async Task Authentication_stage_failure_does_not_affect_requests_without_a_bearer_header()
    {
        // Same host: a cookie-less, header-less request never enters the bearer handler, so it must
        // still succeed. Guards against "fixing" the above by short-circuiting all authentication.
        var client = CreateClientWithFailingAuthentication();

        var response = await client.GetAsync("/api/ping");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
