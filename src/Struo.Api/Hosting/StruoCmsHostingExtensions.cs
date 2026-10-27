// src/Struo.Api/Hosting/StruoCmsHostingExtensions.cs
using System.Globalization;
using Serilog;

namespace Struo.Api.Hosting;

/// <summary>
/// The StruoCMS host composition. A fork's <c>Program.cs</c> calls <see cref="AddStruoCms"/> on the
/// builder, registers its own services after it, and calls <see cref="UseStruoCmsAsync"/> on the built
/// application before <c>RunAsync()</c>.
/// </summary>
public static class StruoCmsHostingExtensions
{
    /// <summary>
    /// Registers every core service, option, authentication scheme, rate limiter and the GraphQL schema.
    /// Registrations a fork may replace use <c>TryAdd*</c>, so the fork's own registration wins whether it
    /// is added before or after this call.
    /// </summary>
    public static WebApplicationBuilder AddStruoCms(
        this WebApplicationBuilder builder, Action<StruoCmsBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // A headless JSON API has no culture-formatted output of its own (outbound JSON is always
        // camelCase/invariant), but SqlSugar re-parses ConditionalModel.FieldValue back with
        // CultureInfo.CurrentCulture according to CSharpTypeName, while ConditionalModelTranslator renders
        // that same value with CultureInfo.InvariantCulture — so the process's default culture MUST be
        // invariant too, or the two disagree (e.g. a rendered "1.5" would re-parse as 15 under de-DE).
        // Set before any service is registered, so every thread the host later spins up inherits it as
        // its default.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        builder.Host.UseSerilog((context, services, configuration) =>
            configuration.ReadFrom.Configuration(context.Configuration).ReadFrom.Services(services));

        builder.Services.AddStruoMvc();
        var graphQl = builder.Services.AddStruoCoreServices(builder.Configuration, builder.Environment);
        builder.Services.AddStruoOptions();
        builder.Services.AddStruoRateLimiting();

        var cms = new StruoCmsBuilder();
        configure?.Invoke(cms);
        cms.ApplyGraphQl(graphQl);
        if (cms.HasGraphQlConfiguration)
        {
            graphQl.TryAddTypeInterceptor<RootFieldCollisionInterceptor>();
            builder.Services.AddSingleton<StruoCmsGraphQlMarker>();
        }
        return builder;
    }

    /// <summary>
    /// Builds the request pipeline, maps the endpoints, and runs the startup gate (migration gate,
    /// configuration warnings, SQL Server isolation check). Call it on the built application, before
    /// <c>RunAsync()</c>.
    /// </summary>
    public static async Task<WebApplication> UseStruoCmsAsync(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseStruoPipeline();
        app.MapStruoEndpoints();
        await app.RunStruoStartupChecksAsync();
        return app;
    }
}
