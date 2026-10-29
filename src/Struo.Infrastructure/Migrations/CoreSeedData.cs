using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Struo.Application.Configuration;
using Struo.Application.Security;

namespace Struo.Infrastructure.Migrations;

/// <summary>One language row of the core seed.</summary>
public sealed record SeedLanguage(string Code, string Name, bool IsDefault, int Sort);

/// <summary>
/// Inputs of the core seed migration: languages, the optional bootstrap admin (email and password hash
/// are both set or both null; an email without a password is refused) and the collections the <c>public</c> role may read.
/// </summary>
public sealed record CoreSeedData(
    IReadOnlyList<SeedLanguage> Languages, string? AdminEmail, string? AdminPasswordHash,
    IReadOnlyList<string> PublicReadCollections)
{
    /// <summary>The placeholder that stands in for the hidden admin credential in previewed SQL.</summary>
    public const string RedactionPlaceholder = "<redacted>";

    private const string RefusedBootstrapValue = "admin";

    public bool HasAdmin => AdminEmail is not null && AdminPasswordHash is not null;

    /// <summary>A copy that hides the admin password hash; unchanged when there is no admin.</summary>
    public CoreSeedData Redacted() =>
        HasAdmin ? this with { AdminPasswordHash = RedactionPlaceholder } : this;

    public static CoreSeedData From(
        LocalizationOptions localization, IPasswordHasher hasher,
        string? email, string? password, IReadOnlyList<string> publicRead,
        PasswordPolicyOptions? policy = null)
    {
        var languages = localization.EffectiveLanguages
            .Select((entry, index) => new SeedLanguage(
                entry.Code, entry.Name,
                string.Equals(entry.Code, localization.DefaultLanguage, StringComparison.OrdinalIgnoreCase),
                index + 1))
            .ToList();
        var effectivePolicy = policy ?? new PasswordPolicyOptions();
        var hasAdmin = !string.IsNullOrWhiteSpace(email);
        if (hasAdmin) RefuseMissingOrWeakPassword(password, effectivePolicy);
        return new CoreSeedData(
            languages,
            hasAdmin ? EmailAddress.Normalize(email!) : null,
            hasAdmin ? hasher.Hash(password!) : null,
            publicRead.Distinct(StringComparer.Ordinal).ToList());
    }

    private static void RefuseMissingOrWeakPassword(string? password, PasswordPolicyOptions policy)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new BootstrapPasswordRefusedException(
                "Auth__BootstrapAdmin__Password is required to create the bootstrap admin: set it to at least " +
                $"{policy.MinLength} characters, or clear Auth__BootstrapAdmin__Email to seed no admin.");
        if (PasswordPolicy.Validate(password, policy) is null
            && !string.Equals(password, RefusedBootstrapValue, StringComparison.OrdinalIgnoreCase)) return;
        throw new BootstrapPasswordRefusedException(
            $"Auth__BootstrapAdmin__Password is too weak: use at least {policy.MinLength} characters and not " +
            $"'{RefusedBootstrapValue}'.");
    }

    /// <summary>Reads the seed inputs from application configuration and hashes the bootstrap password.</summary>
    public static CoreSeedData FromServices(IServiceProvider services)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        return From(
            services.GetRequiredService<IOptions<LocalizationOptions>>().Value,
            services.GetRequiredService<IPasswordHasher>(),
            configuration["Auth:BootstrapAdmin:Email"],
            configuration["Auth:BootstrapAdmin:Password"],
            configuration.GetSection("Rbac:PublicReadCollections").Get<string[]>() ?? [],
            services.GetService<IOptions<PasswordPolicyOptions>>()?.Value);
    }
}
