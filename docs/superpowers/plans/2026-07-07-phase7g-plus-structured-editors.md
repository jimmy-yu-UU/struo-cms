# Phase 7g+ (slice 2) — Structured editors (`Json` + `KeyValue`) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Light up the `Json` and `KeyValue` field interfaces (currently read-only) end-to-end — a `Json` field stores/returns arbitrary JSON, a `KeyValue` field stores/returns a string→string map — validated on write and edited in the SPA.

**Architecture:** `KeyValue` is a `Dictionary<string,string>` mapped to an `IsJson` + `text` column (slice-1 mechanism; Newtonsoft round-trips a BCL dictionary cleanly). `Json` is a `string?` property holding the **raw JSON text** in a plain `text` column (NOT `IsJson`): a persistence probe proved `System.Text.Json.JsonElement?` via `IsJson` reads back disposed on SqlSugarCore/Newtonsoft. On write the `Json` key is stripped from the body before the whole-entity deserialize (existing `StripKeys` pattern) and set from the element's raw text; on read `Project` parses the stored string back to a `JsonElement` so the API emits real structured JSON. Frontend: two registry entries + two `*Field.vue` components on the 7g.6 registry.

**Tech Stack:** .NET 10 / C# · SqlSugarCore 5.1.4.215 (`IsJson` columns; Newtonsoft-backed materialization) · System.Text.Json · xUnit + AwesomeAssertions · Vue 3 + PrimeVue 4.5 (`Textarea`, `InputText`, `Button`) · Vitest + `@vue/test-utils`.

## Global Constraints

- All DB access via SqlSugar ORM; zero vendor SQL (CLAUDE.md §17.4). `InitTables` is dev-only; live DBs get a migration script (InitTables adds tables, not columns).
- Domain references nothing external; persistence attributes live on entities only (§2). No new NuGet/pnpm packages in this slice.
- Outbound JSON is camelCase for DTO property names; `KeyValue` **dictionary keys are user data and are NOT camelCased** — they round-trip verbatim.
- Both interfaces are **non-translatable, non-sortable, non-searchable**; `MaxLength` (7g.5) does **not** apply.
- `Json`'s CLR type is `string?` (raw JSON text); `KeyValue`'s is `Dictionary<string,string>`. Do not use `JsonElement?` for `Json` — the persistence probe (spec §2.1) proved it reads back disposed.
- TDD: failing test first; frequent commits. Backend baseline **344** tests, frontend **205** — both must stay green plus the new tests.
- Frontend `FieldInterface` union already contains `json`/`keyValue` — no union change; `vue-tsc` enforces registry exhaustiveness.

---

### Task 1: `KeyValue` → JSON column convention + structured-column DDL test

**Files:**
- Modify: `src/Struo.Infrastructure/Persistence/SqlSugarClientFactory.cs`
- Test: `tests/Struo.Tests/Persistence/StructuredColumnMappingTests.cs`

**Interfaces:**
- Consumes: `CmsFieldAttribute.Interface`, `FieldInterface.{KeyValue,Json}`.
- Produces: a `[CmsField(Interface=KeyValue)]` `Dictionary<string,string>` property maps to an `IsJson` + `text` column; a `[CmsField(Interface=Json)]` `string?` property maps to a plain nullable `text` column (via the pre-existing `ContentBearingInterfaces` branch — no new code for `Json`). The factory's `MultiValueInterfaces` set is renamed `JsonColumnInterfaces` and gains `KeyValue`.

- [ ] **Step 1: Write the failing round-trip test**

Create `tests/Struo.Tests/Persistence/StructuredColumnMappingTests.cs`:

```csharp
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

/// <summary>
/// The SqlSugarClientFactory convention maps a KeyValue [CmsField] (Dictionary&lt;string,string&gt;)
/// to an `IsJson` + `text` column, and a Json [CmsField] (string?) to a plain nullable `text` column
/// (via the content-bearing convention). Both must be `text`, not the default varchar — `IsJson` alone
/// is `varchar(1)` on Postgres (slice-1 finding) and an undeclared string is `varchar(255)`. Verified
/// cross-db via an actual insert+read (SQLite reports the declared type, pinning the convention here).
/// </summary>
public class StructuredColumnMappingTests
{
    [SugarTable("structured_col_test_entity")]
    private sealed class StructColTestEntity
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public long Id { get; set; }

        [CmsField(Label = "Meta", Interface = FieldInterface.KeyValue)]
        public Dictionary<string, string> Meta { get; set; } = new();

        [CmsField(Label = "Attributes", Interface = FieldInterface.Json)]
        public string? Attributes { get; set; }
    }

    [Fact]
    public void Structured_columns_are_text_and_round_trip()
    {
        var db = new SqliteTestDatabase();
        var client = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = db.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        using (db)
        {
            client.CodeFirst.InitTables<StructColTestEntity>();

            var columns = client.DbMaintenance.GetColumnInfosByTableName("structured_col_test_entity", false);
            foreach (var col in new[] { "Meta", "Attributes" })
            {
                var info = columns.Single(c => c.DbColumnName.Equals(col, StringComparison.OrdinalIgnoreCase));
                info.DataType.Should().ContainEquivalentOf("text");
            }

            var row = new StructColTestEntity
            {
                Meta = new Dictionary<string, string> { ["seo-title"] = "值", ["author"] = "me" },
                Attributes = """{"a":1,"nested":{"x":"人工智慧"},"arr":[1,2]}""",
            };
            client.Insertable(row).ExecuteCommand();

            var read = client.Queryable<StructColTestEntity>().First();
            read.Meta.Should().ContainKey("seo-title");
            read.Meta["seo-title"].Should().Be("值");
            read.Meta["author"].Should().Be("me");
            read.Attributes.Should().Be("""{"a":1,"nested":{"x":"人工智慧"},"arr":[1,2]}""");
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~StructuredColumnMappingTests"`
Expected: FAIL — `KeyValue` is not yet in the JSON-column set, so `Meta` (a `Dictionary`) is not mapped to a JSON column and InitTables/insert throws or the read dictionary is empty/wrong. (`Attributes` as a `string?` Json field already maps to `text` via `ContentBearingInterfaces`, but the test fails overall on `Meta`.)

