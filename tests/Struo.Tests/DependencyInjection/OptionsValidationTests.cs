using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Struo.Api.Auth;
using Struo.Application.Security;
using Struo.Infrastructure.DependencyInjection;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.DependencyInjection;

/// <summary>
/// The five config-bound options (Database / Query / Oidc / Files / Auth:Password) must fail fast at
/// host start (via <c>ValidateOnStart</c> + DataAnnotations/predicate validation) instead of surfacing a
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
        var db = TestBackend.CreateSqliteDatabase();
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
                ["Oidc:Providers:acme:Enabled"] = "false",
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

    [SqliteOnlyFact("options validation; the SQLite temp file only satisfies the required connection string")]
    public async Task Host_starts_with_full_valid_configuration()
    {
        var error = await StartupErrorAsync();
        error.Should().BeNull("a fully-configured host must start cleanly");
    }

    [SqliteOnlyFact("options validation; the SQLite temp file only satisfies the required connection string")]
    public async Task Missing_database_connection_string_fails_startup()
    {
        var error = await StartupErrorAsync(s => s["Database:ConnectionString"] = "");
        var ove = ExceptionChainSearch.FindInner<OptionsValidationException>(error);
        ove.Should().NotBeNull("an empty Database:ConnectionString must fail fast at startup");
        string.Join(" ", ove!.Failures).Should().Contain("ConnectionString");
    }

    [SqliteOnlyFact("options validation; the SQLite temp file only satisfies the required connection string")]
    public async Task Invalid_files_default_status_fails_startup()
    {
        var error = await StartupErrorAsync(s => s["Struo:Files:DefaultStatus"] = "archived");
        var ove = ExceptionChainSearch.FindInner<OptionsValidationException>(error);
        ove.Should().NotBeNull("an unknown Struo:Files:DefaultStatus must fail fast at startup");
        string.Join(" ", ove!.Failures).Should().Contain("Struo:Files:DefaultStatus");
    }

    [SqliteOnlyFact("options validation; the SQLite temp file only satisfies the required connection string")]
    public async Task Query_max_limit_zero_fails_startup()
    {
        var error = await StartupErrorAsync(s => s["Query:MaxLimit"] = "0");
        var ove = ExceptionChainSearch.FindInner<OptionsValidationException>(error);
        ove.Should().NotBeNull("Query:MaxLimit below the [Range] floor must fail fast at startup");
        string.Join(" ", ove!.Failures).Should().Contain("MaxLimit");
    }

    // A valid enabled provider; `mutate` breaks one key. Providers are keyed by id under Oidc:Providers.
    private static void ValidProvider(Dictionary<string, string?> s, string id = "acme")
    {
        var p = $"Oidc:Providers:{id}:";
        s[p + "Enabled"] = "true";
        s[p + "DisplayName"] = "Acme";
        s[p + "Authority"] = "https://login.example.com/tenant/v2.0";
        s[p + "ClientId"] = "client";
        s[p + "ClientSecret"] = "test-secret";
    }

    private static async Task<string> OidcFailureTextAsync(Action<Dictionary<string, string?>> mutate)
    {
        var error = await StartupErrorAsync(mutate);
        var ove = ExceptionChainSearch.FindInner<OptionsValidationException>(error);
        ove.Should().NotBeNull("the Oidc configuration must fail fast at startup");
        return string.Join(" ", ove!.Failures);
    }

    [SqliteOnlyTheory("options validation; the SQLite temp file only satisfies the required connection string")]
    [InlineData("/", null)]
    [InlineData("/admin", null)]
    [InlineData("https://admin.example.com/", "https://admin.example.com")]
    [InlineData("https://ADMIN.example.com:443/x", "https://admin.example.com")]
    public async Task Oidc_return_url_default_that_is_relative_or_an_allowed_origin_starts(string value, string? origin)
    {
        var error = await StartupErrorAsync(s =>
        {
            s["Oidc:ReturnUrlDefault"] = value;
            if (origin is not null) s["Struo:Cors:AllowedOrigins:0"] = origin;
        });
        error.Should().BeNull();
    }

    [SqliteOnlyTheory("options validation; the SQLite temp file only satisfies the required connection string")]
    [InlineData("https://admin.example.com/", null)]
    [InlineData("https://evil.example.com/", "https://admin.example.com")]
    [InlineData("https://admin.example.com.evil.com/", "https://admin.example.com")]
    [InlineData("http://admin.example.com/", "https://admin.example.com")]
    [InlineData("javascript:alert(1)", "https://admin.example.com")]
    public async Task Oidc_return_url_default_absolute_outside_the_allowed_origins_fails_startup(string value, string? origin)
    {
        var text = await OidcFailureTextAsync(s =>
        {
            s["Oidc:ReturnUrlDefault"] = value;
            if (origin is not null) s["Struo:Cors:AllowedOrigins:0"] = origin;
        });

        text.Should().Contain("Oidc:ReturnUrlDefault").And.Contain("Struo:Cors:AllowedOrigins");
    }

    [SqliteOnlyFact("options validation; the SQLite temp file only satisfies the required connection string")]
    public async Task Oidc_valid_enabled_provider_starts()
    {
        var error = await StartupErrorAsync(s => ValidProvider(s));
        error.Should().BeNull();
    }

    [SqliteOnlyTheory("options validation; the SQLite temp file only satisfies the required connection string")]
    [InlineData("ClientId")]
    [InlineData("ClientSecret")]
    [InlineData("Authority")]
    [InlineData("DisplayName")]
    public async Task Oidc_enabled_provider_missing_a_required_field_fails_startup(string field)
    {
        var text = await OidcFailureTextAsync(s =>
        {
            ValidProvider(s);
            s[$"Oidc:Providers:acme:{field}"] = "";
        });
        text.Should().Contain("Oidc:Providers:acme").And.Contain(field);
    }

    [SqliteOnlyFact("options validation; the SQLite temp file only satisfies the required connection string")]
    public async Task Oidc_disabled_provider_is_not_validated()
    {
        var error = await StartupErrorAsync(s => s["Oidc:Providers:acme:ClientId"] = "");
        error.Should().BeNull();
    }

    [SqliteOnlyTheory("options validation; the SQLite temp file only satisfies the required connection string")]
    [InlineData("Beta")]
    [InlineData("a b")]
    [InlineData("a_b")]
    [InlineData("abc\n")]
    public async Task Oidc_provider_id_must_be_lowercase_letters_digits_and_hyphens(string id)
    {
        var text = await OidcFailureTextAsync(s => ValidProvider(s, id));
        text.Should().Contain("lowercase letters, digits and hyphens").And.Contain($"'{id}'");
        text.Should().NotContain(OidcOptions.ProviderIdPattern);
    }

    [SqliteOnlyFact("options validation; the SQLite temp file only satisfies the required connection string")]
    public async Task Oidc_enabled_providers_must_not_share_a_callback_path()
    {
        var text = await OidcFailureTextAsync(s =>
        {
            ValidProvider(s, "one");
            ValidProvider(s, "two");
            s["Oidc:Providers:one:CallbackPath"] = "/signin-shared";
            s["Oidc:Providers:two:CallbackPath"] = "/Signin-Shared/";
        });
        text.Should().Contain("/signin-shared").And.Contain("one").And.Contain("two");
    }

    [SqliteOnlyFact("options validation; the SQLite temp file only satisfies the required connection string")]
    public async Task Oidc_callback_path_must_not_equal_another_providers_sign_out_path()
    {
        var text = await OidcFailureTextAsync(s =>
        {
            ValidProvider(s, "one");
            ValidProvider(s, "two");
            s["Oidc:Providers:one:CallbackPath"] = "/signout-callback-oidc-two";
        });
        text.Should().Contain("/signout-callback-oidc-two").And.Contain("one").And.Contain("two");
    }

    [SqliteOnlyFact("options validation; the SQLite temp file only satisfies the required connection string")]
    public async Task Oidc_two_providers_with_default_callback_paths_start()
    {
        var error = await StartupErrorAsync(s => { ValidProvider(s, "one"); ValidProvider(s, "two"); });
        error.Should().BeNull();
    }

    [SqliteOnlyFact("options validation; the SQLite temp file only satisfies the required connection string")]
    public async Task Oidc_callback_path_must_start_with_a_slash()
    {
        var text = await OidcFailureTextAsync(s =>
        {
            ValidProvider(s);
            s["Oidc:Providers:acme:CallbackPath"] = "signin";
        });
        text.Should().Contain("Oidc:Providers:acme:CallbackPath");
    }

    [SqliteOnlyFact("options validation; the SQLite temp file only satisfies the required connection string")]
    public async Task Oidc_unpinned_multi_tenant_authority_fails_startup()
    {
        var text = await OidcFailureTextAsync(s =>
        {
            ValidProvider(s);
            s["Oidc:Providers:acme:Authority"] = "https://login.microsoftonline.com/common/v2.0";
            s["Oidc:Providers:acme:AllowedTenantId"] = "";
        });
        text.Should().Contain("Oidc:Providers:acme:AllowedTenantId")
            .And.Contain("Oidc:Providers:acme:AllowedEmailDomains");
    }

    [SqliteOnlyFact("options validation; the SQLite temp file only satisfies the required connection string")]
    public async Task Oidc_enabled_google_without_allowed_email_domains_fails_startup()
    {
        var text = await OidcFailureTextAsync(s =>
        {
            ValidProvider(s, "google");
            s["Oidc:Providers:google:Authority"] = "https://accounts.google.com/";
        });
        text.Should().Contain("Oidc:Providers:google:AllowedEmailDomains").And.Contain("Google");
    }

    [SqliteOnlyFact("options validation; the SQLite temp file only satisfies the required connection string")]
    public async Task Oidc_enabled_google_with_the_shipped_placeholders_fails_clearly()
    {
        var text = await OidcFailureTextAsync(s =>
        {
            s["Oidc:Providers:google:Enabled"] = "true";
            s["Oidc:Providers:google:DisplayName"] = "Google";
            s["Oidc:Providers:google:Authority"] = "https://accounts.google.com";
            s["Oidc:Providers:google:ClientId"] = "REPLACE_ME";
            s["Oidc:Providers:google:ClientSecret"] = "x";
        });
        text.Should().Contain("Oidc:Providers:google:AllowedEmailDomains");
    }

    [SqliteOnlyFact("options validation; the SQLite temp file only satisfies the required connection string")]
    public async Task Oidc_enabled_google_with_an_email_domain_starts()
    {
        var error = await StartupErrorAsync(s =>
        {
            ValidProvider(s, "google");
            s["Oidc:Providers:google:Authority"] = "https://accounts.google.com";
            s["Oidc:Providers:google:AllowedEmailDomains:0"] = "corp.com";
        });
        ExceptionChainSearch.FindInner<OptionsValidationException>(error).Should().BeNull();
    }

    [SqliteOnlyFact("options validation; the SQLite temp file only satisfies the required connection string")]
    public async Task Oidc_multi_tenant_pin_on_one_provider_does_not_cover_another()
    {
        var text = await OidcFailureTextAsync(s =>
        {
            ValidProvider(s, "pinned");
            ValidProvider(s, "loose");
            s["Oidc:Providers:pinned:Authority"] = "https://login.microsoftonline.com/common/v2.0";
            s["Oidc:Providers:loose:Authority"] = "https://login.microsoftonline.com/common/v2.0";
            s["Oidc:Providers:pinned:AllowedTenantId"] = "tenant-1";
        });
        text.Should().Contain("Oidc:Providers:loose").And.NotContain("Oidc:Providers:pinned:Allowed");
    }

    [SqliteOnlyFact("options validation; the SQLite temp file only satisfies the required connection string")]
    public async Task Oidc_invalid_prompt_fails_startup()
    {
        var text = await OidcFailureTextAsync(s =>
        {
            ValidProvider(s);
            s["Oidc:Providers:acme:Prompt"] = "pick_one";
        });
        text.Should().Contain("Oidc:Providers:acme:Prompt");
    }

    [SqliteOnlyFact("options validation; the SQLite temp file only satisfies the required connection string")]
    public async Task Oidc_multi_tenant_authority_with_a_domain_pin_starts()
    {
        var error = await StartupErrorAsync(s =>
        {
            ValidProvider(s);
            s["Oidc:Providers:acme:Authority"] = "https://login.microsoftonline.com/common/v2.0";
            s["Oidc:Providers:acme:AllowedEmailDomains:0"] = "corp.com";
        });
        ExceptionChainSearch.FindInner<OptionsValidationException>(error).Should().BeNull();
    }

    [Fact]
    public void MigrateOnStartup_defaults_to_false()
    {
        // 預設必須是安全側：不明確開啟，啟動時就不會套用任何 migration。
        new Struo.Application.Configuration.DatabaseOptions()
            .MigrateOnStartup.Should().BeFalse();
    }

    [Fact]
    public void MigrateOnStartup_binds_true_from_configuration()
    {
        // 這個鍵是啟動時唯一會替你執行 migration 的開關。若某次改名或改 section 讓它靜默失聯，
        // 行為看起來完全正常（永遠只檢查、不套用），沒有任何東西會發現它已是死碼。
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{Struo.Application.Configuration.DatabaseOptions.SectionName}:MigrateOnStartup"] = "true",
            })
            .Build();

        var options = new Struo.Application.Configuration.DatabaseOptions();
        config.GetSection(Struo.Application.Configuration.DatabaseOptions.SectionName).Bind(options);

        options.MigrateOnStartup.Should().BeTrue();
    }

    [Fact]
    public void The_removed_schema_options_no_longer_exist()
    {
        var properties = typeof(Struo.Application.Configuration.DatabaseOptions).GetProperties().Select(p => p.Name);

        properties.Should().NotContain(["AutoSyncSchema", "MigrationsPath"]);
    }

    [SqliteOnlyTheory("options validation; the SQLite temp file only satisfies the required connection string")]
    [InlineData("Struo_")]          // upper-case letter
    [InlineData("1struo_")]         // must start with a letter
    [InlineData("struo-")]          // hyphen not allowed
    [InlineData("struo cms_")]      // whitespace
    [InlineData("abcdefghijklmnopq")] // 17 characters
    public async Task Database_table_prefix_outside_the_allowed_shape_fails_startup(string prefix)
    {
        var error = await StartupErrorAsync(s => s["Database:TablePrefix"] = prefix);
        var ove = ExceptionChainSearch.FindInner<OptionsValidationException>(error);
        ove.Should().NotBeNull($"Database:TablePrefix '{prefix}' must fail fast at startup");
        string.Join(" ", ove!.Failures).Should().Contain("TablePrefix");
    }

    [SqliteOnlyTheory("options validation; the SQLite temp file only satisfies the required connection string")]
    [InlineData("")]
    [InlineData("struo_")]
    [InlineData("acme")]
    [InlineData("a1_b2_")]
    [InlineData("abcdefghijklmnop")] // 16 characters
    public async Task Database_table_prefix_inside_the_allowed_shape_starts(string prefix)
    {
        var error = await StartupErrorAsync(s => s["Database:TablePrefix"] = prefix);
        error.Should().BeNull($"Database:TablePrefix '{prefix}' is valid");
    }

    [Fact]
    public void Shipped_appsettings_leaves_MigrateOnStartup_off_and_drops_the_removed_keys()
    {
        // C# 端預設安全不代表出貨檔安全——模板使用者拿到的是這個 json，不是 new DatabaseOptions()。
        // 用 ConfigurationBuilder 讀取：這是 Program.cs 實際載入設定的同一條路徑，且會走到
        // 與 MigrateOnStartup_binds_true_from_configuration 相同的 ConfigurationBinder，兩者一起
        // 覆蓋真正可能壞掉的機制。
        var path = Path.Combine(RepoRoot.Find(), "src", "Struo.Api", "appsettings.json");
        File.Exists(path).Should().BeTrue($"出貨的 appsettings 必須存在於 '{path}'");

        var config = new ConfigurationBuilder().AddJsonFile(path, optional: false).Build();
        var section = Struo.Application.Configuration.DatabaseOptions.SectionName;

        // 先確認鍵存在：鍵被整段刪掉時 GetValue<bool> 也會回 false，光斷言 false 會假綠燈。
        config[$"{section}:MigrateOnStartup"].Should().NotBeNull(
            "出貨檔必須明寫這個鍵，讓讀者看得到預設值");
        config[$"{section}:AutoSyncSchema"].Should().BeNull("the option does not exist");
        config[$"{section}:MigrationsPath"].Should().BeNull("the option does not exist");

        var options = new Struo.Application.Configuration.DatabaseOptions();
        config.GetSection(section).Bind(options);
        options.MigrateOnStartup.Should().BeFalse("出貨預設必須是安全側");
    }

    [SqliteOnlyFact("options validation; the SQLite temp file only satisfies the required connection string")]
    public async Task Localization_default_language_outside_the_list_fails_startup()
    {
        var error = await StartupErrorAsync(s =>
        {
            s["Localization:Languages:0:Code"] = "en";
            s["Localization:Languages:0:Name"] = "English";
            s["Localization:DefaultLanguage"] = "fr";
        });
        var ove = ExceptionChainSearch.FindInner<OptionsValidationException>(error);
        ove.Should().NotBeNull("Localization:DefaultLanguage must be one of Localization:Languages");
        string.Join(" ", ove!.Failures).Should().Contain("DefaultLanguage");
    }

    [SqliteOnlyFact("options validation; the SQLite temp file only satisfies the required connection string")]
    public async Task Localization_single_language_configuration_starts()
    {
        var error = await StartupErrorAsync(s =>
        {
            s["Localization:Languages:0:Code"] = "ja";
            s["Localization:Languages:0:Name"] = "日本語";
            s["Localization:DefaultLanguage"] = "ja";
        });
        error.Should().BeNull();
    }
}
