using AwesomeAssertions;
using Struo.Application.Configuration;
using Xunit;

namespace Struo.Tests.Localization;

public sealed class AdminUiOptionsValidatorTests
{
    private static readonly AdminUiOptionsValidator Validator = new();

    private static AdminUiOptions Options(string @default, params string[] locales) =>
        new() { DefaultLocale = @default, Locales = [.. locales] };

    [Fact] public void Shipped_defaults_are_valid() => Validator.Validate(null, new AdminUiOptions()).Succeeded.Should().BeTrue();
    [Fact] public void Single_locale_is_valid() => Validator.Validate(null, Options("en", "en")).Succeeded.Should().BeTrue();

    [Fact]
    public void Default_outside_the_list_fails()
    {
        var r = Validator.Validate(null, Options("en", "zh-TW"));
        r.Failed.Should().BeTrue();
        r.FailureMessage.Should().Contain("AdminUi:DefaultLocale");
    }

    [Fact]
    public void Duplicate_locales_fail()
    {
        var r = Validator.Validate(null, Options("en", "en", "EN"));
        r.Failed.Should().BeTrue();
    }

    [Fact]
    public void Malformed_locale_fails()
    {
        var r = Validator.Validate(null, Options("zh TW", "zh TW"));
        r.Failed.Should().BeTrue();
        r.FailureMessage.Should().Contain("AdminUi:Locales");
    }
}