- [ ] **Step 3: Rename the set and add `KeyValue`**

In `src/Struo.Infrastructure/Persistence/SqlSugarClientFactory.cs`, replace the `MultiValueInterfaces` declaration (lines 19-24):

```csharp
    // [CmsField] interfaces whose value is a structured aggregate stored as JSON — map them to a JSON
    // column so SqlSugar (de)serializes the List<>/Dictionary<> automatically (jsonb-in-text). This is
    // the multi-value selects (slice 1) plus KeyValue (slice 2). Json is NOT here — it is a string
    // holding raw JSON text and is widened to `text` by the content-bearing convention below.
    private static readonly HashSet<FieldInterface> JsonColumnInterfaces =
    [
        FieldInterface.MultiSelect, FieldInterface.CheckboxGroup, FieldInterface.Tags,
        FieldInterface.KeyValue
    ];
```

Then update the single use site (the `mvField` check, ~line 59) to reference the renamed set:

```csharp
                    var mvField = property.GetCustomAttribute<CmsFieldAttribute>();
                    if (mvField is not null && JsonColumnInterfaces.Contains(mvField.Interface))
                    {
                        column.IsJson = true;
                        column.DataType = "text";
                        return;
                    }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~StructuredColumnMappingTests"`
Expected: PASS.

- [ ] **Step 5: Run the slice-1 mapping test to confirm no regression**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~MultiValueColumnMappingTests"`
Expected: PASS (the rename did not change multi-value behaviour).

- [ ] **Step 6: Commit**

```bash
git add src/Struo.Infrastructure/Persistence/SqlSugarClientFactory.cs tests/Struo.Tests/Persistence/StructuredColumnMappingTests.cs
git commit -m "feat: map KeyValue to a JSON text column (rename set to JsonColumnInterfaces)"
```

---

### Task 2: `Json` write path + projection in `ItemService`

**Files:**
- Modify: `src/Struo.Application/Query/ItemService.cs` (`Deserialize` — strip + set raw text; `Project` — parse to JsonElement)
- Test: `tests/Struo.Tests/Query/ItemServiceJsonFieldTests.cs`

**Interfaces:**
- Consumes: `FieldMetadata.{Interface,Name,Translatable,Required}`, `FieldInterface.Json`, `EntityDescriptor.FieldToProperty`, the original `body` `JsonElement`.
- Produces: on write, a `Json` field's `string?` property is set to the body element's raw JSON text (present + non-null) or left `null`; on read, `Project` emits a parsed `JsonElement` for a `Json` field (structured JSON, not a quoted string), or `null`. `Required` reuses the existing generic loop.

- [ ] **Step 1: Write the failing tests**

Create `tests/Struo.Tests/Query/ItemServiceJsonFieldTests.cs`:

```csharp
using System.Text.Json;
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Query;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Query;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Security;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

public class ItemServiceJsonFieldTests : IDisposable
{
    [SugarTable("json_thing")]
    [CmsCollection("JsonThing")]
    public sealed class JsonThing : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [CmsField(Label = "Attributes", Interface = FieldInterface.Json)]
        public string? Attributes { get; set; }

        [CmsField(Label = "Required Data", Interface = FieldInterface.Json, Required = true)]
        public string? RequiredData { get; set; }
    }

    private readonly SqliteTestDatabase _file = new();
    private readonly ItemService _svc;

    public ItemServiceJsonFieldTests()
    {
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        db.CodeFirst.InitTables<JsonThing>();
        db.CodeFirst.InitTables<Language>();
        LanguageSeeder.SeedAsync(db).GetAwaiter().GetResult();

        var types = new[] { typeof(JsonThing) };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["jsonthing"] = typeof(JsonThing),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var repo = new SqlSugarItemRepository(db, registry, graph, provider, new StruoQueryOptions());
        var expander = new RelationExpander(repo, graph);
        var resolver = new RelationFilterResolver(repo, graph, provider, registry, new StruoQueryOptions());
        var languages = new LanguageProvider(db);
        _svc = new ItemService(repo, provider, registry, new AllowAllPermissionService(),
            graph, expander, graph, resolver, languages, new StruoQueryOptions(), new GanssHtmlSanitizer());
    }

    public void Dispose() => _file.Dispose();

    private static JsonElement Body(object obj)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(obj));
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task Object_array_and_scalar_json_round_trip_as_structured_json()
    {
        var body = Body(new
        {
            attributes = new { a = 1, nested = new { x = "人工智慧" }, arr = new[] { 1, 2 } },
            requiredData = new[] { "x", "y" },
        });
        var created = await _svc.CreateAsync("jsonthing", body);
        var read = await _svc.GetAsync("jsonthing", created["id"]!.ToString()!);

        read.Should().NotBeNull();
        var attrs = (JsonElement)read!["attributes"]!;
        attrs.ValueKind.Should().Be(JsonValueKind.Object);
        attrs.GetProperty("nested").GetProperty("x").GetString().Should().Be("人工智慧");
        attrs.GetProperty("arr").GetArrayLength().Should().Be(2);

        var req = (JsonElement)read["requiredData"]!;
        req.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Absent_optional_json_projects_as_null()
    {
        var body = Body(new { requiredData = new { ok = true } }); // attributes omitted
        var created = await _svc.CreateAsync("jsonthing", body);
        var read = await _svc.GetAsync("jsonthing", created["id"]!.ToString()!);
        read!["attributes"].Should().BeNull();
    }

    [Fact]
    public async Task Required_json_absent_is_rejected()
    {
        var body = Body(new { attributes = new { a = 1 } }); // requiredData (required) omitted
        var act = () => _svc.CreateAsync("jsonthing", body);
        await act.Should().ThrowAsync<QueryException>()
            .WithMessage("Field 'requiredData' is required.");
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~ItemServiceJsonFieldTests"`
Expected: FAIL — without the strip step, `body.Deserialize(EntityType)` throws trying to bind the `attributes` JSON object into the `string? Attributes` property (a JsonException surfaces), so create fails.

