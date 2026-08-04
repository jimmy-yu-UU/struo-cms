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

        public string[] AllowedFormats { get; set; } = [];
        public int DefaultQuality { get; set; } = 82;

        // Root directory for cached transformed-image variants (IImageVariantCache). Relative
        // paths are resolved against the app's content root (IHostEnvironment.ContentRootPath) at DI
        // registration time, never the process CWD — a relative path resolved against CWD is a known
        // footgun for this codebase once the process is launched from a different working directory
        // (e.g. a systemd unit or a different shell) than the project folder.
        public string CachePath { get; set; } = "App_Data/image-cache";

        /// <summary>Applies defaults that cannot live in a property initializer without
        /// ConfigurationBinder appending to them. Must run after binding and before the value is
        /// consumed.</summary>
        public void ApplyCollectionDefaults()
        {
            if (AllowedFormats.Length == 0) AllowedFormats = DefaultAllowedFormats;
        }
    }

    /// <summary>Applies defaults that cannot live in a property initializer without
    /// ConfigurationBinder appending to them. Must run after binding and before the value is
    /// consumed; <c>FileStorageServiceCollectionExtensions.AddStruoFiles</c> registers this as a
    /// <c>PostConfigure</c> callback.</summary>
    public void ApplyCollectionDefaults() => ImageTransform.ApplyCollectionDefaults();

    public void Validate()
    {
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
