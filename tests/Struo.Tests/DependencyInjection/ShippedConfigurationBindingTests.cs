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
/// </summary>
public sealed class ShippedConfigurationBindingTests
{
    private static string ShippedAppSettingsPath() =>
        Path.Combine(RepoRoot.Find(), "src", "Struo.Api", "appsettings.json");

    /// <summary>
    /// Binds through <see cref="ServiceCollectionExtensions.AddStruoInfrastructure"/> — the same
    /// extension <c>Program</c> calls — with the shipped file as the only configuration source.
    /// No host is started and no connection is opened: options resolution is not connection-bearing.
    /// </summary>
    private static DatabaseOptions BindThroughProductionRegistration()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(ShippedAppSettingsPath(), optional: false)
            .Build();

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
        var shipped = ShippedDatabaseSection();
        var bound = BindThroughProductionRegistration();

        // All four keys, not just one: a single key could pass by coincidence if a future refactor
        // bound part of the section through a different path.
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
