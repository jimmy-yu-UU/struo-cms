using AwesomeAssertions;
using Microsoft.Extensions.Primitives;
using Struo.Api.Auth;
using Xunit;

namespace Struo.Tests.Api;

public class SessionCookieHeadersTests
{
    private const string Name = "struo.session";

    [Fact]
    public void Only_the_session_cookie_and_its_chunks_are_removed()
    {
        StringValues headers = new[]
        {
            "struo.session=abc; path=/; httponly",
            "struo.session=chunks-2; path=/; httponly",
            "struo.sessionC1=part1; path=/; httponly",
            "struo.sessionC2=part2; path=/; httponly",
            "struo.sessionC12=part12; path=/",
            "struo.session=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/",
            "unrelated=1; path=/",
            "struo.session2=keep; path=/",
            "struo.sessionCx=keep; path=/",
            "xstruo.session=keep; path=/",
            "struo.sessionC=keep; path=/",
        };

        var kept = SessionCookieHeaders.Without(headers, Name);

        kept.ToArray().Should().Equal(
            "unrelated=1; path=/",
            "struo.session2=keep; path=/",
            "struo.sessionCx=keep; path=/",
            "xstruo.session=keep; path=/",
            "struo.sessionC=keep; path=/");
    }

    [Fact]
    public void A_host_prefixed_name_is_matched_exactly()
    {
        StringValues headers = new[] { "__Host-struo.session=abc; path=/; secure", "__Host-struo.sessionC1=p; path=/; secure", "struo.session=other" };

        SessionCookieHeaders.Without(headers, "__Host-struo.session").ToArray().Should().Equal("struo.session=other");
    }

    [Fact]
    public void A_name_that_needs_escaping_matches_its_escaped_form()
    {
        StringValues headers = new[] { "a%20b=1; path=/", "a%20bC1=2; path=/", "keep=3" };

        SessionCookieHeaders.Without(headers, "a b").ToArray().Should().Equal("keep=3");
    }

    [Fact]
    public void No_header_stays_no_header()
    {
        SessionCookieHeaders.Without(StringValues.Empty, Name).Count.Should().Be(0);
    }
}
