using Microsoft.Extensions.Options;
using Struo.Domain.Localization;

namespace Struo.Application.Configuration;

public sealed class AdminUiOptionsValidator : IValidateOptions<AdminUiOptions>
{
    public ValidateOptionsResult Validate(string? name, AdminUiOptions options)
    {
        var failures = new List<string>();
        var locales = options.EffectiveLocales;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < locales.Count; i++)
        {
            if (!LocaleFormat.IsValid(locales[i]))
                failures.Add($"AdminUi:Locales:{i} '{locales[i]}' must match [A-Za-z0-9_-]{{1,35}}.");
            else if (!seen.Add(locales[i]))
                failures.Add($"AdminUi:Locales:{i} '{locales[i]}' is listed more than once.");
        }
        if (!locales.Any(l => string.Equals(l, options.DefaultLocale, StringComparison.OrdinalIgnoreCase)))
            failures.Add($"AdminUi:DefaultLocale '{options.DefaultLocale}' must be one of AdminUi:Locales.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
