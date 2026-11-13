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
    public void A_name_with_a_plus_is_matched_as_written()
    {
        StringValues headers = new[]
        {
            "struo+session=abc; path=/; httponly",
            "struo+sessionC1=part1; path=/; httponly",
            "struo+sessionC2=part2; path=/; httponly",
            "struo%2Bsession=keep; path=/",
            "struo session=keep; path=/",
            "unrelated=1; path=/",
        };

        SessionCookieHeaders.Without(headers, "struo+session").ToArray().Should().Equal(
            "struo%2Bsession=keep; path=/", "struo session=keep; path=/", "unrelated=1; path=/");
    }

    [Fact]
    public void Chunk_digits_are_ASCII_digits_only()
    {
        StringValues headers = new[] { "struo.sessionC1=a", "struo.sessionC٣=keep" };

        SessionCookieHeaders.Without(headers, Name).ToArray().Should().Equal("struo.sessionC٣=keep");
    }

    [Fact]
    public void No_header_stays_no_header()
    {
        SessionCookieHeaders.Without(StringValues.Empty, Name).Count.Should().Be(0);
    }
}
