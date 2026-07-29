using System.Text.Json;

namespace Struo.Api.Http;

/// <summary>
/// The camelCase <see cref="JsonSerializerOptions"/> used whenever an error envelope
/// is written directly via <c>HttpResponse.WriteAsJsonAsync</c> outside the MVC pipeline (so
/// <c>EnvelopeResultFilter</c>/the MVC <c>JsonOptions</c> never get a chance to apply). Without this,
/// <c>WriteAsJsonAsync</c> falls back to <c>Microsoft.AspNetCore.Http.Json.JsonOptions</c> — a
/// separate registration from the MVC options configured in Program.cs — which is not camelCase.
/// Shared by <see cref="AuthWiring"/> (cookie-auth challenge/forbid events) and
/// <see cref="CsrfProtectionMiddleware"/> (pre-MVC 403) so the two call sites cannot drift apart.
/// </summary>
internal static class EnvelopeJsonOptionsHolder
{
    public static readonly JsonSerializerOptions Instance = new(JsonSerializerDefaults.Web);
}
