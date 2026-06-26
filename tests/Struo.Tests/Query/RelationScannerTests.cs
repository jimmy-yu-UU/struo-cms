using AwesomeAssertions;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Struo.Sample.Blog;
using Xunit;

namespace Struo.Tests.Query;

public class RelationScannerTests
{
    private static IReadOnlyList<Struo.Domain.Metadata.Models.RelationMetadata> Rel(Type t) =>
        MetadataScanner.ScanRelations(t);

    [Fact]
    public void Scans_m2o_author_with_foreign_key()
    {
        var author = Rel(typeof(Article)).Single(r => r.Name == "author");
        author.Kind.Should().Be(RelationKind.ManyToOne);
        author.TargetCollection.Should().Be("author");
        author.ForeignKey.Should().Be("authorId");
        author.Interface.Should().Be(RelationInterface.Dropdown);
        author.OnDelete.Should().Be(OnDelete.Restrict);
        author.SelfReferencing.Should().BeFalse();
    }

    [Fact]
    public void Scans_m2m_tags()
    {
        var tags = Rel(typeof(Article)).Single(r => r.Name == "tags");
        tags.Kind.Should().Be(RelationKind.ManyToMany);
        tags.TargetCollection.Should().Be("tag");
        tags.Interface.Should().Be(RelationInterface.TagSelect);
    }

    [Fact]
    public void Scans_self_referential_category_tree()
    {
        var rels = Rel(typeof(Category));
        var parent = rels.Single(r => r.Name == "parent");
        parent.Kind.Should().Be(RelationKind.ManyToOne);
        parent.SelfReferencing.Should().BeTrue();
        rels.Single(r => r.Name == "children").Kind.Should().Be(RelationKind.OneToMany);
        rels.Should().Contain(r => r.Name == "articles" && r.Kind == RelationKind.OneToMany);
    }
}
