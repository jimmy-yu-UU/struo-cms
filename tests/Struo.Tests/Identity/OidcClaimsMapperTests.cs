using System.Security.Claims;
using AwesomeAssertions;
using Struo.Api.Auth;
using Xunit;

namespace Struo.Tests.Identity;

public class OidcClaimsMapperTests
{
    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value))));

    [Fact]
    public void Uses_email_claim_when_present()
    {
        var id = OidcClaimsMapper.Map(Principal(("email", "alice@corp.com"), ("name", "Alice")));
        id.Email.Should().Be("alice@corp.com");
        id.Name.Should().Be("Alice");
    }

    [Fact]
    public void Falls_back_to_preferred_username_when_email_absent()
    {
        var id = OidcClaimsMapper.Map(Principal(("preferred_username", "bob@corp.com")));
        id.Email.Should().Be("bob@corp.com");
    }

    [Fact]
    public void Email_verified_boolean_true_normalizes_to_true()
    {
        var id = OidcClaimsMapper.Map(Principal(("email", "a@b.com"), ("email_verified", "true")));
        id.EmailVerified.Should().BeTrue();
    }

    [Fact]
    public void Email_verified_absent_is_null()
    {
        var id = OidcClaimsMapper.Map(Principal(("email", "a@b.com")));
        id.EmailVerified.Should().BeNull();
    }

    [Fact]
    public void Reads_tenant_id_from_tid()
    {
        var id = OidcClaimsMapper.Map(Principal(("email", "a@b.com"), ("tid", "tenant-9")));
        id.TenantId.Should().Be("tenant-9");
    }
}
