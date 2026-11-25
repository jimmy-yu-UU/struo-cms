using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Metadata;

/// <summary>
/// The junction <c>SortField</c> of a many-to-many relation is refused at the scan unless the link writer can
/// store the position in it: it is not a key of the junction, it has a type the writer orders by, and it
/// has a setter.
/// </summary>
public class SortFieldCheckTests
{
    private enum SortKind
    {
        First
    }

    [SugarTable("sfc_target")]
    [CmsCollection("Sfc target", Name = "sfcTarget")]
    private sealed class Target
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    }

    [SugarTable("sfc_junction")]
    private sealed class TypedJunction
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid OwnerId { get; set; }
        public Guid TargetId { get; set; }
        public short Short { get; set; }
        public ushort Ushort { get; set; }
        public int Int { get; set; }
        public uint Uint { get; set; }
        public long Long { get; set; }
        public ulong Ulong { get; set; }
        public float Float { get; set; }
        public double Double { get; set; }
        public decimal Decimal { get; set; }
        public string Text { get; set; } = "";
        public int? NullableInt { get; set; }
        public long? NullableLong { get; set; }
        public decimal? NullableDecimal { get; set; }
        public string? NullableText { get; set; }
        public int InitOnly { get; init; }
        public int PrivateSet { get; private set; }
        public int NoSetter { get; }
        public DateTime When { get; set; }
        public DateTime? NullableWhen { get; set; }
        public Guid Token { get; set; }
        public bool Flag { get; set; }
        public byte Small { get; set; }
        public sbyte Tiny { get; set; }
        public char Letter { get; set; }
        public SortKind Kind { get; set; }
        public DateTime WhenWithoutSetter { get; }
    }

    [SugarTable("sfc_o_short")]
    [CmsCollection("Sfc Short", Name = "sfcShort")]
    private sealed class OrderedByShort
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(TypedJunction), nameof(TypedJunction.OwnerId), nameof(TypedJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = nameof(TypedJunction.Short))]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("sfc_o_ushort")]
    [CmsCollection("Sfc Ushort", Name = "sfcUshort")]
    private sealed class OrderedByUshort
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(TypedJunction), nameof(TypedJunction.OwnerId), nameof(TypedJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = nameof(TypedJunction.Ushort))]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("sfc_o_int")]
    [CmsCollection("Sfc Int", Name = "sfcInt")]
    private sealed class OrderedByInt
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(TypedJunction), nameof(TypedJunction.OwnerId), nameof(TypedJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = nameof(TypedJunction.Int))]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("sfc_o_uint")]
    [CmsCollection("Sfc Uint", Name = "sfcUint")]
    private sealed class OrderedByUint
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(TypedJunction), nameof(TypedJunction.OwnerId), nameof(TypedJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = nameof(TypedJunction.Uint))]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("sfc_o_long")]
    [CmsCollection("Sfc Long", Name = "sfcLong")]
    private sealed class OrderedByLong
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(TypedJunction), nameof(TypedJunction.OwnerId), nameof(TypedJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = nameof(TypedJunction.Long))]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("sfc_o_ulong")]
    [CmsCollection("Sfc Ulong", Name = "sfcUlong")]
    private sealed class OrderedByUlong
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(TypedJunction), nameof(TypedJunction.OwnerId), nameof(TypedJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = nameof(TypedJunction.Ulong))]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("sfc_o_float")]
    [CmsCollection("Sfc Float", Name = "sfcFloat")]
    private sealed class OrderedByFloat
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(TypedJunction), nameof(TypedJunction.OwnerId), nameof(TypedJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = nameof(TypedJunction.Float))]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("sfc_o_double")]
    [CmsCollection("Sfc Double", Name = "sfcDouble")]
    private sealed class OrderedByDouble
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(TypedJunction), nameof(TypedJunction.OwnerId), nameof(TypedJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = nameof(TypedJunction.Double))]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("sfc_o_decimal")]
    [CmsCollection("Sfc Decimal", Name = "sfcDecimal")]
    private sealed class OrderedByDecimal
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(TypedJunction), nameof(TypedJunction.OwnerId), nameof(TypedJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = nameof(TypedJunction.Decimal))]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("sfc_o_text")]
    [CmsCollection("Sfc Text", Name = "sfcText")]
    private sealed class OrderedByText
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(TypedJunction), nameof(TypedJunction.OwnerId), nameof(TypedJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = nameof(TypedJunction.Text))]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("sfc_o_nullableint")]
    [CmsCollection("Sfc NullableInt", Name = "sfcNullableInt")]
    private sealed class OrderedByNullableInt
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(TypedJunction), nameof(TypedJunction.OwnerId), nameof(TypedJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = nameof(TypedJunction.NullableInt))]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("sfc_o_nullablelong")]
    [CmsCollection("Sfc NullableLong", Name = "sfcNullableLong")]
    private sealed class OrderedByNullableLong
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(TypedJunction), nameof(TypedJunction.OwnerId), nameof(TypedJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = nameof(TypedJunction.NullableLong))]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("sfc_o_nullabledecimal")]
    [CmsCollection("Sfc NullableDecimal", Name = "sfcNullableDecimal")]
    private sealed class OrderedByNullableDecimal
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(TypedJunction), nameof(TypedJunction.OwnerId), nameof(TypedJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = nameof(TypedJunction.NullableDecimal))]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("sfc_o_nullabletext")]
    [CmsCollection("Sfc NullableText", Name = "sfcNullableText")]
    private sealed class OrderedByNullableText
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(TypedJunction), nameof(TypedJunction.OwnerId), nameof(TypedJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = nameof(TypedJunction.NullableText))]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("sfc_o_initonly")]
    [CmsCollection("Sfc InitOnly", Name = "sfcInitOnly")]
    private sealed class OrderedByInitOnly
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(TypedJunction), nameof(TypedJunction.OwnerId), nameof(TypedJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = nameof(TypedJunction.InitOnly))]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("sfc_o_privateset")]
    [CmsCollection("Sfc PrivateSet", Name = "sfcPrivateSet")]
    private sealed class OrderedByPrivateSet
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(TypedJunction), nameof(TypedJunction.OwnerId), nameof(TypedJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = nameof(TypedJunction.PrivateSet))]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("sfc_o_nosetter")]
    [CmsCollection("Sfc NoSetter", Name = "sfcNoSetter")]
    private sealed class OrderedByNoSetter
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(TypedJunction), nameof(TypedJunction.OwnerId), nameof(TypedJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = nameof(TypedJunction.NoSetter))]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("sfc_o_when")]
    [CmsCollection("Sfc When", Name = "sfcWhen")]
    private sealed class OrderedByWhen
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(TypedJunction), nameof(TypedJunction.OwnerId), nameof(TypedJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = nameof(TypedJunction.When))]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("sfc_o_nullablewhen")]
    [CmsCollection("Sfc NullableWhen", Name = "sfcNullableWhen")]
    private sealed class OrderedByNullableWhen
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(TypedJunction), nameof(TypedJunction.OwnerId), nameof(TypedJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = nameof(TypedJunction.NullableWhen))]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("sfc_o_token")]
    [CmsCollection("Sfc Token", Name = "sfcToken")]
    private sealed class OrderedByToken
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(TypedJunction), nameof(TypedJunction.OwnerId), nameof(TypedJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = nameof(TypedJunction.Token))]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("sfc_o_flag")]
    [CmsCollection("Sfc Flag", Name = "sfcFlag")]
    private sealed class OrderedByFlag
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(TypedJunction), nameof(TypedJunction.OwnerId), nameof(TypedJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = nameof(TypedJunction.Flag))]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("sfc_o_small")]
    [CmsCollection("Sfc Small", Name = "sfcSmall")]
    private sealed class OrderedBySmall
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(TypedJunction), nameof(TypedJunction.OwnerId), nameof(TypedJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = nameof(TypedJunction.Small))]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("sfc_o_tiny")]
    [CmsCollection("Sfc Tiny", Name = "sfcTiny")]
    private sealed class OrderedByTiny
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(TypedJunction), nameof(TypedJunction.OwnerId), nameof(TypedJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = nameof(TypedJunction.Tiny))]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("sfc_o_letter")]
    [CmsCollection("Sfc Letter", Name = "sfcLetter")]
    private sealed class OrderedByLetter
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(TypedJunction), nameof(TypedJunction.OwnerId), nameof(TypedJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = nameof(TypedJunction.Letter))]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("sfc_o_kind")]
    [CmsCollection("Sfc Kind", Name = "sfcKind")]
    private sealed class OrderedByKind
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(TypedJunction), nameof(TypedJunction.OwnerId), nameof(TypedJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = nameof(TypedJunction.Kind))]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("sfc_o_whenwithoutsetter")]
    [CmsCollection("Sfc WhenWithoutSetter", Name = "sfcWhenWithoutSetter")]
    private sealed class OrderedByWhenWithoutSetter
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(TypedJunction), nameof(TypedJunction.OwnerId), nameof(TypedJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = nameof(TypedJunction.WhenWithoutSetter))]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("sfc_o_ownerid")]
    [CmsCollection("Sfc OwnerId", Name = "sfcOwnerId")]
    private sealed class OrderedByOwnerId
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(TypedJunction), nameof(TypedJunction.OwnerId), nameof(TypedJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = nameof(TypedJunction.OwnerId))]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("sfc_o_targetid")]
    [CmsCollection("Sfc TargetId", Name = "sfcTargetId")]
    private sealed class OrderedByTargetId
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(TypedJunction), nameof(TypedJunction.OwnerId), nameof(TypedJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = nameof(TypedJunction.TargetId))]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("sfc_o_id")]
    [CmsCollection("Sfc Id", Name = "sfcId")]
    private sealed class OrderedById
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(TypedJunction), nameof(TypedJunction.OwnerId), nameof(TypedJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = nameof(TypedJunction.Id))]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    public static TheoryData<Type> OrderableProperties() => new()
    {
        typeof(OrderedByShort), typeof(OrderedByUshort), typeof(OrderedByInt), typeof(OrderedByUint),
        typeof(OrderedByLong), typeof(OrderedByUlong), typeof(OrderedByFloat), typeof(OrderedByDouble),
        typeof(OrderedByDecimal), typeof(OrderedByText), typeof(OrderedByNullableInt),
        typeof(OrderedByNullableLong), typeof(OrderedByNullableDecimal), typeof(OrderedByNullableText),
        typeof(OrderedByInitOnly), typeof(OrderedByPrivateSet),
    };

    // Pins: every type the link writer orders by, with and without '?', and an init or private setter.
    [Theory]
    [MemberData(nameof(OrderableProperties))]
    public void A_sort_property_the_link_writer_orders_by_passes(Type owner) =>
        MetadataScanner.ScanTypes([owner, typeof(Target)]).Should().HaveCount(2);

    public static TheoryData<Type, string, string> UnorderableProperties() => new()
    {
        { typeof(OrderedByWhen), "When", "DateTime" },
        { typeof(OrderedByNullableWhen), "NullableWhen", "DateTime?" },
        { typeof(OrderedByToken), "Token", "Guid" },
        { typeof(OrderedByFlag), "Flag", "bool" },
        { typeof(OrderedBySmall), "Small", "byte" },
        { typeof(OrderedByTiny), "Tiny", "sbyte" },
        { typeof(OrderedByLetter), "Letter", "char" },
        { typeof(OrderedByKind), "Kind", "SortKind" },
    };

    [Theory]
    [MemberData(nameof(UnorderableProperties))]
    public void A_sort_property_of_a_type_the_link_writer_cannot_order_by_is_refused_and_lists_the_accepted_types(
        Type owner, string property, string typeText)
    {
        var scan = () => MetadataScanner.ScanTypes([owner, typeof(Target)]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().StartWith($"Relation '{owner.Name}.Tags' declares SortField '{property}' in [CmsRelation]")
            .And.Contain($"TypedJunction.{property} is of type {typeText}")
            .And.Contain("the links cannot be ordered by a property of that type")
            .And.Contain("short, ushort, int, uint, long, ulong, float, double, decimal or string");
    }

    [Fact]
    public void A_sort_property_without_a_setter_is_refused_and_says_the_link_writer_assigns_the_position()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(OrderedByNoSetter), typeof(Target)]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().StartWith("Relation 'OrderedByNoSetter.Tags' declares SortField 'NoSetter' in [CmsRelation]")
            .And.Contain("TypedJunction.NoSetter has no setter")
            .And.Contain("the link writer assigns the position to it").And.Contain("set or init accessor");
    }

    public static TheoryData<Type, string, string> KeyProperties() => new()
    {
        { typeof(OrderedByOwnerId), "OwnerId", "the junction parent key" },
        { typeof(OrderedByTargetId), "TargetId", "the junction target key" },
        { typeof(OrderedById), "Id", "the primary key" },
    };

    [Theory]
    [MemberData(nameof(KeyProperties))]
    public void A_sort_property_that_is_a_key_of_the_junction_is_refused_and_says_which_key(
        Type owner, string property, string role)
    {
        var scan = () => MetadataScanner.ScanTypes([owner, typeof(Target)]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().StartWith($"Relation '{owner.Name}.Tags' declares SortField '{property}' in [CmsRelation]")
            .And.Contain($"TypedJunction.{property} is {role}")
            .And.Contain("would overwrite the key")
            .And.Contain("Give the junction a property of its own for the order");
    }

    // The key rule is reported before the type rule (the keys are Guid), and the type rule before the setter rule.
    [Fact]
    public void A_key_sort_property_is_reported_as_a_key_not_as_a_type()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(OrderedByOwnerId), typeof(Target)]);

        scan.Should().Throw<MetadataException>().Which.Message.Should().NotContain("is of type");
    }

    [Fact]
    public void A_sort_property_of_a_wrong_type_is_reported_as_a_type_before_the_missing_setter()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(OrderedByWhenWithoutSetter), typeof(Target)]);

        scan.Should().Throw<MetadataException>().Which.Message
            .Should().Contain("is of type DateTime").And.NotContain("no setter");
    }
}
