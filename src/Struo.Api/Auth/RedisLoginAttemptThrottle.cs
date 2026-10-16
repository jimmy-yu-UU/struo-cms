using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Struo.Application.Configuration;
using Struo.Application.Security;

namespace Struo.Api.Auth;

/// <summary>
/// <see cref="ILoginAttemptThrottle"/> on a Redis atomic counter, used when <c>Redis:ConnectionString</c>
/// is set so the count is shared by every instance. <c>INCR</c> counts the attempt and returns the new
/// value in one step, so concurrent attempts each see a distinct count. Only <c>INCR</c>, <c>EXPIRE</c>,
/// <c>TTL</c> and <c>DEL</c> are used. The window is fixed at the first attempt: <c>EXPIRE</c> is set
/// when the key is created, and re-set if a key is ever found without a TTL, so a crash between
/// <c>INCR</c> and <c>EXPIRE</c> cannot leave an account locked forever.
/// <para>
/// A Redis failure propagates as an exception: login fails closed rather than skipping the throttle.
/// </para>
/// </summary>
public sealed class RedisLoginAttemptThrottle : ILoginAttemptThrottle, IDisposable
{
    // Distinct from the distributed-cache throttle's "login-throttle:" keys, which RedisCache stores as
    // hashes that INCR cannot touch.
    private const string DefaultKeyPrefix = "login-attempts:";

    private readonly IOptions<LoginAccountRateLimitOptions> _options;
    private readonly string _keyPrefix;
    private readonly Lazy<Task<IConnectionMultiplexer>> _connection;

    // keyPrefix is overridable so tests can isolate their keys; production uses the default.
    public RedisLoginAttemptThrottle(
        string connectionString, IOptions<LoginAccountRateLimitOptions> options, string keyPrefix = DefaultKeyPrefix)
    {
        _options = options;
        _keyPrefix = keyPrefix;
        // One multiplexer per process, connected on first use. With AbortOnConnectFail off, an
        // unreachable server yields a disconnected multiplexer whose commands throw, so a Redis that
        // comes up later is picked up without a restart.
        _connection = new Lazy<Task<IConnectionMultiplexer>>(async () =>
        {
            var config = ConfigurationOptions.Parse(connectionString);
            config.AbortOnConnectFail = false;
            return await ConnectionMultiplexer.ConnectAsync(config);
        });
    }

    public async Task<LoginThrottleState> TryReserveAsync(string email, CancellationToken ct = default)
    {
        var opts = _options.Value;
        if (!opts.Enabled) return new LoginThrottleState(false, 0);

        var db = (await _connection.Value).GetDatabase();
        var key = Key(email);
        var window = TimeSpan.FromSeconds(opts.WindowSeconds);

        var count = await db.StringIncrementAsync(key);
        TimeSpan? ttl;
        if (count == 1)
        {
            await db.KeyExpireAsync(key, window);
            ttl = window;
        }
        else
        {
            ttl = await db.KeyTimeToLiveAsync(key);
            if (ttl is null)
            {
                await db.KeyExpireAsync(key, window);
                ttl = window;
            }
        }

        if (count <= opts.PermitLimit) return new LoginThrottleState(false, 0);
        return new LoginThrottleState(true, (int)Math.Max(1, Math.Ceiling(ttl.Value.TotalSeconds)));
    }

    public async Task ResetAsync(string email, CancellationToken ct = default)
    {
        if (!_options.Value.Enabled) return;
        await (await _connection.Value).GetDatabase().KeyDeleteAsync(Key(email));
    }

    public void Dispose()
    {
        if (_connection.IsValueCreated && _connection.Value.IsCompletedSuccessfully)
            _connection.Value.Result.Dispose();
    }

    // The hashed, normalized email keeps raw addresses out of Redis key names.
    private string Key(string email) =>
        _keyPrefix + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant())));
}
