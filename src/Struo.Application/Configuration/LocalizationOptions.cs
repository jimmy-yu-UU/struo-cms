namespace Struo.Application.Configuration;

public sealed class LanguageSeedEntry
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>
/// Seed source for the <c>Language</c> table, read by <c>LanguageSeeder</c> only when that table is
/// created during a startup. Afterwards the table is authoritative and is edited in the admin UI
/// (System &gt; Language); this section is not consulted again. Bound from <c>Localization</c>.
/// An empty or absent <c>Languages</c> list means the shipped default.
/// </summary>
public sealed class LocalizationOptions
{
    public const string SectionName = "Localization";

    /// <summary>The configuration binder appends list items to whatever the property already holds,
    /// so the shipped default lives in <see cref="EffectiveLanguages"/>, not in this initializer.</summary>
    public List<LanguageSeedEntry> Languages { get; set; } = [];

    public string DefaultLanguage { get; set; } = "en";

    public static readonly IReadOnlyList<LanguageSeedEntry> ShippedDefault =
    [
        new() { Code = "en", Name = "English" },
        new() { Code = "zh-TW", Name = "繁體中文" },
    ];

    /// <summary>The configured list, or the shipped default when the section binds no languages.</summary>
    public IReadOnlyList<LanguageSeedEntry> EffectiveLanguages => Languages.Count == 0 ? ShippedDefault : Languages;
}
