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
    // Reads Struo:ContentAssemblies from builder.Configuration immediately, before Build() below —
    // any custom configuration provider a fork adds (e.g. Key Vault) must be registered on
    // builder.Configuration before this line to be seen by the metadata scan.
    builder.Services.AddStruoMetadata(builder.Configuration, typeof(Program).Assembly);
    builder.Services.AddStruoData();
    builder.Services.AddStruoGraphQl(builder.Environment, builder.Configuration);
    builder.Services.AddStruoFiles();
    builder.Services.AddStruoAuth(builder.Configuration, builder.Environment);
    builder.Services.AddStruoCors(builder.Configuration);
    builder.Services.AddStruoOidc(builder.Configuration);
    builder.Services.AddOptions<Struo.Application.Configuration.BrandingOptions>()
        .BindConfiguration(Struo.Application.Configuration.BrandingOptions.SectionName);
    // Config-bound tuning for the login rate limiter below (defaults: 5 attempts / 60s).
    builder.Services.AddOptions<Struo.Application.Configuration.LoginRateLimitOptions>()
        .BindConfiguration(Struo.Application.Configuration.LoginRateLimitOptions.SectionName);
    builder.Services.AddOptions<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(AuthSchemes.Cookie)
        .PostConfigure<DistributedCacheTicketStore>((options, store) => options.SessionStore = store);
    builder.Services.AddScoped<SchemaService>();
    // /api/config is anonymous and previously hit the DB on every request; cached with a
    // short TTL (ConfigController) and evicted immediately on a branding save (SettingsController).
    builder.Services.AddMemoryCache();
    builder.Services.AddHealthChecks()
        .AddCheck<DbReadinessCheck>("database", tags: ["ready"])
        .AddCheck<Struo.Infrastructure.Health.CacheReadinessCheck>("cache", tags: ["ready"]);

    // App-layer login rate limiter — fixed-window, partitioned by client IP, applied ONLY to
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
    // Must sit ahead of UseAuthentication: UseExceptionHandler only catches exceptions thrown
    // DOWNSTREAM of its own position, so while it was registered after authentication/authorization,
    // an infrastructure failure INSIDE the authentication stage (an unreachable Redis ticket store,
    // BearerTokenAuthenticationHandler's credential lookup failing) escaped StruoExceptionHandler and
    // went out as a bare 500 with an empty body — breaking the "envelope wraps every REST response"
    // invariant and skipping the mask-and-log treatment of internal detail. Guarded by
    // AuthenticationFailureEnvelopeTests. It stays INSIDE Serilog request logging (so the handled 500
    // is what gets logged) and INSIDE CORS (so the error response still carries CORS headers).
    app.UseExceptionHandler();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseMiddleware<Struo.Api.Auth.CsrfProtectionMiddleware>();
    app.UseMiddleware<Struo.Api.Auth.PermissionResolutionMiddleware>();
    app.UseRateLimiter();

    app.MapControllers();
    app.MapStruoGraphQl();

    // The OpenAPI document and Scalar explorer are mapped outside Production only: they publish the
    // full API surface without authentication. Set them up behind your own auth if you need them in
    // production.
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

        // Snapshot existing tables BEFORE any schema step, so seeders can fire only for tables
        // created during THIS startup (table-creation is the sole seeding trigger).
        var existingBefore = DataSeeder.GetTableNames(db);

        var dbOptions = scope.ServiceProvider
            .GetRequiredService<IOptions<Struo.Application.Configuration.DatabaseOptions>>().Value;

        var entityTypes = scope.ServiceProvider
            .GetRequiredService<Struo.Application.Metadata.IEntityTypeCollector>()
            .CollectForInitTables()
            .ToArray();

        var schemaLogger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>().CreateLogger("Struo.SchemaInit");

        // ALL environments, ALL backends: create tables that do not exist yet, so an empty database
        // bootstraps itself and DataSeeder can seed the tables created during THIS startup. Existing
        // tables are never touched here — InitTables in its default mode would modify and DROP
        // columns, so only entity types whose table is absent are handed to it.
        DatabaseInitializer.CreateMissingTables(db, existingBefore, schemaLogger, entityTypes);

        // Opt-in, Development only: full CodeFirst structural sync of EXISTING tables. Ignored with a
        // warning elsewhere. Evolving existing tables outside dev goes through reviewed migrations.
        if (dbOptions.AutoSyncSchema)
            DatabaseInitializer.SyncSchema(db, app.Environment, schemaLogger, entityTypes);

        // Reviewed *.sql schema migrations — ALTER-only by convention. Config-driven
        // (Database:MigrationsPath), all environments, all backends. The template ships zero scripts.
        // Runs AFTER table creation and BEFORE seeding.
        var migrationsPath = dbOptions.MigrationsPath;
        if (!string.IsNullOrWhiteSpace(migrationsPath))
        {
            var migrationLogger = scope.ServiceProvider
                .GetRequiredService<ILoggerFactory>().CreateLogger("Struo.MigrationRunner");
            await MigrationRunner.ApplyAsync(db, migrationsPath, migrationLogger);
        }

        // Dev fail-fast: assert correctness-critical constraints exist after schema creation.
        // Translation sidecars are derived from metadata (not hardcoded) so a fork's own sidecars are
        // covered the same way core's file_translations is: table/column names are resolved the same
        // way SqlSugar does, via EntityMaintenance, so they always match whatever InitTables/the
        // migrations actually created.
        if (app.Environment.IsDevelopment())
        {
            var metadataProvider = scope.ServiceProvider
                .GetRequiredService<Struo.Application.Metadata.IMetadataProvider>();
            var translationSidecars = metadataProvider.GetCollections()
                .Where(c => c.Translation is not null)
                .Select(c => c.Translation!)
                .Select(t => new TranslationSidecarDescriptor(
                    db.EntityMaintenance.GetTableName(t.TranslationEntityType),
                    db.EntityMaintenance.GetDbColumnName(t.ForeignKeyProperty, t.TranslationEntityType),
                    db.EntityMaintenance.GetDbColumnName(t.LocaleProperty, t.TranslationEntityType)))
                .ToList();

            await SchemaGuard.AssertCriticalConstraintsAsync(db, translationSidecars, default);
        }

        // Unified initial-data seeding — ALL environments. Each seeder fires only when its trigger
        // table was created this run (see DataSeeder); pre-existing tables are left untouched.
        var seedLogger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>().CreateLogger("Struo.DataSeeder");
        var hasher = scope.ServiceProvider.GetRequiredService<Struo.Application.Security.IPasswordHasher>();
        await DataSeeder.SeedAsync(
            db,
            existingBefore,
            hasher,
            builder.Configuration["Auth:BootstrapAdmin:Email"],
            builder.Configuration["Auth:BootstrapAdmin:Password"],
            builder.Configuration.GetSection("Rbac:PublicReadCollections").Get<string[]>() ?? [],
            app.Environment.IsProduction(),
            seedLogger);
    }

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "StruoCMS host terminated unexpectedly");
    // Without this, a startup exception (e.g. ValidateOnStart's OptionsValidationException) is
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