- [ ] **Step 3: Strip `Json` keys before deserialize**

In `src/Struo.Application/Query/ItemService.cs`, in `Deserialize`, immediately after the `translations` strip line (`if (meta.Translation is not null) stripNames.Add("translations");`, ~line 660), add:

```csharp
        // Json fields carry an arbitrary JSON object/array/scalar; binding that into their string
        // property would make System.Text.Json throw. Strip them here and set the raw text below.
        foreach (var jsonField in meta.Fields.Where(f => f.Interface == FieldInterface.Json))
            stripNames.Add(jsonField.Name);
```

- [ ] **Step 4: Set the raw JSON text after deserialize**

In the same method, immediately after the deserialize `if/else` block (right after the closing brace of the `else { ... }` at ~line 674, before the system/read-only strip loop), add:

```csharp
        // Set each Json field's string property from the original body's raw text (stripped above).
        // Present + non-null => store the raw JSON text; absent or explicit JSON null => leave null.
        foreach (var field in meta.Fields.Where(f => f.Interface == FieldInterface.Json && !f.Translatable))
        {
            if (!d.FieldToProperty.TryGetValue(field.Name, out var prop)) continue;
            var pi = d.EntityType.GetProperty(prop);
            if (pi is not { CanWrite: true } || pi.PropertyType != typeof(string)) continue;
            if (body.ValueKind == JsonValueKind.Object
                && body.TryGetProperty(field.Name, out var el)
                && el.ValueKind != JsonValueKind.Null)
            {
                pi.SetValue(entity, el.GetRawText());
            }
        }
```

(The existing generic required loop already rejects a `null` required `Json` string — no extra code needed.)

- [ ] **Step 5: Parse `Json` fields to structured JSON in `Project`**

In `Project`, replace the field-projection line (`dict[field.Name] = d.EntityType.GetProperty(prop)?.GetValue(entity);`, ~line 787) with:

```csharp
            var value = d.EntityType.GetProperty(prop)?.GetValue(entity);
            // Json fields store raw JSON text; parse to a fresh (non-disposed) JsonElement so the API
            // emits structured JSON, not a quoted string. Null stays null.
            if (field.Interface == FieldInterface.Json && value is string rawJson)
                value = JsonSerializer.Deserialize<JsonElement>(rawJson);
            dict[field.Name] = value;
```

- [ ] **Step 6: Run to verify they pass**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~ItemServiceJsonFieldTests"`
Expected: PASS (3/3).

- [ ] **Step 7: Commit**

```bash
git add src/Struo.Application/Query/ItemService.cs tests/Struo.Tests/Query/ItemServiceJsonFieldTests.cs
git commit -m "feat: Json fields store raw JSON text and project as structured JSON"
```

---

### Task 3: `KeyValue` validation branch in `ItemService`

**Files:**
- Modify: `src/Struo.Application/Query/ItemService.cs` (`Deserialize` — new KeyValue branch before `return entity;`)
- Test: `tests/Struo.Tests/Query/ItemServiceKeyValueTests.cs`

**Interfaces:**
- Consumes: `FieldMetadata.{Interface,Name,Translatable,Required}`, `FieldInterface.KeyValue`, `EntityDescriptor.FieldToProperty`.
- Produces: on both write paths, a `KeyValue` field with a blank/whitespace key → `QueryException` (`Field '{name}' has an entry with an empty key.`); a `Required` empty map → `QueryException` (`Field '{name}' is required.`); a valid map round-trips verbatim (keys un-camelCased).

- [ ] **Step 1: Write the failing tests**

Create `tests/Struo.Tests/Query/ItemServiceKeyValueTests.cs`:

```csharp
using System.Text.Json;
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Query;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Query;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Security;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

public class ItemServiceKeyValueTests : IDisposable
{
    [SugarTable("kv_thing")]
    [CmsCollection("KvThing")]
    public sealed class KvThing : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [CmsField(Label = "Meta", Interface = FieldInterface.KeyValue)]
        public Dictionary<string, string> Meta { get; set; } = new();

        [CmsField(Label = "Required Meta", Interface = FieldInterface.KeyValue, Required = true)]
        public Dictionary<string, string> RequiredMeta { get; set; } = new();
    }

    private readonly SqliteTestDatabase _file = new();
    private readonly ItemService _svc;

    public ItemServiceKeyValueTests()
    {
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        db.CodeFirst.InitTables<KvThing>();
        db.CodeFirst.InitTables<Language>();
        LanguageSeeder.SeedAsync(db).GetAwaiter().GetResult();

        var types = new[] { typeof(KvThing) };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["kvthing"] = typeof(KvThing),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var repo = new SqlSugarItemRepository(db, registry, graph, provider, new StruoQueryOptions());
        var expander = new RelationExpander(repo, graph);
        var resolver = new RelationFilterResolver(repo, graph, provider, registry, new StruoQueryOptions());
        var languages = new LanguageProvider(db);
        _svc = new ItemService(repo, provider, registry, new AllowAllPermissionService(),
            graph, expander, graph, resolver, languages, new StruoQueryOptions(), new GanssHtmlSanitizer());
    }

    public void Dispose() => _file.Dispose();

    private static JsonElement Body(object obj)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(obj));
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task Key_value_map_round_trips_verbatim()
    {
        var body = Body(new
        {
            meta = new Dictionary<string, string> { ["seo-title"] = "值", ["author"] = "me" },
            requiredMeta = new Dictionary<string, string> { ["k"] = "v" },
        });
        var created = await _svc.CreateAsync("kvthing", body);
        var read = await _svc.GetAsync("kvthing", created["id"]!.ToString()!);

        var meta = (IDictionary<string, string>)read!["meta"]!;
        meta.Should().ContainKey("seo-title");   // un-camelCased
        meta["seo-title"].Should().Be("值");
        meta["author"].Should().Be("me");
    }

    [Fact]
    public async Task Blank_key_is_rejected()
    {
        var body = Body(new
        {
            requiredMeta = new Dictionary<string, string> { ["k"] = "v" },
            meta = new Dictionary<string, string> { ["  "] = "x" },
        });
        var act = () => _svc.CreateAsync("kvthing", body);
        await act.Should().ThrowAsync<QueryException>()
            .WithMessage("Field 'meta' has an entry with an empty key.");
    }

    [Fact]
    public async Task Required_empty_map_is_rejected()
    {
        var body = Body(new { meta = new Dictionary<string, string> { ["k"] = "v" } }); // requiredMeta omitted
        var act = () => _svc.CreateAsync("kvthing", body);
        await act.Should().ThrowAsync<QueryException>()
            .WithMessage("Field 'requiredMeta' is required.");
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~ItemServiceKeyValueTests"`
Expected: FAIL — no KeyValue branch yet: the blank-key and required-empty cases do not throw. (`Key_value_map_round_trips_verbatim` may already pass — that is fine; the two rejection tests must fail.)

