using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Metadata.ResolutionA
{
    // Plain classes that name a collection: the run time resolves them to the collection class.
    public sealed class Pupil
    {
        public Guid Id { get; set; }
        public Guid? ParentId { get; set; }
    }

    public sealed class Outsider
    {
        public Guid Id { get; set; }
        public Guid? ParentId { get; set; }
    }

    public sealed class Orphan
    {
        public Guid Id { get; set; }
        public Guid? ParentId { get; set; }
    }
}

namespace Struo.Tests.Metadata.ResolutionB
{
    // Same simple name as ResolutionA.Pupil, and no child key.
    [SugarTable("rrn_pupil")]
    [CmsCollection("Pupil", Name = "pupil")]
    public sealed class Pupil
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    }

    // A collection named like ResolutionA.Outsider, and no child key.
    [SugarTable("rrn_insider")]
    [CmsCollection("Insider", Name = "outsider")]
    public sealed class Insider
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    }

    // A collection named like ResolutionA.Orphan whose child key has no column.
    [SugarTable("rrn_shelter")]
    [CmsCollection("Shelter", Name = "orphan")]
    public sealed class Shelter
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsIgnore = true)] public Guid? ParentId { get; set; }
    }
}

namespace Struo.Tests.Metadata
{
    /// <summary>
    /// How a one-to-many element type led to the collection class a key was read off is part of the refusal:
    /// the note qualifies both classes when they share a simple name, and it follows the no-column message too.
    /// </summary>
    public class RelationResolutionNoteTests
    {
        [SugarTable("rrn_owner_same")]
        [CmsCollection("Owner same", Name = "rrnOwnerSame")]
        private sealed class OwnerOverSameNamedClasses
        {
            [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

            [Navigate(NavigateType.OneToMany, "ParentId")]
            [CmsRelation(Interface = RelationInterface.RelatedList)]
            [SugarColumn(IsIgnore = true)] public List<ResolutionA.Pupil> Kids { get; set; } = [];
        }

        [SugarTable("rrn_owner_top")]
        [CmsCollection("Owner top", Name = "rrnOwnerTop")]
        private sealed class OwnerOverTopLevelElement
        {
            [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

            [Navigate(NavigateType.OneToMany, "ParentId")]
            [CmsRelation(Interface = RelationInterface.RelatedList)]
            [SugarColumn(IsIgnore = true)] public List<ResolutionA.Outsider> Kids { get; set; } = [];
        }

        [SugarTable("rrn_owner_ignored")]
        [CmsCollection("Owner ignored", Name = "rrnOwnerIgnored")]
        private sealed class OwnerOverIgnoredKey
        {
            [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

            [Navigate(NavigateType.OneToMany, "ParentId")]
            [CmsRelation(Interface = RelationInterface.RelatedList)]
            [SugarColumn(IsIgnore = true)] public List<ResolutionA.Orphan> Kids { get; set; } = [];
        }

        [Fact]
        public void Two_classes_with_the_same_simple_name_are_named_with_their_namespaces()
        {
            var scan = () => MetadataScanner.ScanTypes([typeof(OwnerOverSameNamedClasses), typeof(ResolutionB.Pupil)]);

            scan.Should().Throw<MetadataException>().Which.Message
                .Should().Contain(
                    "but Pupil (the collection class Struo.Tests.Metadata.ResolutionB.Pupil that the element type " +
                    "Struo.Tests.Metadata.ResolutionA.Pupil resolves to by name 'pupil') has no property");
        }

        [Fact]
        public void A_top_level_element_type_is_named_by_its_simple_name_when_the_names_differ()
        {
            var scan = () => MetadataScanner.ScanTypes([typeof(OwnerOverTopLevelElement), typeof(ResolutionB.Insider)]);

            scan.Should().Throw<MetadataException>().Which.Message
                .Should().Contain(
                    "but Insider (the collection the element type Outsider resolves to by name 'outsider') " +
                    "has no property");
        }

        [Fact]
        public void A_child_key_without_a_column_on_the_resolved_class_says_how_the_class_was_reached()
        {
            var scan = () => MetadataScanner.ScanTypes([typeof(OwnerOverIgnoredKey), typeof(ResolutionB.Shelter)]);

            var message = scan.Should().Throw<MetadataException>().Which.Message;
            message.Should().Contain("Shelter.ParentId is [SugarColumn(IsIgnore = true)]")
                .And.Contain(
                    "The key is read off Shelter (the collection the element type Orphan resolves to " +
                    "by name 'orphan').");
        }
    }
}
