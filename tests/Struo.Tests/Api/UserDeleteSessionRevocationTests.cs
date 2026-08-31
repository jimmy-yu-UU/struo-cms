using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using AwesomeAssertions;
using Struo.Api.Auth;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// Deleting a user through the generic item API (soft-delete: <c>User</c> is soft-deletable) must not
/// leave that user's cookie sessions live and permanently unrevokable. Soft-delete does not touch
/// <c>IsActive</c>, so the per-request liveness check in <c>AuthWiring</c> would not catch this on its
/// own — revocation has to run explicitly, the same way it does after a password change.
/// </summary>
[Collection("ApiIntegration")]
public class UserDeleteSessionRevocationTests(ApiFactory factory)
{
    private async Task<HttpClient> LoginAsAsync(string email, string password)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CsrfProtectionMiddleware.HeaderName, "1");
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        resp.EnsureSuccessStatusCode();
        return client;
    }

    private async Task<(string email, string password, Guid userId)> SeedUserAsync()
    {
        var userId = Guid.CreateVersion7();
        const string password = "delete-pw-123";
        var email = $"delete-{userId:N}@struo.test";
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        await db.Insertable(new User
        {
            Id = userId, Email = email, Password = hasher.Hash(password), IsActive = true,
        }).ExecuteCommandAsync();
        return (email, password, userId);
    }

    [Fact]
    public async Task Deleting_a_user_revokes_their_existing_sessions()
    {
        var (email, password, userId) = await SeedUserAsync();
        var browser = await LoginAsAsync(email, password);
        (await browser.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        var admin = await factory.CreateAuthenticatedClientAsync();
        var del = await admin.DeleteAsync($"/api/items/user/{userId}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await browser.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The 401 above is ALSO produced, for free, by the per-request liveness check: soft-delete stamps
    /// DeletedAt, and every Queryable&lt;User&gt; (including IUserCredentialStore.FindByIdAsync) already
    /// excludes soft-deleted rows via the global ISoftDeletable query filter — so the deleted user's
    /// credential lookup comes back null and OnValidatePrincipal rejects+signs out on that session's
    /// NEXT use regardless of this fix. What that reactive path does NOT do is clean up proactively: a
    /// session that is never presented again would leave its user_sessions row (and, bounded by the
    /// cache's own TTL, its cache entry) sitting there until the user's next login — which, for a
    /// deleted user, never happens. This test isolates that: no second request at all, so the ONLY way
    /// the index row can be gone is the explicit revocation call on the delete path itself.
    /// </summary>
    [Fact]
    public async Task Deleting_a_user_immediately_removes_their_index_rows_without_a_second_request()
    {
        var (email, password, userId) = await SeedUserAsync();
        await LoginAsAsync(email, password); // creates a user_sessions row; the client itself is unused below

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            (await db.Queryable<UserSession>().Where(s => s.UserId == userId).CountAsync())
                .Should().Be(1, "the login above must have indexed the session");
        }

        var admin = await factory.CreateAuthenticatedClientAsync();
        (await admin.DeleteAsync($"/api/items/user/{userId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            (await db.Queryable<UserSession>().Where(s => s.UserId == userId).CountAsync())
                .Should().Be(0, "the delete path must proactively revoke, not rely on the session ever being used again");
        }
    }

    [Fact]
    public async Task Deleting_a_user_does_not_affect_another_users_session()
    {
        var (emailA, passwordA, userIdA) = await SeedUserAsync();
        var (emailB, passwordB, _) = await SeedUserAsync();
        var browserB = await LoginAsAsync(emailB, passwordB);

        var admin = await factory.CreateAuthenticatedClientAsync();
        var del = await admin.DeleteAsync($"/api/items/user/{userIdA}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await browserB.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Deleting a collection other than `user` must not touch the revocation path at all — a
    /// regression guard against the name-gated check in ItemsController.Delete misfiring broadly.</summary>
    [Fact]
    public async Task Deleting_a_non_user_item_does_not_throw()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var created = await admin.PostAsJsonAsync("/api/items/role",
            new { name = $"role-{Guid.NewGuid():N}", description = "throwaway" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>())
            .GetProperty("data").GetProperty("id").GetString()!;

        var del = await admin.DeleteAsync($"/api/items/role/{id}");

        del.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
