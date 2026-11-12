using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Struo.Tests.Support;

/// <summary>
/// An <see cref="IDistributedCache"/> over memory that throws on demand, standing in for a cache server that
/// has become unreachable. Nothing throws until <see cref="Armed"/> is set, so a test can log in against a
/// working cache first. <see cref="ThrowOnGetNumber"/> and <see cref="ThrowOnSet"/> pick the operation that fails
/// and <see cref="Failure"/> the exception it throws.
/// </summary>
public sealed class ThrowingDistributedCache : IDistributedCache
{
    private readonly MemoryDistributedCache _inner = new(Options.Create(new MemoryDistributedCacheOptions()));

    public bool Armed { get; set; }
    /// <summary>The 1-based number, counted from <see cref="Armed"/>, of the read that throws; 0 for none.</summary>
    public int ThrowOnGetNumber { get; set; }
    public bool ThrowOnSet { get; set; }
    public Func<Exception> Failure { get; set; } = () => new TimeoutException("cache unreachable (simulated)");

    public byte[]? Get(string key) => _inner.Get(key);

    private int _gets;

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
        Armed && ++_gets == ThrowOnGetNumber ? throw Failure() : _inner.GetAsync(key, token);

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _inner.Set(key, value, options);

    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
        Armed && ThrowOnSet ? throw Failure() : _inner.SetAsync(key, value, options, token);

    public void Refresh(string key) => _inner.Refresh(key);
    public Task RefreshAsync(string key, CancellationToken token = default) => _inner.RefreshAsync(key, token);
    public void Remove(string key) => _inner.Remove(key);
    public Task RemoveAsync(string key, CancellationToken token = default) => _inner.RemoveAsync(key, token);
}
