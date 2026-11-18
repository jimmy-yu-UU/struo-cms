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
    /// Drops the developer's user-secrets file from the configuration sources. A Development host whose
    /// application assembly carries a <c>UserSecretsId</c> adds that file, which holds real connections.
    /// Call it from <c>ConfigureAppConfiguration</c> of every host that starts <c>Program</c>.
    /// </summary>
    public static void RemoveUserSecrets(IConfigurationBuilder configuration)
    {
        var sources = configuration.Sources
            .OfType<JsonConfigurationSource>()
            .Where(s => s.Path?.Contains("secrets.json", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();
        foreach (var source in sources)
            configuration.Sources.Remove(source);
    }
}
