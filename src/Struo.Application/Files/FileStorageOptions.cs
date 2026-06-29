namespace Struo.Application.Files;

public sealed class FileStorageOptions
{
    public const string SectionName = "Struo:Files";

    public string Backend { get; set; } = "local";          // "local" | "s3"
    public long MaxUploadBytes { get; set; } = 26_214_400;   // 25 MB
    public string[] AllowedContentTypes { get; set; } = [];  // empty = allow all
    public LocalOptions Local { get; set; } = new();
    public S3Options S3 { get; set; } = new();

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
