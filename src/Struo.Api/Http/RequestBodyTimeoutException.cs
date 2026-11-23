using Microsoft.AspNetCore.Http;
using Struo.Domain;

namespace Struo.Api.Http;

/// <summary>
/// A request body the server stopped waiting for: answered <c>408</c> with <c>BAD_USER_INPUT</c> and the
/// fixed message <see cref="JsonBodyLimitMiddleware.UnreadableBodyMessage"/>, which carries no exception text.
/// </summary>
internal sealed class RequestBodyTimeoutException()
    : StruoDomainException(ErrorCodes.BadUserInput, StatusCodes.Status408RequestTimeout,
        JsonBodyLimitMiddleware.UnreadableBodyMessage);
