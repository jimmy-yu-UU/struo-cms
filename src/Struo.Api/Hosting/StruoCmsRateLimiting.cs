// src/Struo.Api/Hosting/StruoCmsRateLimiting.cs
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Struo.Api.Hosting;

/// <summary>The rate limiters behind <c>AddStruoCms</c>: the "login" and "password" policies and the global login-concurrency cap.</summary>
internal static class StruoCmsRateLimiting
{
    // App-layer login rate limiter — fixed-window, partitioned by client IP, applied ONLY to
    // POST /api/auth/login via [EnableRateLimiting("login")] on the action. Deliberately NOT a
    // global limiter: every anonymous login attempt burns full Argon2id CPU (timing-equalized by
    // design), making it a DoS amplifier if left unbounded, whereas the rest of the API is not.
    // Volumetric/global throttling is a web-server-edge concern, out of scope here.
    // Ships DISABLED by default (RateLimiting:Login:Enabled = false, see LoginRateLimitOptions):
    // partitioning by client IP collapses a whole office sharing one NAT egress IP into a single
    // bucket, which is a self-inflicted availability problem, not a defense, for the typical
    // admin-backend deployment this template ships for. AuthController.Login's separate
    // ILoginAttemptThrottle (per-account, not per-IP) is the layer that ships enabled instead — it
    // cannot see the request body at THIS layer (the rate limiter's partitioner below runs before
    // model binding), which is exactly why it belongs in the controller rather than here.
    internal static void AddStruoRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(rateLimiterOptions =>
        {
            rateLimiterOptions.OnRejected = WriteRejection;
            rateLimiterOptions.GlobalLimiter = CreateLoginConcurrencyLimiter();
            rateLimiterOptions.AddPolicy("login", LoginPartition);
            rateLimiterOptions.AddPolicy("password", PasswordPartition);
        });
    }

    private static async ValueTask WriteRejection(OnRejectedContext context, CancellationToken ct)
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        // OnRejected is global across every policy, so the copy must come from the policy that
        // actually rejected — a 429 from the change-password endpoint saying "login attempts" is
        // simply wrong. PolicyName comes off the endpoint's [EnableRateLimiting] metadata.
        var policyName = context.HttpContext.GetEndpoint()
            ?.Metadata.GetMetadata<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()
            ?.PolicyName;
        // A concurrency rejection carries no retry hint, so a slot is retried after one second.
        var retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? (int)Math.Ceiling(retryAfter.TotalSeconds)
            : 1;
        context.HttpContext.Response.Headers.RetryAfter =
            retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        var message = policyName switch
        {
            "login" => "Too many login attempts. Please try again later.",
            "password" => "Too many password change attempts. Please try again later.",
            _ => "Too many requests. Please try again later.",
        };
        await context.HttpContext.Response.WriteAsJsonAsync(
            Struo.Api.Http.Envelope.Error(Struo.Api.Http.ErrorCodes.TooManyRequests, message),
            cancellationToken: ct);
    }

    // Caps concurrent logins process-wide: the "login" endpoint shares one concurrency partition,
    // every other request is unlimited here.
    private static PartitionedRateLimiter<HttpContext> CreateLoginConcurrencyLimiter() =>
        PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        {
            var policyName = httpContext.GetEndpoint()
                ?.Metadata.GetMetadata<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()
                ?.PolicyName;
            var concurrency = httpContext.RequestServices
                .GetRequiredService<IOptions<Struo.Application.Configuration.LoginConcurrencyOptions>>().Value;
            if (policyName != "login" || !concurrency.Enabled)
                return RateLimitPartition.GetNoLimiter("none");

            return RateLimitPartition.GetConcurrencyLimiter("login-concurrency", _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = concurrency.PermitLimit,
                QueueLimit = concurrency.QueueLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            });
        });

    private static RateLimitPartition<string> LoginPartition(HttpContext httpContext)
    {
        var loginOptions = httpContext.RequestServices
            .GetRequiredService<IOptions<Struo.Application.Configuration.LoginRateLimitOptions>>().Value;

        // Partition key = client IP. Behind a reverse proxy, RemoteIpAddress is the proxy's own
        // address unless ForwardedHeaders:Enabled trusts that proxy (see UseStruoForwardedHeaders);
        // otherwise every login attempt through it shares a single partition/bucket.
        var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // RateLimiting:Login:Enabled toggle (config-driven, see LoginRateLimitOptions): when
        // disabled, return a no-op limiter for this partition. The "login" policy still EXISTS
        // (so [EnableRateLimiting("login")] never throws "no policy named login"); it simply
        // never rejects. Disabled is the shipped default for every topology, not a multi-pod
        // special case: the primary reason is shared NAT egress IP (see the AddStruoRateLimiting
        // comment above / LoginRateLimitOptions.Enabled's XML doc), which collapses an office's
        // logins into one partition on a single instance just as much as on many pods. A second,
        // independent reason applies specifically to multiple replicas: this limiter's state is
        // in-memory and per-pod, so even if enabled it can never enforce a true global limit
        // across them — that's delegated to the ingress/edge/WAF instead, which sees the real
        // client IP and sits in front of every pod.
        if (!loginOptions.Enabled)
        {
            return RateLimitPartition.GetNoLimiter<string>(partitionKey);
        }

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = loginOptions.PermitLimit,
            Window = TimeSpan.FromSeconds(loginOptions.WindowSeconds),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });
    }

    private static RateLimitPartition<string> PasswordPartition(HttpContext httpContext)
    {
        var passwordOptions = httpContext.RequestServices
            .GetRequiredService<IOptions<Struo.Application.Configuration.PasswordRateLimitOptions>>().Value;

        // Partition key = the AUTHENTICATED user id. UseAuthentication runs before
        // UseRateLimiter (see StruoCmsPipeline), so the principal is already resolved
        // here. Falling back to the client IP when it is somehow absent is deliberate: the
        // endpoint is [Authorize]d so this should be unreachable, and a bounded-by-IP partition
        // is a safer failure than an unbounded one.
        var currentUser = httpContext.RequestServices
            .GetRequiredService<Struo.Application.Abstractions.ICurrentUserAccessor>();
        var partitionKey = currentUser.GetCurrentUserId()?.ToString()
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";

        if (!passwordOptions.Enabled)
        {
            return RateLimitPartition.GetNoLimiter<string>(partitionKey);
        }

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = passwordOptions.PermitLimit,
            Window = TimeSpan.FromSeconds(passwordOptions.WindowSeconds),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });
    }
}
