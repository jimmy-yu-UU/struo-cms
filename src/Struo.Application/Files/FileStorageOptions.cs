namespace Struo.Application.Files;

public sealed class FileStorageOptions
{
    public const string SectionName = "Struo:Files";

    public string Backend { get; set; } = "local";          // "local" | "s3"
    public long MaxUploadBytes { get; set; } = 26_214_400;   // 25 MB
    public string[] AllowedContentTypes { get; set; } = [];  // empty = allow all

    // When true, GET /api/files/{id}/content 302-redirects to a storage presigned URL (offloads
    // bytes to S3/CDN). Default false: the API streams the bytes itself, which keeps the storage
    // endpoint private and works when the browser cannot reach storage (or its scheme) directly.
    public bool PresignedRedirect { get; set; } = false;

    /// <summary>Status given to a newly uploaded file: <c>published</c> (readable by anyone with its
    /// id) or <c>draft</c> (readable only by callers with a file write grant).</summary>
    public string DefaultStatus { get; set; } = PublishedStatus;

    public const string PublishedStatus = "published";
    public const string DraftStatus = "draft";

    public LocalOptions Local { get; set; } = new();
    public S3Options S3 { get; set; } = new();
    public ImageTransformOptions ImageTransform { get; set; } = new();

    public sealed class LocalOptions { public string RootPath { get; set; } = "App_Data/uploads"; }

    public sealed class S3Options
    {
        public string? Endpoint { get; set; }
        public string? Bucket { get; set; }
        public string? AccessKey { get; set; }
        public string? SecretKey { get; set; }
        public string Region { get; set; } = "us-east-1";
        public bool ForcePathStyle { get; set; } = true;
        public int PresignTtlSeconds { get; set; } = 300;
    }

    public sealed class ImageTransformOptions
    {
        public bool Enabled { get; set; } = true;
        public int MaxWidth { get; set; } = 4096;
        public int MaxHeight { get; set; } = 4096;

        /// <summary>The formats applied by <see cref="ApplyCollectionDefaults"/> when none are
        /// configured. Held as a named default rather than a property initializer because
        /// ConfigurationBinder APPENDS bound array elements to a non-empty collection default instead
        /// of replacing it, which made narrowing this list impossible.</summary>
        public static readonly string[] DefaultAllowedFormats = ["webp", "jpeg", "png", "avif"];

        /// <summary>Empty until <see cref="ApplyCollectionDefaults"/> runs. <c>AddStruoFiles</c>
        /// registers that as a <c>PostConfigure</c>; a caller constructing this type directly (rather
        /// than through DI) must call it itself, or <c>FilesController</c> will reject every explicitly
        /// requested <c>format</c> — an empty list means no format ever matches the allow-check.</summary>
        public string[] AllowedFormats { get; set; } = [];
        public int DefaultQuality { get; set; } = 82;

        // Root directory for cached transformed-image variants (IImageVariantCache). Relative
        // paths are resolved against the app's content root (IHostEnvironment.ContentRootPath) at DI
        // registration time, never the process CWD — a relative path resolved against CWD is a known
        // footgun for this codebase once the process is launched from a different working directory
        // (e.g. a systemd unit or a different shell) than the project folder.
        public string CachePath { get; set; } = "App_Data/image-cache";

        public static readonly int[] DefaultSizeSteps =
            [64, 128, 256, 320, 480, 640, 768, 1024, 1280, 1600, 1920, 2560, 3840, 4096];

        public static readonly string[] DefaultTransformableContentTypes =
            ["image/jpeg", "image/png", "image/webp", "image/avif", "image/gif"];

        /// <summary>Output sizes a requested width/height snaps UP to; above the last step the last
        /// step applies. Empty until <see cref="ApplyCollectionDefaults"/> runs.</summary>
        public int[] SizeSteps { get; set; } = [];

        /// <summary>Source content types that are transformed; any other image (e.g. SVG) is served
        /// as the original bytes. Empty until <see cref="ApplyCollectionDefaults"/> runs.</summary>
        public string[] TransformableContentTypes { get; set; } = [];

