using Microsoft.Extensions.Logging;

namespace Struo.Infrastructure.Identity;

/// <summary>Warns when a production host is configured with the template's default bootstrap admin password.</summary>
public static class BootstrapAdminPasswordWarning
{
    internal const string DefaultPassword = "admin";

    public static void LogIfDefault(bool isProduction, string? password, ILogger logger)
    {
        if (isProduction && string.Equals(password, DefaultPassword, StringComparison.Ordinal))
            logger.LogWarning(
                "Bootstrap admin is using the default password '{Default}'. Change it immediately via Auth__BootstrapAdmin__Password.",
                DefaultPassword);
    }
}
