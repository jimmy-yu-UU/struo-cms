using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Struo.Application.Configuration;

namespace Struo.Api.Auth;

/// <summary>
/// DataProtection for every replica: the application name always comes from
/// <see cref="StruoDataProtectionOptions"/>, and the key ring goes to Redis when
/// <c>Redis:ConnectionString</c> is set, so a cookie signed by one replica is accepted by the others.
/// Without Redis the key ring stays in the default file location.
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

        services.AddDataProtection();
        services.AddSingleton<RedisKeyRingConnection>();

        // Resolved from DI, not captured at registration, so configuration appended after service
        // registration (test hosts, late providers) is honored like the other Redis consumers.
        services.AddOptions<DataProtectionOptions>()
            .Configure<IOptions<StruoDataProtectionOptions>>((options, own) =>
                options.ApplicationDiscriminator = own.Value.ApplicationName);
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
