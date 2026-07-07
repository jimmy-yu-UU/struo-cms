# Phase 7g+ (slice 1) — Multi-value selects Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Light up the `MultiSelect`, `CheckboxGroup`, and `Tags` field interfaces (currently read-only) end-to-end — a multi-value field now stores an array (option-bound values, or free-form tags with optional per-item display text) via a SqlSugar JSON column, validated on write and edited in the SPA.

**Architecture:** Each multi-value field is a real list-typed CLR property (`List<string>` for the two option-bound interfaces; `List<TagItem>` for `Tags`) mapped to a JSON column by a `SqlSugarClientFactory` convention. `System.Text.Json` deserializes the incoming array straight into the entity (no change to `ItemService.Deserialize`'s parse step); a new validation branch enforces option-membership / non-blank tags / required-non-empty and de-duplicates. On the frontend the three interfaces become three registry entries + two/three `*Field.vue` components on the 7g.6 field-type registry.

**Tech Stack:** .NET 10 / C# · SqlSugarCore (`IsJson` columns) · xUnit + AwesomeAssertions · Vue 3 + PrimeVue 4.5 (`MultiSelect`, `Checkbox`, `InputText`, `Button`) · Vitest + `@vue/test-utils`.

## Global Constraints

- All DB access via SqlSugar ORM; zero vendor SQL (CLAUDE.md §17.4).
- Domain references nothing external; persistence attributes live on entities only (§2). `TagItem` is a plain record; `System.Text.Json.Serialization` attributes (BCL, not a NuGet package) are permitted.
- Outbound JSON is camelCase.
- Package versions are never hand-authored — no new packages are added in this slice.
- Multi-value fields are **non-translatable**, **non-sortable**, **non-searchable** in this slice. `MaxLength` does **not** apply to them (it stays a single-string limit).
- TDD: failing test first; frequent commits. Backend baseline **333** tests, frontend **192** — both must stay green plus the new tests.
- Frontend `FieldInterface` union already contains `multiSelect`/`checkboxGroup`/`tags` — no union change; `vue-tsc` enforces registry exhaustiveness.

---

### Task 1: `[CmsOptions]` label becomes optional

**Files:**
- Modify: `src/Struo.Infrastructure/Metadata/MetadataScanner.cs` (`ParseOptions`, ~lines 264-276)
- Test: `tests/Struo.Tests/Metadata/MetadataValidationTests.cs`

**Interfaces:**
- Consumes: `CmsOptionsAttribute.Options` (`IReadOnlyList<string>`), `FieldOption(string Value, string Label)`.
- Produces: `ParseOptions` now accepts a bare `"value"` (→ `Label == Value`) and a `"value:"` / `"value: "` blank label (→ `Label == Value`); a leading-colon `":x"` (empty value) still throws `MetadataException`.

- [ ] **Step 1: Replace the malformed-options test and add a value-fallback test**

In `tests/Struo.Tests/Metadata/MetadataValidationTests.cs`, replace the `MalformedOptions` nested type and its test, and add a scanner test. Change the `MalformedOptions` class to use a leading colon:

```csharp
        [CmsCollection("MalformedOptions")]
        private sealed class MalformedOptions
        {
            [CmsField(Interface = FieldInterface.Select)]
            [CmsOptions(":noValueHere")]   // leading colon => empty value => still invalid
            public string Status { get; set; } = "";
        }

        [CmsCollection("BareOptions")]
        private sealed class BareOptions
        {
            [CmsField(Interface = FieldInterface.Select)]
            [CmsOptions("draft", "published:Published")] // bare "draft" => label defaults to "draft"
            public string Status { get; set; } = "";
        }
```

Replace the existing `Throws_when_cms_options_entry_is_malformed` test body's expectation to the empty-value message, and add:

```csharp
        [Fact]
        public void Throws_when_cms_options_entry_has_empty_value()
        {
            var act = () => MetadataScanner.ScanTypes([typeof(MalformedOptions)]);
            act.Should().Throw<MetadataException>().WithMessage("*value is required*");
        }

        [Fact]
        public void Bare_option_entry_defaults_label_to_value()
        {
            var collections = MetadataScanner.ScanTypes([typeof(BareOptions)]);
            var field = collections.Single().Fields.Single(f => f.Name == "status");
            field.Options.Should().NotBeNull();
            field.Options!.Should().ContainSingle(o => o.Value == "draft" && o.Label == "draft");
            field.Options!.Should().ContainSingle(o => o.Value == "published" && o.Label == "Published");
        }
```

Delete the now-renamed old `Throws_when_cms_options_entry_is_malformed` method (its `*value:label*` expectation no longer holds).

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~MetadataValidationTests"`
Expected: FAIL — `Bare_option_entry_defaults_label_to_value` throws today (bare `"draft"` currently rejected), and `Throws_when_cms_options_entry_has_empty_value` fails on message text.

- [ ] **Step 3: Rewrite `ParseOptions` to make the label optional**

In `src/Struo.Infrastructure/Metadata/MetadataScanner.cs`, replace the `ParseOptions` method body:

```csharp
    private static IReadOnlyList<FieldOption> ParseOptions(PropertyInfo prop, CmsOptionsAttribute attr)
    {
        var list = new List<FieldOption>();
        foreach (var entry in attr.Options)
        {
            var idx = entry.IndexOf(':');
            if (idx == 0)
                throw new MetadataException(
                    $"Field '{prop.DeclaringType?.Name}.{prop.Name}' has malformed [CmsOptions] entry '{entry}' (value is required).");
            if (idx < 0)
            {
                // No colon: label defaults to the value ("draft" => value=label="draft").
                list.Add(new FieldOption(entry, entry));
                continue;
            }
            var value = entry[..idx];
            var label = entry[(idx + 1)..];
            list.Add(new FieldOption(value, string.IsNullOrWhiteSpace(label) ? value : label));
        }
        return list;
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~MetadataValidationTests"`
Expected: PASS (all MetadataValidationTests green).

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Infrastructure/Metadata/MetadataScanner.cs tests/Struo.Tests/Metadata/MetadataValidationTests.cs
git commit -m "feat: [CmsOptions] label is optional (bare value => label defaults to value)"
```

---

### Task 2: `TagItem` value type

**Files:**
- Create: `src/Struo.Domain/Metadata/Models/TagItem.cs`
- Test: `tests/Struo.Tests/Metadata/TagItemTests.cs`

**Interfaces:**
- Produces: `public sealed record TagItem(string Value, string? Label = null)` in namespace `Struo.Domain.Metadata.Models`; serializes camelCase as `{ "value": ..., "label": ... }` with `label` omitted when null.

- [ ] **Step 1: Write the failing serialization test**

Create `tests/Struo.Tests/Metadata/TagItemTests.cs`:

```csharp
using System.Text.Json;
using AwesomeAssertions;
using Struo.Domain.Metadata.Models;
using Xunit;

namespace Struo.Tests.Metadata;

public class TagItemTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Serializes_camelCase_and_omits_null_label()
    {
        JsonSerializer.Serialize(new TagItem("tech"), Web).Should().Be("""{"value":"tech"}""");
        JsonSerializer.Serialize(new TagItem("ai", "人工智慧"), Web)
            .Should().Be("""{"value":"ai","label":"人工智慧"}""");
    }

    [Fact]
    public void Deserializes_from_camelCase_object()
    {
        var t = JsonSerializer.Deserialize<TagItem>("""{"value":"ai","label":"人工智慧"}""", Web);
        t.Should().Be(new TagItem("ai", "人工智慧"));
        var bare = JsonSerializer.Deserialize<TagItem>("""{"value":"tech"}""", Web);
        bare.Should().Be(new TagItem("tech"));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~TagItemTests"`
Expected: FAIL — `TagItem` does not exist (compile error).

- [ ] **Step 3: Create the record**

Create `src/Struo.Domain/Metadata/Models/TagItem.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Struo.Domain.Metadata.Models;

/// <summary>
/// A single free-form tag value with an optional manual display label. When <see cref="Label"/>
/// is null the raw <see cref="Value"/> is displayed. This is the CLR shape stored (as a JSON
/// array) for a <c>Tags</c> field interface. Plain BCL record — no external dependency (§2).
/// </summary>
public sealed record TagItem(
    string Value,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Label = null);
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~TagItemTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Domain/Metadata/Models/TagItem.cs tests/Struo.Tests/Metadata/TagItemTests.cs
git commit -m "feat: add TagItem value type (value + optional display label)"
```

---

### Task 3: Map multi-value interfaces to JSON columns

**Files:**
- Modify: `src/Struo.Infrastructure/Persistence/SqlSugarClientFactory.cs`
- Test: `tests/Struo.Tests/Persistence/MultiValueColumnMappingTests.cs`

**Interfaces:**
- Consumes: `CmsFieldAttribute.Interface`, `FieldInterface.{MultiSelect,CheckboxGroup,Tags}`, `TagItem`.
- Produces: a `[CmsField]` property whose interface is multi-value gets `column.IsJson = true` in the CodeFirst `EntityService` hook, so a `List<string>` / `List<TagItem>` round-trips through a JSON column (`jsonb` on Postgres, JSON-in-text on SQLite). An explicit `[SugarColumn]` still wins.

- [ ] **Step 1: Write the failing round-trip test**

Create `tests/Struo.Tests/Persistence/MultiValueColumnMappingTests.cs`:

```csharp
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

/// <summary>
/// The SqlSugarClientFactory convention maps multi-value [CmsField] interfaces
/// (MultiSelect/CheckboxGroup/Tags) to a JSON column so a List&lt;string&gt; / List&lt;TagItem&gt;
/// round-trips. Verified cross-db via an actual insert+read (jsonb on PG, JSON-in-text on SQLite).
/// </summary>
public class MultiValueColumnMappingTests
{
    [SugarTable("multivalue_col_test_entity")]
    private sealed class MvColTestEntity
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public long Id { get; set; }

        [CmsField(Label = "Regions", Interface = FieldInterface.MultiSelect)]
        public List<string> Regions { get; set; } = [];

        [CmsField(Label = "Keywords", Interface = FieldInterface.Tags)]
        public List<TagItem> Keywords { get; set; } = [];
    }

    [Fact]
    public void Multi_value_lists_round_trip_through_a_json_column()
    {
        var db = new SqliteTestDatabase();
        var client = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = db.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        using (db)
        {
            client.CodeFirst.InitTables<MvColTestEntity>();
            var row = new MvColTestEntity
            {
                Regions = ["apac", "emea"],
                Keywords = [new TagItem("tech"), new TagItem("ai", "人工智慧")],
            };
            client.Insertable(row).ExecuteCommand();

            var read = client.Queryable<MvColTestEntity>().First();
            read.Regions.Should().Equal("apac", "emea");
            read.Keywords.Should().Equal(new TagItem("tech"), new TagItem("ai", "人工智慧"));
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~MultiValueColumnMappingTests"`
Expected: FAIL — without `IsJson`, SqlSugar cannot map `List<string>`/`List<TagItem>` (InitTables/insert throws or the read list is empty/wrong).

- [ ] **Step 3: Add the multi-value convention**

In `src/Struo.Infrastructure/Persistence/SqlSugarClientFactory.cs`, add a static set next to `ContentBearingInterfaces`:

```csharp
    // Multi-value [CmsField] interfaces store an array; map them to a JSON column so SqlSugar
    // (de)serializes the List<> automatically (jsonb on Postgres, JSON-in-text elsewhere).
    private static readonly HashSet<FieldInterface> MultiValueInterfaces =
    [
        FieldInterface.MultiSelect, FieldInterface.CheckboxGroup, FieldInterface.Tags
    ];
```

Then, inside the `EntityService` lambda, immediately after the `if (column.IsPrimarykey || column.IsIgnore) return;` line, insert:

```csharp
                    // Multi-value fields (List<string> / List<TagItem>) -> JSON column. An explicit
                    // [SugarColumn(IsJson=...)] still wins (this only sets the default).
                    var mvField = property.GetCustomAttribute<CmsFieldAttribute>();
                    if (mvField is not null && MultiValueInterfaces.Contains(mvField.Interface))
                    {
                        column.IsJson = true;
                        return;
                    }
```

(`using System.Reflection;` and `Struo.Domain.Metadata.Enums`/`Attributes` are already imported.)

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~MultiValueColumnMappingTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Infrastructure/Persistence/SqlSugarClientFactory.cs tests/Struo.Tests/Persistence/MultiValueColumnMappingTests.cs
git commit -m "feat: map multi-value field interfaces to JSON columns"
```

---

### Task 4: `ItemService` multi-value validation branch

**Files:**
- Modify: `src/Struo.Application/Query/ItemService.cs` (`Deserialize`, add a branch + a private helper)
- Test: `tests/Struo.Tests/Query/ItemServiceMultiValueTests.cs`

**Interfaces:**
- Consumes: `FieldMetadata.{Interface,Options,Required,Translatable,Name}`, `FieldOption.Value`, `TagItem`, `EntityDescriptor.FieldToProperty`.
- Produces: on both write paths, after deserialization, for each non-translatable multi-value field: option-bound values must be members of the field's options (else `QueryException`), tags must have non-blank values (else `QueryException`), `Required` means a non-empty list (else `QueryException`), lists are de-duplicated (option-bound by value; tags by value, keeping the first), and blank tag labels are coerced to null. Error messages: `Field '{name}' has value '{v}' not in its options.`, `Field '{name}' has a tag with an empty value.`, `Field '{name}' is required.`

This task uses a **dedicated test collection** so the required-empty case can be exercised without making a sample field required (which would break existing create tests).

- [ ] **Step 1: Write the failing tests**

Create `tests/Struo.Tests/Query/ItemServiceMultiValueTests.cs`:

```csharp
using System.Text.Json;
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Query;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;
using Struo.Domain.Auditing;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Security;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

public class ItemServiceMultiValueTests : IDisposable
{
    [SugarTable("mv_thing")]
    [CmsCollection("MvThing")]
    public sealed class MvThing : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [CmsField(Label = "Regions", Interface = FieldInterface.MultiSelect, Required = true)]
        [CmsOptions("apac:APAC", "emea:EMEA", "amer")]
        public List<string> Regions { get; set; } = [];

        [CmsField(Label = "Audiences", Interface = FieldInterface.CheckboxGroup)]
        [CmsOptions("b2b:B2B", "b2c:B2C")]
        public List<string> Audiences { get; set; } = [];

        [CmsField(Label = "Keywords", Interface = FieldInterface.Tags)]
        public List<TagItem> Keywords { get; set; } = [];
    }

    private readonly SqliteTestDatabase _file = new();
    private readonly ItemService _svc;

    public ItemServiceMultiValueTests()
    {
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        db.CodeFirst.InitTables<MvThing>();
        db.CodeFirst.InitTables<Language>();
        LanguageSeeder.SeedAsync(db).GetAwaiter().GetResult();

        var types = new[] { typeof(MvThing) };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["mvthing"] = typeof(MvThing),
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
    public async Task Create_round_trips_all_three_multi_value_shapes()
    {
        var body = Body(new
        {
            regions = new[] { "apac", "emea" },
            audiences = new[] { "b2b" },
            keywords = new object[] { new { value = "tech" }, new { value = "ai", label = "人工智慧" } },
        });
        var created = await _svc.CreateAsync("mvthing", body);
        var id = created["id"]!.ToString()!;

        var read = await _svc.GetAsync("mvthing", id);
        read.Should().NotBeNull();
        ((IEnumerable<string>)read!["regions"]!).Should().Equal("apac", "emea");
        ((IEnumerable<string>)read["audiences"]!).Should().Equal("b2b");
        var kws = ((IEnumerable<TagItem>)read["keywords"]!).ToList();
        kws.Should().Equal(new TagItem("tech"), new TagItem("ai", "人工智慧"));
    }

    [Fact]
    public async Task Create_rejects_option_value_not_in_options()
    {
        var body = Body(new { regions = new[] { "apac", "mars" } });
        var act = () => _svc.CreateAsync("mvthing", body);
        await act.Should().ThrowAsync<QueryException>()
            .WithMessage("Field 'regions' has value 'mars' not in its options.");
    }

    [Fact]
    public async Task Create_rejects_required_multi_value_when_empty()
    {
        var body = Body(new { audiences = new[] { "b2b" } }); // regions (required) omitted
        var act = () => _svc.CreateAsync("mvthing", body);
        await act.Should().ThrowAsync<QueryException>()
            .WithMessage("Field 'regions' is required.");
    }

    [Fact]
    public async Task Create_rejects_tag_with_blank_value()
    {
        var body = Body(new
        {
            regions = new[] { "apac" },
            keywords = new object[] { new { value = "  " } },
        });
        var act = () => _svc.CreateAsync("mvthing", body);
        await act.Should().ThrowAsync<QueryException>()
            .WithMessage("Field 'keywords' has a tag with an empty value.");
    }

    [Fact]
    public async Task Create_de_duplicates_and_drops_blank_tag_labels()
    {
        var body = Body(new
        {
            regions = new[] { "apac", "apac", "emea" },
            keywords = new object[] { new { value = "tech", label = " " }, new { value = "tech", label = "X" } },
        });
        var created = await _svc.CreateAsync("mvthing", body);
        var read = await _svc.GetAsync("mvthing", created["id"]!.ToString()!);
        ((IEnumerable<string>)read!["regions"]!).Should().Equal("apac", "emea");
        var kws = ((IEnumerable<TagItem>)read["keywords"]!).ToList();
        kws.Should().ContainSingle();
        kws[0].Should().Be(new TagItem("tech")); // blank label -> null; second "tech" dropped
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~ItemServiceMultiValueTests"`
Expected: FAIL — no validation branch yet: the membership/required/blank cases do not throw, and de-dup does not happen.

- [ ] **Step 3: Implement the validation branch**

In `src/Struo.Application/Query/ItemService.cs`, add the using for the value type near the top:

```csharp
using Struo.Domain.Metadata.Models;
```

Add a private static set and helper to the class (near the other private statics):

```csharp
    private static readonly HashSet<FieldInterface> MultiValueInterfaces =
    [
        FieldInterface.MultiSelect, FieldInterface.CheckboxGroup, FieldInterface.Tags
    ];
```

In `Deserialize`, immediately after the max-length loop (the `foreach (var field in meta.Fields.Where(f => f.MaxLength is > 0 && !f.Translatable))` block) and before `return entity;`, insert:

```csharp
        // Multi-value fields (MultiSelect/CheckboxGroup/Tags) live on the parent entity as
        // List<string> / List<TagItem>. Validate membership / non-blank tags, enforce Required as
        // non-empty, de-duplicate, and coerce blank tag labels to null. Non-translatable only.
        foreach (var field in meta.Fields.Where(f => MultiValueInterfaces.Contains(f.Interface) && !f.Translatable))
        {
            if (!d.FieldToProperty.TryGetValue(field.Name, out var prop)) continue;
            var pi = d.EntityType.GetProperty(prop);
            if (pi is not { CanWrite: true }) continue;

            if (field.Interface == FieldInterface.Tags)
            {
                var tags = (pi.GetValue(entity) as IEnumerable<TagItem>) ?? [];
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var cleaned = new List<TagItem>();
                foreach (var t in tags)
                {
                    if (t is null || string.IsNullOrWhiteSpace(t.Value))
                        throw new QueryException($"Field '{field.Name}' has a tag with an empty value.");
                    if (!seen.Add(t.Value)) continue; // de-dup by value, keep first
                    var label = string.IsNullOrWhiteSpace(t.Label) ? null : t.Label;
                    cleaned.Add(new TagItem(t.Value, label));
                }
                if (field.Required && cleaned.Count == 0)
                    throw new QueryException($"Field '{field.Name}' is required.");
                pi.SetValue(entity, cleaned);
            }
            else // option-bound MultiSelect / CheckboxGroup
            {
                var values = (pi.GetValue(entity) as IEnumerable<string>) ?? [];
                var allowed = (field.Options ?? []).Select(o => o.Value).ToHashSet(StringComparer.Ordinal);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var cleaned = new List<string>();
                foreach (var v in values)
                {
                    if (v is null) continue;
                    if (!allowed.Contains(v))
                        throw new QueryException($"Field '{field.Name}' has value '{v}' not in its options.");
                    if (seen.Add(v)) cleaned.Add(v); // de-dup, keep first
                }
                if (field.Required && cleaned.Count == 0)
                    throw new QueryException($"Field '{field.Name}' is required.");
                pi.SetValue(entity, cleaned);
            }
        }
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~ItemServiceMultiValueTests"`
Expected: PASS (5/5).

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Application/Query/ItemService.cs tests/Struo.Tests/Query/ItemServiceMultiValueTests.cs
git commit -m "feat: validate + de-duplicate multi-value fields on write"
```

---

### Task 5: Sample fields on `Article` + scanner assertions

**Files:**
- Modify: `samples/Struo.Sample.Blog/Article.cs`
- Test: `tests/Struo.Tests/Metadata/MetadataScannerTests.cs` (add multi-value assertions)

**Interfaces:**
- Consumes: the JSON-column convention (Task 3) and the validation branch (Task 4).
- Produces: `Article` exposes `regions` (MultiSelect), `audiences` (CheckboxGroup), `keywords` (Tags); all non-required, non-sortable, non-searchable; `regions` options include a value-fallback label (`amer` → label `amer`).

- [ ] **Step 1: Write the failing scanner test**

Add to `tests/Struo.Tests/Metadata/MetadataScannerTests.cs` (inside its existing test class; it already scans `Article` — reuse the same scan pattern used by neighbouring tests):

```csharp
    [Fact]
    public void Article_exposes_multi_value_fields_with_correct_metadata()
    {
        var collections = MetadataScanner.ScanTypes([typeof(Struo.Sample.Blog.Article)]);
        var article = collections.Single(c => string.Equals(c.Name, "article", StringComparison.OrdinalIgnoreCase));

        var regions = article.Fields.Single(f => f.Name == "regions");
        regions.Interface.Should().Be(FieldInterface.MultiSelect);
        regions.Sortable.Should().BeFalse();
        regions.Searchable.Should().BeFalse();
        regions.Options!.Should().ContainSingle(o => o.Value == "amer" && o.Label == "amer"); // value-fallback

        article.Fields.Single(f => f.Name == "audiences").Interface.Should().Be(FieldInterface.CheckboxGroup);
        article.Fields.Single(f => f.Name == "keywords").Interface.Should().Be(FieldInterface.Tags);
    }
```

(If `MetadataScannerTests` lacks `using Struo.Domain.Metadata.Enums;` or AwesomeAssertions, add them — match the file's existing usings.)

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~MetadataScannerTests.Article_exposes_multi_value_fields"`
Expected: FAIL — `Article` has no `regions`/`audiences`/`keywords` fields yet.

- [ ] **Step 3: Add the sample fields**

In `samples/Struo.Sample.Blog/Article.cs`, add the `using`:

```csharp
using Struo.Domain.Metadata.Models;
```

and add three properties inside the class (after `HeroImageId`, before the `// --- relations ---` block):

```csharp
    [CmsField(Label = "Regions", Interface = FieldInterface.MultiSelect, Sort = 6, Group = "Content")]
    [CmsOptions("apac:APAC", "emea:EMEA", "amer")] // "amer" has no explicit label -> falls back to "amer"
    public List<string> Regions { get; set; } = [];

    [CmsField(Label = "Audiences", Interface = FieldInterface.CheckboxGroup, Sort = 7, Group = "Content")]
    [CmsOptions("b2b:B2B", "b2c:B2C")]
    public List<string> Audiences { get; set; } = [];

    [CmsField(Label = "Keywords", Interface = FieldInterface.Tags, Sort = 8, Group = "Content")]
    public List<TagItem> Keywords { get; set; } = [];
```

- [ ] **Step 4: Run the full backend suite to verify no regressions**

Run: `dotnet test tests/Struo.Tests`
Expected: PASS — the new scanner test passes and all existing tests stay green (existing `Article` creates omit these fields → empty lists → non-required → no throw; extra projected keys don't break key-presence assertions).

- [ ] **Step 5: Commit**

```bash
git add samples/Struo.Sample.Blog/Article.cs tests/Struo.Tests/Metadata/MetadataScannerTests.cs
git commit -m "feat: add sample MultiSelect/CheckboxGroup/Tags fields to Article"
```

---

### Task 6: Frontend option-bound components (`MultiSelectField`, `CheckboxGroupField`)

**Files:**
- Create: `frontend/src/components/fields/MultiSelectField.vue`
- Create: `frontend/src/components/fields/CheckboxGroupField.vue`
- Test: `frontend/src/components/fields/multiValueFields.test.ts`

**Interfaces:**
- Consumes: `FieldMeta.options` (`{ value, label }[]`), PrimeVue `MultiSelect`, `Checkbox`.
- Produces: two components with the uniform contract (`{ field, modelValue, disabled }` props; `update:modelValue` emit) whose model value is `string[]`.

- [ ] **Step 1: Write the failing component tests**

Create `frontend/src/components/fields/multiValueFields.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import MultiSelectField from './MultiSelectField.vue'
import CheckboxGroupField from './CheckboxGroupField.vue'
import type { FieldMeta } from '../../types/schema'

function field(over: Partial<FieldMeta> & { interface: string }): FieldMeta {
  return { name: 'f', label: 'F', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over } as FieldMeta
}
const opts = { global: { plugins: [PrimeVue] } }
const options = [{ value: 'apac', label: 'APAC' }, { value: 'emea', label: 'EMEA' }]

describe('MultiSelectField', () => {
  it('passes options and value to PrimeVue MultiSelect', () => {
    const w = mount(MultiSelectField, { props: { field: field({ interface: 'multiSelect', options }), modelValue: ['apac'] }, ...opts })
    const ms = w.findComponent({ name: 'MultiSelect' })
    expect(ms.props('options')).toEqual(options)
    expect(ms.props('modelValue')).toEqual(['apac'])
  })

  it('relays selection changes as an array', () => {
    const w = mount(MultiSelectField, { props: { field: field({ interface: 'multiSelect', options }), modelValue: [] }, ...opts })
    w.findComponent({ name: 'MultiSelect' }).vm.$emit('update:modelValue', ['apac', 'emea'])
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([['apac', 'emea']])
  })
})

describe('CheckboxGroupField', () => {
  it('renders one checkbox per option', () => {
    const w = mount(CheckboxGroupField, { props: { field: field({ interface: 'checkboxGroup', options }), modelValue: [] }, ...opts })
    expect(w.findAll('.checkbox-option')).toHaveLength(2)
  })

  it('relays the toggled array', () => {
    const w = mount(CheckboxGroupField, { props: { field: field({ interface: 'checkboxGroup', options }), modelValue: ['apac'] }, ...opts })
    w.findComponent({ name: 'Checkbox' }).vm.$emit('update:modelValue', ['apac', 'emea'])
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([['apac', 'emea']])
  })
})
```

- [ ] **Step 2: Run to verify they fail**

Run: `cd frontend && pnpm test -- multiValueFields`
Expected: FAIL — the components do not exist.

- [ ] **Step 3: Create `MultiSelectField.vue`**

Create `frontend/src/components/fields/MultiSelectField.vue`:

```vue
<script setup lang="ts">
import MultiSelect from 'primevue/multiselect'
import type { FieldMeta } from '../../types/schema'
defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
defineEmits<{ (e: 'update:modelValue', v: string[]): void }>()
</script>
<template>
  <MultiSelect :model-value="(modelValue as string[])" :options="field.options ?? []"
    option-label="label" option-value="value" :disabled="disabled" display="chip" :show-toggle-all="false"
    @update:model-value="(v: string[]) => $emit('update:modelValue', v ?? [])" />
</template>
```

- [ ] **Step 4: Create `CheckboxGroupField.vue`**

Create `frontend/src/components/fields/CheckboxGroupField.vue`:

```vue
<script setup lang="ts">
import Checkbox from 'primevue/checkbox'
import type { FieldMeta } from '../../types/schema'
const props = defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
defineEmits<{ (e: 'update:modelValue', v: string[]): void }>()
</script>
<template>
  <div class="checkbox-group">
    <label v-for="opt in field.options ?? []" :key="opt.value" class="checkbox-option">
      <Checkbox :model-value="(props.modelValue as string[])" :value="opt.value" :disabled="disabled"
        @update:model-value="(v: string[]) => $emit('update:modelValue', v ?? [])" />
      <span>{{ opt.label }}</span>
    </label>
  </div>
</template>
```

- [ ] **Step 5: Run to verify they pass**

Run: `cd frontend && pnpm test -- multiValueFields`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/fields/MultiSelectField.vue frontend/src/components/fields/CheckboxGroupField.vue frontend/src/components/fields/multiValueFields.test.ts
git commit -m "feat(frontend): MultiSelectField + CheckboxGroupField components"
```

---

### Task 7: Frontend `TagsField` component + TS type

**Files:**
- Modify: `frontend/src/types/schema.ts` (add `TagItem` type)
- Create: `frontend/src/components/fields/TagsField.vue`
- Test: `frontend/src/components/fields/TagsField.test.ts`

**Interfaces:**
- Produces: `export type TagItem = { value: string; label?: string }` in `types/schema.ts`; `TagsField.vue` (uniform contract) whose model value is `TagItem[]`, editing value + optional display text per row with add/remove, using immutable updates.

- [ ] **Step 1: Add the `TagItem` TS type**

In `frontend/src/types/schema.ts`, add after the `FieldOption` type:

```ts
// A single free-form tag: stored value + optional manual display label (label ?? value shown).
export type TagItem = { value: string; label?: string }
```

- [ ] **Step 2: Write the failing component test**

Create `frontend/src/components/fields/TagsField.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import TagsField from './TagsField.vue'
import type { FieldMeta } from '../../types/schema'

function field(over: Partial<FieldMeta> & { interface: string }): FieldMeta {
  return { name: 'f', label: 'F', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over } as FieldMeta
}
const opts = { global: { plugins: [PrimeVue] } }

describe('TagsField', () => {
  it('renders one row per tag with value + label inputs', () => {
    const w = mount(TagsField, {
      props: { field: field({ interface: 'tags' }), modelValue: [{ value: 'tech' }, { value: 'ai', label: '人工智慧' }] },
      ...opts,
    })
    expect(w.findAll('.tag-row')).toHaveLength(2)
  })

  it('adds a blank row', async () => {
    const w = mount(TagsField, { props: { field: field({ interface: 'tags' }), modelValue: [] }, ...opts })
    await w.get('.tag-add').trigger('click')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([[{ value: '' }]])
  })

  it('removes a row immutably', async () => {
    const w = mount(TagsField, {
      props: { field: field({ interface: 'tags' }), modelValue: [{ value: 'a' }, { value: 'b' }] }, ...opts,
    })
    await w.findAll('.tag-remove')[0].trigger('click')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([[{ value: 'b' }]])
  })

  it('edits value and label immutably', async () => {
    const w = mount(TagsField, { props: { field: field({ interface: 'tags' }), modelValue: [{ value: 'a' }] }, ...opts })
    const inputs = w.findAll('.tag-row input')
    await inputs[0].setValue('tech')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([[{ value: 'tech' }]])
    await inputs[1].setValue('科技')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([[{ value: 'a', label: '科技' }]])
  })
})
```

- [ ] **Step 3: Run to verify it fails**

Run: `cd frontend && pnpm test -- TagsField`
Expected: FAIL — `TagsField.vue` does not exist.

- [ ] **Step 4: Create `TagsField.vue`**

Create `frontend/src/components/fields/TagsField.vue`:

```vue
<script setup lang="ts">
import { computed } from 'vue'
import InputText from 'primevue/inputtext'
import Button from 'primevue/button'
import type { FieldMeta, TagItem } from '../../types/schema'

const props = defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: TagItem[]): void }>()

const items = computed<TagItem[]>(() => (Array.isArray(props.modelValue) ? (props.modelValue as TagItem[]) : []))

function commit(next: TagItem[]) { emit('update:modelValue', next) }
function add() { commit([...items.value, { value: '' }]) }
function remove(i: number) { commit(items.value.filter((_, idx) => idx !== i)) }
function setValue(i: number, v: string) {
  commit(items.value.map((it, idx) => (idx === i ? { ...it, value: v } : it)))
}
function setLabel(i: number, v: string) {
  // Drop the label key entirely when cleared, so an empty label never round-trips as "".
  commit(items.value.map((it, idx) => {
    if (idx !== i) return it
    if (v) return { ...it, value: it.value, label: v }
    const { label: _drop, ...rest } = it
    return rest
  }))
}
</script>
<template>
  <div class="tags-field">
    <div v-for="(it, i) in items" :key="i" class="tag-row">
      <InputText :model-value="it.value" :disabled="disabled" placeholder="value"
        @update:model-value="(v: string) => setValue(i, v ?? '')" />
      <InputText :model-value="it.label ?? ''" :disabled="disabled" placeholder="display text (optional)"
        @update:model-value="(v: string) => setLabel(i, v ?? '')" />
      <Button class="tag-remove" icon="pi pi-times" text :disabled="disabled" @click="remove(i)" />
    </div>
    <Button class="tag-add" icon="pi pi-plus" label="Add" text :disabled="disabled" @click="add" />
  </div>
</template>
```

- [ ] **Step 5: Run to verify it passes**

Run: `cd frontend && pnpm test -- TagsField`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/types/schema.ts frontend/src/components/fields/TagsField.vue frontend/src/components/fields/TagsField.test.ts
git commit -m "feat(frontend): TagsField editor (value + optional display text)"
```

---

### Task 8: Wire the three interfaces into the field-type registry

**Files:**
- Modify: `frontend/src/lib/fieldTypes/registry.ts`
- Modify: `frontend/src/lib/fieldTypes/registry.test.ts` (extend `LIST_ELIGIBLE`, add round-trip + format tests)

**Interfaces:**
- Consumes: `MultiSelectField`, `CheckboxGroupField`, `TagsField`, `FieldMeta.options`, `TagItem`.
- Produces: `registry.multiSelect/checkboxGroup/tags` become real defs (`defaultValue: []`, array `parse`, de-duping/blank-dropping `serialize`, joined-label `listColumn`). Multi-value interfaces are now list-column eligible.

- [ ] **Step 1: Extend the registry tests**

In `frontend/src/lib/fieldTypes/registry.test.ts`, add the three interfaces to `LIST_ELIGIBLE`:

```ts
const LIST_ELIGIBLE = new Set([
  'text', 'textarea', 'slug', 'email', 'url', 'phone', 'color',
  'number', 'slider', 'rating', 'boolean', 'checkbox',
  'date', 'time', 'dateTime', 'select', 'radio',
  'multiSelect', 'checkboxGroup', 'tags',
])
```

And append these tests inside the `describe('field-type registry', ...)` block:

```ts
  it('multi-value defaults are empty arrays and parse coerces non-arrays', () => {
    for (const i of ['multiSelect', 'checkboxGroup', 'tags']) {
      expect(getFieldType(i).defaultValue(field({ interface: i }))).toEqual([])
      expect(getFieldType(i).parse(undefined, field({ interface: i }))).toEqual([])
      expect(getFieldType(i).parse(['a'], field({ interface: i }))).toEqual(['a'])
    }
  })

  it('option-bound serialize drops blanks and de-duplicates', () => {
    const f = field({ interface: 'multiSelect' })
    expect(getFieldType('multiSelect').serialize(['a', '', 'a', 'b'], f)).toEqual(['a', 'b'])
  })

  it('tags serialize drops blank values/labels and de-duplicates by value', () => {
    const f = field({ interface: 'tags' })
    expect(getFieldType('tags').serialize(
      [{ value: 'tech', label: ' ' }, { value: '' }, { value: 'tech', label: 'X' }, { value: 'ai', label: '人工智慧' }], f,
    )).toEqual([{ value: 'tech' }, { value: 'ai', label: '人工智慧' }])
  })

  it('list formatters join labels for option-bound and label??value for tags', () => {
    const ms = field({ interface: 'multiSelect', options: [{ value: 'apac', label: 'APAC' }, { value: 'emea', label: 'EMEA' }] })
    expect(getFieldType('multiSelect').listColumn!.format(['apac', 'emea'], ms)).toBe('APAC, EMEA')
    expect(getFieldType('multiSelect').listColumn!.format(['apac', 'zzz'], ms)).toBe('APAC, zzz') // unknown -> raw value
    const tg = field({ interface: 'tags' })
    expect(getFieldType('tags').listColumn!.format([{ value: 'tech' }, { value: 'ai', label: '人工智慧' }], tg)).toBe('tech, 人工智慧')
  })
```

- [ ] **Step 2: Run to verify they fail**

Run: `cd frontend && pnpm test -- fieldTypes/registry`
Expected: FAIL — the three interfaces still resolve to `readonlyDef` (`listColumn` null, `defaultValue` `''`).

- [ ] **Step 3: Wire the defs into the registry**

In `frontend/src/lib/fieldTypes/registry.ts`, add imports:

```ts
import MultiSelectField from '../../components/fields/MultiSelectField.vue'
import CheckboxGroupField from '../../components/fields/CheckboxGroupField.vue'
import TagsField from '../../components/fields/TagsField.vue'
import type { TagItem } from '../../types/schema'
```

Add the list formatters and def builders near the existing `asOption` helper:

```ts
const asJoinedOptions: ListColumn = {
  format: (v, f) => (Array.isArray(v)
    ? v.map((x) => f.options?.find((o) => o.value === String(x))?.label ?? String(x)).join(', ')
    : String(v ?? '')),
}
const asJoinedTags: ListColumn = {
  format: (v) => (Array.isArray(v)
    ? (v as TagItem[]).map((t) => t?.label ?? t?.value ?? '').join(', ')
    : String(v ?? '')),
}

const arrParse = (raw: unknown): unknown[] => (Array.isArray(raw) ? raw : [])

function optionMultiDef(component: Component): FieldTypeDef {
  return {
    component,
    defaultValue: () => [],
    parse: arrParse,
    serialize: (v) => {
      if (!Array.isArray(v)) return []
      const seen = new Set<string>()
      const out: string[] = []
      for (const x of v) {
        const s = String(x)
        if (s.trim() === '' || seen.has(s)) continue
        seen.add(s); out.push(s)
      }
      return out
    },
    listColumn: asJoinedOptions,
  }
}

const tagsDef: FieldTypeDef = {
  component: TagsField,
  defaultValue: () => [],
  parse: arrParse,
  serialize: (v) => {
    if (!Array.isArray(v)) return []
    const seen = new Set<string>()
    const out: TagItem[] = []
    for (const t of v as Array<Partial<TagItem>>) {
      const value = (t?.value ?? '').trim()
      if (value === '' || seen.has(value)) continue
      seen.add(value)
      const label = (t?.label ?? '').trim()
      out.push(label ? { value, label } : { value })
    }
    return out
  },
  listColumn: asJoinedTags,
}
```

Then replace the three deferred entries in the `registry` object:

```ts
  multiSelect: optionMultiDef(MultiSelectField),
  checkboxGroup: optionMultiDef(CheckboxGroupField),
  tags: tagsDef,
```

(Remove them from the "Deferred" comment block; `json`, `keyValue`, `repeater`, `files`, `hidden`, `uuid` remain `readonlyDef`.)

- [ ] **Step 4: Run the registry tests + typecheck**

Run: `cd frontend && pnpm test -- fieldTypes/registry && pnpm vue-tsc --noEmit`
Expected: PASS and typecheck clean (registry still exhaustive over `FieldInterface`).

- [ ] **Step 5: Run the full frontend suite + build**

Run: `cd frontend && pnpm test && pnpm build`
Expected: all tests green (192 + new), build succeeds (pre-existing chunk-size advisory only).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/lib/fieldTypes/registry.ts frontend/src/lib/fieldTypes/registry.test.ts
git commit -m "feat(frontend): wire MultiSelect/CheckboxGroup/Tags into the field-type registry"
```

---

### Task 9: Full gates + live gate (real Postgres) + docs

**Files:**
- Modify: `docs/ROADMAP.md`
- Modify: `C:\Users\YuJimmy\.claude\projects\D--dotnet-struo-cms\memory\MEMORY.md` + a new memory file (post-verification)

**Interfaces:**
- Consumes: everything above.
- Produces: recorded verification baseline + live-gate result.

- [ ] **Step 1: Run the complete automated gates**

Run: `dotnet build -warnaserror && dotnet test tests/Struo.Tests`
Expected: build clean; **333 + new** tests pass / 0 fail.

Run: `cd frontend && pnpm test && pnpm vue-tsc --noEmit && pnpm build`
Expected: **192 + new** tests pass; typecheck clean; build succeeds.

- [ ] **Step 2: Start the dev API against live Postgres + Redis**

Follow the same recipe prior phases used (see the Phase 7g live-gate note): run the API with `dotnet run --no-launch-profile` in the API project against the live `web-struo-cms-db` Postgres + Redis, authenticate as the bootstrap super-admin, and apply the multi-value columns. Because `InitTables` adds tables but not columns, run the migration for the three new `article` JSON columns (add a script `db/migrations/NNNN-article-multivalue-columns.sql` creating `regions jsonb`, `audiences jsonb`, `keywords jsonb` with default `'[]'::jsonb`, matching the SqlSugar mapping) OR provision a fresh DB. Record which was used.

- [ ] **Step 3: Live-gate checks (API-level, UTF-8 via PowerShell `Invoke-RestMethod` or a UTF-8 file — never Big5 curl)**

1. Create an `article` with `translations.en.title`, `regions: ["apac","emea"]`, `audiences: ["b2b"]`, `keywords: [{"value":"tech"},{"value":"ai","label":"人工智慧"}]` → expect **201**.
2. `GET /api/items/article/{id}` → `regions`/`audiences` are value-string arrays; `keywords` round-trips `{value,label?}` with the label present on `ai` and **absent** on `tech`; UTF-8 label correct; values read back from `jsonb`.
3. `PUT` the item: `regions: ["amer"]` (the value-fallback option), add a label to `tech` → expect **200** and the change persisted on re-`GET`.
4. Create/PUT with an out-of-options `regions` value (e.g. `["mars"]`) → expect **400** `Field 'regions' has value 'mars' not in its options.` (not a 500 — the 7g bug-class guard).

Fix any Postgres-only issue surfaced (SQLite-green ≠ Postgres-correct); re-run until 4/4 green.

- [ ] **Step 4: Update the roadmap**

Add a Phase 7g+ (slice 1) row to `docs/ROADMAP.md` (status ✅ done + live-verified), a verification-baseline line with the final test counts, and mark the multi-value select group done in the 7g+ row (structured editors / `Files` still deferred).

- [ ] **Step 5: Commit + finish the branch**

```bash
git add docs/ROADMAP.md db/migrations/ 2>/dev/null
git commit -m "docs: Phase 7g+ slice 1 done + live-verified (multi-value selects)"
```

Then use the `superpowers:finishing-a-development-branch` skill to merge `phase-7g-plus-multivalue-selects` into `main` (`--no-ff`), and record a new memory file + `MEMORY.md` pointer capturing the outcome (data shapes, the two display-text mechanisms, any live-gate fixes).

---

## Self-Review

**1. Spec coverage:**
- §2 Approach A (typed list + `IsJson`) → Tasks 3, 4. ✅
- §3 data model (`List<string>` / `List<TagItem>`, `TagItem`) → Tasks 2, 3, 5. ✅
- §3 two display-text mechanisms: option label optional → Task 1; per-item tag label → Tasks 2, 7, 8. ✅
- §4 `[CmsOptions]` optional label → Task 1. ✅
- §5 column mapping → Task 3; validation branch (membership/required/tag-blank/de-dup/label-coerce) → Task 4; scanner non-sortable/non-searchable + `MaxLength`-not-applied (guarded by `typeof(string)`, already true) → Task 5 assertions; projection unchanged → verified by Task 4 round-trip. ✅
- §6 frontend registry + components → Tasks 6, 7, 8. ✅
- §7 sample fields → Task 5. ✅
- §8 tests (backend + frontend) + live gate → Tasks 1-9. ✅
- §9 non-goals (non-translatable, no array querying, no MaxLength, no max-count, tags always objects) → respected; multi-value marked non-sortable/non-searchable in scanner (Task 5). Note: the scanner already emits `Sortable`/`Searchable` from the attribute defaults (false unless set), so no code change is needed to keep them false — Task 5 only *asserts* it.

**2. Placeholder scan:** No TBD/TODO/"handle edge cases"/"similar to". Every code step shows full code. Task 9 live-gate steps are procedural (manual verification) with exact request payloads and expected status codes — appropriate for a live gate, not placeholders.

**3. Type consistency:** `TagItem(string Value, string? Label = null)` (backend) ↔ `TagItem = { value: string; label?: string }` (frontend) — camelCase matches the wire. `MultiValueInterfaces` set defined identically in `SqlSugarClientFactory` (Task 3) and `ItemService` (Task 4) — both are local statics by design (no shared dependency across the layer boundary). Error message strings in Task 4 tests match the `throw` strings in Task 4 implementation exactly. Registry def names (`optionMultiDef`, `tagsDef`, `arrParse`, `asJoinedOptions`, `asJoinedTags`) are consistent between Task 8 steps. `.tag-row`/`.tag-add`/`.tag-remove`/`.checkbox-option` selectors match between component templates (Tasks 6, 7) and their tests.

**Note for the implementer:** Task 5 adds non-required sample fields to `Article` on purpose — a *required* multi-value field on an existing sample collection would make every existing create test that omits it fail with `... is required.`. Required-empty behaviour is covered instead by the dedicated `MvThing` collection in Task 4.
