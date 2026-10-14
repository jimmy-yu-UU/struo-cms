using System.Text.Json;
using AwesomeAssertions;
using Struo.Application.Serialization;
using Xunit;

namespace Struo.Tests.Serialization;

public sealed class UtcDateTimeJsonConverterTests
{
    private static readonly JsonSerializerOptions Opts = Build();

    private static JsonSerializerOptions Build()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        o.Converters.Add(new UtcDateTimeJsonConverter());
        return o;
    }

    private sealed record Holder(DateTime At, DateTime? MaybeAt);

    private static DateTime Read(string json) => JsonSerializer.Deserialize<DateTime>(json, Opts);

    [Theory]
    [InlineData("\"2026-10-14T02:00:00Z\"")]
    [InlineData("\"2026-10-14T10:00:00+08:00\"")]
    [InlineData("\"2026-10-13T21:00:00-05:00\"")]
    [InlineData("\"2026-10-14T02:00:00\"")]
    public void Read_yields_the_same_utc_instant(string json)
    {
        var value = Read(json);

        value.Kind.Should().Be(DateTimeKind.Utc);
        value.Should().Be(new DateTime(2026, 10, 14, 2, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Read_keeps_fractional_seconds()
    {
        var value = Read("\"2026-10-14T10:00:00.1234567+08:00\"");

        value.Kind.Should().Be(DateTimeKind.Utc);
        value.Should().Be(new DateTime(2026, 10, 14, 2, 0, 0, DateTimeKind.Utc).AddTicks(1234567));
    }

    [Fact]
    public void Read_null_into_nullable_is_null_and_value_round_trips()
    {
        var h = JsonSerializer.Deserialize<Holder>(
            """{"at":"2026-10-14T10:00:00+08:00","maybeAt":null}""", Opts)!;
        h.MaybeAt.Should().BeNull();
        h.At.Kind.Should().Be(DateTimeKind.Utc);

        var withValue = JsonSerializer.Deserialize<Holder>(
            """{"at":"2026-10-14T02:00:00Z","maybeAt":"2026-10-14T10:00:00+08:00"}""", Opts)!;
        withValue.MaybeAt.Should().Be(new DateTime(2026, 10, 14, 2, 0, 0, DateTimeKind.Utc));
        JsonSerializer.Serialize(new Holder(h.At, null), Opts).Should().Contain("\"maybeAt\":null");
    }

    [Theory]
    [InlineData("\"not a date\"")]
    [InlineData("\"\"")]
    [InlineData("12")]
    public void Read_rejects_what_the_default_parser_rejects(string json)
    {
        var act = () => Read(json);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Write_utc_ends_with_Z()
    {
        var json = JsonSerializer.Serialize(new DateTime(2026, 10, 14, 2, 0, 0, DateTimeKind.Utc), Opts);

        json.Should().Be("\"2026-10-14T02:00:00Z\"");
    }

    [Fact]
    public void Write_unspecified_is_taken_as_utc()
    {
        var json = JsonSerializer.Serialize(new DateTime(2026, 10, 14, 2, 0, 0, DateTimeKind.Unspecified), Opts);

        json.Should().Be("\"2026-10-14T02:00:00Z\"");
    }

    [Fact]
    public void Write_local_is_converted_to_the_same_instant()
    {
        var local = new DateTime(2026, 10, 14, 2, 0, 0, DateTimeKind.Utc).ToLocalTime();

        var json = JsonSerializer.Serialize(local, Opts);

        json.Should().Be("\"2026-10-14T02:00:00Z\"");
    }

    [Fact]
    public void Write_nullable_value_ends_with_Z()
    {
        var json = JsonSerializer.Serialize(
            new Holder(new DateTime(2026, 10, 14, 2, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified)), Opts);

        json.Should().Contain("\"maybeAt\":\"2026-01-01T00:00:00Z\"");
    }
}
