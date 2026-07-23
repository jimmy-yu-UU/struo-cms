// src/Struo.Api/Program.cs
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
        o.SuppressMapClientErrors = true;
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
    builder.Services.AddStruoInfrastructure();
    builder.Services.AddStruoMetadata(builder.Configuration, typeof(Program).Assembly);
    builder.Services.AddStruoData();
    builder.Services.AddStruoGraphQl(builder.Environment);
    builder.Services.AddStruoFiles();
    builder.Services.AddStruoAuth(builder.Configuration, builder.Environment);
    builder.Services.AddStruoCors(builder.Configuration);
    builder.Services.AddStruoOidc(builder.Configuration);
    builder.Services.AddOptions<Struo.Application.Configuration.BrandingOptions>()
        .BindConfiguration(Struo.Application.Configuration.BrandingOptions.SectionName);
    // SEC-7: config-bound tuning for the login rate limiter below (defaults: 5 attempts / 60s).
    builder.Services.AddOptions<Struo.Application.Configuration.LoginRateLimitOptions>()
        .BindConfiguration(Struo.Application.Configuration.LoginRateLimitOptions.SectionName);
    builder.Services.AddOptions<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(AuthSchemes.Cookie)
        .PostConfigure<DistributedCacheTicketStore>((options, store) => options.SessionStore = store);
    builder.Services.AddScoped<SchemaService>();
    // SEC-7: /api/config is anonymous and previously hit the DB on every request; cached with a
    // short TTL (ConfigController) and evicted immediately on a branding save (SettingsController).
    builder.Services.AddMemoryCache();
    builder.Services.AddHealthChecks()
        .AddCheck<DbReadinessCheck>("database", tags: ["ready"])
        .AddCheck<Struo.Infrastructure.Health.CacheReadinessCheck>("cache", tags: ["ready"]);

    // SEC-7: app-layer login rate limiter — fixed-window, partitioned by client IP, applied ONLY to
    // POST /api/auth/login via [EnableRateLimiting("login")] on the action. Deliberately NOT a
    // global limiter: every anonymous login attempt burns full Argon2id CPU (timing-equalized by
    // design), making it a DoS amplifier if left unbounded, whereas the rest of the API is not.
    // Volumetric/global throttling is a web-server-edge concern, out of scope here.
    builder.Services.AddRateLimiter(rateLimiterOptions =>
    {
        rateLimiterOptions.OnRejected = async (context, ct) =>
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                context.HttpContext.Response.Headers.RetryAfter =
                    ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            }
            await context.HttpContext.Response.WriteAsJsonAsync(
                Struo.Api.Http.Envelope.Error(
                    Struo.Api.Http.ErrorCodes.TooManyRequests, "Too many login attempts. Please try again later."),
                cancellationToken: ct);
        };

        rateLimiterOptions.AddPolicy("login", httpContext =>
        {
            var loginOptions = httpContext.RequestServices
                .GetRequiredService<IOptions<Struo.Application.Configuration.LoginRateLimitOptions>>().Value;

            // Partition key = client IP. NOTE: behind a reverse proxy, RemoteIpAddress reflects the
            // proxy's own address unless the proxy is configured to forward the real client IP AND
            // this host is configured with UseForwardedHeaders (deliberately out of scope here) —
            // otherwise every login attempt through that proxy shares a single partition/bucket.
            var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            // RateLimiting:Login:Enabled toggle (config-driven, see LoginRateLimitOptions): when
            // disabled, return a no-op limiter for this partition. The "login" policy still EXISTS
            // (so [EnableRateLimiting("login")] never throws "no policy named login"); it simply
            // never rejects. Intended for multi-pod Kubernetes deployments where per-IP rate
            // limiting is delegated to the ingress/edge/WAF — that layer sees the real client IP and
            // sits in front of ALL pods, whereas this limiter's state is in-memory and per-pod, so
            // it can never enforce a true global limit across replicas in that topology.
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
        });
    });

    var app = builder.Build();

    app.UseSerilogRequestLogging();
    app.UseStruoCors(app.Configuration);
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseExceptionHandler();
    app.UseMiddleware<Struo.Api.Auth.CsrfProtectionMiddleware>();
    app.UseMiddleware<Struo.Api.Auth.PermissionResolutionMiddleware>();
    app.UseRateLimiter();

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

    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();

        // Development-only: SqlSugar CodeFirst creates any missing tables from the entity classes.
        // Never runs in production (InitTables can only add tables, not evolve them safely).
        if (app.Environment.IsDevelopment())
        {
            var entityTypes = scope.ServiceProvider
                .GetRequiredService<Struo.Application.Metadata.IEntityTypeCollector>()
                .CollectForInitTables();
            DatabaseInitializer.InitializeDevelopmentSchema(db, app.Environment, entityTypes.ToArray());
        }

        // Reviewed *.sql schema migrations. Config-driven (Database:MigrationsPath) and allowed in
        // ALL environments — applying reviewed scripts in production is the whole point of the runner.
        // It is a hard no-op on any non-PostgreSQL backend. Ordered so that in Development it runs
        // AFTER InitTables (fresh tables exist) and BEFORE the seeders below.
        var migrationsPath =
            builder.Configuration.GetSection(Struo.Application.Configuration.DatabaseOptions.SectionName)["MigrationsPath"];
        if (!string.IsNullOrWhiteSpace(migrationsPath))
        {
            var migrationLogger = scope.ServiceProvider
                .GetRequiredService<ILoggerFactory>().CreateLogger("Struo.MigrationRunner");
            await MigrationRunner.ApplyAsync(db, migrationsPath, migrationLogger);
        }

        // Dev fail-fast (DB-5): after InitTables + the migration runner have had their chance to create
        // the schema, assert the correctness-critical constraints actually exist (the revisions
        // composite UNIQUE — DB-4 backstop). Throws on divergence rather than running with a silent gap.
        if (app.Environment.IsDevelopment())
        {
            await SchemaGuard.AssertCriticalConstraintsAsync(db, default);
        }

        // Development-only seed data (languages, bootstrap admin, RBAC grants).
        if (app.Environment.IsDevelopment())
        {
            await Struo.Infrastructure.Localization.LanguageSeeder.SeedAsync(db);
            var hasher = scope.ServiceProvider.GetRequiredService<Struo.Application.Security.IPasswordHasher>();
            await Struo.Infrastructure.Identity.AdminUserSeeder.SeedAsync(db, hasher,
                builder.Configuration["Auth:BootstrapAdmin:Email"],
                builder.Configuration["Auth:BootstrapAdmin:Password"]);
            await Struo.Infrastructure.Identity.RbacSeeder.SeedAsync(db,
                builder.Configuration["Auth:BootstrapAdmin:Email"],
                builder.Configuration.GetSection("Rbac:PublicReadCollections").Get<string[]>() ?? []);
        }
    }

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "StruoCMS host terminated unexpectedly");
    // BL-3: without this, a startup exception (e.g. ValidateOnStart's OptionsValidationException) is
    // logged but swallowed here — the process still exits 0, so an orchestrator/supervisor sees a
    // "successful" exit and never restarts or alerts. Force a non-zero exit code so process-exit-code
    // monitoring reflects the actual failure.
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;
