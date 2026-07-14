using System.Text.Json;
using AwesomeAssertions;
using Struo.Api.Http;
using Xunit;

namespace Struo.Tests.Api;

public class EnvelopeTypesTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(400, "BAD_USER_INPUT")]
    [InlineData(401, "UNAUTHORIZED")]
    [InlineData(403, "FORBIDDEN")]
    [InlineData(404, "NOT_FOUND")]
    [InlineData(409, "CONFLICT")]
    [InlineData(418, "INTERNAL_SERVER_ERROR")]
    [InlineData(500, "INTERNAL_SERVER_ERROR")]
    public void ForStatus_maps_status_to_code(int status, string expected) =>
        ErrorCodes.ForStatus(status).Should().Be(expected);

    [Fact]
    public void Success_omits_meta_when_null()
    {
        var json = JsonSerializer.Serialize(Envelope.Success(new { id = 1 }), Web);
        json.Should().Contain("\"success\":true");
        json.Should().Contain("\"data\":");
        json.Should().NotContain("meta");
    }

    [Fact]
    public void Success_includes_meta_when_present()
    {
        var json = JsonSerializer.Serialize(Envelope.Success(new[] { 1 }, new MetaInfo(42, 25, 0)), Web);
        json.Should().Contain("\"meta\":{\"total\":42,\"limit\":25,\"offset\":0}");
    }

    [Fact]
    public void Error_carries_code_and_message_and_omits_details_when_null()
    {
        var json = JsonSerializer.Serialize(Envelope.Error("BAD_USER_INPUT", "nope"), Web);
        json.Should().Contain("\"success\":false");
        json.Should().Contain("\"code\":\"BAD_USER_INPUT\"");
        json.Should().Contain("\"message\":\"nope\"");
        json.Should().NotContain("details");
    }

    [Fact]
    public void Error_includes_details_when_present()
    {
        var json = JsonSerializer.Serialize(
            Envelope.Error("VALIDATION", "bad", new[] { new ValidationDetail("email", "required") }), Web);
        json.Should().Contain("\"details\":[{\"field\":\"email\",\"message\":\"required\"}]");
    }
}