- [ ] **Step 3: Implement the KeyValue validation branch**

In `src/Struo.Application/Query/ItemService.cs`, in `Deserialize`, immediately after the multi-value `foreach` block (the one closing just before `return entity;`, ~line 759) and before `return entity;`, insert:

```csharp
        // KeyValue fields (Dictionary<string,string>) live on the parent entity. Reject blank keys and
        // enforce Required as a non-empty map. Values may be empty; duplicate keys are impossible
        // (System.Text.Json last-wins bind). Non-translatable only.
        foreach (var field in meta.Fields.Where(f => f.Interface == FieldInterface.KeyValue && !f.Translatable))
        {
            if (!d.FieldToProperty.TryGetValue(field.Name, out var prop)) continue;
            var pi = d.EntityType.GetProperty(prop);
            if (pi is not { CanWrite: true }) continue;

            var map = pi.GetValue(entity) as IDictionary<string, string>;
            if (map is not null)
            {
                foreach (var key in map.Keys)
                    if (string.IsNullOrWhiteSpace(key))
                        throw new QueryException($"Field '{field.Name}' has an entry with an empty key.");
            }
            if (field.Required && (map is null || map.Count == 0))
                throw new QueryException($"Field '{field.Name}' is required.");
        }
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~ItemServiceKeyValueTests"`
Expected: PASS (3/3).

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Application/Query/ItemService.cs tests/Struo.Tests/Query/ItemServiceKeyValueTests.cs
git commit -m "feat: validate KeyValue fields (blank-key + required-non-empty) on write"
```

---

### Task 4: Sample fields on `Article` + scanner assertions

**Files:**
- Modify: `samples/Struo.Sample.Blog/Article.cs`
- Test: `tests/Struo.Tests/Metadata/MetadataScannerTests.cs` (add structured-field assertions)

**Interfaces:**
- Consumes: the JSON-column convention (Task 1) and the write/validation paths (Tasks 2, 3).
- Produces: `Article` exposes `attributes` (`Json`, `string?`) and `meta` (`KeyValue`, `Dictionary<string,string>`); both non-required, non-sortable, non-searchable; the `Json` field resolves to `MaxLength == null`.

- [ ] **Step 1: Write the failing scanner test**

Add to `tests/Struo.Tests/Metadata/MetadataScannerTests.cs` (inside its existing test class; match neighbouring tests' scan pattern and usings — add `using Struo.Domain.Metadata.Enums;` if absent):

```csharp
    [Fact]
    public void Article_exposes_structured_fields_with_correct_metadata()
    {
        var collections = MetadataScanner.ScanTypes([typeof(Struo.Sample.Blog.Article)]);
        var article = collections.Single(c => string.Equals(c.Name, "article", StringComparison.OrdinalIgnoreCase));

        var attributes = article.Fields.Single(f => f.Name == "attributes");
        attributes.Interface.Should().Be(FieldInterface.Json);
        attributes.Sortable.Should().BeFalse();
        attributes.Searchable.Should().BeFalse();
        attributes.MaxLength.Should().BeNull(); // Json is content-bearing => unlimited

        var meta = article.Fields.Single(f => f.Name == "meta");
        meta.Interface.Should().Be(FieldInterface.KeyValue);
        meta.Sortable.Should().BeFalse();
        meta.Searchable.Should().BeFalse();
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~MetadataScannerTests.Article_exposes_structured_fields"`
Expected: FAIL — `Article` has no `attributes`/`meta` fields yet.

- [ ] **Step 3: Add the sample fields**

In `samples/Struo.Sample.Blog/Article.cs`, add two properties inside the class after `Keywords` (line 43) and before the `// --- relations ---` block (line 45):

```csharp
    [CmsField(Label = "Attributes", Interface = FieldInterface.Json, Sort = 9, Group = "Content")]
    public string? Attributes { get; set; }

    [CmsField(Label = "Meta", Interface = FieldInterface.KeyValue, Sort = 10, Group = "Content")]
    public Dictionary<string, string> Meta { get; set; } = new();
```

- [ ] **Step 4: Run the full backend suite to verify no regressions**

Run: `dotnet test tests/Struo.Tests`
Expected: PASS — the new scanner test passes and all existing tests stay green (existing `Article` creates omit these fields → `attributes` stays null, `meta` stays empty; neither is required, so no throw; extra projected keys don't break key-presence assertions).

- [ ] **Step 5: Commit**

```bash
git add samples/Struo.Sample.Blog/Article.cs tests/Struo.Tests/Metadata/MetadataScannerTests.cs
git commit -m "feat: add sample Json + KeyValue fields to Article"
```

---

### Task 5: Frontend `JsonField.vue` component

**Files:**
- Create: `frontend/src/components/fields/JsonField.vue`
- Test: `frontend/src/components/fields/JsonField.test.ts`

**Interfaces:**
- Consumes: PrimeVue `Textarea`.
- Produces: a component with the uniform contract (`{ field, modelValue, disabled }` props; `update:modelValue` emit). Emits the parsed JS value on valid JSON, emits `null` on a blank buffer, and **suppresses** the emit (keeping the last valid model) on malformed input while showing an invalid state.

- [ ] **Step 1: Write the failing component tests**

Create `frontend/src/components/fields/JsonField.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import JsonField from './JsonField.vue'
import type { FieldMeta } from '../../types/schema'

function field(over: Partial<FieldMeta> & { interface: string }): FieldMeta {
  return { name: 'f', label: 'F', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over } as FieldMeta
}
const opts = { global: { plugins: [PrimeVue] } }

describe('JsonField', () => {
  it('initialises the textarea from the model value (pretty-printed)', () => {
    const w = mount(JsonField, { props: { field: field({ interface: 'json' }), modelValue: { a: 1 } }, ...opts })
    const ta = w.find('textarea')
    expect((ta.element as HTMLTextAreaElement).value).toContain('"a": 1')
  })

  it('emits the parsed value on valid JSON input', async () => {
    const w = mount(JsonField, { props: { field: field({ interface: 'json' }), modelValue: null }, ...opts })
    await w.find('textarea').setValue('{"x": [1, 2]}')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([{ x: [1, 2] }])
  })

  it('emits null on a blank buffer', async () => {
    const w = mount(JsonField, { props: { field: field({ interface: 'json' }), modelValue: { a: 1 } }, ...opts })
    await w.find('textarea').setValue('   ')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([null])
  })

  it('does not emit and shows an error on malformed JSON', async () => {
    const w = mount(JsonField, { props: { field: field({ interface: 'json' }), modelValue: null }, ...opts })
    await w.find('textarea').setValue('{ not json')
    expect(w.emitted('update:modelValue')).toBeUndefined()
    expect(w.find('.json-error').exists()).toBe(true)
  })
})
```

- [ ] **Step 2: Run to verify they fail**

Run: `cd frontend && pnpm test -- JsonField`
Expected: FAIL — the component does not exist.

- [ ] **Step 3: Create `JsonField.vue`**

Create `frontend/src/components/fields/JsonField.vue`:

```vue
<script setup lang="ts">
import { ref, watch } from 'vue'
import Textarea from 'primevue/textarea'
import type { FieldMeta } from '../../types/schema'

const props = defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()

function pretty(v: unknown): string {
  return v === null || v === undefined ? '' : JSON.stringify(v, null, 2)
}

const text = ref(pretty(props.modelValue))
const error = ref<string | null>(null)

// Re-sync the buffer when the model changes from outside (e.g. locale switch / form reset),
// but not from our own emits (guarded by comparing parsed equality is overkill — only reset
// when the incoming value differs from what our buffer currently parses to).
watch(() => props.modelValue, (v) => {
  try {
    const current = text.value.trim() === '' ? null : JSON.parse(text.value)
    if (JSON.stringify(current) !== JSON.stringify(v)) {
      text.value = pretty(v)
      error.value = null
    }
  } catch {
    text.value = pretty(v)
    error.value = null
  }
})

function onInput(v: string) {
  text.value = v
  if (v.trim() === '') {
    error.value = null
    emit('update:modelValue', null)
    return
  }
  try {
    const parsed = JSON.parse(v)
    error.value = null
    emit('update:modelValue', parsed)
  } catch (e) {
    error.value = (e as Error).message
    // suppress emit — keep the last valid model value
  }
}
</script>
<template>
  <div class="json-field">
    <Textarea :model-value="text" :disabled="disabled" rows="6" class="json-textarea"
      :invalid="error !== null" spellcheck="false"
      @update:model-value="(v: string) => onInput(v ?? '')" />
    <small v-if="error" class="json-error p-error">{{ error }}</small>
  </div>
</template>
```

- [ ] **Step 4: Run to verify they pass**

Run: `cd frontend && pnpm test -- JsonField`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/fields/JsonField.vue frontend/src/components/fields/JsonField.test.ts
git commit -m "feat(frontend): JsonField editor (validated JSON textarea)"
```

---

### Task 6: Frontend `KeyValueField.vue` component

**Files:**
- Create: `frontend/src/components/fields/KeyValueField.vue`
- Test: `frontend/src/components/fields/KeyValueField.test.ts`

**Interfaces:**
- Consumes: PrimeVue `InputText`, `Button`.
- Produces: a component with the uniform contract whose model value is a `Record<string, string>` object; row-based editing (key + value inputs, add/remove), emitting a blank-key-dropped object with immutable updates.

- [ ] **Step 1: Write the failing component tests**

Create `frontend/src/components/fields/KeyValueField.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import KeyValueField from './KeyValueField.vue'
import type { FieldMeta } from '../../types/schema'

function field(over: Partial<FieldMeta> & { interface: string }): FieldMeta {
  return { name: 'f', label: 'F', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over } as FieldMeta
}
const opts = { global: { plugins: [PrimeVue] } }

describe('KeyValueField', () => {
  it('renders one row per entry', () => {
    const w = mount(KeyValueField, {
      props: { field: field({ interface: 'keyValue' }), modelValue: { a: '1', b: '2' } }, ...opts,
    })
    expect(w.findAll('.kv-row')).toHaveLength(2)
  })

  it('adds a blank row without emitting a blank key', async () => {
    const w = mount(KeyValueField, { props: { field: field({ interface: 'keyValue' }), modelValue: {} }, ...opts })
    await w.get('.kv-add').trigger('click')
    // A blank new row does not contribute a key yet -> object stays empty.
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([{}])
    expect(w.findAll('.kv-row')).toHaveLength(1)
  })

  it('emits an object once a key is typed', async () => {
    const w = mount(KeyValueField, { props: { field: field({ interface: 'keyValue' }), modelValue: {} }, ...opts })
    await w.get('.kv-add').trigger('click')
    const inputs = w.findAll('.kv-row input')
    await inputs[0].setValue('seo-title')
    await inputs[1].setValue('值')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([{ 'seo-title': '值' }])
  })

  it('removes a row immutably', async () => {
    const w = mount(KeyValueField, {
      props: { field: field({ interface: 'keyValue' }), modelValue: { a: '1', b: '2' } }, ...opts,
    })
    await w.findAll('.kv-remove')[0].trigger('click')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([{ b: '2' }])
  })
})
```

- [ ] **Step 2: Run to verify they fail**

Run: `cd frontend && pnpm test -- KeyValueField`
Expected: FAIL — the component does not exist.

- [ ] **Step 3: Create `KeyValueField.vue`**

Create `frontend/src/components/fields/KeyValueField.vue`:

```vue
<script setup lang="ts">
import { ref, watch } from 'vue'
import InputText from 'primevue/inputtext'
import Button from 'primevue/button'
import type { FieldMeta } from '../../types/schema'

type Row = { key: string; value: string }

const props = defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: Record<string, string>): void }>()

