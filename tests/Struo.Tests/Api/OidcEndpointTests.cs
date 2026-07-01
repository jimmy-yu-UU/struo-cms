using System.Net;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

[Collection("ApiIntegration")]
public class OidcEndpointTests(ApiFactory factory)
{
    [Fact]
    public async Task Login_oidc_returns_404_when_oidc_disabled()
    {
        var client = factory.CreateClient();
        var resp = await client.GetAsync("/api/auth/login/oidc?returnUrl=/admin");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
