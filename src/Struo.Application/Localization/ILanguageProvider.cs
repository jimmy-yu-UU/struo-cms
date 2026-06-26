using Struo.Domain.Localization;

namespace Struo.Application.Localization;

public interface ILanguageProvider
{
    IReadOnlyList<LanguageInfo> Enabled();
    string DefaultCode();
    bool IsEnabled(string code);
    void Invalidate();
}
