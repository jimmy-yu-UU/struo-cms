using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Struo.Domain.Query;

namespace Struo.Api.Http;

/// <summary>
/// The request body of the upload endpoint. The server's default body cap (30,000,000 bytes) would stop
/// an upload below <c>Struo:Files:MaxUploadBytes</c> once that key is raised above it, so the cap for
/// this endpoint alone is <c>MaxUploadBytes</c> plus <see cref="MultipartHeadroomBytes"/>. A body over the
/// cap is refused as <see cref="PayloadTooLargeException"/>, the same error and limit detail as an
/// over-limit file.
/// </summary>
internal static class UploadRequestBody
{
    /// <summary>
    /// What the multipart framing adds around the file bytes. A request with one file part, a
    /// 255-character file name and a <c>folderId</c> part measured 588 bytes of framing; 64 KiB leaves
    /// room for further parts and headers a client adds.
    /// </summary>
    internal const long MultipartHeadroomBytes = 64 * 1024;

    // ASP.NET reads a multipart section only up to FormOptions.MultipartBodyLengthLimit, 128 MiB by
    // default; a larger section fails the read with "Multipart body length limit exceeded" (a 500).
    private const long DefaultMultipartLimitBytes = 134_217_728;

    /// <summary>Reads the multipart form of an upload request under the upload's own body cap.</summary>
    internal static async Task<IFormCollection> ReadFormAsync(
        HttpRequest request, long maxUploadBytes, CancellationToken ct)
    {
        RaiseBodyLimit(request, maxUploadBytes);
        try
        {
            return await request.ReadFormAsync(ct);
        }
        catch (Exception e) when (IsBodyTooLarge(e))
        {
            throw PayloadTooLargeException.FileTooLarge(maxUploadBytes);
        }
    }

    /// <summary>Sets this request's body cap, and the multipart section cap when the upload limit exceeds the default.</summary>
    internal static void RaiseBodyLimit(HttpRequest request, long maxUploadBytes)
    {
        var cap = maxUploadBytes + MultipartHeadroomBytes;
        var feature = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = cap;

        if (cap > DefaultMultipartLimitBytes)
        {
            var options = new FormOptions { MultipartBodyLengthLimit = cap };
            request.HttpContext.Features.Set<IFormFeature>(new FormFeature(request, options));
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
