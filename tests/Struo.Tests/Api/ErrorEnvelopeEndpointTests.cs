using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

[Collection("ApiIntegration")]
public class ErrorEnvelopeEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement;

    [Fact]
    public async Task Domain_query_exception_is_bad_user_input_envelope()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/items/article?filter[ghost][_eq]=x");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var root = Root(await resp.Content.ReadAsStringAsync());
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("error").GetProperty("code").GetString().Should().Be("BAD_USER_INPUT");
        root.GetProperty("error").GetProperty("message").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Unknown_collection_is_not_found_envelope()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/items/nope");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Root(await resp.Content.ReadAsStringAsync())
            .GetProperty("error").GetProperty("code").GetString().Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Malformed_json_body_is_validation_envelope_with_details()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var content = new StringContent("{ not json", Encoding.UTF8, "application/json");
        var resp = await client.PostAsync("/api/items/article", content);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = Root(await resp.Content.ReadAsStringAsync()).GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("VALIDATION");
        error.GetProperty("details").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Bare_not_found_has_clean_human_message()
    {
        var client = _factory.CreateClient(); // article is public-read in test config
        var resp = await client.GetAsync($"/api/items/article/{System.Guid.NewGuid()}"); // valid collection, nonexistent id -> bare NotFound()
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
        var error = Root(await resp.Content.ReadAsStringAsync()).GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("NOT_FOUND");
        error.GetProperty("message").GetString().Should().Be("Resource not found."); // clean DefaultMessage(404), NOT a type name
    }

    // CS-2: a malformed id on a by-id route must be a mapped 400 (QueryException), not a masked
    // 500 from a raw FormatException/ArgumentException escaping ConvertId.
    [Fact]
    public async Task Malformed_id_is_bad_user_input_envelope_not_masked_500()
    {
        var client = _factory.CreateClient(); // article is public-read in test config
        var resp = await client.GetAsync("/api/items/article/not-a-guid");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = Root(await resp.Content.ReadAsStringAsync()).GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("BAD_USER_INPUT");
        var message = error.GetProperty("message").GetString();
        message.Should().NotBeNullOrEmpty();
        message.Should().NotContain("FormatException");
        message.Should().NotContain("ArgumentException");
        message.Should().NotContain("Exception");
    }

    // AUTH-2: CsrfProtectionMiddleware used to write a bespoke `{ error: { message } }` body on
    // rejection instead of the standard envelope every other error path uses (see AUTH-1's
    // OnRedirectToAccessDenied for the pattern this must match).
    [Fact]
    public async Task Csrf_rejected_write_is_forbidden_envelope()
    {
        // CreateAuthenticatedClientAsync mirrors the SPA and pre-adds the CSRF header for its own
        // login call (see ApiFactory) — strip it back off so this request exercises the actual
        // rejection path (cookie present, header missing).
        var client = await _factory.CreateAuthenticatedClientAsync();
        client.DefaultRequestHeaders.Remove(Struo.Api.Auth.CsrfProtectionMiddleware.HeaderName);
        var resp = await client.PutAsJsonAsync(
            "/api/settings/branding", new { brandName = "X", logoFileId = (string?)null });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var root = Root(await resp.Content.ReadAsStringAsync());
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("error").GetProperty("code").GetString().Should().Be("FORBIDDEN");
        root.GetProperty("error").GetProperty("message").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Bad_login_is_unauthorized_envelope_via_fail_helper()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(Struo.Api.Auth.CsrfProtectionMiddleware.HeaderName, "1");
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { email = "nope@x.test", password = "wrongwrong" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var root = Root(await resp.Content.ReadAsStringAsync());
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("error").GetProperty("code").GetString().Should().Be("UNAUTHORIZED");
    }
}
