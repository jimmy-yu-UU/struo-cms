// tests/Struo.Tests/Api/CoreSchemaSnapshotTests.cs
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// Pins the two things the admin SPA mirrors by hand — the core collection metadata it receives, and
/// the interface enums it re-declares (frontend/src/types/schema.ts,
/// frontend/src/lib/fieldTypes/types.ts, frontend/src/lib/relationInputKind.ts). Both mirrors degrade
/// silently when they drift: an unknown FieldInterface falls back to a read-only renderer in
/// registry.ts's getFieldType, a field with no list-column formatter is dropped from the list view
/// altogether by selectListColumns, and an unmapped RelationInterface falls back to 'readonly' in
/// relationInputKind. None of that shows up in dotnet build, vue-tsc, or the unit suites.
///
/// The collection snapshot makes any change to the core content model visible in the diff. The
/// interface-enum snapshot covers what the collection snapshot cannot: a new enum member no core
/// collection happens to use. frontend/tests/schemaContract.test.ts consumes both.
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

        CompareOrWrite(Serialize(kept), RepoRoot.SchemaSnapshotPath(),
            "the core collection metadata served by GET /api/schema");
    }

    /// <summary>
    /// Pins every declared FieldInterface/RelationInterface member, independently of whether any
    /// collection uses it. Without this, the frontend contract test only ever sees interfaces a core
    /// collection happens to use — and new field types are typically introduced for content
    /// collections, not for the seven framework tables, so the common case went ungated.
    /// </summary>
    [Fact]
    public void InterfaceEnums_MatchCommittedSnapshot() =>
        CompareOrWrite(SerializeInterfaceEnums(), RepoRoot.InterfacesSnapshotPath(),
            "the declared FieldInterface/RelationInterface members");

    /// <summary>
    /// Compares <paramref name="actual"/> against the committed file, or rewrites that file when
    /// UPDATE_SCHEMA_SNAPSHOT=1. <paramref name="whatDrifted"/> names the subject in the failure
    /// message, so a red build is actionable without reading this source.
    /// </summary>
    private static void CompareOrWrite(string actual, string path, string whatDrifted)
    {
        var relative = Path.GetRelativePath(RepoRoot.Find(), path).Replace('\\', '/');

        if (Environment.GetEnvironmentVariable(UpdateEnvVar) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
            return;
        }

        File.Exists(path).Should().BeTrue(
            $"{relative} is missing. Regenerate it with " +
            $"`{UpdateEnvVar}=1 dotnet test --filter CoreSchemaSnapshot` and commit the result " +
            "(PowerShell form: see schema/README.md).");

        Normalize(actual).Should().Be(Normalize(File.ReadAllText(path)),
            $"{whatDrifted} no longer matches {relative}. If the change is intended, regenerate with " +
            $"`{UpdateEnvVar}=1 dotnet test --filter CoreSchemaSnapshot` and commit it — " +
            "frontend/tests/schemaContract.test.ts then verifies the admin SPA can still handle it.");
    }

    /// <summary>
    /// Enum member names in the same camelCase form the wire uses. The names go through
    /// JsonNamingPolicy.CamelCase — the very policy Program.cs hands to JsonStringEnumConverter —
    /// rather than a local re-implementation, so this file cannot drift from how the API actually
    /// serializes an enum. Declaration order is preserved: it groups related interfaces and keeps the
    /// committed file readable, and nothing consumes it as an ordered list.
    /// </summary>
    private static string SerializeInterfaceEnums()
    {
        var doc = new JsonObject
        {
            ["fieldInterfaces"] = CamelCaseNames<FieldInterface>(),
            ["relationInterfaces"] = CamelCaseNames<RelationInterface>(),
        };

        return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true })
            .Replace("\r\n", "\n").TrimEnd('\n') + "\n";
    }

    private static JsonArray CamelCaseNames<T>() where T : struct, Enum =>
        new(Enum.GetNames<T>()
            .Select(n => (JsonNode)JsonValue.Create(JsonNamingPolicy.CamelCase.ConvertName(n))!)
            .ToArray());

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
