using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Metadata;

// A [CmsField] on a [SugarColumn(IsIgnore = true)] property has no column: it is marked Ignored, and a
// Searchable or Sortable declaration on it fails the scan.
public sealed class IgnoredFieldScanTests
{
    [CmsCollection("Ignored searchable", Name = "ifsSearch")]
    public sealed class IgnoredSearchable
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Interface = FieldInterface.Text, Searchable = true)]
        [SugarColumn(IsIgnore = true)]
        public string Blurb { get; set; } = "";
    }

    [CmsCollection("Ignored sortable", Name = "ifsSort")]
    public sealed class IgnoredSortable
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Interface = FieldInterface.Number, Sortable = true)]
        [SugarColumn(IsIgnore = true)]
        public int Rank { get; set; }
    }

    [CmsCollection("Ignored plain", Name = "ifsPlain")]
    public sealed class IgnoredPlain
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Interface = FieldInterface.Text)] public string Name { get; set; } = "";
        [CmsField(Interface = FieldInterface.Text)]
        [SugarColumn(IsIgnore = true)]
        public string Badge { get; set; } = "";
        [CmsField(Label = "Section", Interface = FieldInterface.Divider)]
        [SugarColumn(IsIgnore = true)]
        public string Section { get; set; } = "";
        [CmsField(Interface = FieldInterface.Repeater)] public List<IgnoredItem> Items { get; set; } = [];
    }

    public sealed class IgnoredItem
    {
        [CmsField(Interface = FieldInterface.Text)] public string Question { get; set; } = "";
    }

    [Theory]
    [InlineData(typeof(IgnoredSearchable), "IgnoredSearchable.Blurb", "Searchable", "searched")]
    [InlineData(typeof(IgnoredSortable), "IgnoredSortable.Rank", "Sortable", "sorted")]
    public void Searchable_or_Sortable_on_a_property_without_a_column_fails_the_scan_and_says_what_to_change(
        Type type, string field, string declaration, string verb)
    {
        var act = () => MetadataScanner.ScanTypes([type]);

        act.Should().Throw<MetadataException>().Which.Message.Should()
            .Contain(field).And.Contain("IsIgnore").And.Contain("no column").And.Contain($"cannot be {verb}")
            .And.Contain($"Remove {declaration} = true").And.Contain("remove IsIgnore");
    }

    [Fact]
    public void A_property_without_a_column_is_marked_Ignored_and_the_others_are_not()
    {
        var fields = MetadataScanner.ScanTypes([typeof(IgnoredPlain)]).Single().Fields;

        fields.Where(f => f.Ignored).Select(f => f.Name).Should().BeEquivalentTo(["badge", "section"]);
        fields.Single(f => f.Name == "name").Ignored.Should().BeFalse();
    }

    [Fact]
    public void Repeater_sub_fields_are_never_marked_Ignored()
    {
        var items = MetadataScanner.ScanTypes([typeof(IgnoredPlain)]).Single().Fields.Single(f => f.Name == "items");

        items.Fields.Should().ContainSingle().Which.Ignored.Should().BeFalse();
    }
}
