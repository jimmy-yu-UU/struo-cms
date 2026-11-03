using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.StackExchangeRedis;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Struo.Application.Configuration;

namespace Struo.Api.Auth;

/// <summary>
/// DataProtection for every replica: the application name always comes from
/// <see cref="StruoDataProtectionOptions"/>, and the key ring goes to Redis when
/// <c>Redis:ConnectionString</c> is set, so a cookie signed by one replica is accepted by the others.
/// Without Redis the key ring stays in the default file location. When <c>DataProtection:Certificate:Path</c>
/// is set, the keys are encrypted with that certificate wherever the key ring lives.
/// </summary>
public static class DataProtectionWiring
{
    /// <summary>The Redis key holding the key ring (a list of XML elements).</summary>
    public const string RedisKeyRingKey = "DataProtection-Keys";

    public static IServiceCollection AddStruoDataProtection(this IServiceCollection services)
    {
        services.AddOptions<StruoDataProtectionOptions>()
            .BindConfiguration(StruoDataProtectionOptions.SectionName)
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.ApplicationName),
                "DataProtection:ApplicationName must not be blank.")
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<StruoDataProtectionOptions>, KeyProtectionValidator>();

        services.AddDataProtection();
        services.AddSingleton<RedisKeyRingConnection>();

        // Resolved from DI, not captured at registration, so configuration appended after service
        // registration (test hosts, late providers) is honored like the other Redis consumers.
        services.AddOptions<DataProtectionOptions>()
            .Configure<IOptions<StruoDataProtectionOptions>>((options, own) =>
                options.ApplicationDiscriminator = own.Value.ApplicationName);
        services.AddSingleton<DataProtectionKeyProtection>();
        services.AddOptions<KeyManagementOptions>()
            .Configure<DataProtectionKeyProtection>((options, protection) =>
            {
                if (protection.Encryptor is not null) options.XmlEncryptor = protection.Encryptor;
            });
        services.AddOptions<KeyManagementOptions>()
            .Configure<IConfiguration, RedisKeyRingConnection>((options, config, connection) =>
            {
                var redis = config.GetValue<string>("Redis:ConnectionString");
                if (string.IsNullOrWhiteSpace(redis)) return;
                options.XmlRepository = new RedisXmlRepository(
                    () => connection.GetDatabase(redis), RedisKeyRingKey);
            });

        return services;
    }

    /// <summary>Fails startup when <c>RequireKeyProtection</c> has no certificate or a configured file cannot be loaded.</summary>
    private sealed class KeyProtectionValidator : IValidateOptions<StruoDataProtectionOptions>
    {
        public ValidateOptionsResult Validate(string? name, StruoDataProtectionOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.Certificate.Path))
            {
                if (options.RequireKeyProtection)
                {
                    return ValidateOptionsResult.Fail(
                        "DataProtection:RequireKeyProtection is true but DataProtection:Certificate:Path is not set.");
                }

                return options.Certificate.Previous.Count > 0
                    ? ValidateOptionsResult.Fail(
                        "DataProtection:Certificate:Previous is set but DataProtection:Certificate:Path is not.")
                    : ValidateOptionsResult.Success;
            }

            try
            {
                DataProtectionCertificates.Load(options)?.Dispose();
                return ValidateOptionsResult.Success;
            }
            catch (InvalidOperationException ex)
            {
                return ValidateOptionsResult.Fail(ex.Message);
            }
        }
    }

    /// <summary>
    /// One multiplexer for the key ring, connected on first use. With <c>AbortOnConnectFail</c> off an
    /// unreachable server yields a disconnected multiplexer whose commands throw, so a Redis that comes
    /// up later is picked up without a restart; this is the same behaviour as the login throttle.
    /// </summary>
    public sealed class RedisKeyRingConnection : IDisposable
    {
        private readonly object _gate = new();
        private ConnectionMultiplexer? _multiplexer;

        public IDatabase GetDatabase(string connectionString)
        {
            lock (_gate)
            {
                if (_multiplexer is null)
                {
                    var config = ConfigurationOptions.Parse(connectionString);
                    config.AbortOnConnectFail = false;
                    _multiplexer = ConnectionMultiplexer.Connect(config);
                }
                return _multiplexer.GetDatabase();
            }
        }

        public void Dispose() => _multiplexer?.Dispose();
    }
}