function toRows(v: unknown): Row[] {
  if (v && typeof v === 'object' && !Array.isArray(v)) {
    return Object.entries(v as Record<string, unknown>).map(([key, value]) => ({ key, value: String(value ?? '') }))
  }
  return []
}

const rows = ref<Row[]>(toRows(props.modelValue))

// Re-derive rows only when the incoming model differs from what our rows serialize to,
// so typing (which emits) doesn't clobber the working array (incl. blank rows).
watch(() => props.modelValue, (v) => {
  if (JSON.stringify(serialize(rows.value)) !== JSON.stringify(v ?? {})) {
    rows.value = toRows(v)
  }
})

function serialize(list: Row[]): Record<string, string> {
  const out: Record<string, string> = {}
  for (const r of list) {
    const k = r.key.trim()
    if (k === '') continue // drop blank keys; last-wins on duplicates
    out[k] = r.value
  }
  return out
}

function commit(next: Row[]) {
  rows.value = next
  emit('update:modelValue', serialize(next))
}
function add() { commit([...rows.value, { key: '', value: '' }]) }
function remove(i: number) { commit(rows.value.filter((_, idx) => idx !== i)) }
function setKey(i: number, key: string) {
  commit(rows.value.map((r, idx) => (idx === i ? { ...r, key } : r)))
}
function setValue(i: number, value: string) {
  commit(rows.value.map((r, idx) => (idx === i ? { ...r, value } : r)))
}
</script>
<template>
  <div class="key-value-field">
    <div v-for="(r, i) in rows" :key="i" class="kv-row">
      <InputText :model-value="r.key" :disabled="disabled" placeholder="key"
        @update:model-value="(v: string) => setKey(i, v ?? '')" />
      <InputText :model-value="r.value" :disabled="disabled" placeholder="value"
        @update:model-value="(v: string) => setValue(i, v ?? '')" />
      <Button class="kv-remove" icon="pi pi-times" text :disabled="disabled" @click="remove(i)" />
    </div>
    <Button class="kv-add" icon="pi pi-plus" label="Add" text :disabled="disabled" @click="add" />
  </div>
