using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

// POST /api/users applies the Email field rule to the normalized (trimmed, lowercased) address, with the
// message the items API gives.
[Collection("ApiIntegration")]
public class UsersControllerEmailFormatTests(ApiFactory factory)
{
    [Theory]
    [InlineData("plain")]
    [InlineData("a@b")]
    [InlineData("a@@b.co")]
    [InlineData("a b@c.d")]
    [InlineData("@b.co")]
    public async Task Create_with_a_malformed_email_is_400(string email)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();

        var resp = await admin.PostAsJsonAsync("/api/users",
            new { email, password = "password-123", name = "N" });

        var body = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
        var error = JsonDocument.Parse(body).RootElement.GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("BAD_USER_INPUT");
        error.GetProperty("message").GetString().Should().Be("Field 'email' must be a valid email address.");
    }

    [Fact]
    public async Task Create_checks_the_normalized_email_so_surrounding_blanks_and_case_are_fine()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var local = $"Mixed-{Guid.NewGuid():N}";

        var resp = await admin.PostAsJsonAsync("/api/users",
            new { email = $"  {local}@Struo.Test ", password = "password-123" });

        var body = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.Created, body);
        JsonDocument.Parse(body).RootElement.GetProperty("data").GetProperty("email").GetString()
            .Should().Be($"{local}@struo.test".ToLowerInvariant());
    }
}
