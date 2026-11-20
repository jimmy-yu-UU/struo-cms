using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Struo.Application.Files;
using Struo.Domain.Query;

namespace Struo.Api.Http;

/// <summary>
/// The request body of the upload endpoint. The server's default body cap (30,000,000 bytes) would stop
/// an upload below <c>Struo:Files:MaxUploadBytes</c> once that key is raised above it, so the cap for
/// this endpoint alone is <c>MaxUploadBytes</c> plus <see cref="MultipartHeadroomBytes"/>. A body over the
/// cap is refused as <see cref="PayloadTooLargeException"/>, the same error and limit detail as an
/// over-limit file; a form that cannot be read is a 400 with a fixed message.
/// </summary>
internal static class UploadRequestBody
{
    /// <summary>
    /// What the multipart framing adds around the file bytes. A request with one file part, a
    /// 255-character file name and a <c>folderId</c> part measured 588 bytes of framing; 64 KiB leaves
    /// room for further parts and headers a client adds.
    /// </summary>
    internal const long MultipartHeadroomBytes = FileStorageOptions.UploadFramingHeadroomBytes;

    /// <summary>The message of the 400 for a request form that cannot be read; it carries no exception text.</summary>
    internal const string UnreadableFormMessage = "The request form could not be read.";

    /// <summary>Reads the multipart form of an upload request under the upload's own body cap.</summary>
    internal static async Task<IFormCollection> ReadFormAsync(
        HttpRequest request, long maxUploadBytes, ILogger logger, CancellationToken ct)
    {
        RaiseBodyLimit(request, maxUploadBytes, logger);
        try
        {
            return await request.ReadFormAsync(ct);
        }
        catch (Exception e) when (IsBodyTooLarge(e))
        {
            throw PayloadTooLargeException.FileTooLarge(maxUploadBytes);
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            // A client that left mid-upload is not a bad request: it is a cancellation, which the
            // framework logs as an aborted request and does not try to answer.
            request.HttpContext.RequestAborted.ThrowIfCancellationRequested();
            throw new QueryException(UnreadableFormMessage);
        }
    }

    /// <summary>
    /// Sets this request's body cap. The multipart section limit, <c>FormOptions.MultipartBodyLengthLimit</c>,
    /// is raised too when the configured one is below the cap: ASP.NET reads a multipart section only up
    /// to it, and a larger section fails the read ("Multipart body length limit exceeded", a 500).
    /// </summary>
    internal static void RaiseBodyLimit(HttpRequest request, long maxUploadBytes, ILogger logger)
    {
        var cap = maxUploadBytes + MultipartHeadroomBytes;
        var feature = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false })
            feature.MaxRequestBodySize = cap;
        else if (feature is not null)
            logger.LogWarning(
                "The request body size limit cannot be changed for this upload, so Struo:Files:MaxUploadBytes does not raise it.");

        var configured = request.HttpContext.RequestServices?.GetService<IOptions<FormOptions>>()?.Value ?? new FormOptions();
        if (cap > configured.MultipartBodyLengthLimit)
        {
            var raised = new FormOptions
            {
                BufferBody = configured.BufferBody,
                MemoryBufferThreshold = configured.MemoryBufferThreshold,
                BufferBodyLengthLimit = configured.BufferBodyLengthLimit,
                ValueCountLimit = configured.ValueCountLimit,
                KeyLengthLimit = configured.KeyLengthLimit,
                ValueLengthLimit = configured.ValueLengthLimit,
                MultipartBoundaryLengthLimit = configured.MultipartBoundaryLengthLimit,
                MultipartHeadersCountLimit = configured.MultipartHeadersCountLimit,
                MultipartHeadersLengthLimit = configured.MultipartHeadersLengthLimit,
                MultipartBodyLengthLimit = cap,
            };
            request.HttpContext.Features.Set<IFormFeature>(new FormFeature(request, raised));
        }
    }

    // Kestrel's 413 reaches the caller either directly or wrapped by the form reader.
    private static bool IsBodyTooLarge(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
            if (e is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge })
                return true;
        return false;
    }
}
