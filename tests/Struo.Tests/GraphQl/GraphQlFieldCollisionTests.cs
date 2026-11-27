using AwesomeAssertions;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Api.GraphQl;
using Struo.Application.Metadata;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.GraphQl;

/// <summary>
/// A GraphQL field name declared twice in one type stops the schema build with a message that names the
/// collection, the type, the field and both declarations, whichever builder added the second one. The same
/// name in two different types is allowed.
/// </summary>
public sealed class GraphQlFieldCollisionTests
{
    [SugarTable("gfc_target")] [CmsCollection("GfcTarget", Name = "gfcTarget")]
    public sealed class Target
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Name", Interface = FieldInterface.Text)] public string Name { get; set; } = "";
    }

    // An Image key and a relation on another key, named like the File object the Image field adds.
    [SugarTable("gfc_image")] [CmsCollection("GfcImage", Name = "gfcImage")]
    public sealed class ImageAndRelation
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsNullable = true)] [CmsField(Label = "Cover", Interface = FieldInterface.Image)] public Guid? CoverId { get; set; }
        [SugarColumn(IsNullable = true)] public Guid? CoverRef { get; set; }
        [Navigate(NavigateType.ManyToOne, nameof(CoverRef))] [CmsRelation(Interface = RelationInterface.Dropdown)] [SugarColumn(IsIgnore = true)]
        public Target? Cover { get; set; }
    }

    // The Image field and the relation share the key: the scan refuses it before any schema is built.
    [SugarTable("gfc_image_key")] [CmsCollection("GfcImageKey", Name = "gfcImageKey")]
    public sealed class ImageKeyRelation
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsNullable = true)] [CmsField(Label = "Cover", Interface = FieldInterface.Image)] public Guid? CoverId { get; set; }
        [Navigate(NavigateType.ManyToOne, nameof(CoverId))] [CmsRelation(Interface = RelationInterface.Dropdown)] [SugarColumn(IsIgnore = true)]
        public Target? Cover { get; set; }
    }

    [SugarTable("gfc_translated_image")] [CmsCollection("GfcTranslatedImage", Name = "gfcTranslatedImage")]
    public sealed class TranslatedImageAndRelation
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsNullable = true)] public Guid? CoverRef { get; set; }
        [Navigate(NavigateType.ManyToOne, nameof(CoverRef))] [CmsRelation(Interface = RelationInterface.Dropdown)] [SugarColumn(IsIgnore = true)]
        public Target? Cover { get; set; }
        [CmsTranslations(typeof(TranslatedImageText))] [SugarColumn(IsIgnore = true)] public List<TranslatedImageText> Translations { get; set; } = [];
    }

    public sealed class TranslatedImageText
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid TranslatedImageAndRelationId { get; set; }
        public string Locale { get; set; } = "";
        [SugarColumn(IsNullable = true)] [CmsField(Label = "Cover", Interface = FieldInterface.Image, Translatable = true)] public Guid? CoverId { get; set; }
    }

    [SugarTable("gfc_translations")] [CmsCollection("GfcTranslations", Name = "gfcTranslations")]
    public sealed class TranslationsField
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Translations", Interface = FieldInterface.Text)] public string Translations { get; set; } = "";
        [CmsTranslations(typeof(TranslationsFieldText))] [SugarColumn(IsIgnore = true)] public List<TranslationsFieldText> Locales { get; set; } = [];
    }

    public sealed class TranslationsFieldText
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid TranslationsFieldId { get; set; }
        public string Locale { get; set; } = "";
        [CmsField(Label = "Title", Interface = FieldInterface.Text, Translatable = true)] public string Title { get; set; } = "";
    }

    [SugarTable("gfc_id")] [CmsCollection("GfcId", Name = "gfcId")]
    public sealed class IdField
    {
        [SugarColumn(IsPrimaryKey = true)] [CmsField(Label = "Id", Interface = FieldInterface.Text)] public Guid Id { get; set; }
    }

    [SugarTable("gfc_version")] [CmsCollection("GfcVersion", Name = "gfcVersion")]
    public sealed class VersionField
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Name", Interface = FieldInterface.Text)] public string Name { get; set; } = "";
        [CmsField(Label = "Version", Interface = FieldInterface.Number)] public long Version { get; set; }
    }

    [SugarTable("gfc_links")] [CmsCollection("GfcLinks", Name = "gfcLinks")]
    public sealed class LinksField
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Links", Interface = FieldInterface.Text)] public string ChildrenLinks { get; set; } = "";
        [Navigate(typeof(LinksFieldLink), nameof(LinksFieldLink.LinksFieldId), nameof(LinksFieldLink.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)] [SugarColumn(IsIgnore = true)]
        public List<Target> Children { get; set; } = [];
    }

    [SugarTable("gfc_links_link")] [CmsCollection("GfcLinksLink", Name = "gfcLinksLink", Hidden = true)]
    public sealed class LinksFieldLink
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Parent", Interface = FieldInterface.Uuid)] public Guid LinksFieldId { get; set; }
        [CmsField(Label = "Target", Interface = FieldInterface.Uuid)] public Guid TargetId { get; set; }
        [CmsField(Label = "Note", Interface = FieldInterface.Text)] public string? Note { get; set; }
    }

    [SugarTable("gfc_payload")] [CmsCollection("GfcPayload", Name = "gfcPayload")]
    public sealed class PayloadOwner
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [Navigate(typeof(PayloadLink), nameof(PayloadLink.PayloadOwnerId), nameof(PayloadLink.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)] [SugarColumn(IsIgnore = true)]
        public List<Target> Children { get; set; } = [];
    }

    [SugarTable("gfc_payload_link")] [CmsCollection("GfcPayloadLink", Name = "gfcPayloadLink", Hidden = true)]
    public sealed class PayloadLink
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Parent", Interface = FieldInterface.Uuid)] public Guid PayloadOwnerId { get; set; }
        [CmsField(Label = "Target", Interface = FieldInterface.Uuid)] public Guid TargetId { get; set; }
        [CmsField(Label = "ID", Interface = FieldInterface.Text)] public string? ID { get; set; }
    }

    [SugarTable("gfc_case")] [CmsCollection("GfcCase", Name = "gfcCase")]
    public sealed class CaseDiffers
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Title", Interface = FieldInterface.Text)] public string Title { get; set; } = "";
        [CmsField(Label = "TItle", Interface = FieldInterface.Text)] public string TItle { get; set; } = "";
    }

    [SugarTable("gfc_case_same")] [CmsCollection("GfcCaseSame", Name = "gfcCaseSame")]
    public sealed class CaseSame
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Title", Interface = FieldInterface.Text)] public string Title { get; set; } = "";
        [CmsField(Label = "TITLE", Interface = FieldInterface.Text)] public string TITLE { get; set; } = "";
    }

    [SugarTable("gfc_image_text")] [CmsCollection("GfcImageText", Name = "gfcImageText")]
    public sealed class ImageAndText
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsNullable = true)] [CmsField(Label = "Cover", Interface = FieldInterface.Image)] public Guid? CoverId { get; set; }
        [CmsField(Label = "Cover", Interface = FieldInterface.Text)] public string Cover { get; set; } = "";
    }

    [SugarTable("gfc_files_text")] [CmsCollection("GfcFilesText", Name = "gfcFilesText")]
    public sealed class FilesAndText
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsJson = true, IsNullable = true)] [CmsField(Label = "Gallery", Interface = FieldInterface.Files)] public List<Guid>? Gallery { get; set; }
        [CmsField(Label = "Files", Interface = FieldInterface.Text)] public string GalleryFiles { get; set; } = "";
    }

    [SugarTable("gfc_relation_version")] [CmsCollection("GfcRelationVersion", Name = "gfcRelationVersion")]
    public sealed class RelationVersion
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsNullable = true)] public Guid? Ref { get; set; }
        [Navigate(NavigateType.ManyToOne, nameof(Ref))] [CmsRelation(Interface = RelationInterface.Dropdown)] [SugarColumn(IsIgnore = true)]
        public Target? Version { get; set; }
    }

    [SugarTable("gfc_repeater")] [CmsCollection("GfcRepeater", Name = "gfcRepeater")]
    public sealed class RepeaterCase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsJson = true, IsNullable = true)] [CmsField(Label = "Rows", Interface = FieldInterface.Repeater)]
        public List<RepeaterRow>? Rows { get; set; }
    }

    public sealed class RepeaterRow
    {
        [CmsField(Label = "Label", Interface = FieldInterface.Text)] public string Label { get; set; } = "";
        [CmsField(Label = "LABEL", Interface = FieldInterface.Text)] public string LABEL { get; set; } = "";
    }

    private static async Task<Exception?> BuildAsync(params Type[] content)
    {
        try
        {
            Type[] types = [.. FrameworkEntityTypes.All, .. content];
            var collections = MetadataScanner.ScanTypes(types);
            await new ServiceCollection()
                .AddSingleton<IMetadataProvider>(new CachedMetadataProvider(collections))
                .AddSingleton<IEntityRegistry>(new EntityRegistry(MetadataScanner.ScanDescriptors(types)))
                .AddSingleton<IM2MDescriptorSource>(new RelationshipGraph(collections, MetadataScanner.CollectionTypeMap(types)))
                .AddScoped<IGraphQlDataSource, FakeGraphQlDataSource>()
                .AddSingleton<StruoTypeModule>()
                .AddGraphQLServer()
                .AddStruoSchemaTypes()
                .BuildRequestExecutorAsync();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static async Task<string> RefusalAsync(params Type[] content)
    {
        var error = await BuildAsync(content);
        error.Should().NotBeNull("the collection declares a field name twice");
        var refusal = ExceptionChainSearch.FindInner<MetadataException>(error);
        refusal.Should().NotBeNull(error!.ToString());
        return refusal!.Message;
    }

    [Fact]
    public async Task An_Image_field_and_a_relation_on_another_key_name_the_File_object_and_the_relation()
    {
        var message = await RefusalAsync(typeof(Target), typeof(ImageAndRelation));

        message.Should().Contain("collection 'gfcImage'")
            .And.Contain("The type 'GfcImage' declares the field 'cover' more than once")
            .And.Contain("the Image field 'ImageAndRelation.CoverId', whose File object is exposed as 'cover'")
            .And.Contain("the relation 'ImageAndRelation.Cover'")
            .And.Contain("Rename one of the declarations");
    }

    [Fact]
    public async Task A_translatable_Image_field_and_a_relation_on_the_parent_name_both_declarations()
    {
        var message = await RefusalAsync(typeof(Target), typeof(TranslatedImageAndRelation));

        message.Should().Contain("The type 'GfcTranslatedImage' declares the field 'cover' more than once")
            .And.Contain("the Image field 'TranslatedImageText.CoverId', whose File object is exposed as 'cover'")
            .And.Contain("the relation 'TranslatedImageAndRelation.Cover'");
    }

    [Fact]
    public async Task A_field_named_translations_collides_in_the_output_type_and_both_write_inputs()
    {
        var message = await RefusalAsync(typeof(TranslationsField));

        message.Should().Contain("collection 'gfcTranslations'")
            .And.Contain("The type 'GfcTranslations' declares the field 'translations'")
            .And.Contain("The type 'GfcTranslationsCreateInput' declares the field 'translations'")
            .And.Contain("The type 'GfcTranslationsUpdateInput' declares the field 'translations'")
            .And.Contain("the Text field 'TranslationsField.Translations' and the translations of the translation sidecar of 'gfcTranslations'");
    }

    [Fact]
    public async Task A_CmsField_on_Id_collides_in_the_output_type_and_the_filter_input()
    {
        var message = await RefusalAsync(typeof(IdField));

        message.Should().Contain("The type 'GfcId' declares the field 'id' more than once")
            .And.Contain("The type 'GfcIdFilterInput' declares the field 'id' more than once")
            .And.Contain("the 'id' field the CMS adds and the Text field 'IdField.Id'");
    }

    [Fact]
    public async Task A_CmsField_named_Version_on_a_collection_collides_with_the_version_the_CMS_adds()
    {
        var message = await RefusalAsync(typeof(VersionField));

        message.Should().Contain("The type 'GfcVersion' declares the field 'version' more than once")
            .And.Contain("The type 'GfcVersionUpdateInput' declares the field 'version' more than once")
            .And.Contain("the 'version' field the CMS adds and the Number field 'VersionField.Version'");
    }

    [Fact]
    public async Task A_field_named_like_the_links_of_a_many_to_many_with_payload_names_the_relation()
    {
        var message = await RefusalAsync(typeof(Target), typeof(LinksField), typeof(LinksFieldLink));

        message.Should().Contain("collection 'gfcLinks'")
            .And.Contain("The type 'GfcLinks' declares the field 'childrenLinks' more than once")
            .And.Contain("The type 'GfcLinksCreateInput' declares the field 'childrenLinks' more than once")
            .And.Contain("the Text field 'LinksField.ChildrenLinks' and the links of the many-to-many relation 'LinksField.Children' with junction payload");
    }

    [Fact]
    public async Task A_junction_payload_field_named_id_collides_in_the_link_input()
    {
        var message = await RefusalAsync(typeof(Target), typeof(PayloadOwner), typeof(PayloadLink));

        message.Should().Contain("collection 'gfcPayload'")
            .And.Contain("The type 'GfcPayloadChildrenLinkInput' declares the field 'id' more than once")
            .And.Contain("the 'id' field the CMS adds and the payload field 'PayloadLink.ID' of the junction of 'children'");
    }

    [Fact]
    public async Task Two_properties_that_camel_case_to_the_same_name_name_both()
    {
        var message = await RefusalAsync(typeof(CaseSame));

        message.Should().Contain("The type 'GfcCaseSame' declares the field 'title' more than once")
            .And.Contain("the Text field 'CaseSame.Title' and the Text field 'CaseSame.TITLE'");
    }

    [Fact]
    public async Task Two_names_that_differ_only_in_case_are_two_fields_and_the_schema_builds()
    {
        (await BuildAsync(typeof(CaseDiffers))).Should().BeNull();
    }

    [Fact]
    public async Task An_Image_field_and_a_text_field_named_like_its_File_object_name_both()
    {
        var message = await RefusalAsync(typeof(ImageAndText));

        message.Should().Contain("The type 'GfcImageText' declares the field 'cover' more than once")
            .And.Contain("the Image field 'ImageAndText.CoverId', whose File object is exposed as 'cover'")
            .And.Contain("the Text field 'ImageAndText.Cover'");
    }

    [Fact]
    public async Task A_Files_field_and_a_text_field_named_like_its_File_list_name_both()
    {
        var message = await RefusalAsync(typeof(FilesAndText));

        message.Should().Contain("The type 'GfcFilesText' declares the field 'galleryFiles' more than once")
            .And.Contain("the Files field 'FilesAndText.Gallery', whose File list is exposed as 'galleryFiles'")
            .And.Contain("the Text field 'FilesAndText.GalleryFiles'");
    }

    [Fact]
    public async Task A_relation_named_Version_collides_with_the_version_the_CMS_adds()
    {
        var message = await RefusalAsync(typeof(Target), typeof(RelationVersion));

        message.Should().Contain("The type 'GfcRelationVersion' declares the field 'version' more than once")
            .And.Contain("the 'version' field the CMS adds and the relation 'RelationVersion.Version'");
    }

    [Fact]
    public async Task Two_Repeater_sub_fields_with_the_same_name_collide_in_the_item_types()
    {
        var message = await RefusalAsync(typeof(RepeaterCase));

        message.Should().Contain("The type 'GfcRepeaterRowsItem' declares the field 'label' more than once")
            .And.Contain("The type 'GfcRepeaterRowsItemInput' declares the field 'label' more than once")
            .And.Contain("the Text sub-field 'label' of the Repeater 'RepeaterCase.Rows'");
    }

    [Fact]
    public void A_File_field_that_is_the_key_of_a_relation_gets_the_scan_message_not_this_one()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(Target), typeof(ImageKeyRelation)]);

        scan.Should().Throw<MetadataException>().Which.Message
            .Should().Contain("a File or Image field already resolves to the file").And.NotContain("declares the field");
    }

    [Fact]
    public async Task The_same_name_in_two_different_types_is_allowed()
    {
        (await BuildAsync(typeof(Target), typeof(CaseDiffers), typeof(ImageKeyRelationFree))).Should().BeNull();
    }

    // Both this collection and the target have a field named name; the Image field is not a relation key.
    [SugarTable("gfc_free")] [CmsCollection("GfcFree", Name = "gfcFree")]
    public sealed class ImageKeyRelationFree
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Name", Interface = FieldInterface.Text)] public string Name { get; set; } = "";
        [SugarColumn(IsNullable = true)] [CmsField(Label = "Cover", Interface = FieldInterface.Image)] public Guid? CoverId { get; set; }
        [SugarColumn(IsNullable = true)] public Guid? OwnerId { get; set; }
        [Navigate(NavigateType.ManyToOne, nameof(OwnerId))] [CmsRelation(Interface = RelationInterface.Dropdown)] [SugarColumn(IsIgnore = true)]
        public Target? Owner { get; set; }
    }
}
