using Microsoft.AspNetCore.Http;

namespace Struo.Api.Http;

/// <summary>Classifies HTTP status codes.</summary>
internal static class HttpStatusClass
{
    /// <summary>True for a 4xx status.</summary>
    internal static bool IsClientError(int status) =>
        status is >= StatusCodes.Status400BadRequest and < StatusCodes.Status500InternalServerError;
}
