// src/Struo.Api/Program.cs
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Serilog;
using SqlSugar;
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
        .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

    builder.Services.AddOpenApi();
    builder.Services.AddStruoInfrastructure(builder.Configuration);
    builder.Services.AddHealthChecks()
        .AddCheck<DbReadinessCheck>("database", tags: ["ready"]);

    var app = builder.Build();

    app.UseSerilogRequestLogging();

    app.MapControllers();
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
        options.WithTheme(ScalarTheme.Mars)
               .WithDefaultHttpClient(ScalarTarget.JavaScript, ScalarClient.Axios));

    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

    if (app.Environment.IsDevelopment())
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        DatabaseInitializer.InitializeDevelopmentSchema(db, app.Environment, typeof(Article));
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
