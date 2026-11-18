using AwesomeAssertions;
using Struo.Api.GraphQl;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Xunit;

namespace Struo.Tests.GraphQl;

/// <summary>The text of a date or time value, by the interface of the field that holds it.</summary>
public sealed class TemporalTextTests
{
    private static readonly DateTime Moment = new(2026, 3, 4, 5, 6, 7);

    // A Local value of the instant 2026-03-04T05:06:07Z, whatever zone the host is in.
    private static DateTime LocalMoment() =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(Moment, DateTimeKind.Utc), TimeZoneInfo.Local);

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    public void An_instant_of_Utc_or_unspecified_kind_prints_as_it_is_with_a_Z(DateTimeKind kind) =>
        TemporalText.FormatInstant(DateTime.SpecifyKind(Moment, kind)).Should().Be("2026-03-04T05:06:07Z");

    [Fact]
    public void An_instant_of_Local_kind_prints_as_the_UTC_instant() =>
        TemporalText.FormatInstant(LocalMoment()).Should().Be("2026-03-04T05:06:07Z");

    [Theory]
    [InlineData(0L, "2026-03-04T05:06:07Z")]
    [InlineData(5_000_000L, "2026-03-04T05:06:07.5Z")]
    [InlineData(2_500_000L, "2026-03-04T05:06:07.25Z")]
    [InlineData(1L, "2026-03-04T05:06:07.0000001Z")]
    [InlineData(1_234_567L, "2026-03-04T05:06:07.1234567Z")]
    public void Fractional_seconds_print_only_when_there_are_some_and_without_trailing_zeros(long ticks, string expected) =>
        TemporalText.FormatInstant(Moment.AddTicks(ticks)).Should().Be(expected);

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public void A_date_prints_the_date_part_of_the_value_without_a_zone_conversion(DateTimeKind kind) =>
        TemporalText.FormatDate(new DateTime(2026, 3, 4, 23, 30, 0, kind)).Should().Be("2026-03-04");

    [Fact]
    public void The_interface_of_the_field_decides_the_form()
    {
        var value = Moment.AddTicks(2_500_000);

        TemporalText.Of(FieldInterface.Date, value).Should().Be("2026-03-04");
        TemporalText.Of(FieldInterface.DateTime, value).Should().Be("2026-03-04T05:06:07.25Z");
        TemporalText.Of(FieldInterface.Time, value).Should().Be("2026-03-04T05:06:07.25Z");
        TemporalText.Instant(value).Should().Be("2026-03-04T05:06:07.25Z");
    }

    [Fact]
    public void A_value_that_is_not_a_DateTime_is_returned_as_it_is()
    {
        TemporalText.Of(FieldInterface.DateTime, null).Should().BeNull();
        TemporalText.Of(FieldInterface.Date, "text").Should().Be("text");
        TemporalText.Instant(42L).Should().Be(42L);
    }

    private static FieldMetadata Field(string name, FieldInterface face) => new() { Name = name, Label = name, Interface = face };

    [Fact]
    public void Kinds_lists_the_temporal_fields_and_ignores_the_case_of_a_name_when_asked_to()
    {
        var kinds = TemporalText.Kinds(
            [Field("title", FieldInterface.Text), Field("day", FieldInterface.Date), Field("at", FieldInterface.DateTime)],
            StringComparer.OrdinalIgnoreCase);

        kinds.Should().BeEquivalentTo(new Dictionary<string, FieldInterface>
        {
            ["day"] = FieldInterface.Date, ["at"] = FieldInterface.DateTime,
        });
        kinds.ContainsKey("DAY").Should().BeTrue();
    }

    [Fact]
    public void Two_fields_whose_names_differ_only_in_case_resolve_to_the_first_one()
    {
        var kinds = TemporalText.Kinds(
            [Field("stamp", FieldInterface.DateTime), Field("sTAMP", FieldInterface.Date)], StringComparer.OrdinalIgnoreCase);

        kinds["sTAMP"].Should().Be(FieldInterface.DateTime);
    }

    [Fact]
    public void A_first_field_that_is_not_temporal_hides_a_later_temporal_one_of_the_same_name()
    {
        var kinds = TemporalText.Kinds(
            [Field("stamp", FieldInterface.Text), Field("sTAMP", FieldInterface.Date)], StringComparer.OrdinalIgnoreCase);

        kinds.Should().BeEmpty();
    }
}
