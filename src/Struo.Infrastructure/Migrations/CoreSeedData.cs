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
/// are both set or both null) and the collections the <c>public</c> role may read.
/// </summary>
public sealed record CoreSeedData(
    IReadOnlyList<SeedLanguage> Languages, string? AdminEmail, string? AdminPasswordHash,
    IReadOnlyList<string> PublicReadCollections)
{
    public bool HasAdmin => AdminEmail is not null && AdminPasswordHash is not null;

    public static CoreSeedData From(
        LocalizationOptions localization, IPasswordHasher hasher,
        string? email, string? password, IReadOnlyList<string> publicRead)
    {
        var languages = localization.EffectiveLanguages
            .Select((entry, index) => new SeedLanguage(
                entry.Code, entry.Name,
                string.Equals(entry.Code, localization.DefaultLanguage, StringComparison.OrdinalIgnoreCase),
                index + 1))
            .ToList();
        var hasAdmin = !string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(password);
        return new CoreSeedData(
            languages,
            hasAdmin ? email : null,
            hasAdmin ? hasher.Hash(password!) : null,
            publicRead.Distinct(StringComparer.Ordinal).ToList());
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
            configuration.GetSection("Rbac:PublicReadCollections").Get<string[]>() ?? []);
    }
}
