using Struo.Domain.Localization;

namespace Struo.Application.Localization;

public interface ILanguageProvider
{
    Task<IReadOnlyList<LanguageInfo>> GetEnabledAsync();
    Task<string> DefaultCodeAsync();
    Task<bool> IsEnabledAsync(string code);

    /// <summary>
    /// The configured spelling of <paramref name="code"/> when it names an enabled language
    /// (case-insensitive match), otherwise null.
    /// </summary>
    Task<string?> CanonicalizeAsync(string code);

    void Invalidate();
}
