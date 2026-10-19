namespace Struo.Application.Files;

/// <summary>
/// Process-wide limit on how many image transforms run at once. A caller that cannot get a slot within
/// <see cref="FileStorageOptions.ImageTransformOptions.QueueTimeoutSeconds"/> is turned away so a burst
/// of cold requests cannot exhaust CPU or memory.
/// </summary>
public sealed class ImageTransformGate(FileStorageOptions options)
{
    private readonly SemaphoreSlim _slots = new(
        options.ImageTransform.MaxConcurrentTransforms, options.ImageTransform.MaxConcurrentTransforms);
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(options.ImageTransform.QueueTimeoutSeconds);

    /// <summary>Returns a lease to dispose when the transform ends, or null when no slot freed in time.</summary>
    public async Task<IDisposable?> TryEnterAsync(CancellationToken ct) =>
        await _slots.WaitAsync(_timeout, ct) ? new Lease(_slots) : null;

    private sealed class Lease(SemaphoreSlim slots) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) slots.Release();
        }
    }
}

/// <summary>The collaborators of the on-the-fly image transform endpoint.</summary>
public sealed record ImageTransformServices(
    IImageTransformer Transformer,
    IImageVariantCache Cache,
    ImageTransformGate Gate,
    IImageDimensionReader Dimensions);
