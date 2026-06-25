// src/Struo.Api/Program.cs
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Serilog;
using SqlSugar;
using Struo.Application.Metadata;
using Struo.Infrastructure.DependencyInjection;
using Struo.Infrastructure.Health;
using Struo.Infrastructure.Persistence;
using Struo.Sample.Blog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) =>
        configuration.ReadFrom.Configuration(context.Configuration).ReadFrom.Services(services));

    builder.Services
        .AddControllers()
        .AddJsonOptions(o =>
        {
            o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            o.JsonSerializerOptions.Converters.Add(
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        });

    builder.Services.AddOpenApi();
    builder.Services.AddStruoInfrastructure(builder.Configuration);
    builder.Services.AddStruoMetadata(typeof(Article).Assembly);
    builder.Services.AddStruoData(builder.Configuration);
    builder.Services.AddScoped<SchemaService>();
    builder.Services.AddHealthChecks()
        .AddCheck<DbReadinessCheck>("database", tags: ["ready"]);

    var app = builder.Build();

    app.UseSerilogRequestLogging();

    app.Use(async (context, next) =>
    {
        try { await next(); }
        catch (Struo.Domain.Query.CollectionNotFoundException ex)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new { error = new { message = ex.Message } });
        }
        catch (Struo.Domain.Query.QueryException ex)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = new { message = ex.Message } });
        }
    });

    app.MapControllers();

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
        DatabaseInitializer.InitializeDevelopmentSchema(db, app.Environment, typeof(Article), typeof(Tag));
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