</template>
```

- [ ] **Step 4: Run to verify they pass**

Run: `cd frontend && pnpm test -- KeyValueField`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/fields/KeyValueField.vue frontend/src/components/fields/KeyValueField.test.ts
git commit -m "feat(frontend): KeyValueField editor (row-based key/value map)"
```

---

### Task 7: Wire `json`/`keyValue` into the field-type registry

**Files:**
- Modify: `frontend/src/lib/fieldTypes/registry.ts`
- Modify: `frontend/src/lib/fieldTypes/registry.test.ts`

**Interfaces:**
- Consumes: `JsonField`, `KeyValueField`, `FieldMeta`.
- Produces: `registry.json`/`registry.keyValue` become real defs (`json`: `defaultValue null`, pass-through parse/serialize, minified-JSON list column; `keyValue`: `defaultValue {}`, object parse, blank-key-dropping serialize, joined list column). Both become list-column eligible.

- [ ] **Step 1: Extend the registry tests**

In `frontend/src/lib/fieldTypes/registry.test.ts`:

(a) Move the read-only baseline off `json` (line 25) — it is no longer read-only:

```ts
  it('falls back to the read-only def for unknown interfaces', () => {
    expect(getFieldType('somethingNew').component).toBe(getFieldType('repeater').component)
    expect(getFieldType('somethingNew').listColumn).toBeNull()
  })
```

(b) Add `json` and `keyValue` to `LIST_ELIGIBLE`:

```ts
const LIST_ELIGIBLE = new Set([
  'text', 'textarea', 'slug', 'email', 'url', 'phone', 'color',
  'number', 'slider', 'rating', 'boolean', 'checkbox',
  'date', 'time', 'dateTime', 'select', 'radio',
  'multiSelect', 'checkboxGroup', 'tags',
  'json', 'keyValue',
])
```

(c) Append these tests inside the `describe('field-type registry', ...)` block:

