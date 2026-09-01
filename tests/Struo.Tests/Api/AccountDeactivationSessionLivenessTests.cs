using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using AwesomeAssertions;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// The cookie scheme must reject the next request from a deactivated account's existing session — and
/// actually remove its ticket from the store, not merely refuse it (a per-request liveness check as the
/// guarantee, plus the ticket dying for real via SignOutAsync -> ITicketStore.RemoveAsync). Deactivation
/// here goes through a direct DB write, deliberately NOT the generic item-update endpoint, to prove the
/// guard works regardless of how IsActive was flipped — a per-request check does not depend on
/// deactivation always going through one specific write path.
/// </summary>
[Collection("ApiIntegration")]
public class AccountDeactivationSessionLivenessTests(ApiFactory factory)
{
    private async Task<(HttpClient client, Guid userId, string email, string password)> SeedAndLoginAsync()
    {
        var userId = Guid.CreateVersion7();
        const string password = "liveness-pw-123";
        var email = $"liveness-{userId:N}@struo.test";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            await db.Insertable(new User
            {
                Id = userId, Email = email, Password = hasher.Hash(password), IsActive = true,
            }).ExecuteCommandAsync();
        }
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        return (client, userId, email, password);
    }

    private async Task DeactivateAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        await db.Updateable<User>().SetColumns(u => u.IsActive == false).Where(u => u.Id == userId).ExecuteCommandAsync();
    }

    [Fact]
    public async Task Deactivating_the_account_rejects_the_next_request_on_its_existing_session()
    {
        var (client, userId, _, _) = await SeedAndLoginAsync();
        (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        await DeactivateAsync(userId);

        (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Deactivating_the_account_actually_removes_the_ticket_from_the_store_not_merely_rejects_it()
    {
        var (client, userId, _, _) = await SeedAndLoginAsync();

        string ticketKey;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            var row = await db.Queryable<UserSession>().Where(s => s.UserId == userId).FirstAsync();
            row.Should().NotBeNull("logging in must have indexed the new session");
            ticketKey = row!.TicketKey;
        }

        await DeactivateAsync(userId);
        (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using (var scope = factory.Services.CreateScope())
        {
            var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
            (await cache.GetAsync(ticketKey)).Should().BeNull("the rejected ticket must be gone from the store, not just refused");
        }
    }

    /// <summary>Control: an active account's existing session must keep working normally — this guards
    /// against the liveness check over-rejecting (e.g. treating "row not found yet" as inactive).</summary>
    [Fact]
    public async Task An_active_accounts_existing_session_keeps_working()
    {
        var (client, _, _, _) = await SeedAndLoginAsync();

        (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
