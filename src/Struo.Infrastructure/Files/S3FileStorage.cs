using Amazon.S3;
using Amazon.S3.Model;
using Struo.Application.Files;
using Struo.Domain.Query;

namespace Struo.Infrastructure.Files;

/// <summary>
/// S3-compatible byte storage (AWS S3, MinIO, etc.). <see cref="AmazonS3Config.ForcePathStyle"/>
/// makes it work against MinIO. Supports presigned download URLs.
/// </summary>
public sealed class S3FileStorage : IFileStorage, IDisposable
{
    private readonly IAmazonS3 _client;
    private readonly string _bucket;

    public S3FileStorage(FileStorageOptions options)
        : this(CreateClient(options.S3), options.S3.Bucket!) { }

    // Test seam: inject a fake IAmazonS3 to assert request assembly (ContentType / download
    // disposition) without a live S3/MinIO endpoint. Production always goes through the public ctor.
    internal S3FileStorage(IAmazonS3 client, string bucket)
    {
        _client = client;
        _bucket = bucket;
    }

    internal static IAmazonS3 CreateClient(FileStorageOptions.S3Options s3)
    {
        var config = new AmazonS3Config
        {
            ForcePathStyle = s3.ForcePathStyle,
            AuthenticationRegion = s3.Region,
            // Presigned URLs must carry the endpoint's real scheme: the SDK defaults to https,
            // which an http-only MinIO refuses after the redirect.
            UseHttp = s3.Endpoint?.StartsWith("http://", StringComparison.OrdinalIgnoreCase) == true,
        };
        // AmazonS3Config requires exactly one of ServiceURL/RegionEndpoint. Production always supplies
        // Endpoint (FileStorageOptions.Validate() enforces it when Backend=s3); the RegionEndpoint
        // fallback only exists so this factory stays constructible for a null endpoint in isolation
        // (e.g. unit tests), never on the real save/read/presign path.
        if (s3.Endpoint is null) config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(s3.Region);
        else config.ServiceURL = s3.Endpoint;
        return new AmazonS3Client(s3.AccessKey, s3.SecretKey, config);
    }

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default) =>
        // Record the validated content type as S3 object metadata so a presigned/direct GET
        // serves the correct Content-Type instead of a generic application/octet-stream.
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket, Key = key, InputStream = content, AutoCloseStream = false,
            ContentType = contentType
        }, ct);

    public async Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var resp = await _client.GetObjectAsync(_bucket, key, ct);
            return resp.ResponseStream;
        }
        // Only the missing-key case (row exists, blob doesn't — DB/storage drift) becomes a 404.
        // Matched on ErrorCode rather than the HTTP status alone: a missing bucket also reports 404
        // ("NoSuchBucket") but is a real misconfiguration, and every other S3 fault (network,
        // permissions, ...) must keep surfacing as a 500 instead of being hidden as a false 404.
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
        {
            throw new FileBlobNotFoundException(key);
        }
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default) =>
        await _client.DeleteObjectAsync(_bucket, key, ct);

    public bool SupportsPresignedUrls => true;

    public Task<string?> GetPresignedUrlAsync(string key, TimeSpan ttl, CancellationToken ct = default) =>
        Task.FromResult<string?>(_client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _bucket, Key = key, Verb = HttpVerb.GET, Expires = DateTime.UtcNow.Add(ttl),
            // Force a download disposition on the direct-from-storage GET so a browser follows
            // the 302 into a download rather than rendering the bytes inline (defence in depth for
            // types the API path can't wrap in Content-Disposition: attachment, e.g. SVG).
            ResponseHeaderOverrides = new ResponseHeaderOverrides { ContentDisposition = "attachment" }
        }));

    public void Dispose() => _client.Dispose();
}
