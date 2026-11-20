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
/// this endpoint alone is <c>MaxUploadBytes</c> plus <see cref="FileStorageOptions.UploadFramingHeadroomBytes"/>. A body over the
/// cap is refused as <see cref="PayloadTooLargeException"/>, the same error and limit detail as an
/// over-limit file; a form that cannot be read is a 400 with a fixed message.
/// </summary>
internal static class UploadRequestBody
{
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

            // Kestrel's own rejection (a body timeout, say) keeps its failure, and so does a disk fault
            // on the server while the form is buffered; the exception handler answers both.
            if (e is BadHttpRequestException || IsDiskFault(e))
                throw;

            logger.LogWarning(e, "The upload request form could not be read.");
            throw new QueryException(UnreadableFormMessage);
        }
    }

    /// <summary>
    /// Sets this request's body cap. The multipart section limit, <c>FormOptions.MultipartBodyLengthLimit</c>,
    /// is raised to it too when the configured one is lower, because ASP.NET reads a multipart section
    /// only up to that limit.
    /// </summary>
    internal static void RaiseBodyLimit(HttpRequest request, long maxUploadBytes, ILogger logger)
    {
        var cap = maxUploadBytes + FileStorageOptions.UploadFramingHeadroomBytes;
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

    // The disk of the server is full or over quota: ENOSPC and EDQUOT on Linux, which .NET puts in the
    // HResult of the IOException, and the Windows disk-full errors as HRESULTs.
    private static bool IsDiskFault(Exception exception) =>
        exception.HResult is 28 or 122 or unchecked((int)0x80070070) or unchecked((int)0x80070027);

    // Kestrel's 413 reaches the caller either directly or wrapped by the form reader.
    private static bool IsBodyTooLarge(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
            if (e is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge })
                return true;
        return false;
    }
}
