using AwesomeAssertions;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace Struo.Tests.Hosting;

/// <summary>
/// A collection whose GraphQL create input type would have no field stops the host with a message about
/// the collection; one ordinary field next to the unwritable ones is enough to start.
/// </summary>
[Collection("ApiIntegration")]
public sealed class WritableFieldsStartupTests(ITestOutputHelper output)
{
    private static string ContentWith(params EmittedEntity[] entities) =>
        EmittedEntities.Build(EmittedEntities.UniqueName("writable"), entities).GetName().Name!;

    private static EmittedEntity Unwritable(string name, params EmittedProperty[] extra) =>
        new(name,
        [
            new EmittedProperty("Secret", typeof(string), FieldInterface.Hidden),
            new EmittedProperty("Rule", typeof(string), FieldInterface.Divider),
            new EmittedProperty("Pass", typeof(string), FieldInterface.Password),
            .. extra,
        ]);

    // Production: the Development schema gate runs first and reports a collection whose table does not exist.
    [SqliteOnlyFact("starts a host on a SQLite temp database")]
    public async Task A_collection_with_nothing_GraphQL_can_write_fails_at_host_start_naming_the_collection()
    {
        var content = ContentWith(Unwritable("Vault"));

        var error = await ForkHost.StartupErrorAsync(
            mutate: s => s["Struo:ContentAssemblies:1"] = content, environment: "Production");

        output.WriteLine(error?.ToString());
        var rejected = error.Should().BeOfType<MetadataException>().Which;
        rejected.Message.Should().Contain("Collection 'vault'")
            .And.Contain("create input type would be empty")
            .And.Contain("'secret' (Interface = Hidden").And.Contain("'rule' (Interface = Divider")
            .And.Contain("'pass' (Interface = Password")
            .And.Contain("Add a [CmsField] property");
    }

    [SqliteOnlyFact("starts a host on a SQLite temp database")]
    public async Task One_ordinary_field_next_to_the_unwritable_ones_starts_the_host()
    {
        var content = ContentWith(
            Unwritable("Ledger", new EmittedProperty("Name", typeof(string), FieldInterface.Text)));

        var error = await ForkHost.StartupErrorAsync(
            mutate: s => s["Struo:ContentAssemblies:1"] = content, environment: "Production");

        error.Should().BeNull();
    }
}
