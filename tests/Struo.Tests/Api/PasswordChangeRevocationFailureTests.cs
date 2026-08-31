using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Api.Auth;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// A revocation failure after a successful password change must be surfaced to the caller (a
/// distinguishable error code, not an indistinguishable 204), and the revocation call itself must not
/// inherit the request's own CancellationToken — the password write already committed, so a client
/// disconnecting right after must not also skip revoking that now-superseded set of sessions. Own
/// derived host (a throwing/spying <see cref="IUserSessionRevocationService"/>) so the shared ApiFactory
/// fixture's other ~55 test classes keep the real revocation behavior.
/// </summary>
[Collection("ApiIntegration")]
public class PasswordChangeRevocationFailureTests(ApiFactory factory)
{
    private sealed class ThrowingRevocationService : IUserSessionRevocationService
    {
        public Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default) =>
            throw new InvalidOperationException("session index unreachable");
    }

    /// <summary>Records the CancellationToken it was actually called with, without doing any real
    /// revocation work.</summary>
    private sealed class SpyRevocationService : IUserSessionRevocationService
    {
        public CancellationToken? ReceivedToken;
        public Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default)
        {
            ReceivedToken = ct;
            return Task.CompletedTask;
        }
    }

    private async Task<(string email, string password, Guid userId)> SeedUserOnBaseFactoryAsync()
    {
        var userId = Guid.CreateVersion7();
        const string password = "revocation-pw-123";
        var email = $"revocation-{userId:N}@struo.test";
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        await db.Insertable(new User
        {
            Id = userId, Email = email, Password = hasher.Hash(password), IsActive = true,
        }).ExecuteCommandAsync();
        return (email, password, userId);
    }

    private async Task<HttpClient> LoginOnAsync(WebApplicationFactory<Program> host, string email, string password)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(CsrfProtectionMiddleware.HeaderName, "1");
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        resp.EnsureSuccessStatusCode();
        return client;
    }

    [Fact]
    public async Task Revocation_failure_is_surfaced_as_a_distinct_error_but_the_password_is_still_changed()
    {
        var (email, password, userId) = await SeedUserOnBaseFactoryAsync();
        var host = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.AddScoped<IUserSessionRevocationService, ThrowingRevocationService>()));
        var client = await LoginOnAsync(host, email, password);

        var resp = await client.PutAsJsonAsync($"/api/users/{userId}/password",
            new { newPassword = "new-revocation-pw-456", currentPassword = password });

        resp.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("SESSION_REVOCATION_FAILED");
        // The real failure detail must not leak to the client.
        (await resp.Content.ReadAsStringAsync()).Should().NotContain("session index unreachable");

        // The password write itself is durable regardless of the response: log in fresh with the NEW
        // password to prove it, on a host with the real (working) revocation service.
        var freshClient = factory.CreateClient();
        var relogin = await freshClient.PostAsJsonAsync("/api/auth/login",
            new { email, password = "new-revocation-pw-456" });
        relogin.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Revocation_is_called_with_CancellationToken_None_not_the_requests_own_token()
    {
        var (email, password, userId) = await SeedUserOnBaseFactoryAsync();
        var spy = new SpyRevocationService();
        var host = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.AddSingleton<IUserSessionRevocationService>(spy)));
        var client = await LoginOnAsync(host, email, password);

        var resp = await client.PutAsJsonAsync($"/api/users/{userId}/password",
            new { newPassword = "new-revocation-pw-789", currentPassword = password });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        spy.ReceivedToken.Should().Be(CancellationToken.None,
            "the write already committed — a disconnecting client must not also skip revocation");
    }
}
