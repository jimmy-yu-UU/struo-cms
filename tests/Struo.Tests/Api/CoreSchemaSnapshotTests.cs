// tests/Struo.Tests/Api/CoreSchemaSnapshotTests.cs
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Struo.Infrastructure.Metadata;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// Pins the core collection metadata the admin SPA actually receives. The frontend mirrors this
/// shape by hand (frontend/src/types/schema.ts, frontend/src/lib/fieldTypes/types.ts) and silently
/// degrades when the mirror drifts: an unknown FieldInterface falls back to a read-only renderer
/// (registry.ts:222) and a field with no list-column formatter is dropped from the list view
/// altogether (selectListColumns.ts:10). Neither shows up in dotnet build, vue-tsc, or the unit
/// suites. This test makes any change to the core content model visible in the diff, and
/// frontend/tests/schemaContract.test.ts then proves the SPA can still render it.
/// </summary>
[Collection("ApiIntegration")]
public sealed class CoreSchemaSnapshotTests
{
    private const string UpdateEnvVar = "UPDATE_SCHEMA_SNAPSHOT";

    [Fact]
    public async Task CoreSchema_MatchesCommittedSnapshot()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateAuthenticatedClientAsync();

        var res = await client.GetAsync("/api/schema");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var core = CoreCollectionNames();
        var kept = CoreCollectionsInWireOrder(await res.Content.ReadAsStringAsync(), core);

        kept.Should().HaveCount(core.Count,
            "every core collection should appear in the GET /api/schema response");

        var actual = Serialize(kept);
        var path = RepoRoot.SchemaSnapshotPath();
        var relative = Path.GetRelativePath(RepoRoot.Find(), path).Replace('\\', '/');

        if (Environment.GetEnvironmentVariable(UpdateEnvVar) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
            return;
        }

        File.Exists(path).Should().BeTrue(
            $"{relative} is missing. Regenerate it with " +
            $"`{UpdateEnvVar}=1 dotnet test --filter CoreSchemaSnapshot` and commit the result.");

        Normalize(actual).Should().Be(Normalize(File.ReadAllText(path)),
            $"the core collection metadata served by GET /api/schema no longer matches {relative}. " +
            $"If the change is intended, regenerate the snapshot with " +
            $"`{UpdateEnvVar}=1 dotnet test --filter CoreSchemaSnapshot` and commit it — " +
            "frontend/tests/schemaContract.test.ts then verifies the admin SPA can still render it.");
    }

    /// <summary>
    /// Core = the FrameworkEntityTypes that actually carry [CmsCollection] (7 of the 10). Derived by
    /// running the real scanner over that list instead of hard-coding names, so a future core
    /// collection cannot be silently left out of this gate. Filtering is required because the test
    /// host has the Blog sample wired in process-wide (Support/ContentAssemblyEnvBootstrap.cs), so
    /// /api/schema also serves article/category/tag — sample collections, deleted on fork.
    /// </summary>
    private static IReadOnlySet<string> CoreCollectionNames() =>
        MetadataScanner.ScanTypes(FrameworkEntityTypes.All)
            .Select(c => c.Name)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Collections are sorted by name so the committed file has a stable diff (scan order can follow
    /// assembly load order). Field arrays are deliberately left in wire order: selectListColumns
    /// slices the first MAX_COLUMNS entries in array order, so reordering fields here would make the
    /// frontend contract test assert against a column set production never produces.
    /// </summary>
    private static JsonNode[] CoreCollectionsInWireOrder(string envelopeJson, IReadOnlySet<string> core) =>
        JsonNode.Parse(envelopeJson)!["data"]!.AsArray()
            .Where(n => core.Contains(n!["name"]!.GetValue<string>()))
            .OrderBy(n => n!["name"]!.GetValue<string>(), StringComparer.Ordinal)
            .Select(n => n!.DeepClone())
            .ToArray();

    // Line endings are normalized explicitly rather than relying on the serializer's newline
    // default, so the committed file is LF on every platform (.gitattributes expects LF for .json).
    private static string Serialize(JsonNode[] collections) =>
        JsonSerializer.Serialize(new JsonArray(collections),
            new JsonSerializerOptions { WriteIndented = true })
                .Replace("\r\n", "\n").TrimEnd('\n') + "\n";

    // Compare on content, not line endings: a Windows checkout can present the committed file
    // either way depending on core.autocrlf.
    private static string Normalize(string s) => s.Replace("\r\n", "\n").TrimEnd('\n');
}
