using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// A top-level Select value outside its options is a 400 BAD_USER_INPUT naming the field, the same
/// over REST and GraphQL (the fixture article's <c>status</c> allows draft and published).
/// </summary>
[Collection("ApiIntegration")]
public class FieldFormatApiTests(ApiFactory factory)
{
    private const string Message = "Field 'status' value 'archived' is not in its options.";
    private readonly ApiFactory _factory = factory;

    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    [Fact]
    public async Task Rest_create_with_a_select_outside_its_options_is_a_400()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();

        var resp = await c.PostAsJsonAsync("/api/items/fxArticle", new
        {
            status = "archived",
            translations = new { en = new { title = "A" } },
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = Root(await resp.Content.ReadAsStringAsync()).GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("BAD_USER_INPUT");
        error.GetProperty("message").GetString().Should().Be(Message);
    }

    [Fact]
    public async Task Graphql_create_with_a_select_outside_its_options_is_a_400_equivalent()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();

        var resp = await c.PostAsJsonAsync("/graphql", new
        {
            query = "mutation { createFxArticle(input: { status: \"archived\", translations: [ " +
                    "{ locale: \"en\", fields: { title: \"a\" } } ] }) { id } }",
        });

        var err = Root(await resp.Content.ReadAsStringAsync()).GetProperty("errors")[0];
        err.GetProperty("extensions").GetProperty("code").GetString().Should().Be("BAD_USER_INPUT");
        err.GetProperty("message").GetString().Should().Be(Message);
    }

    [Fact]
    public async Task Rest_update_with_a_select_outside_its_options_is_a_400_and_writes_nothing()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var created = await c.PostAsJsonAsync("/api/items/fxArticle", new
        {
            status = "draft",
            translations = new { en = new { title = "A" } },
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = Root(await created.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString();

        var resp = await c.PutAsJsonAsync($"/api/items/fxArticle/{id}", new { status = "archived" });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var read = Root(await c.GetStringAsync($"/api/items/fxArticle/{id}")).GetProperty("data");
        read.GetProperty("status").GetString().Should().Be("draft");
    }

    // The core `user` collection's email is an Email field, so the same rule reaches it over both surfaces.
    private const string EmailMessage = "Field 'email' must be a valid email address.";

    [Fact]
    public async Task Rest_update_of_a_user_email_that_is_not_an_email_is_a_400()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();

        var resp = await c.PutAsJsonAsync($"/api/items/user/{_factory.AdminUserId}", new { email = "not-an-email" });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = Root(await resp.Content.ReadAsStringAsync()).GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("BAD_USER_INPUT");
        error.GetProperty("message").GetString().Should().Be(EmailMessage);
    }

    [Fact]
    public async Task Graphql_update_of_a_user_email_that_is_not_an_email_is_a_400_equivalent()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();

        var resp = await c.PostAsJsonAsync("/graphql", new
        {
            query = $"mutation {{ updateUser(id: \"{_factory.AdminUserId}\", input: {{ email: \"not-an-email\" }}) {{ id }} }}",
        });

        var err = Root(await resp.Content.ReadAsStringAsync()).GetProperty("errors")[0];
        err.GetProperty("extensions").GetProperty("code").GetString().Should().Be("BAD_USER_INPUT");
        err.GetProperty("message").GetString().Should().Be(EmailMessage);
    }

    // The items API checks the email as sent: UserWriteInterceptor trims and lowercases it only afterwards,
    // so a padded address is a 400 here while POST /api/users accepts it.
    [Fact]
    public async Task Rest_update_of_a_user_email_checks_it_before_it_is_trimmed()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();

        var resp = await c.PutAsJsonAsync($"/api/items/user/{_factory.AdminUserId}", new { email = " A@b.co " });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Root(await resp.Content.ReadAsStringAsync()).GetProperty("error").GetProperty("message").GetString()
            .Should().Be(EmailMessage);
    }
}
