using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Struo.Application.Configuration;
using Struo.Infrastructure.DependencyInjection;
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
/// The expected side below is read with <see cref="JsonDocument"/> by LITERAL path
/// (<c>"Database"</c>), and the actual side comes entirely from the production binder. The section
/// name as literally spelled in the file is the only thing the two sides share, which is exactly the
/// coupling under test.
/// </para>
/// <para>
/// Scope: this closes the divergence for the <c>Database</c> section only. The same shipped-file-vs-binder
/// mismatch is possible for <c>Struo:Files</c>, <c>Oidc</c>, <c>RateLimiting:Login</c>,
/// <c>Auth:BootstrapAdmin</c> and <c>Branding</c>, none of which is covered here.
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
    /// Binds through <see cref="ServiceCollectionExtensions.AddStruoInfrastructure"/> — the same
    /// extension <c>Program</c> calls — with <paramref name="configuration"/> as the only
    /// configuration source. No host is started and no connection is opened: options resolution is
    /// not connection-bearing.
    /// </summary>
    private static DatabaseOptions BindThroughProductionRegistration(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        // BindConfiguration resolves IConfiguration out of the container, so the shipped file has to
        // be registered here. This is the seam under test — not a convenience.
        services.AddSingleton<IConfiguration>(configuration);
        services.AddStruoInfrastructure();

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
    }

    /// <summary>
    /// Reads the shipped file's <c>Database</c> object by literal path. Clone() detaches the element
    /// so it stays readable after the JsonDocument is disposed — see chapter 05's pitfall about a
    /// JsonElement outliving its document.
    /// </summary>
    private static JsonElement ShippedDatabaseSection()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ShippedAppSettingsPath()));
        return doc.RootElement.GetProperty("Database").Clone();
    }

    [Fact]
    public void Shipped_appsettings_Database_section_binds_through_the_production_registration()
    {
        var configuration = ShippedConfiguration();

        // Presence check, read off the SAME IConfiguration instance handed to AddStruoInfrastructure
        // below (not off the JsonDocument parse in ShippedDatabaseSection, and not off the bound
        // DatabaseOptions). This is load-bearing against the shipped file's own "Database" section
        // going missing or being emptied out: GetChildren() would then be empty and this fails as a
        // plain assertion, matching the sibling idiom at OptionsValidationTests.cs:170-172 ("鍵被整段
        // 刪掉時 GetValue<bool> 也會回 false，光斷言 false 會假綠燈"). It does NOT, by itself, catch the
        // section-name divergence this file's Step 3 mutation exercises (BindConfiguration bound to a
        // hardcoded "Db" instead of DatabaseOptions.SectionName) — that mutation lives inside
        // AddStruoInfrastructure and never touches this raw configuration object, so this assertion
        // still finds "Database" present and passes even while the mutation is active. See the comment
        // below the value assertions for what actually fires red for that mutation today.
        configuration.GetSection(DatabaseOptions.SectionName).GetChildren().Should().NotBeEmpty(
            $"the shipped file must have a non-empty '{DatabaseOptions.SectionName}' section for the " +
            "comparisons below to mean anything");

        var shipped = ShippedDatabaseSection();
        var bound = BindThroughProductionRegistration(configuration);

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
}
