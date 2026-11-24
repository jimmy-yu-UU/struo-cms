using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Struo.Api.Http;

/// <summary>
/// The one log entry of a request body the server failed to read and answered with a 4xx. A 413 is the size
/// cap, not a failed read, and is not logged here. The entry carries the exception and the status, and
/// nothing of the request.
/// </summary>
internal static class RequestBodyReadLog
{
    private const string Template = "The request body could not be read (status {StatusCode}).";

    /// <summary>Writes the Warning for <paramref name="rejected"/> when it is a 4xx other than 413.</summary>
    internal static void Rejected(ILogger? logger, BadHttpRequestException rejected)
    {
        if (!HttpStatusClass.IsClientError(rejected.StatusCode)
            || rejected.StatusCode == StatusCodes.Status413PayloadTooLarge)
            return;
        if (logger?.IsEnabled(LogLevel.Warning) == true)
            logger.LogWarning(rejected, Template, rejected.StatusCode);
    }
}
