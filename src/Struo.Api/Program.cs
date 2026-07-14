// src/Struo.Api/Program.cs
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Serilog;
using SqlSugar;
using Struo.Api.Auth;
using Struo.Api.GraphQl;
using Struo.Application.Metadata;
using Struo.Infrastructure.DependencyInjection;
using Struo.Infrastructure.Health;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Persistence;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) =>
        configuration.ReadFrom.Configuration(context.Configuration).ReadFrom.Services(services));

    builder.Services
        .AddControllers(o => o.Filters.Add<Struo.Api.Http.EnvelopeResultFilter>())
        .AddJsonOptions(o =>
        {
            o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            o.JsonSerializerOptions.Converters.Add(
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        });

    builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(o =>
    {
        o.InvalidModelStateResponseFactory = ctx =>
        {
            var details = ctx.ModelState
                .Where(kv => kv.Value is { Errors.Count: > 0 })
                .SelectMany(kv => kv.Value!.Errors.Select(e =>
                    new Struo.Api.Http.ValidationDetail(
                        kv.Key,
                        string.IsNullOrEmpty(e.ErrorMessage) ? "Invalid value." : e.ErrorMessage)))
                .ToList();
            return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(
                Struo.Api.Http.Envelope.Error(Struo.Api.Http.ErrorCodes.Validation,
                    "One or more validation errors occurred.", details));
        };
    });

    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<Struo.Api.Http.StruoExceptionHandler>();

    builder.Services.AddOpenApi();
    builder.Services.AddStruoInfrastructure(builder.Configuration);
    builder.Services.AddStruoMetadata(builder.Configuration, typeof(Program).Assembly);
    builder.Services.AddStruoData(builder.Configuration);
    builder.Services.AddStruoGraphQl(builder.Environment);
    builder.Services.AddStruoFiles(builder.Configuration);
    builder.Services.AddStruoAuth(builder.Configuration, builder.Environment);
    builder.Services.AddStruoCors(builder.Configuration);
    builder.Services.AddStruoOidc(builder.Configuration);
    builder.Services.AddOptions<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(AuthSchemes.Cookie)
        .PostConfigure<DistributedCacheTicketStore>((options, store) => options.SessionStore = store);
    builder.Services.AddScoped<SchemaService>();
    builder.Services.AddHealthChecks()
        .AddCheck<DbReadinessCheck>("database", tags: ["ready"])
        .AddCheck<Struo.Infrastructure.Health.CacheReadinessCheck>("cache", tags: ["ready"]);

    var app = builder.Build();

    app.UseSerilogRequestLogging();
    app.UseStruoCors(app.Configuration);
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseExceptionHandler();
    app.UseMiddleware<Struo.Api.Auth.CsrfProtectionMiddleware>();
    app.UseMiddleware<Struo.Api.Auth.PermissionResolutionMiddleware>();

    app.MapControllers();
    app.MapStruoGraphQl();

    // API schema + interactive explorer are exposed in non-production only.
    // Production exposure would publish the full API surface unauthenticated;
    // revisit once authn/authz lands (Phase 6) if prod docs are desired.
    if (!app.Environment.IsProduction())
    {
        app.MapOpenApi();
        app.MapScalarApiReference(options =>
            options.WithTheme(ScalarTheme.Mars)
                   .WithDefaultHttpClient(ScalarTarget.JavaScript, ScalarClient.Axios));
    }

    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

    if (app.Environment.IsDevelopment())
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var entityTypes = scope.ServiceProvider
            .GetRequiredService<Struo.Application.Metadata.IEntityTypeCollector>()
            .CollectForInitTables();
        DatabaseInitializer.InitializeDevelopmentSchema(db, app.Environment, entityTypes.ToArray());
        await Struo.Infrastructure.Localization.LanguageSeeder.SeedAsync(db);
        var hasher = scope.ServiceProvider.GetRequiredService<Struo.Application.Security.IPasswordHasher>();
        await Struo.Infrastructure.Identity.AdminUserSeeder.SeedAsync(db, hasher,
            builder.Configuration["Auth:BootstrapAdmin:Email"],
            builder.Configuration["Auth:BootstrapAdmin:Password"]);
        await Struo.Infrastructure.Identity.RbacSeeder.SeedAsync(db,
            builder.Configuration["Auth:BootstrapAdmin:Email"],
            builder.Configuration.GetSection("Rbac:PublicReadCollections").Get<string[]>() ?? []);
    }

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "StruoCMS host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;
