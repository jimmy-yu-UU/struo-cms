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

// On an update a junction key equal to the stored value needs no read on its target, unless the key field is
// Hidden (the item read does not return it); a string key is matched exactly.
public sealed class JunctionUnchangedKeyGuardTests
{
    private sealed class Row
    {
        public string OwnerId { get; set; } = "";
        public Guid? TargetId { get; set; }
    }

    private sealed class Source : IM2MDescriptorSource
    {
        public IReadOnlyList<M2MDescriptor> M2MDescriptors(string collection) => [];

        public IReadOnlyList<JunctionKeyLink> JunctionKeyLinks(string junctionCollection) =>
            [new JunctionKeyLink("OwnerId", "owner", "TargetId", "target")];
    }

    private sealed class Permissions : IPermissionService
    {
        public bool CanRead(string collection) => false;
        public bool CanWrite(string collection) => true;
        public bool CanDelete(string collection) => false;
        public IReadOnlyCollection<string> ReadableFields(string collection, IEnumerable<string> allFieldNames) => [];
    }

    private static readonly EntityDescriptor Descriptor = new(
        typeof(Row), new Dictionary<string, string> { ["ownerId"] = "OwnerId", ["targetId"] = "TargetId" }, "OwnerId");

    private static CollectionMetadata Meta(bool hiddenTarget) => new()
    {
        Name = "junction", Label = "Junction", FieldGroups = [],
        Fields =
        [
            new FieldMetadata { Name = "ownerId", Label = "Owner", Interface = FieldInterface.Text },
            new FieldMetadata { Name = "targetId", Label = "Target", Interface = FieldInterface.Uuid, Hidden = hiddenTarget },
        ],
    };

    private static void Require(bool hiddenTarget, Row stored, string json)
    {
        var guard = new RelationTargetReadGuard(new Source(), new Permissions());
        using var body = JsonDocument.Parse(json);
        guard.RequireJunctionKeys(Meta(hiddenTarget), Descriptor, body.RootElement, stored);
    }

    private static string Body(string owner, Guid target) => $$"""{"ownerId":"{{owner}}","targetId":"{{target}}"}""";

    [Fact]
    public void Keys_equal_to_the_stored_ones_pass_without_read()
    {
        var target = Guid.NewGuid();

        var act = () => Require(false, new Row { OwnerId = "ABC", TargetId = target }, Body("ABC", target));

        act.Should().NotThrow();
    }

    [Fact]
    public void A_string_key_differing_only_in_case_is_checked_like_a_new_value()
    {
        var target = Guid.NewGuid();

        var act = () => Require(false, new Row { OwnerId = "ABC", TargetId = target }, Body("abc", target));

        act.Should().Throw<PermissionDeniedException>().WithMessage("Read not permitted on 'owner'.");
    }

    [Fact]
    public void A_key_in_two_spellings_is_unchanged_only_when_every_spelling_holds_the_stored_value()
    {
        var target = Guid.NewGuid();
        var stored = new Row { OwnerId = "ABC", TargetId = target };

        var both = () => Require(false, stored, $$"""{"targetId":"{{target}}","TargetId":"{{target}}"}""");
        var last = () => Require(false, stored, $$"""{"targetId":"{{target}}","TargetId":"{{Guid.NewGuid()}}"}""");
        var first = () => Require(false, stored, $$"""{"targetId":"{{Guid.NewGuid()}}","TargetId":"{{target}}"}""");

        both.Should().NotThrow();
        last.Should().Throw<PermissionDeniedException>().WithMessage("Read not permitted on 'target'.");
        first.Should().Throw<PermissionDeniedException>().WithMessage("Read not permitted on 'target'.");
    }

    [Fact]
    public void A_changed_key_is_refused()
    {
        var stored = new Row { OwnerId = "ABC", TargetId = Guid.NewGuid() };

        var act = () => Require(false, stored, Body("ABC", Guid.NewGuid()));

        act.Should().Throw<PermissionDeniedException>().WithMessage("Read not permitted on 'target'.");
    }

    [Fact]
    public void A_null_key_is_not_unchanged_when_the_stored_key_is_not_null()
    {
        var stored = new Row { OwnerId = "ABC", TargetId = Guid.NewGuid() };

        var act = () => Require(false, stored, """{"ownerId":null}""");

        act.Should().Throw<PermissionDeniedException>().WithMessage("Read not permitted on 'owner'.");
    }

    [Fact]
    public void A_stored_null_key_sent_as_null_is_unchanged()
    {
        var act = () => Require(false, new Row { OwnerId = "ABC", TargetId = null }, """{"targetId":null}""");

        act.Should().NotThrow();
    }

    [Fact]
    public void A_stored_null_key_sent_as_a_value_is_checked()
    {
        var act = () => Require(false, new Row { OwnerId = "ABC", TargetId = null }, Body("ABC", Guid.NewGuid()));

        act.Should().Throw<PermissionDeniedException>().WithMessage("Read not permitted on 'target'.");
    }

    [Fact]
    public void A_hidden_key_is_not_exempt_even_when_equal()
    {
        var target = Guid.NewGuid();

        var act = () => Require(true, new Row { OwnerId = "ABC", TargetId = target }, Body("ABC", target));

        act.Should().Throw<PermissionDeniedException>().WithMessage("Read not permitted on 'target'.");
    }
}
