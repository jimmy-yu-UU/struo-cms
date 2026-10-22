using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Struo.Application.Security;

/// <summary>Startup validation of <see cref="OidcOptions"/>. A disabled provider is never checked, so a
/// placeholder entry can stay in the file. Messages name the offending <c>Oidc:Providers:{id}</c> key.</summary>
public sealed partial class OidcOptionsValidator : IValidateOptions<OidcOptions>
{
    [GeneratedRegex(OidcOptions.ProviderIdPattern)]
    private static partial Regex ProviderIdRegex();

    public ValidateOptionsResult Validate(string? name, OidcOptions options)
    {
        var failures = new List<string>();
        foreach (var (id, provider) in options.Providers)
        {
            if (!ProviderIdRegex().IsMatch(id))
                failures.Add($"Oidc:Providers:'{id}' is not a valid provider id; use lowercase letters, digits and hyphens");
            if (provider.Enabled) ValidateEnabled(id, provider, failures);
        }
        ValidateUniqueCallbackPaths(options, failures);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateEnabled(string id, OidcProviderOptions p, List<string> failures)
    {
        var key = $"Oidc:Providers:{id}";
        var missing = new[]
            {
                (Key: "DisplayName", Value: p.DisplayName), (Key: "Authority", Value: p.Authority),
                (Key: "ClientId", Value: p.ClientId), (Key: "ClientSecret", Value: p.ClientSecret)
            }
            .Where(f => string.IsNullOrWhiteSpace(f.Value))
            .Select(f => f.Key)
            .ToList();
        if (missing.Count > 0)
            failures.Add($"{key} is enabled and requires {string.Join(", ", missing)}");

        if (!string.IsNullOrWhiteSpace(p.CallbackPath) && !p.CallbackPath.StartsWith('/'))
            failures.Add($"{key}:CallbackPath must start with '/'");

        if (p.IsUnpinnedMultiTenant())
            failures.Add($"{key}:Authority is multi-tenant (common, organizations or consumers); " +
                         $"set {key}:AllowedTenantId or {key}:AllowedEmailDomains, or use a single-tenant Authority");

        if (p.IsUnpinnedGoogle())
            failures.Add($"{key}:Authority is Google, where any Google account can sign in and get a new account; " +
                         $"set {key}:AllowedEmailDomains");

        if (!p.HasValidPrompt())
            failures.Add($"{key}:Prompt must be empty or space-separated values from: " +
                         string.Join(", ", OidcProviderOptions.AllowedPromptValues));
    }

    // The callback, signed-out callback and remote sign-out path of every enabled provider are all served
    // by the one handler that owns the path, so no two may coincide.
    private static void ValidateUniqueCallbackPaths(OidcOptions options, List<string> failures)
    {
        var groups = options.EnabledProviders()
            .SelectMany(p => new[]
            {
                (p.Id, Path: p.Options.EffectiveCallbackPath(p.Id)),
                (p.Id, Path: OidcProviderOptions.SignedOutCallbackPathFor(p.Id)),
                (p.Id, Path: OidcProviderOptions.RemoteSignOutPathFor(p.Id)),
            })
            .GroupBy(x => x.Path.TrimEnd('/'), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);
        foreach (var g in groups)
            failures.Add($"Oidc providers {string.Join(", ", g.Select(x => x.Id).Distinct())} share the path " +
                         $"'{g.Key}'; give each its own Oidc:Providers:<id>:CallbackPath");
    }
}
