using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Struo.Api.Http;

/// <summary>
/// The id a client can quote to find one request in the server log. It is the request's trace id, which
/// is the id Serilog stamps on every log event written during that request, so the response and the log
/// agree. The HTTP trace identifier is the fallback when no activity is running.
/// </summary>
public static class RequestTraceId
{
    public static string? For(HttpContext? context)
    {
        var activity = Activity.Current;
        if (activity is not null && activity.TraceId != default)
            return activity.TraceId.ToHexString();
        return context?.TraceIdentifier;
    }
}
