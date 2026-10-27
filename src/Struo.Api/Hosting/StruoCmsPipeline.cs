// src/Struo.Api/Hosting/StruoCmsPipeline.cs
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Serilog;
using Struo.Api.Auth;
using Struo.Api.GraphQl;
using Struo.Api.Http;

namespace Struo.Api.Hosting;

/// <summary>The middleware chain and endpoint mapping behind <c>UseStruoCmsAsync</c>.</summary>
internal static class StruoCmsPipeline
{
    internal static void UseStruoPipeline(this WebApplication app)
    {
        // First in the pipeline, ahead of everything else, so an error response, a CORS preflight, or a
        // bare 404 carries this header just as much as a normal 200 — none of those short-circuit through
        // a later stage. Registered via Response.OnStarting (fires right before headers are written, the
        // latest point at which "is it already set" can be answered) and TryAdd (only if absent), so any
        // downstream handler that sets this header itself still wins. This is the one security response
        // header the application sends: Struo.Api serves no HTML of its own (no wwwroot/UseStaticFiles/
        // MapFallbackToFile — the admin SPA is deployed separately), so nosniff is defence in depth behind
        // the file-download endpoint's Content-Disposition: attachment and the image-transform path's
        // fixed raster content types, not a patch for an open hole. Strict-Transport-Security,
        // X-Frame-Options/CSP frame-ancestors, and Referrer-Policy are left to the reverse proxy — see
        // docs/guide/en/20-deployment.md, section "## Production checklist".
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
                return Task.CompletedTask;
            });
            await next();
        });

        // Rewrites the client address, scheme and host from X-Forwarded-* when ForwardedHeaders:Enabled, so it
        // sits ahead of request logging, authentication, session rotation and the per-IP rate limiter.
        app.UseStruoForwardedHeaders();
        app.UseSerilogRequestLogging();
        app.UseStruoCors(app.Configuration);
        // Must sit ahead of UseAuthentication: UseExceptionHandler only catches exceptions thrown
        // DOWNSTREAM of its own position, so while it was registered after authentication/authorization,
        // an infrastructure failure INSIDE the authentication stage (an unreachable Redis ticket store,
        // BearerTokenAuthenticationHandler's credential lookup failing) escaped StruoExceptionHandler and
        // went out as a bare 500 with an empty body — breaking the "envelope wraps every REST response"
        // invariant and skipping the mask-and-log treatment of internal detail. Guarded by
        // AuthenticationFailureEnvelopeTests. It stays INSIDE Serilog request logging (so the handled 500
        // is what gets logged) and INSIDE CORS (so the error response still carries CORS headers).
        app.UseExceptionHandler();
        app.UseMiddleware<Struo.Api.Auth.SessionRotationMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<Struo.Api.Auth.CsrfProtectionMiddleware>();
        // Health probes never touch the role/permission store, so a database blip cannot fail liveness.
        app.UseWhen(
            ctx => !ctx.Request.Path.StartsWithSegments("/health"),
            branch => branch.UseMiddleware<Struo.Api.Auth.PermissionResolutionMiddleware>());
        app.UseRateLimiter();
    }

    internal static void MapStruoEndpoints(this WebApplication app)
    {
        app.MapControllers();
        app.MapStruoGraphQl();

        // The OpenAPI document and Scalar explorer publish the full API surface without authentication,
        // so they are mapped in Development only unless OpenApi:Enabled says otherwise.
        if (Struo.Api.EnvironmentGates.ResolveOpenApiEnabled(app.Configuration, app.Environment))
        {
            app.MapOpenApi();
            app.MapScalarApiReference(options =>
                options.WithTheme(ScalarTheme.Mars)
                       .WithDefaultHttpClient(ScalarTarget.JavaScript, ScalarClient.Axios));
        }

        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
    }
}
