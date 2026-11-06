using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Struo.Application.Configuration;
using Struo.Domain.Query;

namespace Struo.Api.Http;

/// <summary>
/// Caps the body of JSON requests (and anything posted to <c>/graphql</c>) at
/// <see cref="ApiOptions.MaxJsonBodyBytes"/>. A declared length over the cap is rejected before the body
/// is read; an undeclared (chunked) body is cut off by a counting stream once it passes the cap.
/// Both surface as <see cref="PayloadTooLargeException"/>, which the exception handler answers with the
/// 413 error envelope. Multipart uploads are not JSON and keep <c>Struo:Files:MaxUploadBytes</c>.
/// </summary>
public sealed class JsonBodyLimitMiddleware(RequestDelegate next, IOptions<ApiOptions> options)
{
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
            if (!isGraphQl) throw new PayloadTooLargeException(Message(_limit));
            await WriteGraphQlErrorAsync(context);
            return;
        }

        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = _limit;

        var body = new LimitedReadStream(context.Request.Body, _limit);
        context.Request.Body = body;
        // GraphQL catches the read failure and answers 400 with a GraphQL error body; the status is the
        // only part that has to change to match the declared-length case.
        if (isGraphQl)
            context.Response.OnStarting(() =>
            {
                if (body.Exceeded) context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                return Task.CompletedTask;
            });
        await next(context);
    }

    // /graphql reports transport errors as { errors: [...] }, not as the REST envelope.
    private Task WriteGraphQlErrorAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        var error = new Dictionary<string, object?>
        {
            ["message"] = Message(_limit),
            ["extensions"] = new Dictionary<string, object?>
            {
                ["code"] = ErrorCodes.PayloadTooLarge,
                ["traceId"] = RequestTraceId.For(context),
            },
        };
        return context.Response.WriteAsJsonAsync(
            new Dictionary<string, object?> { ["errors"] = new[] { error } }, context.RequestAborted);
    }

    internal static string Message(long limit) =>
        $"Request body exceeds the maximum of {limit} bytes for JSON requests.";

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
/// been read, and flags <see cref="Exceeded"/>. Kestrel, when it enforces the same limit through
/// <c>IHttpMaxRequestBodySizeFeature</c>, throws its own 413 first; that one is flagged and rethrown, so
/// the feature stays as a guard and <see cref="Exceeded"/> is set either way.
/// </summary>
public sealed class LimitedReadStream(Stream inner, long limit) : Stream
{
    private long _read;

    public bool Exceeded { get; private set; }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        try
        {
            return Count(inner.Read(buffer, offset, count));
        }
        catch (BadHttpRequestException e) when (IsTooLarge(e))
        {
            Exceeded = true;
            throw;
        }
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        await ReadAsync(buffer.AsMemory(offset, count), ct);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        try
        {
            return Count(await inner.ReadAsync(buffer, ct));
        }
        catch (BadHttpRequestException e) when (IsTooLarge(e))
        {
            Exceeded = true;
            throw;
        }
    }

    private static bool IsTooLarge(BadHttpRequestException e) =>
        e.StatusCode == StatusCodes.Status413PayloadTooLarge;

    private int Count(int read)
    {
        _read += read;
        if (_read <= limit) return read;
        Exceeded = true;
        throw new PayloadTooLargeException(JsonBodyLimitMiddleware.Message(limit));
    }

    // Read-only: there is nothing buffered to flush.
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
