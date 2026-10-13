using System.Data;
using AwesomeAssertions;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Settings;
using Xunit;
using SugarDbType = SqlSugar.DbType;

namespace Struo.Tests.Persistence;

/// <summary>
/// <see cref="UtcInstantConverter"/> without a database: the parameter it binds and the value it reads
/// for each <see cref="DateTimeKind"/>, <see cref="DateTimeOffset"/> and null. The suite's live runs
/// cover the same converter through PostgreSQL and SQL Server (<c>UtcInstantColumnTests</c>).
/// </summary>
public class UtcInstantConverterTests
{
    private static readonly DateTime Instant = new(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
    private readonly UtcInstantConverter _converter = new();

    [Fact]
    public void Utc_write_binds_the_value_as_DateTimeOffset()
    {
        var parameter = _converter.ParameterConverter<DateTime>(Instant, 3);

        parameter.ParameterName.Should().Be("@UtcInstant3");
        parameter.DbType.Should().Be(DbType.DateTimeOffset);
        ((DateTime)parameter.Value).Should().Be(Instant);
        ((DateTime)parameter.Value).Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Local_write_is_converted_to_utc()
    {
        var value = (DateTime)_converter.ParameterConverter<DateTime>(Instant.ToLocalTime(), 0).Value;

        value.Should().Be(Instant);
        value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Unspecified_write_is_taken_as_utc()
    {
        var value = (DateTime)_converter.ParameterConverter<DateTime>(
            DateTime.SpecifyKind(Instant, DateTimeKind.Unspecified), 0).Value;

        value.Should().Be(Instant);
        value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Null_write_binds_DBNull()
    {
        var parameter = _converter.ParameterConverter<DateTime?>(null!, 1);

        parameter.Value.Should().Be(DBNull.Value);
        parameter.DbType.Should().Be(DbType.DateTimeOffset);
    }

    // Columns typed object keep each value's Kind and type exactly as a provider's reader returns it.
    private static IDataRecord Record(params object[] values)
    {
        var table = new DataTable();
        foreach (var i in Enumerable.Range(0, values.Length)) table.Columns.Add("c" + i, typeof(object));
        table.Rows.Add(values);
        var reader = table.CreateDataReader();
        reader.Read().Should().BeTrue();
        return reader;
    }

    [Fact]
    public void Reads_return_utc_for_every_provider_value_shape()
    {
        var record = Record(
            Instant,
            Instant.ToLocalTime(),
            DateTime.SpecifyKind(Instant, DateTimeKind.Unspecified),
            new DateTimeOffset(Instant).ToOffset(TimeSpan.FromHours(8)));

        foreach (var index in Enumerable.Range(0, 4))
        {
            var value = _converter.QueryConverter<DateTime>(record, index);
            value.Should().Be(Instant, "column {0}", index);
            value.Kind.Should().Be(DateTimeKind.Utc, "column {0}", index);
        }
    }

    [Fact]
    public void Null_reads_return_default()
    {
        var record = Record(DBNull.Value);

        _converter.QueryConverter<DateTime?>(record, 0).Should().BeNull();
        _converter.QueryConverter<DateTime>(record, 0).Should().Be(default);
    }

    [Fact]
    public void A_nullable_read_of_a_value_returns_the_utc_instant()
    {
        var value = _converter.QueryConverter<DateTime?>(Record(Instant.ToLocalTime()), 0);

        value.Should().Be(Instant);
        value!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Any_other_value_passes_through()
    {
        _converter.QueryConverter<string>(Record("text"), 0).Should().Be("text");
        _converter.ParameterConverter<string>("text", 0).Value.Should().Be("text");
    }

    [Theory]
    [InlineData(SugarDbType.PostgreSQL, true)]
    [InlineData(SugarDbType.SqlServer, true)]
    [InlineData(SugarDbType.MySql, false)]
    [InlineData(SugarDbType.Sqlite, false)]
    [InlineData(SugarDbType.Oracle, false)]
    public void Applies_only_to_postgresql_and_sql_server(SugarDbType dbType, bool expected) =>
        UtcInstantConverter.AppliesTo(dbType).Should().Be(expected);

    [Fact]
    public void Only_zone_aware_DateTime_properties_take_it()
    {
        UtcInstantConverter.IsUtcInstantProperty(typeof(MediaFolder).GetProperty(nameof(MediaFolder.CreatedAt))!)
            .Should().BeTrue();
        UtcInstantConverter.IsUtcInstantProperty(typeof(UtcInstantRow).GetProperty(nameof(UtcInstantRow.MaybeAt))!)
            .Should().BeTrue();
        UtcInstantConverter.IsUtcInstantProperty(typeof(Struo.Infrastructure.Identity.User).GetProperty("CreatedAt")!)
            .Should().BeFalse();
        UtcInstantConverter.IsUtcInstantProperty(typeof(SiteSettings).GetProperty(nameof(SiteSettings.BrandName))!)
            .Should().BeFalse();
    }
}
