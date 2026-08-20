using System.Reflection;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Struo.Api.Auth;
using Struo.Application.Configuration;
using Struo.Application.Files;
using Struo.Application.Security;
using Struo.Infrastructure.DependencyInjection;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.DependencyInjection;

/// <summary>
/// Pins the one coupling no other test exercises: the SHIPPED appsettings.json passing through the
/// REAL registration path.
///
/// <para>
/// <see cref="OptionsValidationTests"/> feeds an in-memory dictionary keyed by the literal
/// <c>"Database:..."</c>; its <c>Shipped_appsettings_disables_AutoSyncSchema</c> reads the real file
/// through <see cref="DatabaseOptions.SectionName"/>. Neither is the combination that boots in
/// production. So this scenario goes undetected: production binds a literal <c>"Database"</c>,
/// someone later renames the constant's value to <c>"Db"</c> and updates the shipped file to match —
/// both existing tests stay green, because each one is internally consistent, while a real boot binds
/// a section the file no longer has and every <c>Database:*</c> value silently reverts to its C#
/// default.
/// </para>
/// <para>
/// For the three binder-based tests below (<c>Database</c>, <c>Struo:Files</c>, <c>Oidc</c>), the
/// expected side is read with <see cref="JsonDocument"/> by LITERAL path (e.g. <c>"Database"</c>), and
/// the actual side comes entirely from the production binder. The section name as literally spelled in
/// the file is the only thing the two sides share there, which is exactly the coupling under test. The
/// three key-shape tests (<c>Branding</c>, <c>RateLimiting:Login</c>, <c>Auth:Password</c>) do NOT
/// follow this pattern — they locate the section through the <c>SectionName</c> constant on both sides,
/// for the reason recorded on those tests, so this paragraph's guarantee does not extend to them.
/// </para>
/// <para>
/// Scope: <c>Database</c>, <c>Struo:Files</c> and <c>Oidc</c> are covered the same way — shipped file in,
/// production registration extension called, values compared against a literal-path JsonDocument read.
/// <c>Auth:BootstrapAdmin</c> is covered differently because it has no options type at all (<c>Program</c>
/// reads it through the raw <see cref="IConfiguration"/> indexer), and <c>Branding</c>,
/// <c>RateLimiting:Login</c> and <c>Auth:Password</c> are covered by key shape only. The reason for that
/// last split is recorded on each test rather than here, because it is a property of those sections, not
/// of this file.
/// </para>
/// </summary>
public sealed class ShippedConfigurationBindingTests
{
    private static string ShippedAppSettingsPath() =>
        Path.Combine(RepoRoot.Find(), "src", "Struo.Api", "appsettings.json");

    /// <summary>
    /// The shipped file as an <see cref="IConfiguration"/>, built once so both the presence check and
    /// the DI registration below read the SAME instance — not two separately-built copies that could
    /// in principle drift from each other.
    /// </summary>
    private static IConfiguration ShippedConfiguration() =>
        new ConfigurationBuilder()
            .AddJsonFile(ShippedAppSettingsPath(), optional: false)
            .Build();

    /// <summary>
    /// Binds <typeparamref name="TOptions"/> by running <paramref name="register"/> — one of the same
    /// registration extensions <c>Program</c> calls — with <paramref name="configuration"/> as the only
    /// configuration source. No host is started and no connection is opened: options resolution in this
    /// codebase is not connection-bearing.
    /// </summary>
    private static TOptions BindThroughProductionRegistration<TOptions>(
        IConfiguration configuration, Action<IServiceCollection, IConfiguration> register)
        where TOptions : class
    {
        var services = new ServiceCollection();
        // BindConfiguration resolves IConfiguration out of the container, so the shipped file has to
        // be registered here. This is the seam under test — not a convenience.
        services.AddSingleton<IConfiguration>(configuration);
        register(services, configuration);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<TOptions>>().Value;
    }

