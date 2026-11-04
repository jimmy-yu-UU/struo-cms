using AwesomeAssertions;
using Struo.Application.Security;
using Xunit;

namespace Struo.Tests.Identity;

public class EmailAddressTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsUsable_rejects_a_blank_value(string? email) =>
        EmailAddress.IsUsable(email).Should().BeFalse();

    [Theory]
    [InlineData("  A@B.c  ")]
    [InlineData("no-at-sign")]
    [InlineData("a@b@c")]
    public void IsUsable_accepts_any_non_blank_value_whatever_its_shape(string email) =>
        EmailAddress.IsUsable(email).Should().BeTrue();

    [Fact]
    public void IsUsable_caps_the_trimmed_length_at_320()
    {
        EmailAddress.IsUsable(new string('a', 320)).Should().BeTrue();
        EmailAddress.IsUsable($" {new string('a', 320)} ").Should().BeTrue();
        EmailAddress.IsUsable(new string('a', 321)).Should().BeFalse();
    }

    [Fact]
    public void Normalize_trims_and_lowercases() =>
        EmailAddress.Normalize("  A@B.C ").Should().Be("a@b.c");
}