```ts
  it('json default is null and parse coerces nullish to null', () => {
    const f = field({ interface: 'json' })
    expect(getFieldType('json').defaultValue(f)).toBeNull()
    expect(getFieldType('json').parse(undefined, f)).toBeNull()
    expect(getFieldType('json').parse({ a: 1 }, f)).toEqual({ a: 1 })
    expect(getFieldType('json').serialize({ a: 1 }, f)).toEqual({ a: 1 })
  })

  it('keyValue default is an empty object and parse coerces non-objects', () => {
    const f = field({ interface: 'keyValue' })
    expect(getFieldType('keyValue').defaultValue(f)).toEqual({})
    expect(getFieldType('keyValue').parse(undefined, f)).toEqual({})
    expect(getFieldType('keyValue').parse(['x'], f)).toEqual({})
    expect(getFieldType('keyValue').parse({ a: '1' }, f)).toEqual({ a: '1' })
  })

  it('keyValue serialize drops blank keys and keeps last-wins', () => {
    const f = field({ interface: 'keyValue' })
    expect(getFieldType('keyValue').serialize({ a: '1', '': 'x', ' ': 'y', b: '2' }, f)).toEqual({ a: '1', b: '2' })
  })

  it('list formatters: json minifies, keyValue joins k: v', () => {
    const j = field({ interface: 'json' })
    expect(getFieldType('json').listColumn!.format({ a: 1, b: [2] }, j)).toBe('{"a":1,"b":[2]}')
    const kv = field({ interface: 'keyValue' })
    expect(getFieldType('keyValue').listColumn!.format({ a: '1', b: '2' }, kv)).toBe('a: 1, b: 2')
  })
```

- [ ] **Step 2: Run to verify they fail**

Run: `cd frontend && pnpm test -- fieldTypes/registry`
Expected: FAIL — `json`/`keyValue` still resolve to `readonlyDef` (list column null, `json` default `''`), and the read-only baseline moved to `repeater`.

- [ ] **Step 3: Wire the defs into the registry**

In `frontend/src/lib/fieldTypes/registry.ts`, add imports (near the other field imports, lines 5-17):

```ts
import JsonField from '../../components/fields/JsonField.vue'
import KeyValueField from '../../components/fields/KeyValueField.vue'
```

Add the list formatters + def builders near `asJoinedTags` (after line 37):

```ts
const asMinifiedJson: ListColumn = {
  format: (v) => (v === null || v === undefined ? '' : JSON.stringify(v)),
}
const asJoinedKeyValue: ListColumn = {
  format: (v) => (v && typeof v === 'object' && !Array.isArray(v)
    ? Object.entries(v as Record<string, unknown>).map(([k, val]) => `${k}: ${String(val ?? '')}`).join(', ')
    : String(v ?? '')),
}

const jsonDef: FieldTypeDef = {
  component: JsonField,
  defaultValue: () => null,
  parse: (raw) => raw ?? null,
  serialize: (v) => v ?? null,
  listColumn: asMinifiedJson,
}

const keyValueDef: FieldTypeDef = {
  component: KeyValueField,
  defaultValue: () => ({}),
  parse: (raw) => (raw && typeof raw === 'object' && !Array.isArray(raw) ? raw : {}),
  serialize: (v) => {
    if (!v || typeof v !== 'object' || Array.isArray(v)) return {}
    const out: Record<string, string> = {}
    for (const [k, val] of Object.entries(v as Record<string, unknown>)) {
      const key = k.trim()
      if (key === '') continue
      out[key] = String(val ?? '')
    }
    return out
  },
  listColumn: asJoinedKeyValue,
}
```

Then replace the two deferred entries in the `registry` object (lines 129-130):

```ts
  json: jsonDef,
  keyValue: keyValueDef,
```

