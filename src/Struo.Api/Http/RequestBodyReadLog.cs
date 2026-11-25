using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Struo.Api.Http;

/// <summary>
/// The one log entry of a request body that could not be read. A 4xx the server put on the read is a Warning
/// with the exception and the status; a 413 is the size cap, not a failed read, and is not logged here. A
/// failure of a GraphQL body read that is not the client's is an Error with the exception. No entry carries
/// anything of the request.
/// </summary>
internal static class RequestBodyReadLog
{
    private const string Template = "The request body could not be read (status {StatusCode}).";
    private const string GraphQlTemplate = "The GraphQL request body could not be read.";

    /// <summary>Writes the Warning for a 4xx <paramref name="rejected"/> other than 413.</summary>
    internal static void Rejected(ILogger? logger, BadHttpRequestException rejected)
    {
        if (rejected.StatusCode == StatusCodes.Status413PayloadTooLarge) return;
        if (logger?.IsEnabled(LogLevel.Warning) == true)
            logger.LogWarning(rejected, Template, rejected.StatusCode);
    }

    /// <summary>Writes the Error for a GraphQL body read that failed for a reason that is not the client's.</summary>
    internal static void GraphQlFailed(ILogger logger, Exception failure) =>
        logger.LogError(failure, GraphQlTemplate);
}
