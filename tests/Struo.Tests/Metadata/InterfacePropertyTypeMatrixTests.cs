using AwesomeAssertions;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Metadata;

// Every FieldInterface member against 40 property types: the scan accepts the pairs in Accepted and
// rejects every other pair with a MetadataException. The expectations are written here, not read from
// the product's table.
public sealed class InterfacePropertyTypeMatrixTests
{
    private static readonly (string Id, Type Type)[] Types = InterfaceTypeExpectations.Types;
    private static readonly Dictionary<FieldInterface, string[]> Accepted = InterfaceTypeExpectations.Accepted;
    private static readonly FieldInterface[] WithOptions = InterfaceTypeExpectations.WithOptions;

    [Fact]
    public void The_matrix_has_33_interfaces_and_40_types()
    {
        Accepted.Keys.Should().BeEquivalentTo(Enum.GetValues<FieldInterface>());
        Types.Length.Should().Be(40);
        Types.Select(t => t.Id).Should().OnlyHaveUniqueItems();
        Accepted.Values.SelectMany(v => v).Should().OnlyContain(id => Types.Any(t => t.Id == id));
    }

    [Fact]
    public void The_scan_accepts_exactly_the_pairs_the_matrix_lists()
    {
        var cells = Enum.GetValues<FieldInterface>()
            .SelectMany(i => Types.Select(t => (Interface: i, Type: t)))
            .ToList();
        var entities = cells
            .Select((c, n) => new EmittedEntity(
                $"M{n}", [new EmittedProperty("V", c.Type.Type, c.Interface, WithOptions.Contains(c.Interface))]))
            .ToList();
        var assembly = EmittedEntities.Build(EmittedEntities.UniqueName("matrix"), entities);

        var wrong = new List<string>();
        for (var n = 0; n < cells.Count; n++)
        {
            var (face, (id, _)) = cells[n];
            var shouldPass = Accepted[face].Contains(id);
            var type = assembly.GetType($"Emitted.M{n}")!;
            try
            {
                MetadataScanner.ScanTypes([type]);
                if (!shouldPass) wrong.Add($"{face}|{id}: accepted, expected a MetadataException");
            }
            catch (MetadataException ex)
            {
                if (shouldPass) wrong.Add($"{face}|{id}: rejected ({ex.Message}), expected accepted");
            }
        }

        wrong.Should().BeEmpty();
    }
}
