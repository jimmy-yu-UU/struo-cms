// tests/Struo.Tests/Query/AvgProbe.cs
using System.Linq.Expressions;
using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Query;
using Struo.Infrastructure.Query;

namespace Struo.Tests.Query;

[SugarTable("avg_probe")]
public sealed class AvgProbe
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    public int IntValue { get; set; }
    public long LongValue { get; set; }
    public long BigLong { get; set; }
    [SugarColumn(Length = 18, DecimalDigits = 4)] public decimal DecimalValue { get; set; }
    public int? NullableInt { get; set; }
}

internal static class AvgProbeAssertions
{
    private static readonly (AggregateOp Op, string Property)[] Slots =
    [
        (AggregateOp.Avg, nameof(AvgProbe.IntValue)), (AggregateOp.Avg, nameof(AvgProbe.LongValue)),
        (AggregateOp.Avg, nameof(AvgProbe.DecimalValue)), (AggregateOp.Avg, nameof(AvgProbe.NullableInt)),
        (AggregateOp.Sum, nameof(AvgProbe.IntValue)), (AggregateOp.Min, nameof(AvgProbe.IntValue)),
        (AggregateOp.Max, nameof(AvgProbe.IntValue)), (AggregateOp.Count, nameof(AvgProbe.NullableInt)),
        (AggregateOp.Avg, nameof(AvgProbe.BigLong)),
    ];

    public static ISugarQueryable<AggregateRow> Query(ISqlSugarClient db, bool floatAverageOfIntegers)
    {
        var proj = (Expression<Func<AvgProbe, AggregateRow>>)ColumnSelectorFactory.AggregateProjection(typeof(AvgProbe), Slots, floatAverageOfIntegers);
        return db.Queryable<AvgProbe>().Select(proj);
    }

    public static void SeedAndAssert(ISqlSugarClient db)
    {
        db.CodeFirst.InitTables<AvgProbe>();
        db.Deleteable<AvgProbe>().Where(x => true).ExecuteCommand();
        db.Insertable(new[]
        {
            new AvgProbe { Id = Guid.NewGuid(), IntValue = 2, LongValue = 2, BigLong = 10_000_000_000_000, DecimalValue = 1.25m, NullableInt = 2 },
            new AvgProbe { Id = Guid.NewGuid(), IntValue = 3, LongValue = 3, BigLong = 10_000_000_000_001, DecimalValue = 2.5m, NullableInt = 3 },
            new AvgProbe { Id = Guid.NewGuid(), IntValue = 2, LongValue = 2, BigLong = 10_000_000_000_000, DecimalValue = 1.25m, NullableInt = null },
            new AvgProbe { Id = Guid.NewGuid(), IntValue = 3, LongValue = 3, BigLong = 10_000_000_000_001, DecimalValue = 2.5m, NullableInt = null },
        }).ExecuteCommand();

        var row = Query(db, db.CurrentConnectionConfig.DbType == DbType.SqlServer).First()!;
        var raws = new[] { row.V0, row.V1, row.V2, row.V3, row.V4, row.V5, row.V6, row.V7, row.V8 };
        var values = Slots.Select((s, i) => AggregateValueNormalizer.Normalize(
            s.Op, ColumnSelectorFactory.PropertyType(typeof(AvgProbe), s.Property), raws[i])).ToArray();

        values[0].Should().BeOfType<double>().Which.Should().Be(2.5);
        values[1].Should().BeOfType<double>().Which.Should().Be(2.5);
        values[2].Should().BeOfType<double>().Which.Should().Be(1.875);
        values[3].Should().BeOfType<double>().Which.Should().Be(2.5); // NULL rows are ignored
        values[4].Should().BeOfType<long>().Which.Should().Be(10L);
        values[5].Should().BeOfType<int>().Which.Should().Be(2);
        values[6].Should().BeOfType<int>().Which.Should().Be(3);
        values[7].Should().BeOfType<long>().Which.Should().Be(2L);
        values[8].Should().BeOfType<double>().Which.Should().Be(10_000_000_000_000.5);
    }

    private static readonly int[] RepeatingMeanValues = [1, 2, 2];

    // 1, 2, 2 has the mean 1.666…, which MySQL/MariaDB round to div_precision_increment (default 4) decimals.
    public static void SeedRepeatingMeanAndAssert(ISqlSugarClient db)
    {
        db.CodeFirst.InitTables<AvgProbe>();
        db.Deleteable<AvgProbe>().Where(x => true).ExecuteCommand();
        db.Insertable(RepeatingMeanValues.Select(v => new AvgProbe
        {
            Id = Guid.NewGuid(), IntValue = v, LongValue = v, BigLong = v, DecimalValue = v, NullableInt = v,
        }).ToArray()).ExecuteCommand();

        var row = Query(db, db.CurrentConnectionConfig.DbType == DbType.SqlServer).First()!;
        var averages = new[] { (0, row.V0), (1, row.V1), (2, row.V2), (3, row.V3), (8, row.V8) };
        foreach (var (slot, raw) in averages)
        {
            AggregateValueNormalizer.Normalize(AggregateOp.Avg, ColumnSelectorFactory.PropertyType(typeof(AvgProbe), Slots[slot].Property), raw)
                .Should().BeOfType<double>().Which.Should().BeApproximately(5.0 / 3, 1e-4, Slots[slot].Property);
        }
    }
}
