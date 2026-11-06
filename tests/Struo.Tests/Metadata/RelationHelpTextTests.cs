using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Metadata;

public sealed class RelationHelpTextTests
{
    [SugarTable("rht_parents")]
    [CmsCollection("Rht parent")]
    public sealed class RhtParent
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Name", Interface = FieldInterface.Text)] public string Name { get; set; } = "";
        public Guid? OwnerId { get; set; }

        [Navigate(NavigateType.OneToOne, nameof(OwnerId))]
        [CmsRelation(Interface = RelationInterface.Dropdown, HelpText = "Who owns this parent.")]
        [SugarColumn(IsIgnore = true)]
        public RhtChild? Owner { get; set; }

        [Navigate(NavigateType.OneToOne, nameof(OwnerId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)]
        public RhtChild? Backup { get; set; }
    }

    [SugarTable("rht_children")]
    [CmsCollection("Rht child")]
    public sealed class RhtChild
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Name", Interface = FieldInterface.Text)] public string Name { get; set; } = "";
    }

    [Fact]
    public void The_help_text_of_a_relation_reaches_its_metadata_and_is_null_when_unset()
    {
        var parent = MetadataScanner.ScanTypes([typeof(RhtParent), typeof(RhtChild)])
            .Single(c => c.Name == "rhtParent");

        parent.Relations.Single(r => r.Name == "owner").HelpText.Should().Be("Who owns this parent.");
        parent.Relations.Single(r => r.Name == "backup").HelpText.Should().BeNull();
    }
}
