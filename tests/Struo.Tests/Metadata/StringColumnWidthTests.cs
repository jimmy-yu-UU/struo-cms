using AwesomeAssertions;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Metadata;

// The default MaxLength of a string field is the width of its column; the factory builds the column
// from the same rule.
public class StringColumnWidthTests
{
    private static readonly (string Property, int? Width)[] Expected =
    [
        (nameof(WidthProbeEntity.PlainText), 255),
        (nameof(WidthProbeEntity.HiddenText), 255),
        (nameof(WidthProbeEntity.ClockText), 255),
        (nameof(WidthProbeEntity.UuidText), 255),
        (nameof(WidthProbeEntity.ExplicitLimit), 255),
        (nameof(WidthProbeEntity.NarrowText), 100),
        (nameof(WidthProbeEntity.BodyText), null),
        (nameof(WidthProbeEntity.RichBody), null),
        (nameof(WidthProbeEntity.RawJson), null),
        (nameof(WidthProbeEntity.ShapedText), null),
        (nameof(WidthProbeEntity.TypedText), null)
    ];

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    [Fact]
    public void A_field_without_MaxLength_defaults_to_its_column_width()
    {
        var fields = MetadataScanner.ScanTypes([typeof(WidthProbeEntity)]).Single().Fields;

        foreach (var (property, width) in Expected.Where(e => e.Property != nameof(WidthProbeEntity.ExplicitLimit)))
            fields.Single(f => f.Name == Camel(property)).MaxLength.Should().Be(width, property);
    }

    [Fact]
    public void An_explicit_MaxLength_is_kept()
    {
        var fields = MetadataScanner.ScanTypes([typeof(WidthProbeEntity)]).Single().Fields;

        fields.Single(f => f.Name == "explicitLimit").MaxLength.Should().Be(120);
    }

    [Fact]
    public void The_factory_builds_each_bounded_column_with_the_scanner_width()
    {
        using var db = TestBackend.CreateUnitDatabase();
        var client = SqlSugarClientFactory.Create(db.Options(), new TestCurrentUserAccessor(Guid.Empty));

        var columns = client.EntityMaintenance.GetEntityInfo<WidthProbeEntity>().Columns;

        foreach (var (property, width) in Expected.Where(e => e.Width is not null))
            columns.Single(c => c.PropertyName == property).Length.Should().Be(width, property);
    }
}
