// tests/Struo.Tests/Query/AggregateValueNormalizerTests.cs
using AwesomeAssertions;
using Struo.Domain.Query;
using Struo.Infrastructure.Query;
using Xunit;

namespace Struo.Tests.Query;

public class AggregateValueNormalizerTests
{
    [Fact] public void Null_stays_null() => AggregateValueNormalizer.Normalize(AggregateOp.Sum, typeof(int), null).Should().BeNull();

    [Theory]
    [InlineData(4)] [InlineData(4L)] [InlineData(4.0)]
    public void Count_is_always_long(object raw) =>
        AggregateValueNormalizer.Normalize(AggregateOp.Count, typeof(string), raw).Should().BeOfType<long>().And.Be(4L);

    [Fact] public void Sum_of_int_widens_to_long() =>
        AggregateValueNormalizer.Normalize(AggregateOp.Sum, typeof(int), 10L).Should().BeOfType<long>().And.Be(10L);

    [Fact] public void Sum_of_nullable_int_widens_to_long() =>
        AggregateValueNormalizer.Normalize(AggregateOp.Sum, typeof(int?), 10).Should().BeOfType<long>().And.Be(10L);

    [Fact] public void Sum_of_decimal_stays_decimal() =>
        AggregateValueNormalizer.Normalize(AggregateOp.Sum, typeof(decimal), 1.5).Should().BeOfType<decimal>().And.Be(1.5m);

    [Fact] public void Sum_of_double_stays_double() =>
        AggregateValueNormalizer.Normalize(AggregateOp.Sum, typeof(double), 1.5m).Should().BeOfType<double>().And.Be(1.5);

    [Theory]
    [InlineData(2.5)] [InlineData(2.5f)]
    public void Avg_is_double_from_double_or_float(object raw) =>
        AggregateValueNormalizer.Normalize(AggregateOp.Avg, typeof(int), raw).Should().BeOfType<double>().And.Be(2.5);

    [Fact] public void Avg_from_decimal_is_double() =>
        AggregateValueNormalizer.Normalize(AggregateOp.Avg, typeof(int), 2.5m).Should().BeOfType<double>().And.Be(2.5);

    [Fact] public void Min_of_int_read_as_long_comes_back_as_int() =>
        AggregateValueNormalizer.Normalize(AggregateOp.Min, typeof(int), 3L).Should().BeOfType<int>().And.Be(3);

    [Fact] public void Max_of_datetime_read_as_string_is_parsed() =>
        AggregateValueNormalizer.Normalize(AggregateOp.Max, typeof(DateTime?), "2026-09-05 21:08:59.953")
            .Should().BeOfType<DateTime>().And.Be(new DateTime(2026, 9, 5, 21, 8, 59, 953));

    [Fact] public void Max_of_datetime_read_as_datetime_is_kept() =>
        AggregateValueNormalizer.Normalize(AggregateOp.Max, typeof(DateTime), new DateTime(2026, 1, 1))
            .Should().Be(new DateTime(2026, 1, 1));

    // Finding D: Npgsql returns a `date`/`time` column's aggregate as a DateTime (midnight-anchored
    // for `date`), not as a DateOnly/TimeOnly — Convert.ChangeType has no built-in conversion for
    // either, so it throws without this special case.
    [Fact] public void Max_of_dateonly_read_as_datetime_from_npgsql_is_converted() =>
        AggregateValueNormalizer.Normalize(AggregateOp.Max, typeof(DateOnly), new DateTime(2026, 9, 5))
            .Should().BeOfType<DateOnly>().And.Be(new DateOnly(2026, 9, 5));

    [Fact] public void Min_of_timeonly_read_as_datetime_from_npgsql_is_converted() =>
        AggregateValueNormalizer.Normalize(AggregateOp.Min, typeof(TimeOnly), new DateTime(1, 1, 1, 21, 8, 59))
            .Should().BeOfType<TimeOnly>().And.Be(new TimeOnly(21, 8, 59));

    // SQL Server hands a datetimeoffset aggregate over as DateTimeOffset and PostgreSQL returns a
    // timestamptz as a Local DateTime; both come back as the UTC instant.
    [Fact] public void Max_of_a_datetimeoffset_is_the_utc_datetime()
    {
        var result = AggregateValueNormalizer.Normalize(AggregateOp.Max, typeof(DateTime),
            new DateTimeOffset(2026, 3, 4, 10, 36, 7, TimeSpan.FromHours(5.5)));
        result.Should().BeOfType<DateTime>();
        ((DateTime)result!).Should().Be(new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc));
        ((DateTime)result).Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact] public void Min_of_a_datetimeoffset_with_a_nullable_target_is_the_utc_datetime()
    {
        var result = AggregateValueNormalizer.Normalize(AggregateOp.Min, typeof(DateTime?),
            new DateTimeOffset(2026, 3, 4, 13, 6, 7, TimeSpan.FromHours(8)));
        ((DateTime)result!).Should().Be(new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc));
        ((DateTime)result).Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact] public void Max_of_a_local_datetime_is_converted_to_utc()
    {
        var local = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc).ToLocalTime();
        var result = (DateTime)AggregateValueNormalizer.Normalize(AggregateOp.Max, typeof(DateTime), local)!;
        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Should().Be(new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Utc)]
    public void Max_of_an_unspecified_or_utc_datetime_is_untouched(DateTimeKind kind)
    {
        var raw = new DateTime(2026, 3, 4, 5, 6, 7, kind);
        var result = (DateTime)AggregateValueNormalizer.Normalize(AggregateOp.Max, typeof(DateTime), raw)!;
        result.Should().Be(raw);
        result.Kind.Should().Be(kind);
    }
}
