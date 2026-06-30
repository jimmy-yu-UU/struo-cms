using System.Net;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Identity;

[Collection("ApiIntegration")]
public class AuthHelperTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    [Fact]
    public async Task CreateAuthenticatedClientAsync_yields_a_client_that_passes_me()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var me = await client.GetAsync("/api/auth/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.AdminUserId.Should().NotBe(System.Guid.Empty);
    }
}
