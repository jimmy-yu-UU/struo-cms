// tests/Struo.Tests.Support/HostIsolation.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;

namespace Struo.Tests.Support;

/// <summary>
/// The configuration every test host that loads the gitignored <c>appsettings.Development.json</c> must
/// override, so it never reaches the maintainer's real services: Redis, an S3 or MinIO bucket, the image
/// cache path and the single-sign-on providers. Layer it as an in-memory source; it wins over the file.
/// </summary>
public static class HostIsolation
{
    /// <summary>The path the framework gives the user-secrets source: the file name, without a directory.</summary>
    private const string UserSecretsFileName = "secrets.json";

    public static Dictionary<string, string?> ExternalTargets(string filesRoot, string imageCacheRoot) => new()
    {
        ["Redis:ConnectionString"] = "",
        ["Struo:Files:Backend"] = "local",
        ["Struo:Files:Local:RootPath"] = filesRoot,
        ["Struo:Files:ImageTransform:CachePath"] = imageCacheRoot,
        ["Struo:Files:S3:Endpoint"] = "",
        ["Struo:Files:S3:Bucket"] = "",
        ["Struo:Files:S3:AccessKey"] = "",
        ["Struo:Files:S3:SecretKey"] = "",
        ["Oidc:Providers:microsoft:Enabled"] = "false",
        ["Oidc:Providers:google:Enabled"] = "false",
    };

    /// <summary>
    /// True for the JSON source the framework adds for the user secrets. A source of a fork's own whose name
    /// only contains the file name (<c>my-secrets.json</c>) is not one.
    /// </summary>
    public static bool IsUserSecretsSource(IConfigurationSource source) =>
        source is JsonConfigurationSource { Path: UserSecretsFileName };

    /// <summary>The user-secrets sources of a built configuration; only provider types and paths are read.</summary>
    public static IReadOnlyList<string> UserSecretsSources(IConfiguration configuration) =>
        ((IConfigurationRoot)configuration).Providers
            .OfType<JsonConfigurationProvider>()
            .Where(p => IsUserSecretsSource(p.Source))
            .Select(p => p.Source.Path!)
            .ToList();

    /// <summary>
    /// Drops the developer's user-secrets file from the configuration sources. A Development host whose
    /// application assembly carries a <c>UserSecretsId</c> adds that file, which holds real connections.
    /// Call it from <c>ConfigureAppConfiguration</c> of every host that starts <c>Program</c>. It runs when
    /// the host is built; <see cref="TestHostEnvironment.Pin"/> keeps the file out of the reads that happen
    /// earlier.
    /// </summary>
    public static void RemoveUserSecrets(IConfigurationBuilder configuration)
    {
        foreach (var source in configuration.Sources.Where(IsUserSecretsSource).ToList())
            configuration.Sources.Remove(source);
    }
}
