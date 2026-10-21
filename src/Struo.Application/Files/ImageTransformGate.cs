using System.Diagnostics;

namespace Struo.Application.Files;

/// <summary>
/// Process-wide limit on how many image transforms run at once. A caller that cannot get a slot within
/// <see cref="FileStorageOptions.ImageTransformOptions.QueueTimeoutSeconds"/> is turned away so a burst
/// of cold requests cannot exhaust CPU or memory. Anonymous callers hold at most one slot fewer than
/// the total, so an authenticated caller can always use the remaining slot.
/// </summary>
public sealed class ImageTransformGate(FileStorageOptions options)
{
    private readonly SemaphoreSlim _slots = new(
        options.ImageTransform.MaxConcurrentTransforms, options.ImageTransform.MaxConcurrentTransforms);
    private readonly SemaphoreSlim _anonymousSlots = new(
        options.ImageTransform.MaxConcurrentTransforms - 1, options.ImageTransform.MaxConcurrentTransforms - 1);
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(options.ImageTransform.QueueTimeoutSeconds);

    /// <summary>Returns a lease to dispose when the transform ends, or null when no slot freed within
    /// the queue timeout. An anonymous caller first takes one of the anonymous slots, then a total slot;
    /// both waits share the one timeout.</summary>
    public async Task<IDisposable?> TryEnterAsync(bool anonymous, CancellationToken ct)
    {
        if (!anonymous)
            return await _slots.WaitAsync(_timeout, ct) ? new Lease(_slots, null) : null;

        var waited = Stopwatch.StartNew();
        if (!await _anonymousSlots.WaitAsync(_timeout, ct)) return null;
        var entered = false;
        try
        {
            var remaining = _timeout - waited.Elapsed;
            entered = await _slots.WaitAsync(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, ct);
        }
        finally
        {
            if (!entered) _anonymousSlots.Release();
        }
        return entered ? new Lease(_slots, _anonymousSlots) : null;
    }

    private sealed class Lease(SemaphoreSlim slots, SemaphoreSlim? anonymousSlots) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            slots.Release();
            anonymousSlots?.Release();
        }
    }
}

/// <summary>The collaborators of the on-the-fly image transform endpoint.</summary>
public sealed record ImageTransformServices(
    IImageTransformer Transformer,
    IImageVariantCache Cache,
    ImageTransformGate Gate,
    IImageDimensionReader Dimensions);
