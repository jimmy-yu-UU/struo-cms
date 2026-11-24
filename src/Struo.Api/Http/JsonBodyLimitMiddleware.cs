using System.Buffers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Struo.Application.Configuration;
using Struo.Domain.Query;

namespace Struo.Api.Http;

/// <summary>
/// Caps the body of JSON requests (and anything posted to <c>/graphql</c>) at
/// <see cref="ApiOptions.MaxJsonBodyBytes"/>. A declared length over the cap is rejected before the body
/// is read: a REST request throws <see cref="PayloadTooLargeException"/>, which the exception handler
/// answers with the 413 error envelope, and a GraphQL request is answered here with a 413
/// <c>PAYLOAD_TOO_LARGE</c> GraphQL error. An undeclared (chunked) REST body is cut off by a counting stream
/// once it passes the cap and surfaces as the same exception. A GraphQL body is read up to the cap before
/// GraphQL sees it: one that passes the cap is answered 413 <c>PAYLOAD_TOO_LARGE</c>, and one whose read
/// fails with a 4xx from the server (such as a timeout) is answered with that status and
/// <c>BAD_USER_INPUT</c> and logged once as a Warning by <see cref="RequestBodyReadLog"/>. Any request to
/// <c>/graphql</c> whose media type is <c>multipart/form-data</c> is answered 415 <c>BAD_USER_INPUT</c>
/// (<see cref="MultipartMessage"/>) before its size is checked and before its body is read. A GET to
/// <c>/graphql</c> whose <c>variables</c> or <c>extensions</c> query-string value fails
/// <see cref="GraphQlQueryStringCheck"/> is answered 400 <c>BAD_USER_INPUT</c>. Every 413 carries
/// <see cref="TooLargeMessage"/>, which names no limit. Multipart uploads to the REST upload endpoint
/// are not JSON and keep <c>Struo:Files:MaxUploadBytes</c>.
/// </summary>
public sealed class JsonBodyLimitMiddleware(
    RequestDelegate next, IOptions<ApiOptions> options, ILogger<JsonBodyLimitMiddleware> logger)
{
    /// <summary>The message of every over-limit request body, JSON or Kestrel's own.</summary>
    internal const string TooLargeMessage = "Request body is too large.";

    /// <summary>The message of a request body that could not be read, such as one the server timed out on.</summary>
    internal const string UnreadableBodyMessage = "The request body could not be read.";

    /// <summary>The message of the 415 for a multipart request to <c>/graphql</c>.</summary>
    internal const string MultipartMessage =
        "GraphQL does not accept multipart requests; upload a file through POST /api/files.";

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
        if (isGraphQl && await RefuseGraphQlRequestShapeAsync(context)) return;

        if (context.Request.ContentLength > _limit)
        {
            if (!isGraphQl) throw new PayloadTooLargeException(TooLargeMessage);
            await WriteGraphQlErrorAsync(context);
            return;
        }

        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = _limit;

        if (!isGraphQl)
            context.Request.Body = new LimitedReadStream(context.Request.Body, _limit);
        else if (!await BufferGraphQlBodyAsync(context))
            return;
        await next(context);
    }

    // A multipart request is refused for its type before its size or its body matters; a GET whose query-string
    // values GraphQL cannot accept is refused next. Neither is a failed read, so neither is logged.
    private static async Task<bool> RefuseGraphQlRequestShapeAsync(HttpContext context)
    {
        if (IsMultipartFormData(context.Request.ContentType))
        {
            await WriteGraphQlErrorAsync(context, StatusCodes.Status415UnsupportedMediaType,
                ErrorCodes.BadUserInput, MultipartMessage);
            return true;
        }
        if (GraphQlQueryStringCheck.Problem(context.Request) is not { } problem) return false;
        await WriteGraphQlErrorAsync(context, StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput, problem);
        return true;
    }

    // The body is read here, up to the cap, before GraphQL sees it, so a failed read is answered by this
    // middleware. A declared length over the cap was refused before this point and the read loop in
    // ReadWithinLimitAsync stops at the cap, so the buffer never holds more than the cap.
    private async Task<bool> BufferGraphQlBodyAsync(HttpContext context)
    {
        if (!CanHaveBody(context)) return true;
        var (buffered, failureStatus) = await ReadWithinLimitAsync(context);
        if (buffered is null)
        {
            await WriteGraphQlErrorAsync(context, failureStatus);
            return false;
        }
        context.Request.Body = buffered;
        return true;
    }

    // A GET or HEAD, or a request the server already knows has no body, is passed on unread.
    private static bool CanHaveBody(HttpContext context) =>
        context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody
            ?? !(HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method));

    // The whole body, or the HTTP status of why it could not be read: 413 once it passes the cap
    // (including Kestrel's own 413 for the same cap), or the 4xx status the server put on a failed read.
    // A failure the server did not call a client error is not caught here.
    private async Task<(MemoryStream? Body, int FailureStatus)> ReadWithinLimitAsync(HttpContext context)
    {
        var buffer = new MemoryStream();
        var chunk = ArrayPool<byte>.Shared.Rent(BufferChunkBytes);
        try
        {
            int read;
            while ((read = await context.Request.Body.ReadAsync(chunk, context.RequestAborted)) > 0)
            {
                if (buffer.Length + read > _limit) return (null, StatusCodes.Status413PayloadTooLarge);
                await buffer.WriteAsync(chunk.AsMemory(0, read), context.RequestAborted);
            }
        }
        catch (BadHttpRequestException e) when (HttpStatusClass.IsClientError(e.StatusCode))
        {
            RequestBodyReadLog.Rejected(logger, e);
            return (null, e.StatusCode);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }
        buffer.Position = 0;
        return (buffer, 0);
    }

    // /graphql reports transport errors as { errors: [...] }, not as the REST envelope.
    private static Task WriteGraphQlErrorAsync(HttpContext context, int status = StatusCodes.Status413PayloadTooLarge)
    {
        var tooLarge = status == StatusCodes.Status413PayloadTooLarge;
        return WriteGraphQlErrorAsync(context, status,
            tooLarge ? ErrorCodes.PayloadTooLarge : ErrorCodes.BadUserInput,
            tooLarge ? TooLargeMessage : UnreadableBodyMessage);
    }

    private static Task WriteGraphQlErrorAsync(HttpContext context, int status, string code, string message)
    {
        context.Response.StatusCode = status;
        var error = new Dictionary<string, object?>
        {
            ["message"] = message,
            ["extensions"] = new Dictionary<string, object?>
            {
                ["code"] = code,
                ["traceId"] = RequestTraceId.For(context),
            },
        };
        return context.Response.WriteAsJsonAsync(
            new Dictionary<string, object?> { ["errors"] = new[] { error } }, context.RequestAborted);
    }

    internal static bool IsCapped(HttpRequest request) => IsGraphQl(request) || IsJson(request.ContentType);

    private static bool IsGraphQl(HttpRequest request) =>
        request.Path.StartsWithSegments("/graphql", StringComparison.OrdinalIgnoreCase);

    private static string MediaTypeOf(string? contentType) => (contentType ?? "").Split(';', 2)[0].Trim();

    private static bool IsMultipartFormData(string? contentType) =>
        MediaTypeOf(contentType).Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase);

    private static bool IsJson(string? contentType)
    {
        var mediaType = MediaTypeOf(contentType);
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
