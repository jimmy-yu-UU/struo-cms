using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Metadata;

/// <summary>
/// The delete and restore operations own <c>DeletedAt</c> and <c>DeletedBy</c> of a soft-deletable collection and a
/// body never sets them, so a <c>[CmsField]</c> on one is refused at startup. A property that only shares the
/// name, on a class that is not soft-deletable, is an ordinary field.
/// </summary>
public sealed class SoftDeleteFieldCheckTests
{
    [SugarTable("sdf_field_at")]
    [CmsCollection("Field at", Name = "sdfFieldAt")]
    private sealed class FieldOnDeletedAt : ISoftDeletable
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Deleted at", Interface = FieldInterface.DateTime)]
        public DateTime? DeletedAt { get; set; }

        public Guid? DeletedBy { get; set; }
    }

    [SugarTable("sdf_field_by")]
    [CmsCollection("Field by", Name = "sdfFieldBy")]
    private sealed class FieldOnDeletedBy : ISoftDeletable
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public DateTime? DeletedAt { get; set; }

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Deleted by", Interface = FieldInterface.Uuid)]
        public Guid? DeletedBy { get; set; }
    }

    private abstract class DeletedByBase : ISoftDeletable
    {
        public DateTime? DeletedAt { get; set; }

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Deleted by", Interface = FieldInterface.Uuid)]
        public Guid? DeletedBy { get; set; }
    }

    [SugarTable("sdf_inherited")]
    [CmsCollection("Inherited", Name = "sdfInherited")]
    private sealed class FieldOnInheritedDeletedBy : DeletedByBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    }

    [SugarTable("sdf_plain")]
    [CmsCollection("Plain", Name = "sdfPlain")]
    private sealed class SoftDeletableWithoutField : ISoftDeletable
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public DateTime? DeletedAt { get; set; }
        public Guid? DeletedBy { get; set; }

        [CmsField(Label = "Name", Interface = FieldInterface.Text)]
        public string Name { get; set; } = "";
    }

    // Not soft-deletable: the names are ordinary properties.
    [SugarTable("sdf_not_soft")]
    [CmsCollection("Not soft", Name = "sdfNotSoft")]
    private sealed class NamedDeletedAtButNotSoftDeletable
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Deleted at", Interface = FieldInterface.DateTime)]
        public DateTime? DeletedAt { get; set; }

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Deleted by", Interface = FieldInterface.Uuid)]
        public Guid? DeletedBy { get; set; }
    }

    [Theory]
    [InlineData(nameof(FieldOnDeletedAt), "DeletedAt")]
    [InlineData(nameof(FieldOnDeletedBy), "DeletedBy")]
    [InlineData(nameof(FieldOnInheritedDeletedBy), "DeletedBy")]
    public void A_field_on_a_soft_delete_column_is_refused_and_names_the_property(string className, string property)
    {
        Type type = className switch
        {
            nameof(FieldOnDeletedAt) => typeof(FieldOnDeletedAt),
            nameof(FieldOnDeletedBy) => typeof(FieldOnDeletedBy),
            _ => typeof(FieldOnInheritedDeletedBy),
        };

        var scan = () => MetadataScanner.ScanTypes([type]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().Contain($"'{property}'").And.Contain(type.Name).And.Contain("soft-delet")
            .And.Contain("Remove [CmsField]");
    }

    [Fact]
    public void A_soft_deletable_collection_without_a_field_on_those_columns_is_accepted()
    {
        var collections = MetadataScanner.ScanTypes([typeof(SoftDeletableWithoutField)]);

        collections.Single().Fields.Select(f => f.Name).Should().Equal("name");
    }

    [Fact]
    public void A_property_named_like_a_soft_delete_column_on_a_class_that_is_not_soft_deletable_is_a_field()
    {
        var collections = MetadataScanner.ScanTypes([typeof(NamedDeletedAtButNotSoftDeletable)]);

        collections.Single().Fields.Select(f => f.Name).Should().BeEquivalentTo("deletedAt", "deletedBy");
    }
}
