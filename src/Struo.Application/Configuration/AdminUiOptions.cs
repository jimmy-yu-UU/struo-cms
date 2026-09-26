namespace Struo.Application.Configuration;

/// <summary>
/// Which of the admin SPA's bundled message catalogs are offered, and which one is the default. Read
/// at startup and published anonymously through <c>GET /api/config</c>; independent of content
/// languages (<see cref="LocalizationOptions"/>). Whether a code names a catalog the SPA actually
/// ships is decided on the SPA side, which is the only place that knows its catalogs.
/// </summary>
public sealed class AdminUiOptions
{
    public const string SectionName = "AdminUi";

    /// <summary>Binder appends list items, so the shipped default lives in <see cref="EffectiveLocales"/>.</summary>
    public List<string> Locales { get; set; } = [];

    public string DefaultLocale { get; set; } = "zh-TW";

    public static readonly IReadOnlyList<string> ShippedDefault = ["zh-TW", "en"];

    public IReadOnlyList<string> EffectiveLocales => Locales.Count == 0 ? ShippedDefault : Locales;
}
