using Struo.Application.Configuration;

namespace Struo.Tests.Support;

/// <summary>The shipped Localization defaults (en as default, zh-TW second), so every test that
/// seeds languages keeps the semantics the suite's <c>"en"</c> assertions rely on.</summary>
public static class TestLocalization
{
    public static LocalizationOptions Default => new()
    {
        Languages =
        [
            new LanguageSeedEntry { Code = "en", Name = "English" },
            new LanguageSeedEntry { Code = "zh-TW", Name = "繁體中文" },
        ],
        DefaultLanguage = "en",
    };
}
