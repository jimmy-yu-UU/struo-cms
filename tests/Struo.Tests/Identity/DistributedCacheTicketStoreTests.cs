using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using Struo.Api.Auth;
using Xunit;

namespace Struo.Tests.Identity;

public class DistributedCacheTicketStoreTests
{
    private static AuthenticationTicket Ticket()
    {
        var identity = new ClaimsIdentity("Cookies");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString()));
        return new AuthenticationTicket(new ClaimsPrincipal(identity), "Cookies");
    }

    private static IDistributedCache MemoryCache() =>
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    [Fact]
    public async Task Store_then_retrieve_round_trips_then_remove_clears()
    {
        var store = new DistributedCacheTicketStore(MemoryCache());
        var key = await store.StoreAsync(Ticket());

        var retrieved = await store.RetrieveAsync(key);
        retrieved.Should().NotBeNull();
        retrieved!.Principal.FindFirst(ClaimTypes.NameIdentifier).Should().NotBeNull();

        await store.RemoveAsync(key);
        (await store.RetrieveAsync(key)).Should().BeNull();
    }
}
