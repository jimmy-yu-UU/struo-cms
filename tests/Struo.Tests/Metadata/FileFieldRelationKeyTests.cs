using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Metadata;

/// <summary>
/// A <c>File</c> or <c>Image</c> field that is also the key of a many-to-one relation is two declarations of one
/// link and is refused at the scan, whatever the relation's name and target. A file field without a relation, a
/// relation on a key with another interface, and a relation on a key with no <c>[CmsField]</c> are accepted.
/// </summary>
public sealed class FileFieldRelationKeyTests
{
    [SugarTable("ffrk_target")]
    [CmsCollection("ffrkTarget", Name = "ffrkTarget")]
    private sealed class OtherTarget
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    }

    [SugarTable("ffrk_image_to_file")]
    [CmsCollection("ffrkImageToFile", Name = "ffrkImageToFile")]
    private sealed class ImageKeyToFile
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Cover", Interface = FieldInterface.Image)]
        public Guid? CoverId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(CoverId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)]
        public Struo.Infrastructure.Files.File? Cover { get; set; }
    }

    [SugarTable("ffrk_file_to_file")]
    [CmsCollection("ffrkFileToFile", Name = "ffrkFileToFile")]
    private sealed class FileKeyToFile
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Attachment", Interface = FieldInterface.File)]
        public Guid? AttachmentId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(AttachmentId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)]
        public Struo.Infrastructure.Files.File? Attachment { get; set; }
    }

    [SugarTable("ffrk_image_to_other")]
    [CmsCollection("ffrkImageToOther", Name = "ffrkImageToOther")]
    private sealed class ImageKeyToOther
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Cover", Interface = FieldInterface.Image)]
        public Guid? CoverId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(CoverId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)]
        public OtherTarget? Picture { get; set; }
    }

    [SugarTable("ffrk_files_key")]
    [CmsCollection("ffrkFilesKey", Name = "ffrkFilesKey")]
    private sealed class FilesListKey
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [SugarColumn(IsJson = true, IsNullable = true)]
        [CmsField(Label = "Gallery", Interface = FieldInterface.Files)]
        public List<Guid>? GalleryIds { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(GalleryIds))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)]
        public Struo.Infrastructure.Files.File? Gallery { get; set; }
    }

    [SugarTable("ffrk_image_no_relation")]
    [CmsCollection("ffrkImageNoRelation", Name = "ffrkImageNoRelation")]
    private sealed class ImageWithoutRelation
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Cover", Interface = FieldInterface.Image)]
        public Guid? CoverId { get; set; }
    }

    [SugarTable("ffrk_uuid_key")]
    [CmsCollection("ffrkUuidKey", Name = "ffrkUuidKey")]
    private sealed class UuidKeyToFile
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Cover", Interface = FieldInterface.Uuid)]
        public Guid? CoverId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(CoverId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)]
        public Struo.Infrastructure.Files.File? Cover { get; set; }
    }

    [SugarTable("ffrk_hidden_key")]
    [CmsCollection("ffrkHiddenKey", Name = "ffrkHiddenKey")]
    private sealed class HiddenInterfaceKeyToFile
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Cover", Interface = FieldInterface.Hidden)]
        public Guid? CoverId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(CoverId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)]
        public Struo.Infrastructure.Files.File? Cover { get; set; }
    }

    [SugarTable("ffrk_plain_key")]
    [CmsCollection("ffrkPlainKey", Name = "ffrkPlainKey")]
    private sealed class PlainKeyToFile
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsNullable = true)] public Guid? CoverId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(CoverId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)]
        public Struo.Infrastructure.Files.File? Cover { get; set; }
    }

    private static MetadataException ScanRefusal(params Type[] types) =>
        FluentActions.Invoking(() => MetadataScanner.ScanTypes(types)).Should().Throw<MetadataException>().Which;

    private static readonly Type[] FileCollections = [typeof(Struo.Infrastructure.Files.File), typeof(MediaFolder)];

    [Theory]
    [InlineData(typeof(ImageKeyToFile), "Cover", "CoverId", "Image")]
    [InlineData(typeof(FileKeyToFile), "Attachment", "AttachmentId", "File")]
    public void A_file_or_image_field_that_is_a_relation_key_to_the_file_collection_is_refused(
        Type owner, string relation, string key, string face)
    {
        var message = ScanRefusal([.. FileCollections, owner]).Message;

        message.Should().Be(
            $"Relation '{owner.Name}.{relation}' uses '{owner.Name}.{key}' as its key, but '{key}' is a " +
            $"[CmsField(Interface = {face})] property: a File or Image field already resolves to the file (REST " +
            "returns the id, GraphQL the File object, and the write checks of the file field apply), so the relation " +
            $"is a second declaration of the same link. Remove [CmsRelation] and [Navigate] from '{relation}', or give " +
            $"'{key}' a non-file interface such as Uuid if the target is not the file collection.");
    }

    [Fact]
    public void A_file_or_image_field_that_is_a_relation_key_to_another_collection_is_refused()
    {
        var message = ScanRefusal(typeof(OtherTarget), typeof(ImageKeyToOther)).Message;

        message.Should().StartWith("Relation 'ImageKeyToOther.Picture' uses 'ImageKeyToOther.CoverId' as its key, but 'CoverId' is a [CmsField(Interface = Image)] property")
            .And.Contain("Remove [CmsRelation] and [Navigate] from 'Picture'");
    }

    [Fact]
    public void A_Files_list_property_cannot_be_a_relation_key_because_a_key_is_typed_as_the_primary_key()
    {
        var message = ScanRefusal([.. FileCollections, typeof(FilesListKey)]).Message;

        message.Should().Contain("Relation 'FilesListKey.Gallery' uses the key 'FilesListKey.GalleryIds', a List<Guid> property");
    }

    [Fact]
    public void A_file_field_without_a_relation_is_accepted()
    {
        FluentActions.Invoking(() => MetadataScanner.ScanTypes([typeof(ImageWithoutRelation)])).Should().NotThrow();
    }

    [Theory]
    [InlineData(typeof(UuidKeyToFile))]
    [InlineData(typeof(HiddenInterfaceKeyToFile))]
    [InlineData(typeof(PlainKeyToFile))]
    public void A_relation_on_a_key_that_is_not_a_file_field_is_accepted(Type owner)
    {
        FluentActions.Invoking(() => MetadataScanner.ScanTypes([.. FileCollections, owner])).Should().NotThrow();
    }
}
