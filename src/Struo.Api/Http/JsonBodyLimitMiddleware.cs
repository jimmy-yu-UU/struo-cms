using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Struo.Application.Configuration;
using Struo.Domain.Query;

namespace Struo.Api.Http;

/// <summary>
/// Caps the body of JSON requests (and anything posted to <c>/graphql</c>) at
/// <see cref="ApiOptions.MaxJsonBodyBytes"/>. A declared length over the cap is rejected before the body
/// is read. An undeclared (chunked) REST body is cut off by a counting stream once it passes the cap and
/// surfaces as <see cref="PayloadTooLargeException"/>, which the exception handler answers with the 413
/// error envelope; an undeclared GraphQL body is read up to the cap before GraphQL sees it, so an
/// over-cap one is refused here. Every path answers 413 <c>PAYLOAD_TOO_LARGE</c> with
/// <see cref="TooLargeMessage"/>, which names no limit. Multipart uploads are not JSON and keep
/// <c>Struo:Files:MaxUploadBytes</c>.
/// </summary>
public sealed class JsonBodyLimitMiddleware(RequestDelegate next, IOptions<ApiOptions> options)
{
    /// <summary>The message of every over-limit request body, JSON or Kestrel's own.</summary>
    internal const string TooLargeMessage = "Request body is too large.";

    private const string UnreadableMessage = "The request body could not be read.";

    private const int BufferChunkBytes = 81920;

    private readonly long _limit = options.Value.MaxJsonBodyBytes;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsCapped(context.Request))
        {
            await next(context);
            return;
        }

        var isGraphQl = IsGraphQl(context.Request);
        if (context.Request.ContentLength > _limit)
        {
            if (!isGraphQl) throw new PayloadTooLargeException(TooLargeMessage);
            await WriteGraphQlErrorAsync(context);
            return;
        }

        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = _limit;

        if (!isGraphQl)
        {
            context.Request.Body = new LimitedReadStream(context.Request.Body, _limit);
        }
        else if (context.Request.ContentLength is null && CanHaveBody(context))
        {
            // GraphQL turns a failed body read into its own transport error, so the cap is checked
            // before it reads: a GraphQL body is at most the cap, which is small.
            var (buffered, failureStatus) = await ReadWithinLimitAsync(context);
            if (buffered is null)
            {
                await WriteGraphQlErrorAsync(context, failureStatus);
                return;
            }
            context.Request.Body = buffered;
        }
        await next(context);
    }

    // A GET or HEAD, or a request the server already knows has no body, is passed on unread.
    private static bool CanHaveBody(HttpContext context) =>
        context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody
            ?? !(HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method));

    // The whole body, or the HTTP status of why it could not be read: 413 once it passes the cap
    // (including Kestrel's own 413 for the same cap), or the status of any other bad request.
    private async Task<(MemoryStream? Body, int FailureStatus)> ReadWithinLimitAsync(HttpContext context)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[BufferChunkBytes];
        try
        {
            int read;
            while ((read = await context.Request.Body.ReadAsync(chunk, context.RequestAborted)) > 0)
            {
                if (buffer.Length + read > _limit) return (null, StatusCodes.Status413PayloadTooLarge);
                buffer.Write(chunk, 0, read);
            }
        }
        catch (BadHttpRequestException e)
        {
            return (null, e.StatusCode);
        }
        buffer.Position = 0;
        return (buffer, 0);
    }

    // /graphql reports transport errors as { errors: [...] }, not as the REST envelope.
    private static Task WriteGraphQlErrorAsync(HttpContext context, int status = StatusCodes.Status413PayloadTooLarge)
    {
        context.Response.StatusCode = status;
        var tooLarge = status == StatusCodes.Status413PayloadTooLarge;
        var error = new Dictionary<string, object?>
        {
            ["message"] = tooLarge ? TooLargeMessage : UnreadableMessage,
            ["extensions"] = new Dictionary<string, object?>
            {
                ["code"] = tooLarge ? ErrorCodes.PayloadTooLarge : ErrorCodes.BadUserInput,
                ["traceId"] = RequestTraceId.For(context),
            },
        };
        return context.Response.WriteAsJsonAsync(
            new Dictionary<string, object?> { ["errors"] = new[] { error } }, context.RequestAborted);
    }

    internal static bool IsCapped(HttpRequest request) => IsGraphQl(request) || IsJson(request.ContentType);

    private static bool IsGraphQl(HttpRequest request) =>
        request.Path.StartsWithSegments("/graphql", StringComparison.OrdinalIgnoreCase);

    private static bool IsJson(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType)) return false;
        var mediaType = contentType.Split(';', 2)[0].Trim();
        return mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("text/json", StringComparison.OrdinalIgnoreCase)
            || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Read-only stream that throws <see cref="PayloadTooLargeException"/> once more than <c>limit</c> bytes have
/// been read. Kestrel, when it enforces the same limit through <c>IHttpMaxRequestBodySizeFeature</c>, throws
/// its own 413 first; that one passes through unchanged and maps to the same error.
/// </summary>
public sealed class LimitedReadStream(Stream inner, long limit) : Stream
{
    private long _read;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Count(inner.Read(buffer, offset, count));

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        await ReadAsync(buffer.AsMemory(offset, count), cancellationToken);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Count(await inner.ReadAsync(buffer, cancellationToken));

    private int Count(int read)
    {
        _read += read;
        if (_read <= limit) return read;
        throw new PayloadTooLargeException(JsonBodyLimitMiddleware.TooLargeMessage);
    }

    // Read-only: there is nothing buffered to flush.
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
