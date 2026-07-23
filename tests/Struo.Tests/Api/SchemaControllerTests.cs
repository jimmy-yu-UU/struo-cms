// tests/Struo.Tests/Api/SchemaControllerTests.cs
using System.Linq;
using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// SEC-8: <c>/api/schema</c> exposed the full content model (every collection, every field) to
/// anonymous callers — a reconnaissance target, and inconsistent with production disabling GraphQL
/// introspection. The frontend only ever calls this endpoint from post-login-only code paths
/// (AppShell.onMounted, CollectionListView, ItemFormView, MediaDetailDialog, RelationPicker/RelatedList),
/// all mounted behind the router's authGuard, so gating this the same way as <c>/api/languages</c>
/// (<see cref="LanguagesEndpointTests"/>) doesn't break any anonymous frontend flow.
/// </summary>
[Collection("ApiIntegration")]
public sealed class SchemaControllerTests
{
    [Fact]
    public async Task GetAll_Anonymous_Returns401()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        var res = await client.GetAsync("/api/schema");
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetOne_Anonymous_Returns401()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        var res = await client.GetAsync("/api/schema/article");
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAll_Authenticated_Returns200_WithFullCollectionList()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateAuthenticatedClientAsync();

        var res = await client.GetAsync("/api/schema");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var arr = doc.RootElement.GetProperty("data");
        arr.GetArrayLength().Should().BeGreaterThan(0);
        arr.EnumerateArray().Select(e => e.GetProperty("name").GetString())
           .Should().Contain("article");
    }

    [Fact]
    public async Task GetOne_Authenticated_Returns200_WithoutHiddenFields()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateAuthenticatedClientAsync();

        var res = await client.GetAsync("/api/schema/article");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("name").GetString().Should().Be("article");
        // SchemaService.WithoutHiddenFields still applies for authenticated callers -- SEC-8 only
        // changes anonymous access, it doesn't relax the pre-existing Hidden-field guarantee: the
        // Hidden top-level field (internalNote) and the Hidden+Translatable field (internalSlug) must
        // still be absent from the "fields" array.
        var fieldNames = data.GetProperty("fields").EnumerateArray()
            .Select(f => f.GetProperty("name").GetString()).ToList();
        fieldNames.Should().NotContain("internalNote");
        fieldNames.Should().NotContain("internalSlug");

        // SEC-14: TranslationMetadata.Fields is a second, separate raw sidecar field-name list under
        // "translation.fields" -- it must be filtered the same way, or the Hidden+Translatable field's
        // NAME (internalSlug) still leaked here even after SEC-13 redacted its VALUE.
        var translationFieldNames = data.GetProperty("translation").GetProperty("fields")
            .EnumerateArray().Select(f => f.GetString()).ToList();
        translationFieldNames.Should().NotContain("internalSlug");
        translationFieldNames.Should().Contain("title"); // an ordinary translatable field still lists
    }
}
