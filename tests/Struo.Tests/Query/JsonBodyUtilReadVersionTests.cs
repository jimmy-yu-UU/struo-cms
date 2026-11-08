// tests/Struo.Tests/Query/JsonBodyUtilReadVersionTests.cs
using System.Text.Json;
using AwesomeAssertions;
using Struo.Application.Query;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.Query;

/// <summary>
/// <c>ReadVersion</c> finds the <c>version</c> key ignoring case, accepts it once, and rejects an
/// invalid value or a repeated key under any casing with <see cref="InvalidVersionException"/>.
/// </summary>
public class JsonBodyUtilReadVersionTests
{
    private static JsonElement Body(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    [Theory]
    [InlineData("{\"version\":5}", 5)]
    [InlineData("{\"Version\":5}", 5)]
    [InlineData("{\"VERSION\":\"7\"}", 7)]
    [InlineData("{\"vErSiOn\":9}", 9)]
    public void A_single_key_in_any_casing_is_read(string raw, long expected) =>
        JsonBodyUtil.ReadVersion(Body(raw)).Should().Be(expected);

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"status\":\"x\"}")]
    [InlineData("{\"version\":null}")]
    [InlineData("{\"Version\":null}")]
    public void An_absent_or_null_key_is_no_version(string raw) =>
        JsonBodyUtil.ReadVersion(Body(raw)).Should().BeNull();

    [Theory]
    [InlineData("{\"Version\":\"abc\"}")]
    [InlineData("{\"VERSION\":1.5}")]
    [InlineData("{\"Version\":true}")]
    [InlineData("{\"version\":\"abc\"}")]
    public void An_invalid_value_under_any_casing_throws(string raw)
    {
        var act = () => JsonBodyUtil.ReadVersion(Body(raw));
        act.Should().Throw<InvalidVersionException>();
    }

    [Theory]
    [InlineData("{\"version\":5,\"Version\":5}")]
    [InlineData("{\"version\":5,\"Version\":9}")]
    [InlineData("{\"Version\":5,\"VERSION\":null}")]
    [InlineData("{\"version\":null,\"version\":null}")]
    public void A_repeated_key_in_any_casing_throws(string raw)
    {
        var act = () => JsonBodyUtil.ReadVersion(Body(raw));
        act.Should().Throw<InvalidVersionException>().WithMessage("*at most once*");
    }
}
