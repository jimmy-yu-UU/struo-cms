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
/// A many-to-one foreign key that is also a declared, visible field is returned by an item read, so an update that
/// sends it back with the stored value needs no read on its target. The key types are covered here with
/// hand-built metadata and a row type without initialisers: a <c>Guid?</c>, <c>int?</c>, <c>long?</c> and
/// <c>string</c> key, a <c>Hidden</c> declared key and a key that is not a field.
/// </summary>
public sealed class RelationTargetReadGuardDeclaredKeyTests
{
    private const string Target = "partner";
    private static readonly Guid StoredGuid = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherGuid = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private sealed class Row
    {
        public Guid Id { get; set; }
        public Guid? UuidKey { get; set; }
        public int? IntKey { get; set; }
        public long? LongKey { get; set; }
        public string? TextKey { get; set; }
        public Guid? HiddenKey { get; set; }
        public Guid? PlainKey { get; set; }
    }

    private sealed class NoJunctions : IM2MDescriptorSource
    {
        public IReadOnlyList<M2MDescriptor> M2MDescriptors(string collection) => [];
        public IReadOnlyList<JunctionKeyLink> JunctionKeyLinks(string junctionCollection) => [];
    }

    private sealed class ReadsNothing : IPermissionService
    {
        public bool CanRead(string collection) => false;
        public bool CanWrite(string collection) => true;
        public bool CanDelete(string collection) => true;

        public IReadOnlyCollection<string> ReadableFields(string collection, IEnumerable<string> allFieldNames) =>
            allFieldNames.ToList();
    }

    private static readonly RelationTargetReadGuard Guard = new(new NoJunctions(), new ReadsNothing());

