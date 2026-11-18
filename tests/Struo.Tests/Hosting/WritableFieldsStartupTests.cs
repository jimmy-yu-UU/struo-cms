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

    private static EmittedProperty Name => new("Name", typeof(string), FieldInterface.Text);

    private static async Task<Exception?> StartAsync(EmittedEntity entity) =>
        await ForkHost.StartupErrorAsync(
            mutate: s => s["Struo:ContentAssemblies:1"] = ContentWith(entity), environment: "Production");

    [SqliteOnlyFact("starts a host on a SQLite temp database")]
    public async Task A_sidecar_whose_translatable_fields_are_all_Hidden_fails_at_host_start()
    {
        var error = await StartAsync(new EmittedEntity(
            "SideHidden", [Name], [new EmittedProperty("Title", typeof(string), FieldInterface.Text, Hidden: true)]));

        output.WriteLine(error?.ToString());
        error.Should().BeOfType<MetadataException>().Which.Message
            .Should().Contain("Collection 'sideHidden' has a translation sidecar")
            .And.Contain("'title' (Hidden = true)").And.Contain("Make at least one translatable field writable");
    }

    [SqliteOnlyFact("starts a host on a SQLite temp database")]
    public async Task A_sidecar_whose_translatable_fields_are_all_Password_fails_at_host_start()
    {
        var error = await StartAsync(new EmittedEntity(
            "SidePassword", [Name], [new EmittedProperty("Secret", typeof(string), FieldInterface.Password)]));

        error.Should().BeOfType<MetadataException>().Which.Message
            .Should().Contain("Collection 'sidePassword' has a translation sidecar")
            .And.Contain("'secret' (Interface = Password, which is never written)");
    }

    private static EmittedEntity WithRows(string name, params EmittedProperty[] subs) =>
        new(name, [Name, new EmittedProperty("Rows", null, FieldInterface.Repeater, false, new EmittedChild(name + "Row", subs))]);

    [SqliteOnlyFact("starts a host on a SQLite temp database")]
    public async Task A_Repeater_whose_sub_fields_are_all_Hidden_fails_at_host_start()
    {
        var error = await StartAsync(WithRows(
            "RowsHidden", new EmittedProperty("Label", typeof(string), FieldInterface.Text, Hidden: true)));

        output.WriteLine(error?.ToString());
        error.Should().BeOfType<MetadataException>().Which.Message
            .Should().Contain("Repeater 'rowsHidden.rows' has no sub-field GraphQL can write")
            .And.Contain("'label' (Hidden = true)").And.Contain("Make at least one sub-field writable");
    }

    [SqliteOnlyFact("starts a host on a SQLite temp database")]
    public async Task A_Repeater_whose_sub_fields_are_all_ReadOnly_fails_at_host_start()
    {
        var error = await StartAsync(WithRows(
            "RowsReadOnly", new EmittedProperty("Label", typeof(string), FieldInterface.Text, ReadOnly: true)));

        error.Should().BeOfType<MetadataException>().Which.Message
            .Should().Contain("Repeater 'rowsReadOnly.rows' has no sub-field GraphQL can write")
            .And.Contain("'label' (ReadOnly = true)");
    }

    [SqliteOnlyFact("starts a host on a SQLite temp database")]
    public async Task A_collection_whose_only_input_is_the_translations_starts_the_host()
    {
        var error = await StartAsync(new EmittedEntity(
            "OnlyTranslations", [new EmittedProperty("Secret", typeof(string), FieldInterface.Hidden)],
            [new EmittedProperty("Title", typeof(string), FieldInterface.Text)]));

        error.Should().BeNull();
    }
}
