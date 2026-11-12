using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Struo.Api.Auth;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Identity;

/// <summary>
/// Renewing a session runs from the cookie handler's response callback, where an exception cannot become an
/// error response. A cache that fails while renewing is logged and skipped; storing and retrieving a ticket
/// surface the failure, so a login that cannot be saved fails visibly.
/// </summary>
public class DistributedCacheTicketStoreRenewalFailureTests
{
    private const string Secret = "renewal-secret@example.test";

    private static AuthenticationTicket Ticket()
    {
        var identity = new ClaimsIdentity("Cookies");
        identity.AddClaim(new Claim(ClaimTypes.Email, Secret));
        return new AuthenticationTicket(new ClaimsPrincipal(identity), "Cookies");
    }

    private static (DistributedCacheTicketStore Store, ThrowingDistributedCache Cache, ListLogger<DistributedCacheTicketStore> Log)
        NewStore()
    {
        var cache = new ThrowingDistributedCache();
        var log = new ListLogger<DistributedCacheTicketStore>();
        var scopes = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        return (new DistributedCacheTicketStore(cache, scopes, log), cache, log);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task A_cache_failure_while_renewing_is_logged_and_does_not_escape(bool onGet, bool onSet)
    {
        var (store, cache, log) = NewStore();
        var key = await store.StoreAsync(Ticket());
        cache.Armed = true;
        cache.ThrowOnGetNumber = onGet ? 1 : 0;
        cache.ThrowOnSet = onSet;

        await store.RenewAsync(key, Ticket());

        var warning = log.Entries.Should().ContainSingle().Which;
        warning.Level.Should().Be(LogLevel.Warning);
        warning.Message.Should().Contain(key).And.NotContain(Secret);
    }

    [Fact]
    public async Task A_cancellation_raised_by_the_cache_while_renewing_is_treated_like_any_other_failure()
    {
        var (store, cache, log) = NewStore();
        var key = await store.StoreAsync(Ticket());
        cache.Armed = true;
        cache.ThrowOnGetNumber = 1;
        cache.Failure = () => new OperationCanceledException();

        await store.RenewAsync(key, Ticket());

        log.Entries.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Warning);
    }

    [Fact]
    public async Task Storing_and_retrieving_surface_a_cache_failure()
    {
        var (store, cache, _) = NewStore();
        var key = await store.StoreAsync(Ticket());
        cache.Armed = true;
        cache.ThrowOnGetNumber = 1;
        cache.ThrowOnSet = true;

        await FluentActions.Awaiting(() => store.StoreAsync(Ticket())).Should().ThrowAsync<TimeoutException>();
        await FluentActions.Awaiting(() => store.RetrieveAsync(key)).Should().ThrowAsync<TimeoutException>();
    }
}
