using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Struo.Api.Auth;
using Struo.Infrastructure.DependencyInjection;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.DependencyInjection;

/// <summary>
/// The four config-bound options (Database / Query / Oidc / Files) must fail fast at host
/// start (via <c>ValidateOnStart</c> + DataAnnotations/predicate validation) instead of surfacing a
/// confusing first-request 500.
///
/// These tests build a real generic host that invokes the SAME registration extension methods used by
/// <c>Program</c> (<see cref="ServiceCollectionExtensions.AddStruoInfrastructure"/>,
/// <see cref="DataServiceCollectionExtensions.AddStruoData"/>,
/// <see cref="FileStorageServiceCollectionExtensions.AddStruoFiles"/>, and
/// <see cref="OidcWiring.AddStruoOidc"/>), then drive <c>StartAsync</c> directly. A generic host is
/// used deliberately: the app's <c>Program</c> wraps startup in a top-level try/catch that logs and
/// swallows the fatal exception (the process then exits — still fail-fast in production), which a
/// WebApplicationFactory would hide behind a reused, mid-run captured host. Driving StartAsync here
/// observes the real <see cref="OptionsValidationException"/> the startup validator throws.
/// </summary>
public sealed class OptionsValidationTests
{
    private static async Task<Exception?> StartupErrorAsync(Action<Dictionary<string, string?>>? mutate = null)
    {
        var db = new SqliteTestDatabase();
        var filesRoot = Path.Combine(Path.GetTempPath(), "struo-optval-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Full, VALID baseline; `mutate` breaks exactly one key.
            var settings = new Dictionary<string, string?>
            {
                ["Database:DbType"] = "Sqlite",
                ["Database:ConnectionString"] = db.ConnectionString,
                ["Struo:Files:Backend"] = "local",
                ["Struo:Files:Local:RootPath"] = filesRoot,
                ["Oidc:Enabled"] = "false",
                // Query left at its defaults (all valid) unless mutated.
            };
            mutate?.Invoke(settings);

            var builder = Host.CreateApplicationBuilder();
            builder.Configuration.AddInMemoryCollection(settings);
            builder.Services.AddStruoInfrastructure();
            builder.Services.AddStruoData();
            builder.Services.AddStruoFiles();
            builder.Services.AddStruoOidc(builder.Configuration);

            using var host = builder.Build();
            try
            {
                await host.StartAsync();
                await host.StopAsync();
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }
        finally
        {
            db.Dispose();
            if (Directory.Exists(filesRoot)) Directory.Delete(filesRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Host_starts_with_full_valid_configuration()
    {
        var error = await StartupErrorAsync();
        error.Should().BeNull("a fully-configured host must start cleanly");
    }

    [Fact]
    public async Task Missing_database_connection_string_fails_startup()
    {
        var error = await StartupErrorAsync(s => s["Database:ConnectionString"] = "");
        var ove = ExceptionChainSearch.FindInner<OptionsValidationException>(error);
        ove.Should().NotBeNull("an empty Database:ConnectionString must fail fast at startup");
        string.Join(" ", ove!.Failures).Should().Contain("ConnectionString");
    }

    [Fact]
    public async Task Query_max_limit_zero_fails_startup()
    {
        var error = await StartupErrorAsync(s => s["Query:MaxLimit"] = "0");
        var ove = ExceptionChainSearch.FindInner<OptionsValidationException>(error);
        ove.Should().NotBeNull("Query:MaxLimit below the [Range] floor must fail fast at startup");
        string.Join(" ", ove!.Failures).Should().Contain("MaxLimit");
    }

    [Fact]
    public async Task Oidc_enabled_without_client_id_fails_startup()
    {
        var error = await StartupErrorAsync(s =>
        {
            s["Oidc:Enabled"] = "true";
            s["Oidc:Authority"] = "https://login.example.com/tenant/v2.0";
            s["Oidc:ClientId"] = "";
            s["Oidc:ClientSecret"] = "test-secret";
        });
        var ove = ExceptionChainSearch.FindInner<OptionsValidationException>(error);
        ove.Should().NotBeNull("OIDC enabled without ClientId must fail fast at startup");
        string.Join(" ", ove!.Failures).Should().Contain("Oidc");
    }

    [Fact]
    public void AutoSyncSchema_defaults_to_false()
    {
        // 預設必須是安全側：不開啟就不會有任何自動結構同步。
        new Struo.Application.Configuration.DatabaseOptions()
            .AutoSyncSchema.Should().BeFalse();
    }

    [Fact]
    public void AutoSyncSchema_binds_true_from_configuration()
    {
        // 這個鍵是 Production 上唯一能觸發破壞性結構同步的開關（Program.cs:204）。若某次改名或改
        // section 讓它靜默失聯，行為看起來完全正常（永遠不同步），沒有任何東西會發現它已是死碼。
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{Struo.Application.Configuration.DatabaseOptions.SectionName}:AutoSyncSchema"] = "true",
            })
            .Build();

        var options = new Struo.Application.Configuration.DatabaseOptions();
        config.GetSection(Struo.Application.Configuration.DatabaseOptions.SectionName).Bind(options);

        options.AutoSyncSchema.Should().BeTrue();
    }

    [Fact]
    public void Shipped_appsettings_disables_AutoSyncSchema()
    {
        // C# 端預設安全不代表出貨檔安全——模板使用者拿到的是這個 json，不是 new DatabaseOptions()。
        // 用 ConfigurationBuilder 讀取：這是 Program.cs 實際載入設定的同一條路徑，且會走到
        // 與 AutoSyncSchema_binds_true_from_configuration 相同的 ConfigurationBinder，兩者一起
        // 覆蓋真正可能壞掉的機制。
        var path = Path.Combine(RepoRoot.Find(), "src", "Struo.Api", "appsettings.json");
        File.Exists(path).Should().BeTrue($"出貨的 appsettings 必須存在於 '{path}'");

        var config = new ConfigurationBuilder().AddJsonFile(path, optional: false).Build();

        // 先確認鍵存在：鍵被整段刪掉時 GetValue<bool> 也會回 false，光斷言 false 會假綠燈。
        config[$"{Struo.Application.Configuration.DatabaseOptions.SectionName}:AutoSyncSchema"].Should().NotBeNull(
            "出貨檔必須明寫這個鍵，讓讀者看得到預設值");

        var options = new Struo.Application.Configuration.DatabaseOptions();
        config.GetSection(Struo.Application.Configuration.DatabaseOptions.SectionName).Bind(options);
        options.AutoSyncSchema.Should().BeFalse("出貨預設必須是安全側");
    }
}
