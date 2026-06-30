using AwesomeAssertions;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Xunit;

namespace Struo.Tests.Identity;

public class PasswordHasherTests
{
    private readonly IPasswordHasher _hasher = new Argon2idPasswordHasher();

    [Fact]
    public void Hash_is_not_the_plaintext_and_differs_each_call()
    {
        var a = _hasher.Hash("correct horse");
        var b = _hasher.Hash("correct horse");
        a.Should().NotBe("correct horse");
        a.Should().NotBe(b); // random salt per call
    }

    [Fact]
    public void Verify_true_for_correct_password_false_otherwise()
    {
        var encoded = _hasher.Hash("s3cret!");
        _hasher.Verify(encoded, "s3cret!").Should().BeTrue();
        _hasher.Verify(encoded, "wrong").Should().BeFalse();
    }
}
