// tests/Struo.Tests/GraphQl/GraphQlUpdateRequiredSemanticsTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.GraphQl;

/// <summary>
/// GraphQL parity for the update-path Required semantics change (2026-09-08): <c>updateRole</c>
/// goes through the same <see cref="Struo.Application.Query.ItemService.UpdateAsync"/> as REST, so
/// omitting <c>name</c> (Required=true) from an <c>updateRole</c> input must now succeed and keep
/// the role's stored name, exactly like the REST partial-update rule in chapter 12 of the manual.
/// Drives through the real host pipeline (ApiFactory), not
/// <see cref="GraphQlMutationExecutionTests"/>'s fake data
/// source, so it actually exercises <c>ItemDeserializer</c>/<c>FieldValueRules</c>.
/// </summary>
[Collection("ApiIntegration")]
public class GraphQlUpdateRequiredSemanticsTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement;

    private static async Task<JsonElement> PostGraphQlAsync(HttpClient client, string query)
    {
        var response = await client.PostAsJsonAsync("/graphql", new { query });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return Root(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UpdateRole_omitting_name_succeeds_and_keeps_the_stored_name()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var roleName = $"gql-required-{Guid.NewGuid():N}";

        var create = await PostGraphQlAsync(admin,
            $"mutation {{ createRole(input: {{ name: \"{roleName}\" }}) {{ id name }} }}");
        create.TryGetProperty("errors", out var createErrors).Should().BeFalse($"unexpected errors: {createErrors}");
        var id = create.GetProperty("data").GetProperty("createRole").GetProperty("id").GetString()!;

        var update = await PostGraphQlAsync(admin,
            $"mutation {{ updateRole(id: \"{id}\", input: {{ description: \"gql desc\" }}) {{ name description }} }}");

        update.TryGetProperty("errors", out var updateErrors).Should().BeFalse($"unexpected errors: {updateErrors}");
        var updated = update.GetProperty("data").GetProperty("updateRole");
        updated.GetProperty("name").GetString().Should().Be(roleName);
        updated.GetProperty("description").GetString().Should().Be("gql desc");
    }
}