        /// <summary>Transforms that may run at the same time across all requests, at least 2.
        /// Anonymous requests may hold at most one slot fewer, so an authenticated request can
        /// always use the remaining slot.</summary>
        public int MaxConcurrentTransforms { get; set; } = 2;

        /// <summary>Seconds a request waits for a free transform slot before answering 503.</summary>
        public int QueueTimeoutSeconds { get; set; } = 10;

        /// <summary>Disk cache size cap in bytes; least-recently-used variants are evicted above it.
        /// 0 or less = no cap.</summary>
        public long CacheMaxBytes { get; set; } = 2_147_483_648;

        /// <summary>Largest source (width x height) that is decoded; a larger source is served as the
        /// original bytes.</summary>
        public long MaxSourcePixels { get; set; } = 50_000_000;

        /// <summary>Applies defaults that cannot live in a property initializer without
        /// ConfigurationBinder appending to them. Must run after binding and before the value is
        /// consumed.</summary>
        public void ApplyCollectionDefaults()
        {
            if (AllowedFormats.Length == 0) AllowedFormats = [.. DefaultAllowedFormats];
            if (SizeSteps.Length == 0) SizeSteps = [.. DefaultSizeSteps];
            if (TransformableContentTypes.Length == 0)
                TransformableContentTypes = [.. DefaultTransformableContentTypes];
        }

        public void Validate()
        {
            if (MaxWidth < 1)
                throw new InvalidOperationException("Struo:Files:ImageTransform:MaxWidth must be at least 1.");
            if (MaxHeight < 1)
                throw new InvalidOperationException("Struo:Files:ImageTransform:MaxHeight must be at least 1.");
            if (DefaultQuality is < 1 or > 100)
                throw new InvalidOperationException("Struo:Files:ImageTransform:DefaultQuality must be between 1 and 100.");
            if (SizeSteps.Any(s => s < 1))
                throw new InvalidOperationException("Struo:Files:ImageTransform:SizeSteps entries must be positive.");
            if (MaxConcurrentTransforms < 2)
                throw new InvalidOperationException("Struo:Files:ImageTransform:MaxConcurrentTransforms must be at least 2.");
            if (QueueTimeoutSeconds < 0)
                throw new InvalidOperationException("Struo:Files:ImageTransform:QueueTimeoutSeconds must not be negative.");
            if (MaxSourcePixels < 1)
                throw new InvalidOperationException("Struo:Files:ImageTransform:MaxSourcePixels must be positive.");
        }
    }

    /// <summary>Applies defaults that cannot live in a property initializer without
    /// ConfigurationBinder appending to them. Must run after binding and before the value is
    /// consumed; <c>FileStorageServiceCollectionExtensions.AddStruoFiles</c> registers this as a
    /// <c>PostConfigure</c> callback.</summary>
    public void ApplyCollectionDefaults() => ImageTransform.ApplyCollectionDefaults();

    public void Validate()
    {
        ImageTransform.Validate();
        if (DefaultStatus is not (PublishedStatus or DraftStatus))
            throw new InvalidOperationException(
                $"Unknown Struo:Files:DefaultStatus '{DefaultStatus}' (expected '{PublishedStatus}' or '{DraftStatus}').");
        switch (Backend?.ToLowerInvariant())
        {
            case "local":
                if (string.IsNullOrWhiteSpace(Local.RootPath))
                    throw new InvalidOperationException("Struo:Files:Local:RootPath is required when Backend=local.");
                break;
            case "s3":
                if (string.IsNullOrWhiteSpace(S3.Endpoint) || string.IsNullOrWhiteSpace(S3.Bucket)
                    || string.IsNullOrWhiteSpace(S3.AccessKey) || string.IsNullOrWhiteSpace(S3.SecretKey))
                    throw new InvalidOperationException(
                        "Struo:Files:S3 Endpoint, Bucket, AccessKey and SecretKey are required when Backend=s3.");
                break;
            default:
                throw new InvalidOperationException($"Unknown Struo:Files:Backend '{Backend}' (expected 'local' or 's3').");
        }
    }
}
