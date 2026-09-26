using AwesomeAssertions;
using Struo.Application.Configuration;
using Xunit;

namespace Struo.Tests.Localization;

public sealed class LocalizationOptionsValidatorTests
{
    private static readonly LocalizationOptionsValidator Validator = new();

    private static LocalizationOptions Options(string defaultLanguage, params (string Code, string Name)[] languages) => new()
    {
        DefaultLanguage = defaultLanguage,
        Languages = languages.Select(l => new LanguageSeedEntry { Code = l.Code, Name = l.Name }).ToList(),
    };

    [Fact]
    public void Shipped_defaults_are_valid()
    {
        Validator.Validate(null, new LocalizationOptions()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Single_language_that_is_also_the_default_is_valid()
    {
        Validator.Validate(null, Options("ja", ("ja", "日本語"))).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Default_matches_case_insensitively()
    {
        Validator.Validate(null, Options("ZH-tw", ("zh-TW", "繁體中文"))).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Empty_list_means_the_shipped_default()
    {
        var options = Options("en"); // no languages bound
        options.EffectiveLanguages.Select(l => l.Code).Should().Equal("en", "zh-TW");
        Validator.Validate(null, options).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("en us")]
    [InlineData("")]
    [InlineData("a-very-long-code-that-exceeds-thirty-five-chars")]
    public void Malformed_code_fails(string code)
    {
        var r = Validator.Validate(null, Options("en", (code, "X"), ("en", "English")));
        r.Failed.Should().BeTrue();
        r.FailureMessage.Should().Contain("Code");
    }

    [Fact]
    public void Duplicate_codes_differing_only_in_case_fail()
    {
        var r = Validator.Validate(null, Options("en", ("en", "English"), ("EN", "English again")));
        r.Failed.Should().BeTrue();
        r.FailureMessage.Should().Contain("en");
    }

    [Fact]
    public void Blank_name_fails()
    {
        var r = Validator.Validate(null, Options("en", ("en", " ")));
        r.Failed.Should().BeTrue();
        r.FailureMessage.Should().Contain("Name");
    }

    [Fact]
    public void Default_outside_the_list_fails()
    {
        var r = Validator.Validate(null, Options("fr", ("en", "English")));
        r.Failed.Should().BeTrue();
        r.FailureMessage.Should().Contain("Localization:DefaultLanguage");
    }
}
