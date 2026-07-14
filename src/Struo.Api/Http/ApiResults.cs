using Microsoft.AspNetCore.Mvc;

namespace Struo.Api.Http;

public static class ApiResults
{
    /// <summary>
    /// Builds an error-envelope result at the given status. It carries a fully-formed
    /// <see cref="ErrorEnvelope"/>, so the result filter leaves it untouched (idempotent).
    /// </summary>
    public static ObjectResult Fail(int status, string code, string message) =>
        new(Envelope.Error(code, message)) { StatusCode = status };
}
