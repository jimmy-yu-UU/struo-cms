using AwesomeAssertions;
using Struo.Application.Security;
using Xunit;

namespace Struo.Tests.Identity;

public class AccessTokenHasherTests
{
    [Fact]
    public void Generate_returns_token_and_matching_hash()
    {
        var (token, hash) = AccessTokenHasher.Generate();
        token.Should().NotBeNullOrWhiteSpace();
        token.Length.Should().BeGreaterThan(32);            // 256-bit base64url
        AccessTokenHasher.Hash(token).Should().Be(hash);    // deterministic
    }

    [Fact]
    public void Different_tokens_each_call()
    {
        AccessTokenHasher.Generate().token.Should().NotBe(AccessTokenHasher.Generate().token);
    }
}
