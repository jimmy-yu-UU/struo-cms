using StackExchange.Redis;

namespace Struo.Api.Auth;

/// <summary>
/// Production startup warnings for a Redis-backed key ring: one when the keys are stored without a
/// certificate, one when Redis has no password. Only configuration key names are logged, never the
/// connection string.
/// </summary>
public static class DataProtectionWarnings
{
    public static void Log(IConfiguration config, IHostEnvironment env, ILogger logger)
    {
        if (!env.IsProduction()) return;
        var redis = config.GetValue<string>("Redis:ConnectionString");
        if (string.IsNullOrWhiteSpace(redis)) return;

        if (string.IsNullOrWhiteSpace(config.GetValue<string>("DataProtection:Certificate:Path")))
        {
            logger.LogWarning(
                "DataProtection:Certificate:Path is empty while Redis:ConnectionString is set: the key ring is stored " +
                "in Redis unencrypted, so anyone who can read Redis can forge session cookies. Set " +
                "DataProtection:Certificate:Path (and DataProtection:Certificate:Password) to encrypt it.");
        }

        if (HasNoPassword(redis))
        {
            logger.LogWarning(
                "Redis:ConnectionString has no password: anyone who can connect to Redis can take over every " +
                "session. Add a password to the connection string and keep Redis off public networks.");
        }
    }

    // An unparseable string is left to the Redis consumers, which report it themselves.
    private static bool HasNoPassword(string connectionString)
    {
        try
        {
            return string.IsNullOrEmpty(ConfigurationOptions.Parse(connectionString).Password);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
