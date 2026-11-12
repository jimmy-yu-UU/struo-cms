using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Metadata;

// SqlSugar's Navigate has six constructors. The scanner supports (NavigateType, string, string[]) for
// many-to-one and one-to-many, and (Type, string, string, string[]) for many-to-many; every other
// constructor stops startup. The SQL-carrying overloads pass null here: no SQL text in fixtures.
public class NavigateOverloadTests
{
    [CmsCollection("Target", Name = "nvTarget")]
    private sealed class NvTarget
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public string Code { get; set; } = "";
    }

    [CmsCollection("Child", Name = "nvChild")]
    private sealed class NvChild
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid? ParentId { get; set; }
    }

    private sealed class NvLink
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid LeftId { get; set; }
        public Guid RightId { get; set; }
    }

    [CmsCollection("Supported M2O", Name = "nvM2oSupported")]
    private sealed class NvM2oSupported
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid? TargetId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(TargetId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        public NvTarget? Target { get; set; }
    }

    [CmsCollection("Supported O2M", Name = "nvO2mSupported")]
    private sealed class NvO2mSupported
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(NavigateType.OneToMany, nameof(NvChild.ParentId))]
        [CmsRelation(Interface = RelationInterface.RelatedList)]
        [SugarColumn(IsIgnore = true)]
        public List<NvChild> Children { get; set; } = [];
    }

    [CmsCollection("Supported M2M", Name = "nvM2mSupported")]
    private sealed class NvM2mSupported
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(NvLink), nameof(NvLink.LeftId), nameof(NvLink.RightId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)]
        public List<NvTarget> Targets { get; set; } = [];
    }

    [CmsCollection("M2O with two keys", Name = "nvM2oTwoKeys")]
    private sealed class NvM2oTwoKeys
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public string? TargetCode { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(TargetCode), nameof(NvTarget.Code))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        public NvTarget? Target { get; set; }
    }

    [CmsCollection("M2O with the target primary key", Name = "nvM2oTargetPk")]
    private sealed class NvM2oTargetPk
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid? TargetId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(TargetId), nameof(NvTarget.Id))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        public NvTarget? Target { get; set; }
    }

    [CmsCollection("O2M with two keys", Name = "nvO2mTwoKeys")]
    private sealed class NvO2mTwoKeys
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(NavigateType.OneToMany, nameof(Id), nameof(NvChild.ParentId))]
        [CmsRelation(Interface = RelationInterface.RelatedList)]
        [SugarColumn(IsIgnore = true)]
        public List<NvChild> Children { get; set; } = [];
    }

    [CmsCollection("M2O with whereSql", Name = "nvM2oWhere")]
    private sealed class NvM2oWhere
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid? TargetId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(TargetId), nameof(NvTarget.Id), whereSql: null)]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        public NvTarget? Target { get; set; }
    }

    [CmsCollection("M2M with class keys", Name = "nvM2mFiveArgs")]
    private sealed class NvM2mFiveArgs
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(NvLink), nameof(NvLink.LeftId), nameof(NvLink.RightId), nameof(Id), nameof(NvTarget.Id))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)]
        public List<NvTarget> Targets { get; set; } = [];
    }

    [CmsCollection("M2M with mappingSql", Name = "nvM2mMappingSql")]
    private sealed class NvM2mMappingSql
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(NvLink), nameof(NvLink.LeftId), nameof(NvLink.RightId), mappingSql: null)]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)]
        public List<NvTarget> Targets { get; set; } = [];
    }

    [CmsCollection("Navigate without a relation attribute", Name = "nvNoRelation")]
    private sealed class NvNoRelation
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public string? TargetCode { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(TargetCode), nameof(NvTarget.Code))]
        [SugarColumn(IsIgnore = true)]
        public NvTarget? Target { get; set; }
    }

    private static readonly Dictionary<string, Type> Types = new[]
    {
        typeof(NvM2oSupported), typeof(NvO2mSupported), typeof(NvM2mSupported), typeof(NvM2oTwoKeys),
        typeof(NvM2oTargetPk), typeof(NvO2mTwoKeys), typeof(NvM2oWhere), typeof(NvM2mFiveArgs),
        typeof(NvM2mMappingSql)
    }.ToDictionary(t => t.Name);

    private static Type[] WithNeighbours(string name) =>
        [Types[name], typeof(NvTarget), typeof(NvChild), typeof(NvLink)];

    [Theory]
    [InlineData(nameof(NvM2oSupported))]
    [InlineData(nameof(NvO2mSupported))]
    [InlineData(nameof(NvM2mSupported))]
    public void The_two_supported_forms_scan(string name)
    {
        var types = WithNeighbours(name);

        var collections = MetadataScanner.ScanTypes(types);
        var relation = collections.Single(c => c.Name == MetadataScanner.CollectionNameOf(Types[name])).Relations.Single();
        MetadataScanner.ScanDescriptors(types).Should().ContainKey(MetadataScanner.CollectionNameOf(Types[name]));
        _ = new RelationshipGraph(collections, MetadataScanner.CollectionTypeMap(types));

        relation.Kind.Should().Be(name switch
        {
            nameof(NvM2oSupported) => RelationKind.ManyToOne,
            nameof(NvO2mSupported) => RelationKind.OneToMany,
            _ => RelationKind.ManyToMany
        });
    }

    [Fact]
    public void The_supported_many_to_one_reads_its_foreign_key()
    {
        var relation = MetadataScanner.ScanRelations(typeof(NvM2oSupported)).Single();

        relation.ForeignKey.Should().Be("targetId");
    }

    [Theory]
    [InlineData(nameof(NvM2oTwoKeys), "Target")]
    [InlineData(nameof(NvM2oTargetPk), "Target")]
    [InlineData(nameof(NvO2mTwoKeys), "Children")]
    [InlineData(nameof(NvM2oWhere), "Target")]
    [InlineData(nameof(NvM2mFiveArgs), "Targets")]
    [InlineData(nameof(NvM2mMappingSql), "Targets")]
    public void Every_other_constructor_stops_ScanTypes_naming_type_property_and_supported_forms(string name, string property)
    {
        var act = () => MetadataScanner.ScanTypes(WithNeighbours(name));

        AssertUnsupported(act, name, property);
    }

    [Theory]
    [InlineData(nameof(NvM2oTwoKeys), "Target")]
    [InlineData(nameof(NvM2oTargetPk), "Target")]
    [InlineData(nameof(NvO2mTwoKeys), "Children")]
    [InlineData(nameof(NvM2oWhere), "Target")]
    [InlineData(nameof(NvM2mFiveArgs), "Targets")]
    [InlineData(nameof(NvM2mMappingSql), "Targets")]
    public void Every_other_constructor_stops_ScanRelations(string name, string property)
    {
        var act = () => MetadataScanner.ScanRelations(Types[name]);

        AssertUnsupported(act, name, property);
    }

    [Theory]
    [InlineData(nameof(NvM2oTwoKeys), "Target")]
    [InlineData(nameof(NvM2oTargetPk), "Target")]
    [InlineData(nameof(NvO2mTwoKeys), "Children")]
    [InlineData(nameof(NvM2oWhere), "Target")]
    public void Every_other_foreign_key_constructor_stops_ScanDescriptors(string name, string property)
    {
        var act = () => MetadataScanner.ScanDescriptors(WithNeighbours(name));

        AssertUnsupported(act, name, property);
    }

    [Theory]
    [InlineData(nameof(NvM2oTwoKeys), RelationKind.ManyToOne, "Target")]
    [InlineData(nameof(NvO2mTwoKeys), RelationKind.OneToMany, "Children")]
    [InlineData(nameof(NvM2mFiveArgs), RelationKind.ManyToMany, "Targets")]
    [InlineData(nameof(NvM2mMappingSql), RelationKind.ManyToMany, "Targets")]
    public void RelationshipGraph_rejects_the_same_constructors(string name, RelationKind kind, string property)
    {
        var camel = char.ToLowerInvariant(property[0]) + property[1..];
        var owner = MetadataScanner.CollectionNameOf(Types[name]);
        var types = WithNeighbours(name);
        var scanned = MetadataScanner.ScanTypes([typeof(NvTarget), typeof(NvChild), typeof(NvLink)]);
        var handBuilt = new CollectionMetadata
        {
            Name = owner,
            Label = owner,
            FieldGroups = [],
            Fields = [],
            Relations =
            [
                new RelationMetadata
                {
                    Name = camel,
                    Label = property,
                    Kind = kind,
                    TargetCollection = kind == RelationKind.OneToMany ? "nvChild" : "nvTarget",
                    Interface = RelationInterface.Dropdown,
                    ForeignKey = kind == RelationKind.ManyToOne ? "targetCode" : null
                }
            ]
        };

        var act = () => new RelationshipGraph([.. scanned, handBuilt], MetadataScanner.CollectionTypeMap(types));

        AssertUnsupported(act, name, property);
    }

    [Fact]
    public void A_Navigate_on_a_collection_class_is_checked_without_CmsRelation()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(NvNoRelation), typeof(NvTarget)]);

        AssertUnsupported(act, nameof(NvNoRelation), "Target");
    }

    [Fact]
    public void Every_Navigate_declaration_in_source_scans()
    {
        var core = typeof(MetadataScanner).Assembly;
        var fixtures = typeof(Struo.Tests.Fixtures.FxArticle).Assembly;

        var act = () => MetadataScanner.Scan(core, fixtures);

        act.Should().NotThrow();
    }

    private static void AssertUnsupported(Action act, string typeName, string property) =>
        act.Should().Throw<MetadataException>()
            .WithMessage($"*'{typeName}.{property}'*unsupported*[Navigate(NavigateType, foreignKey)]*" +
                         "[Navigate(junctionType, parentKey, targetKey)]*");

    private static void AssertUnsupported<T>(Func<T> act, string typeName, string property) =>
        AssertUnsupported(new Action(() => act()), typeName, property);
}
