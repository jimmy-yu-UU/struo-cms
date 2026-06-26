namespace Struo.Domain.Localization;

/// <summary>A configured language (mirrors an enabled row of the Language collection).</summary>
public sealed record LanguageInfo(string Code, string Name, bool IsDefault, bool Enabled, int Sort);
