using System.Text.Json;
using AwesomeAssertions;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Application.Security;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.Query;

/// <summary>
/// The target-read guard looks only at keys a body can set. Metadata is built by hand here for keys the scanner
/// does not accept, so the scanner is skipped: a junction key that is system or <c>ReadOnly</c>
/// (<c>ValidateOneJunctionCollection</c> refuses it), and a translatable file field that is <c>ReadOnly</c>.
/// </summary>
public sealed class RelationTargetReadGuardServerOwnedTests
{
    private const string Junction = "ledgerTag";

    private sealed class JunctionSource : IM2MDescriptorSource
    {
        public IReadOnlyList<M2MDescriptor> M2MDescriptors(string collection) => [];

        public IReadOnlyList<JunctionKeyLink> JunctionKeyLinks(string junctionCollection) =>
            junctionCollection == Junction ? [new JunctionKeyLink("LedgerId", "ledger", "TagId", "tag")] : [];
    }

    private sealed class ReadsNothing : IPermissionService
    {
        public bool CanRead(string collection) => false;
        public bool CanWrite(string collection) => true;
        public bool CanDelete(string collection) => true;

        public IReadOnlyCollection<string> ReadableFields(string collection, IEnumerable<string> allFieldNames) =>
            allFieldNames.ToList();
    }

    private static readonly RelationTargetReadGuard Guard = new(new JunctionSource(), new ReadsNothing());

    private static FieldMetadata Key(string name, bool readOnly, bool system = false) => new()
    {
        Name = name, Label = name, Interface = FieldInterface.Uuid, ReadOnly = readOnly, IsSystem = system,
    };

    private static CollectionMetadata JunctionOf(bool readOnly, bool system = false) => new()
    {
        Name = Junction, Label = Junction, FieldGroups = [],
        Fields = [Key("ledgerId", readOnly, system), Key("tagId", readOnly, system)],
    };

    private static CollectionMetadata ImageHolder(bool readOnly, bool translatable) => new()
    {
        Name = "holder", Label = "Holder", FieldGroups = [],
        Fields =
        [
            new FieldMetadata
            {
                Name = "coverId", Label = "Cover", Interface = FieldInterface.Image,
                ReadOnly = readOnly, Translatable = translatable,
            },
        ],
    };

    private static JsonElement Body(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private const string JunctionBody = """{"ledgerId":"11111111-1111-1111-1111-111111111111","TAGID":"22222222-2222-2222-2222-222222222222"}""";

    [Fact]
    public void A_writable_junction_key_needs_read_on_its_target()
    {
        var act = () => Guard.Require(JunctionOf(readOnly: false), Body(JunctionBody));

        act.Should().Throw<PermissionDeniedException>();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void A_junction_key_that_is_read_only_or_system_needs_no_read(bool readOnly, bool system)
    {
        var meta = JunctionOf(readOnly, system);
        var body = Body(JunctionBody);

        var act = () => Guard.Require(meta, body);

        act.Should().NotThrow();
        Guard.UnreadableKeys(meta, body).Should().BeEmpty();
    }

    [Fact]
    public void A_junction_key_is_judged_by_its_own_field_only()
    {
        var meta = new CollectionMetadata
        {
            Name = Junction, Label = Junction, FieldGroups = [],
            Fields = [Key("ledgerId", readOnly: true), Key("tagId", readOnly: false)],
        };

        Guard.UnreadableKeys(meta, Body(JunctionBody)).Select(link => link.Target).Should().Equal("tag");
    }

    [Fact]
    public void A_read_only_image_field_needs_no_read_and_a_writable_one_does()
    {
        var body = Body("""{"coverId":"11111111-1111-1111-1111-111111111111"}""");

        var readOnly = () => Guard.Require(ImageHolder(readOnly: true, translatable: false), body);
        var writable = () => Guard.Require(ImageHolder(readOnly: false, translatable: false), body);

        readOnly.Should().NotThrow();
        writable.Should().Throw<PermissionDeniedException>();
    }

    // The scanner refuses ReadOnly on a translatable field at startup, so a collection never reaches the guard
    // with one; built by hand, the guard still treats the field as a link the body can set.
    [Fact]
    public void A_translatable_file_field_is_guarded_even_when_marked_read_only()
    {
        var body = Body("""{"translations":{"en":{"coverId":"11111111-1111-1111-1111-111111111111"}}}""");

        var act = () => Guard.Require(ImageHolder(readOnly: true, translatable: true), body);

        act.Should().Throw<PermissionDeniedException>();
    }
}