(Leave `repeater`, `files`, `hidden`, `uuid` as `readonlyDef`. Note: `getFieldType`'s fallback on line 138 still returns `readonlyDef` for unknown interfaces — unchanged.)

- [ ] **Step 4: Run the registry tests + typecheck**

Run: `cd frontend && pnpm test -- fieldTypes/registry && pnpm vue-tsc --noEmit`
Expected: PASS and typecheck clean (registry still exhaustive over `FieldInterface`).

- [ ] **Step 5: Run the full frontend suite + build**

Run: `cd frontend && pnpm test && pnpm build`
Expected: all tests green (205 + new), build succeeds (pre-existing chunk-size advisory only).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/lib/fieldTypes/registry.ts frontend/src/lib/fieldTypes/registry.test.ts
git commit -m "feat(frontend): wire Json + KeyValue into the field-type registry"
```

---

### Task 8: Full gates + live gate (real Postgres) + migration + docs

**Files:**
- Create: `db/migrations/002-article-structured-columns.sql`
- Modify: `docs/ROADMAP.md`
- Modify: `C:\Users\YuJimmy\.claude\projects\D--dotnet-struo-cms\memory\MEMORY.md` + a new memory file (post-verification)

**Interfaces:**
- Consumes: everything above.
- Produces: a migration for the two new `article` columns; recorded verification baseline + live-gate result.

- [ ] **Step 1: Run the complete automated gates**

Run: `dotnet build -warnaserror && dotnet test tests/Struo.Tests`
Expected: build clean; **344 + new** tests pass / 0 fail.

Run: `cd frontend && pnpm test && pnpm vue-tsc --noEmit && pnpm build`
Expected: **205 + new** tests pass; typecheck clean; build succeeds.

- [ ] **Step 2: Write the migration script**

Create `db/migrations/002-article-structured-columns.sql`:

```sql
-- Phase 7g+ slice 2 — structured-editor columns on the sample `Article` collection.
--
-- CONTEXT: SqlSugar `InitTables` creates missing TABLES but never adds columns to an existing
-- table. Live databases provisioned before this merge already have an `articles` table, so the two
-- new fields (attributes/meta) must be added here. A freshly provisioned database gets them from
-- CodeFirst and does not need this script.
--
-- COLUMN TYPES — both `text`:
--   * `attributes` (Json field): a plain `text` column holding RAW JSON text. It is NOT an `IsJson`
--     column — the Json field is a `string?` property (SqlSugar/Newtonsoft materializes a
--     System.Text.Json.JsonElement back disposed, so we store the raw text and parse on read). It is
--     NULLABLE (absent / JSON null => SQL NULL).
--   * `meta` (KeyValue field): an `IsJson` `text` column holding a serialized Dictionary. `text`, NOT
--     the `IsJson` default `varchar(1)` (which truncates on Postgres — the slice-1 finding), and NOT
--     NULL with a `'{}'` default (the CLR property is `= new()`, so an empty map serializes to "{}").
--
-- Identifiers are LOWERCASE and unquoted (SqlSugar emits unquoted identifiers; Postgres folds them to
-- lowercase — existing columns are `status`, `publishedat`, `regions`, ...). Idempotent.

ALTER TABLE articles ADD COLUMN IF NOT EXISTS attributes text NULL;
ALTER TABLE articles ADD COLUMN IF NOT EXISTS meta       text NOT NULL DEFAULT '{}';
```

- [ ] **Step 3: Start the dev API against live Postgres + Redis**

Follow the recipe prior phases used (see the slice-1 / Phase 7g live-gate notes): run the API with `dotnet run --no-launch-profile` in `src/Struo.Api` against the live `web-struo-cms-db` Postgres + Redis, authenticate as the bootstrap super-admin. Apply `db/migrations/002-article-structured-columns.sql` to the live DB (or provision fresh). Record which was used.

- [ ] **Step 4: Live-gate checks (API-level, UTF-8 via PowerShell `Invoke-RestMethod` or a UTF-8 file — never Big5 curl)**

1. Create an `article` with `translations.en.title`, `attributes` = `{ "featured": true, "score": 9, "note": "人工智慧" }`, `meta` = `{ "seo-title": "標題", "author": "me" }` → expect **201**.
2. `GET /api/items/article/{id}` → `attributes` reads back as a real JSON **object** (not a string) with `note` = `人工智慧` correct by code point (人=U+4EBA 工=U+5DE5 智=U+667A 慧=U+6167); `meta` reads back as an object with the `seo-title` key **verbatim** (un-camelCased) and `標題` correct. Both read from the `text` columns without truncation.
3. `PUT` the item: set `attributes` to a top-level **array** (e.g. `[1, "two", { "three": 3 }]`), add a `meta` key → expect **200**; re-`GET` confirms the array and the added key persisted.
4. Create/`PUT` with a `meta` entry whose key is blank (`{ "": "x" }`) → expect **400** `Field 'meta' has an entry with an empty key.` (not a 500 — the 7g bug-class guard).

Fix any Postgres-only issue surfaced (SQLite-green ≠ Postgres-correct); re-run until 4/4 green.

- [ ] **Step 5: Update the roadmap**

Add a Phase 7g+ (slice 2) row to `docs/ROADMAP.md` (status ✅ done + live-verified), a verification-baseline line with the final test counts, and update the 7g+ "remaining" row (structured editors now = `Repeater` only; `Files` still deferred).

- [ ] **Step 6: Commit + finish the branch**

```bash
git add docs/ROADMAP.md db/migrations/002-article-structured-columns.sql
git commit -m "docs: Phase 7g+ slice 2 done + live-verified (Json + KeyValue structured editors)"
```

Then use the `superpowers:finishing-a-development-branch` skill to merge the slice-2 branch into `main` (`--no-ff`), and record a new memory file + `MEMORY.md` pointer capturing the outcome (the two data shapes, the `Json`-raw-string / `JsonElement?`-disposed finding, the strip-on-write + parse-on-project mechanism, any live-gate fixes).

---

## Self-Review

**1. Spec coverage:**
- §2 approach (KeyValue `IsJson` text; Json raw string in plain text) → Tasks 1, 2. ✅
- §2.1 `JsonElement?`-disposed finding → captured as the reason Json is a string (Task 2 + memory in Task 8). ✅
- §3 data model (`string?` / `Dictionary<string,string>`; keys un-camelCased) → Tasks 1, 2, 3, 4; verbatim-key assertion in Task 3. ✅
- §4 column mapping (KeyValue → JSON set; Json via ContentBearingInterfaces) → Task 1; write strip+set → Task 2; KeyValue validation → Task 3; Json required via generic loop → Task 2 (test) + no extra code; MaxLength not applied (Json already excluded from ShortStringInterfaces) → Task 4 assertion; scanner non-sortable/searchable → Task 4; projection parse → Task 2. ✅
- §5 frontend registry + components → Tasks 5, 6, 7 (incl. the line-25 baseline caveat → Task 7 step 1a). ✅
- §6 sample fields → Task 4. ✅
- §7 tests (backend + frontend) + live gate → Tasks 1-8. ✅
- §8 non-goals (non-translatable, no querying, no MaxLength, no JSON schema, no tree editor, no key-order guarantee) → respected; no task introduces any of them.

**2. Placeholder scan:** No TBD/TODO/"handle edge cases"/"similar to". Every code step shows full code. Task 8 live-gate steps are procedural (manual verification) with exact payloads and expected status codes — appropriate for a live gate, not placeholders.

**3. Type consistency:** `Json` = `string?` and `KeyValue` = `Dictionary<string,string>` consistently across Tasks 1-4 and the migration (Task 8). Error strings match exactly between Task 3's tests and its implementation (`Field '{name}' has an entry with an empty key.`, `Field '{name}' is required.`). The factory set is renamed `MultiValueInterfaces` → `JsonColumnInterfaces` in one place (Task 1) with its single use site updated; `ItemService`'s own `MultiValueInterfaces` (a different set, 3 interfaces) is untouched. Frontend def names (`jsonDef`, `keyValueDef`, `asMinifiedJson`, `asJoinedKeyValue`) are consistent between Task 7 steps. `.kv-row`/`.kv-add`/`.kv-remove`/`.json-error` selectors match between components (Tasks 5, 6) and their tests. Registry list-eligible set (Task 7) matches the new defs' non-null `listColumn`.

**Note for the implementer:** Task 4 adds **non-required** sample fields on purpose — a *required* `Json`/`KeyValue` field on `Article` would make every existing create test that omits it fail. Required behaviour is covered by the dedicated `JsonThing`/`KvThing` collections (Tasks 2, 3).