    private static readonly EntityDescriptor Descriptor = new(
        typeof(Row),
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = "Id", ["uuidKey"] = "UuidKey", ["intKey"] = "IntKey", ["longKey"] = "LongKey",
            ["textKey"] = "TextKey", ["hiddenKey"] = "HiddenKey",
        },
        "Id");

    private static FieldMetadata Declared(string name, FieldInterface face, bool hidden = false) =>
        new() { Name = name, Label = name, Interface = face, Hidden = hidden };

    private static RelationMetadata ManyToOne(string foreignKey) => new()
    {
        Name = foreignKey + "Row", Label = foreignKey, Kind = RelationKind.ManyToOne, TargetCollection = Target,
        Interface = RelationInterface.Dropdown, ForeignKey = foreignKey,
    };

    private static readonly CollectionMetadata Meta = new()
    {
        Name = "owner", Label = "Owner", FieldGroups = [],
        Fields =
        [
            Declared("uuidKey", FieldInterface.Text), Declared("intKey", FieldInterface.Number),
            Declared("longKey", FieldInterface.Number), Declared("textKey", FieldInterface.Text),
            Declared("hiddenKey", FieldInterface.Text, hidden: true),
        ],
        Relations =
        [
            ManyToOne("uuidKey"), ManyToOne("intKey"), ManyToOne("longKey"), ManyToOne("textKey"),
            ManyToOne("hiddenKey"), ManyToOne("plainKey"),
        ],
    };

    private static Row Stored() => new()
    {
        Id = Guid.NewGuid(), UuidKey = StoredGuid, IntKey = 10, LongKey = 5_000_000_000L, TextKey = "alpha",
        HiddenKey = StoredGuid, PlainKey = StoredGuid,
    };

    private static JsonElement Body(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    // The checks of an update in the order the service runs them: before the stored row is loaded, after it.
    private static void Update(JsonElement body, Row stored)
    {
        Guard.RequireRelationLinks(Meta, body);
        Guard.RequireStoredKeys(Meta, Descriptor, body, stored);
    }

    public static TheoryData<string> UnchangedBodies() => new()
    {
        $$"""{"uuidKey":"{{StoredGuid}}"}""",
        $$"""{"uuidKey":"{{StoredGuid.ToString().ToUpperInvariant()}}"}""",
        """{"intKey":10}""",
        """{"longKey":5000000000}""",
        """{"textKey":"alpha"}""",
        $$"""{"uuidKey":"{{StoredGuid}}","UUIDKEY":"{{StoredGuid}}","intKey":10,"longKey":5000000000,"textKey":"alpha"}""",
    };

    [Theory]
    [MemberData(nameof(UnchangedBodies))]
    public void A_key_sent_back_with_its_stored_value_needs_no_read_on_the_target(string json)
    {
        var act = () => Update(Body(json), Stored());

        act.Should().NotThrow();
    }

    [Fact]
    public void A_stored_null_sent_as_null_needs_no_read_on_the_target()
    {
        var stored = Stored();
        stored.UuidKey = null;
        stored.IntKey = null;
        stored.TextKey = null;

        var act = () => Update(Body("""{"uuidKey":null,"intKey":null,"textKey":null}"""), stored);

        act.Should().NotThrow();
    }

    public static TheoryData<string> ChangedBodies() => new()
    {
        $$"""{"uuidKey":"{{OtherGuid}}"}""",
        """{"uuidKey":"not-an-id"}""",
        """{"uuidKey":null}""",
        """{"intKey":11}""",
        """{"intKey":"10"}""",
        """{"intKey":10.0}""",
        """{"longKey":5000000001}""",
        """{"textKey":"ALPHA"}""",
        """{"textKey":"alpha "}""",
        """{"textKey":null}""",
        $$"""{"uuidKey":"{{StoredGuid}}","UUIDKEY":"{{OtherGuid}}"}""",
        $$"""{"UUIDKEY":"{{OtherGuid}}","uuidKey":"{{StoredGuid}}"}""",
        $$"""{"intKey":10,"intkey":11}""",
        $$"""{"uuidKey":"{{StoredGuid}}","textKey":"beta"}""",
    };

    [Theory]
    [MemberData(nameof(ChangedBodies))]
    public void A_changed_value_in_any_spelling_is_refused(string json)
    {
        var act = () => Update(Body(json), Stored());

        act.Should().Throw<PermissionDeniedException>().WithMessage($"Read not permitted on '{Target}'.");
    }

    [Fact]
    public void A_stored_value_sent_for_a_stored_null_is_a_change()
    {
        var stored = Stored();
        stored.IntKey = null;

        var act = () => Update(Body("""{"intKey":10}"""), stored);

        act.Should().Throw<PermissionDeniedException>();
    }

    [Theory]
    [InlineData("hiddenKey")]
    [InlineData("plainKey")]
    public void A_key_an_item_read_does_not_return_is_refused_even_with_the_stored_value(string key)
    {
        var act = () => Update(Body($$"""{"{{key}}":"{{StoredGuid}}"}"""), Stored());

        act.Should().Throw<PermissionDeniedException>();
    }

    [Theory]
    [InlineData("hiddenKey")]
    [InlineData("plainKey")]
    public void A_key_an_item_read_does_not_return_is_refused_before_the_stored_row_is_needed(string key)
    {
        var act = () => Guard.RequireRelationLinks(Meta, Body($$"""{"{{key}}":"{{OtherGuid}}"}"""));

        act.Should().Throw<PermissionDeniedException>();
    }

    [Fact]
    public void A_declared_key_is_not_refused_before_the_stored_row_is_loaded()
    {
        var act = () => Guard.RequireRelationLinks(Meta, Body($$"""{"uuidKey":"{{OtherGuid}}"}"""));

        act.Should().NotThrow();
    }

    [Fact]
    public void A_create_refuses_a_declared_key_whatever_its_value()
    {
        var act = () => Guard.Require(Meta, Body($$"""{"uuidKey":"{{StoredGuid}}"}"""));

        act.Should().Throw<PermissionDeniedException>();
    }

    [Fact]
    public void A_no_value_spelling_of_a_key_still_needs_read_on_the_target()
    {
        var act = () => Guard.Require(Meta, Body("""{"textKey":"","intKey":0}"""));

        act.Should().Throw<PermissionDeniedException>();
    }

    [Fact]
    public void A_revert_leaves_a_declared_key_out_whatever_its_value()
    {
        var keys = Guard.UnreadableKeys(Meta, Body($$"""{"uuidKey":"{{StoredGuid}}","intKey":10}"""));

        keys.Select(link => link.Key).Should().Equal("uuidKey", "intKey");
    }
}