    /// <summary>
    /// Reads one object out of the shipped file by LITERAL path — <paramref name="path"/> is spelled
    /// here the way the file spells it, never via a SectionName constant, which is what makes the
    /// comparison able to catch a constant that no longer matches the file. Clone() detaches the
    /// element so it stays readable after the JsonDocument is disposed — see chapter 05's pitfall
    /// about a JsonElement outliving its document.
    /// </summary>
    private static JsonElement ShippedSection(params string[] path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ShippedAppSettingsPath()));
        var element = doc.RootElement;
        foreach (var segment in path)
            element = element.GetProperty(segment);
        return element.Clone();
    }

    private static string[] StringArray(JsonElement array) =>
        [.. array.EnumerateArray().Select(e => e.GetString()!)];

    [Fact]
    public void Shipped_appsettings_Database_section_binds_through_the_production_registration()
    {
        var configuration = ShippedConfiguration();

        // Presence check, read off the SAME IConfiguration instance handed to AddStruoInfrastructure
        // below (not off the JsonDocument parse in ShippedSection("Database"), and not off the bound
        // DatabaseOptions). This is load-bearing against the shipped file's own "Database" section
        // going missing or being emptied out: GetChildren() would then be empty and this fails as a
        // plain assertion, matching the sibling idiom at
        // OptionsValidationTests.Shipped_appsettings_disables_AutoSyncSchema ("鍵被整段
        // 刪掉時 GetValue<bool> 也會回 false，光斷言 false 會假綠燈"). It does NOT, by itself, catch the
        // section-name divergence this file's Step 3 mutation exercises (BindConfiguration bound to a
        // hardcoded "Db" instead of DatabaseOptions.SectionName) — that mutation lives inside
        // AddStruoInfrastructure and never touches this raw configuration object, so this assertion
        // still finds "Database" present and passes even while the mutation is active. See the comment
        // below the value assertions for what actually fires red for that mutation today.
        configuration.GetSection(DatabaseOptions.SectionName).GetChildren().Should().NotBeEmpty(
            $"the shipped file must have a non-empty '{DatabaseOptions.SectionName}' section for the " +
            "comparisons below to mean anything");

        var shipped = ShippedSection("Database");
        var bound = BindThroughProductionRegistration<DatabaseOptions>(
            configuration, (services, _) => services.AddStruoInfrastructure());

        // Honest accounting of what these four assertions can and cannot catch. Only two of them have
        // real discriminating power: MigrationsPath (shipped "" vs the C# default null) and
        // ConnectionString (the shipped non-empty string vs the C# default empty string). DbType and
        // AutoSyncSchema happen to equal the DatabaseOptions default (PostgreSQL / false) in the
        // shipped file, so they pass whether or not the real value bound correctly — they are kept for
        // documentation value and because a future change to either default would make them
        // discriminate too, not because they discriminate today.
        //
        // In practice, for the mutation this file's Step 3 exercises (an unbound/misbound section),
        // NONE of the four ever get the chance to run: DatabaseOptions.ConnectionString carries
        // [Required(AllowEmptyStrings = false)], so IOptions<DatabaseOptions>.Value throws
        // OptionsValidationException before any Should() call below executes. That exception — not
        // these assertions — is this guard's actual signal today. They remain load-bearing as a
        // fallback: if ConnectionString's [Required] is ever relaxed, MigrationsPath and
        // ConnectionString are what keep this test honest instead of it going silently green.
        //
        // DbType is compared as a parsed enum, not as a string. ConfigurationBinder parses enum
        // members case-insensitively, so comparing bound.DbType.ToString() against the file's
        // spelling would produce a FALSE red if the file ever said "postgresql" — a spelling this
        // test has no business policing.
        var expectedDbType = Enum.Parse<StruoDbType>(shipped.GetProperty("DbType").GetString()!, ignoreCase: true);
        bound.DbType.Should().Be(expectedDbType);
        bound.ConnectionString.Should().Be(shipped.GetProperty("ConnectionString").GetString());
        bound.AutoSyncSchema.Should().Be(shipped.GetProperty("AutoSyncSchema").GetBoolean());
        bound.MigrationsPath.Should().Be(shipped.GetProperty("MigrationsPath").GetString());
    }

    [Fact]
    public void Shipped_appsettings_Files_section_binds_through_the_production_registration()
    {
        var configuration = ShippedConfiguration();

        configuration.GetSection(FileStorageOptions.SectionName).GetChildren().Should().NotBeEmpty(
            $"the shipped file must have a non-empty '{FileStorageOptions.SectionName}' section for the " +
            "comparisons below to mean anything");

        var shipped = ShippedSection("Struo", "Files");
        var bound = BindThroughProductionRegistration<FileStorageOptions>(
            configuration, (services, _) => services.AddStruoFiles());

        // These two are what discriminate. AllowedContentTypes ships 18 entries against a C# default of
        // [] — and empty means ALLOW ALL here, so a section that failed to bind would silently widen
        // upload validation to every content type, which is the worst of the failure modes in this file.
        // The S3 placeholders ship "REPLACE_ME" against a C# default of null.
        bound.AllowedContentTypes.Should().Equal(StringArray(shipped.GetProperty("AllowedContentTypes")));
        var shippedS3 = shipped.GetProperty("S3");
        bound.S3.Endpoint.Should().Be(shippedS3.GetProperty("Endpoint").GetString());
        bound.S3.Bucket.Should().Be(shippedS3.GetProperty("Bucket").GetString());

        // The rest ship values equal to their C# default, so they pass whether or not the section bound.
        // Kept for the same reason as the Database test's DbType/AutoSyncSchema: they document the
        // shipped contract, and they start discriminating the moment either side changes. Note they are
        // not dead weight against a *file-side* drift: GetProperty throws KeyNotFoundException if the
        // file stops spelling one of these keys, which is a red test, not a silent pass.
        bound.Backend.Should().Be(shipped.GetProperty("Backend").GetString());
        bound.MaxUploadBytes.Should().Be(shipped.GetProperty("MaxUploadBytes").GetInt64());
        bound.PresignedRedirect.Should().Be(shipped.GetProperty("PresignedRedirect").GetBoolean());
        bound.Local.RootPath.Should().Be(shipped.GetProperty("Local").GetProperty("RootPath").GetString());

        var shippedImage = shipped.GetProperty("ImageTransform");
        bound.ImageTransform.Enabled.Should().Be(shippedImage.GetProperty("Enabled").GetBoolean());
        bound.ImageTransform.MaxWidth.Should().Be(shippedImage.GetProperty("MaxWidth").GetInt32());
        bound.ImageTransform.MaxHeight.Should().Be(shippedImage.GetProperty("MaxHeight").GetInt32());
        bound.ImageTransform.DefaultQuality.Should().Be(shippedImage.GetProperty("DefaultQuality").GetInt32());
        bound.ImageTransform.CachePath.Should().Be(shippedImage.GetProperty("CachePath").GetString());

        // Discriminating too, unlike the group above: ImageTransformOptions.AllowedFormats defaults to
        // [] in C# (ApplyCollectionDefaults supplies DefaultAllowedFormats only when still empty after
        // binding), so this compares the shipped 4 entries against a genuinely bound value, not a
        // coincidental match with the default.
        bound.ImageTransform.AllowedFormats.Should().Equal(StringArray(shippedImage.GetProperty("AllowedFormats")));
    }

    [Fact]
    public void Shipped_appsettings_Oidc_section_binds_through_the_production_registration()
    {
        var configuration = ShippedConfiguration();

        configuration.GetSection(OidcOptions.SectionName).GetChildren().Should().NotBeEmpty(
            $"the shipped file must have a non-empty '{OidcOptions.SectionName}' section for the " +
            "comparisons below to mean anything");

        var shipped = ShippedSection("Oidc");
        // AddStruoOidc takes IConfiguration as a parameter as well as binding through the container, so
        // both of its inputs come from the shipped file here — the same pair Program hands it.
        var bound = BindThroughProductionRegistration<OidcOptions>(
            configuration, (services, config) => services.AddStruoOidc(config));

        // Discriminating: all three ship non-null placeholder text against a C# default of null.
        bound.Authority.Should().Be(shipped.GetProperty("Authority").GetString());
        bound.ClientId.Should().Be(shipped.GetProperty("ClientId").GetString());
        bound.AllowedTenantId.Should().Be(shipped.GetProperty("AllowedTenantId").GetString());

        // Equal to their C# defaults today; same reasoning as the Files test above. Enabled is the one
        // worth naming: shipped false and default false, so this assertion would NOT notice a section
        // that stopped binding — the placeholders above are what would. AllowedEmailDomains ships []
        // against a C# default of [] (OidcOptions.cs). It still discriminates on file-side key drift
        // only: a renamed shipped key makes GetProperty throw before this line runs, rather than this
        // Equal() catching a binding failure.
        bound.Enabled.Should().Be(shipped.GetProperty("Enabled").GetBoolean());
        bound.CallbackPath.Should().Be(shipped.GetProperty("CallbackPath").GetString());
        bound.ReturnUrlDefault.Should().Be(shipped.GetProperty("ReturnUrlDefault").GetString());
        bound.RequireEmailVerified.Should().Be(shipped.GetProperty("RequireEmailVerified").GetBoolean());
        bound.AllowedEmailDomains.Should().Equal(StringArray(shipped.GetProperty("AllowedEmailDomains")));

        // Scopes: OidcOptions.Scopes now defaults to [] (ApplyCollectionDefaults supplies DefaultScopes
        // only when the bound value is still empty), so ConfigurationBinder replaces rather than
        // appends here and this comparison discriminates cleanly against the shipped 3 entries.
        bound.Scopes.Should().Equal(StringArray(shipped.GetProperty("Scopes")));
    }

    /// <summary>
    /// <c>Auth:BootstrapAdmin</c> has no options type: <c>Program</c> passes
    /// <c>Configuration["Auth:BootstrapAdmin:Email"]</c> and <c>[":Password"]</c> straight into
    /// <c>DataSeeder.SeedAsync</c>, so there is no binder to compare a bound object against. What makes it
    /// worth pinning anyway is the failure mode on the consuming side: <c>AdminUserSeeder.SeedAsync</c>
    /// returns early when either value is null or blank, so a shipped file whose keys no longer sit at
    /// those literal paths seeds NO admin at all on a fresh install — nobody can log in, and nothing logs
    /// an error.
    /// <para>
    /// The password is additionally compared against <c>DataSeeder.DefaultAdminPassword</c>, the constant
    /// the production default-password WARNING compares against. If those drift apart the warning
    /// silently stops firing while the default account is still created — the exact combination a
    /// production deployment must not be in.
    /// </para>
    /// <para>
    /// The literal paths below are spelled the way <c>Program</c> spells them. That is the coupling under
    /// test, and its limit: renaming the section in <c>Program</c> AND in the file leaves this red until
    /// the test is updated too, which is intended, while a rename in <c>Program</c> alone is invisible
    /// here because nothing in this test reads <c>Program</c>.
    /// </para>
    /// </summary>
    [Fact]
    public void Shipped_appsettings_bootstrap_admin_sits_at_the_paths_Program_reads()
    {
        var configuration = ShippedConfiguration();

        configuration["Auth:BootstrapAdmin:Email"].Should().NotBeNullOrWhiteSpace(
            "AdminUserSeeder seeds nothing when the email is null or blank, leaving a fresh install " +
            "with no account to log in with");
        configuration["Auth:BootstrapAdmin:Password"].Should().Be(DataSeeder.DefaultAdminPassword,
            "the shipped password is what DataSeeder's production WARNING compares against; if the two " +
            "drift the warning silently stops firing");
    }

    /// <summary>
    /// <c>Branding</c>, <c>RateLimiting:Login</c> and <c>Auth:Password</c> cannot be covered the way the
    /// three sections above are, for two independent reasons — both properties of those sections, not
    /// gaps in this harness:
    /// <list type="number">
    /// <item>none of the three is registered by an extension method. <c>Program</c> binds all of them
    /// inline, so there is no callable seam short of booting a host, and a test that re-wrote the same
    /// <c>AddOptions/BindConfiguration</c> pair itself would only assert what the test wrote.</item>
    /// <item>every value they ship equals its C# default (<c>Name</c> "StruoCMS" / <c>LogoUrl</c> null;
    /// <c>Enabled</c> true / <c>PermitLimit</c> 5 / <c>WindowSeconds</c> 60; <c>MinLength</c> 8 /
    /// <c>MaxLength</c> 128), so even a real binder comparison would pass whether or not the section
    /// bound at all.</item>
    /// </list>
    /// What remains, and does discriminate, is key shape: a misspelled key in the shipped file
    /// (<c>PermitLimits</c>, <c>LogoURL</c>) reverts that value to its default with no error anywhere.
    /// Comparing the children of the section the <c>SectionName</c> constant names against the options
    /// type's property names catches that in both directions — an unbindable key in the file, and a
    /// property the file forgot to document.
    /// <para>
    /// Limit: both sides here are located through the same <c>SectionName</c> constant, and <c>Program</c>
    /// binds all three of these sections inline with that same constant. So this test cannot catch the
    /// class doc's headline scenario for these three sections: if <c>Program</c> is edited to bind a
    /// hardcoded literal that has drifted from <c>SectionName</c> (and the shipped file is updated to
    /// match that same literal), production silently reverts to defaults while this test — which never reads
    /// <c>Program</c> — stays green. This is the same limit the bootstrap-admin test records above.
    /// </para>
    /// </summary>
    [Fact]
    public void Shipped_appsettings_Branding_keys_match_BrandingOptions() =>
        AssertShippedKeysMatchProperties<BrandingOptions>(BrandingOptions.SectionName);

    /// <inheritdoc cref="Shipped_appsettings_Branding_keys_match_BrandingOptions"/>
    [Fact]
    public void Shipped_appsettings_login_rate_limit_keys_match_LoginRateLimitOptions() =>
        AssertShippedKeysMatchProperties<LoginRateLimitOptions>(LoginRateLimitOptions.SectionName);

    /// <inheritdoc cref="Shipped_appsettings_Branding_keys_match_BrandingOptions"/>
    [Fact]
    public void Shipped_appsettings_password_policy_keys_match_PasswordPolicyOptions() =>
        AssertShippedKeysMatchProperties<PasswordPolicyOptions>(PasswordPolicyOptions.SectionName);

    private static void AssertShippedKeysMatchProperties<TOptions>(string sectionName)
    {
        var keys = ShippedConfiguration().GetSection(sectionName).GetChildren()
            // "// Xxx" keys are this file's own documentation convention (as on Database:MigrationsPath).
            // They are real configuration keys to the provider but bind to nothing, and they sit in the
            // PARENT section of the three covered here — filtered anyway so the convention can be used
            // inside these sections later without turning this test red for a comment.
            .Select(child => child.Key)
            .Where(key => !key.StartsWith("//", StringComparison.Ordinal))
            .ToArray();

        keys.Should().NotBeEmpty($"the shipped file must have a non-empty '{sectionName}' section");
        keys.Should().BeEquivalentTo(
            typeof(TOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name),
            $"every key under '{sectionName}' must bind to a {typeof(TOptions).Name} property and every " +
            "property must be represented in the shipped file — a misspelled key on either side keeps " +
            "the C# default with no error");
    }
}
