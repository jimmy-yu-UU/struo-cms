// tests/Struo.Tests/GraphQl/RestGraphQlDateTimeParityTests.cs
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace Struo.Tests.GraphQl;

/// <summary>
/// REST and GraphQL print the same text for the same stored instant: the audit stamps (<c>createdAt</c>,
/// <c>updatedAt</c>) and an ordinary <c>DateTime</c> field. The comparison only discriminates while the
/// host's local zone is not UTC, so it is meant to run under a non-UTC host zone on every backend.
/// </summary>
[Collection("ApiIntegration")]
public class RestGraphQlDateTimeParityTests(ApiFactory factory, ITestOutputHelper output)
{
    private static readonly string[] Fields = ["createdAt", "updatedAt", "publishedAt"];

    [Fact]
    public async Task The_same_row_prints_the_same_date_times_over_REST_and_GraphQL()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var created = await admin.PostAsJsonAsync("/api/items/fxArticle", new
        {
            status = "draft",
            publishedAt = "2026-10-14T10:00:00+08:00",
            translations = new { en = new { title = "datetime-parity-probe" } }
        });
        created.StatusCode.Should().Be(System.Net.HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync())
            .RootElement.GetProperty("data").GetProperty("id").GetString()!;

        var rest = JsonDocument.Parse(await admin.GetStringAsync($"/api/items/fxArticle/{id}"))
            .RootElement.GetProperty("data");
        var gqlResponse = await admin.PostAsJsonAsync("/graphql", new
        {
            query = $"{{ fxArticle(id: \"{id}\") {{ {string.Join(' ', Fields)} }} }}"
        });
        var gqlRoot = JsonDocument.Parse(await gqlResponse.Content.ReadAsStringAsync()).RootElement;
        gqlRoot.TryGetProperty("errors", out var errors).Should().BeFalse($"unexpected errors: {errors}");
        var gql = gqlRoot.GetProperty("data").GetProperty("fxArticle");

        output.WriteLine($"host zone {TimeZoneInfo.Local.Id}, backend {TestBackend.Current}");
        foreach (var field in Fields)
            output.WriteLine($"{field}: REST {rest.GetProperty(field).GetString()} | GraphQL {gql.GetProperty(field).GetString()}");

        foreach (var field in Fields)
            gql.GetProperty(field).GetString().Should().Be(rest.GetProperty(field).GetString(), field);
    }
}
