using Microsoft.Extensions.Options;
using Struo.Domain.Localization;

namespace Struo.Application.Configuration;

public sealed class LocalizationOptionsValidator : IValidateOptions<LocalizationOptions>
{
    public ValidateOptionsResult Validate(string? name, LocalizationOptions options)
    {
        var failures = new List<string>();
        var languages = options.EffectiveLanguages;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < languages.Count; i++)
        {
            var entry = languages[i];
            if (!LocaleFormat.IsValid(entry.Code))
                failures.Add($"Localization:Languages:{i}:Code '{entry.Code}' must match [A-Za-z0-9_-]{{1,35}}.");
            else if (!seen.Add(entry.Code))
                failures.Add($"Localization:Languages:{i}:Code '{entry.Code}' is listed more than once (codes compare case-insensitively).");
            if (string.IsNullOrWhiteSpace(entry.Name))
                failures.Add($"Localization:Languages:{i}:Name must not be blank.");
        }

        if (!languages.Any(l => string.Equals(l.Code, options.DefaultLanguage, StringComparison.OrdinalIgnoreCase)))
            failures.Add($"Localization:DefaultLanguage '{options.DefaultLanguage}' must be one of Localization:Languages.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
