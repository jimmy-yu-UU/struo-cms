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
    public void Scans_m2o_category_with_foreign_key()
    {
        var category = Rel(typeof(Article)).Single(r => r.Name == "category");
        category.Kind.Should().Be(RelationKind.ManyToOne);
        category.TargetCollection.Should().Be("category");
        category.ForeignKey.Should().Be("categoryId");
        category.Interface.Should().Be(RelationInterface.Dropdown);
        category.OnDelete.Should().Be(OnDelete.SetNull);
        category.SelfReferencing.Should().BeFalse();
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
