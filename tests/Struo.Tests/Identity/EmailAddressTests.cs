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
    public void IsUsable_caps_the_trimmed_length_at_the_email_column_length()
    {
        EmailAddress.MaxLength.Should().Be(255);
        EmailAddress.IsUsable(new string('a', 255)).Should().BeTrue();
        EmailAddress.IsUsable($" {new string('a', 255)} ").Should().BeTrue();
        EmailAddress.IsUsable(new string('a', 256)).Should().BeFalse();
    }

    [Fact]
    public void Normalize_trims_and_lowercases() =>
        EmailAddress.Normalize("  A@B.C ").Should().Be("a@b.c");

    [Theory]
    [InlineData("a@b.c", "a@b.c")]
    [InlineData("A@B.C", "  a@b.c ")]
    public void IsSameAccount_holds_for_spellings_that_normalize_to_the_same_string(string stored, string presented) =>
        EmailAddress.IsSameAccount(stored, presented).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsSameAccount_fails_for_a_missing_or_blank_stored_email(string? stored) =>
        EmailAddress.IsSameAccount(stored, "a@b.c").Should().BeFalse();

    [Fact]
    public void IsSameAccount_fails_for_a_collation_equivalent_variant()
    {
        EmailAddress.IsSameAccount("a@b.c", "a@b\u0301.c").Should().BeFalse();
        EmailAddress.IsSameAccount("jos\u00e9@b.c", "jose@b.c").Should().BeFalse();
    }
}
