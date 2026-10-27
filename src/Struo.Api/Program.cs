// src/Struo.Api/Program.cs — owned by the fork. The StruoCMS composition lives in Struo.Api.Hosting.
using Serilog;
using Struo.Api.Hosting;
using Struo.Infrastructure.Migrations.Commands;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var cli = MigrationCli.Parse(args);
    var builder = WebApplication.CreateBuilder(cli.HostArgs);

    // Add configuration sources (Key Vault, ...) before AddStruoCms(): the content-assembly scan reads
    // the configuration during that call.
    builder.AddStruoCms();
    // Register your own services here, after AddStruoCms(): a registration made after it wins over the
    // core default. For the services the core registers with TryAdd, one made before it wins too.

    var app = builder.Build();

    if (await app.TryRunStruoCommandAsync(cli)) return;

    await app.UseStruoCmsAsync();
    await app.RunAsync();
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
    await Log.CloseAndFlushAsync();
}
