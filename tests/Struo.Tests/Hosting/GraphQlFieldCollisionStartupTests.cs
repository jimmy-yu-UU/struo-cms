using AwesomeAssertions;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace Struo.Tests.Hosting;

/// <summary>
/// A GraphQL field name declared twice in one type stops the host at start with the message that names the
/// collection, the type, the field and both declarations.
/// </summary>
[Collection("ApiIntegration")]
public sealed class GraphQlFieldCollisionStartupTests(ITestOutputHelper output)
{
    private static async Task<Exception?> StartAsync(params EmittedEntity[] entities) =>
        await ForkHost.StartupErrorAsync(
            mutate: s => s["Struo:ContentAssemblies:1"] = EmittedEntities.Build(EmittedEntities.UniqueName("gfc"), entities).GetName().Name!,
            environment: "Production");

    [SqliteOnlyFact("starts a host on a SQLite temp database")]
    public async Task An_Image_field_and_a_relation_on_another_key_fail_at_host_start_naming_both()
    {
        var error = await StartAsync(
            new EmittedEntity("GfcHostTarget", [new EmittedProperty("Name", typeof(string), FieldInterface.Text)]),
            new EmittedEntity(
                "GfcHostOwner",
                [
                    new EmittedProperty("CoverId", typeof(Guid?), FieldInterface.Image, Nullable: true),
                    new EmittedProperty("CoverRef", typeof(Guid?), FieldInterface.Uuid, Nullable: true),
                ],
                Relations: [new EmittedRelation("Cover", "GfcHostTarget", "CoverRef")]));

        output.WriteLine(error?.ToString());
        error.Should().BeOfType<MetadataException>().Which.Message
            .Should().Contain("collection 'gfcHostOwner'")
            .And.Contain("The type 'GfcHostOwner' declares the field 'cover' more than once")
            .And.Contain("the Image field 'GfcHostOwner.CoverId', whose File object is exposed as 'cover'")
            .And.Contain("the relation 'GfcHostOwner.Cover'");
    }

    [SqliteOnlyFact("starts a host on a SQLite temp database")]
    public async Task A_field_named_Version_fails_at_host_start_naming_the_field_and_the_CMS_field()
    {
        var error = await StartAsync(new EmittedEntity(
            "GfcHostVersion", [new EmittedProperty("Version", typeof(long), FieldInterface.Number)]));

        error.Should().BeOfType<MetadataException>().Which.Message
            .Should().Contain("The type 'GfcHostVersion' declares the field 'version' more than once")
            .And.Contain("the 'version' field the CMS adds and the Number field 'GfcHostVersion.Version'");
    }
}
