# Phase 7g+ slice 4 — `Repeater` field Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Light up the last `FieldInterface` value — `Repeater` — as an ordered list of typed child
objects (`List<TChild>` where `TChild` is a POCO of `[CmsField]` sub-properties), persisted as a JSON
`text` column, edited by a recursive card-list frontend component.

**Architecture:** Reuse the proven slice-1 JSON-column persistence convention (`IsJson` + `text`) exactly
as `Tags` (`List<TagItem>`) already does. The scanner recurses into the child POCO to build a nested
sub-field schema on `FieldMetadata.Fields`. `ItemService` validates each row's sub-fields (Required,
option membership, MaxLength) and drops fully-blank rows. The frontend registers a `RepeaterField.vue`
that renders each row as a mini-form, dispatching each sub-field through the existing field registry
(`getFieldType`) recursively.

**Tech Stack:** .NET 10 / C# · SqlSugarCore (`IsJson`) · System.Text.Json · xUnit + AwesomeAssertions ·
Vue 3 + PrimeVue · Vitest + @vue/test-utils.

## Global Constraints

- All DB access via SqlSugar ORM; zero vendor SQL. `IsJson` columns MUST also set `DataType = "text"`
  (`IsJson` alone → `varchar(1)` on Postgres — slice-1 finding `3b4ab40`).
- Outbound JSON = camelCase. Sub-field metadata names are `Camel(prop.Name)`.
- The field is **non-translatable, non-sortable, non-searchable**; parent-level `MaxLength` N/A.
- Allowed sub-field interfaces (whitelist): `Text, Textarea, Markdown, Code, Slug, Email, Url, Color,
  Phone, Number, Slider, Rating, Boolean, Checkbox, Date, Time, DateTime, Select, Radio`. Every other
  interface (incl. `Repeater`, `RichText`, `File`/`Image`/`Files`, `MultiSelect`/`CheckboxGroup`/`Tags`,
  `Json`/`KeyValue`, `Password`/`Hidden`/`Uuid`/`Divider`) fail-fasts at scan.
- Immutable updates on the frontend (spread, never mutate in place).
- Every JSON author-side code path is System.Text.Json; Newtonsoft only appears as SqlSugar-internal
  `IsJson` materialization.
- Spec: `docs/superpowers/specs/2026-07-07-phase7g-plus-repeater-design.md`.

---

### Task 1: Scanner builds the Repeater nested sub-field schema (happy path)

**Files:**
- Modify: `src/Struo.Domain/Metadata/Models/FieldMetadata.cs`
- Modify: `src/Struo.Infrastructure/Metadata/MetadataScanner.cs`
- Test: `tests/Struo.Tests/Metadata/MetadataScannerTests.cs`

**Interfaces:**
- Produces: `FieldMetadata.Fields` (`IReadOnlyList<FieldMetadata>?`, null unless the field is a Repeater).
- Produces: `MetadataScanner.RepeaterAllowedInterfaces` (`HashSet<FieldInterface>`), used by Task 2.

- [ ] **Step 1: Write the failing test**

Add to `MetadataScannerTests.cs` (inside the existing `MetadataScannerTests` class):

```csharp
public sealed class FaqRow
{
    [CmsField(Label = "Question", Interface = FieldInterface.Text, Required = true)]
    public string Question { get; set; } = "";

    [CmsField(Label = "Answer", Interface = FieldInterface.Textarea)]
    public string Answer { get; set; } = "";

    [CmsField(Label = "Category", Interface = FieldInterface.Select)]
    [CmsOptions("general:General", "billing:Billing")]
    public string? Category { get; set; }
}

[CmsCollection("RepeaterHost")]
public sealed class RepeaterHost
{
    [CmsField(Interface = FieldInterface.Text)] public string Name { get; set; } = "";

    [CmsField(Label = "FAQs", Interface = FieldInterface.Repeater)]
    public List<FaqRow> Faqs { get; set; } = new();
}

[Fact]
public void Repeater_field_carries_nested_sub_field_schema()
{
    var meta = MetadataScanner.ScanTypes([typeof(RepeaterHost)]).Single();
    var faqs = meta.Fields.Single(f => f.Name == "faqs");

    faqs.Interface.Should().Be(FieldInterface.Repeater);
    faqs.Fields.Should().NotBeNull();
    faqs.Fields!.Select(f => f.Name).Should().Equal("question", "answer", "category");

    var question = faqs.Fields!.Single(f => f.Name == "question");
    question.Interface.Should().Be(FieldInterface.Text);
    question.Required.Should().BeTrue();

    var category = faqs.Fields!.Single(f => f.Name == "category");
    category.Options!.Select(o => o.Value).Should().Equal("general", "billing");
}

[Fact]
public void Non_repeater_field_has_null_sub_fields()
{
    var meta = MetadataScanner.ScanTypes([typeof(RepeaterHost)]).Single();
    meta.Fields.Single(f => f.Name == "name").Fields.Should().BeNull();
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~MetadataScannerTests.Repeater_field_carries_nested_sub_field_schema"`
Expected: FAIL — `FieldMetadata` has no `Fields` member (compile error) or `Fields` is null.

- [ ] **Step 3a: Add `Fields` to the metadata record**

In `src/Struo.Domain/Metadata/Models/FieldMetadata.cs`, add after `Options`:

```csharp
    public IReadOnlyList<FieldOption>? Options { get; init; }

    /// <summary>Repeater child-object sub-field schema; null = not a Repeater.</summary>
    public IReadOnlyList<FieldMetadata>? Fields { get; init; }
    public bool IsSystem { get; init; }
```

- [ ] **Step 3b: Add the whitelist + recursion in the scanner**

In `src/Struo.Infrastructure/Metadata/MetadataScanner.cs`, add the whitelist near the other static sets:

```csharp
    // 7g+ slice 4: sub-field interfaces allowed inside a Repeater child object (the lean scalar set).
    // Everything else — RichText, File/Image/Files, multi-value, Json/KeyValue, nested Repeater,
    // Password/Hidden/Uuid/Divider — fail-fasts at scan.
    private static readonly HashSet<FieldInterface> RepeaterAllowedInterfaces =
    [
        FieldInterface.Text, FieldInterface.Textarea, FieldInterface.Markdown, FieldInterface.Code,
        FieldInterface.Slug, FieldInterface.Email, FieldInterface.Url, FieldInterface.Color,
        FieldInterface.Phone,
        FieldInterface.Number, FieldInterface.Slider, FieldInterface.Rating,
        FieldInterface.Boolean, FieldInterface.Checkbox,
        FieldInterface.Date, FieldInterface.Time, FieldInterface.DateTime,
        FieldInterface.Select, FieldInterface.Radio
    ];
```

In `BuildField`, after the MaxLength validation and before `return new FieldMetadata { … }`, resolve the
Repeater child schema:

```csharp
        IReadOnlyList<FieldMetadata>? childFields = null;
        if (attr.Interface == FieldInterface.Repeater)
            childFields = BuildRepeaterChildFields(prop);
```

and pass it into the returned record:

```csharp
            Options = options,
            Fields = childFields,
            IsSystem = false
```

Then add the recursion helper:

```csharp
    /// <summary>
    /// Resolves a Repeater's child-object sub-field schema by recursing into the element type of its
    /// <c>List&lt;TChild&gt;</c> property. Each sub-property carrying <c>[CmsField]</c> is scanned with
    /// the same <see cref="BuildField"/> logic; the sub-field interface must be in
    /// <see cref="RepeaterAllowedInterfaces"/> and must not be translatable. Fails fast on any
    /// violation (Task 2 covers the guards).
    /// </summary>
    private static IReadOnlyList<FieldMetadata> BuildRepeaterChildFields(PropertyInfo prop)
    {
        var t = prop.PropertyType;
        var isList = t.IsGenericType
            && typeof(IEnumerable).IsAssignableFrom(t)
            && t.GetGenericArguments().Length == 1;
        var childType = isList ? t.GetGenericArguments()[0] : null;
        if (childType is null || !childType.IsClass || childType == typeof(string))
            throw new MetadataException(
                $"Repeater field '{Camel(prop.Name)}' must be a List<T> of a child object type.");

        var subProps = childType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<CmsFieldAttribute>() is not null)
            .ToList();
        if (subProps.Count == 0)
            throw new MetadataException(
                $"Repeater field '{Camel(prop.Name)}' child type '{childType.Name}' must declare at least one [CmsField].");

        var fields = new List<FieldMetadata>();
        foreach (var sub in subProps)
        {
            var subAttr = sub.GetCustomAttribute<CmsFieldAttribute>()!;
            if (!RepeaterAllowedInterfaces.Contains(subAttr.Interface))
                throw new MetadataException(
                    $"Repeater field '{Camel(prop.Name)}' sub-field '{Camel(sub.Name)}' uses interface " +
                    $"'{subAttr.Interface}', which is not allowed inside a Repeater.");
            if (subAttr.Translatable)
                throw new MetadataException(
                    $"Repeater field '{Camel(prop.Name)}' sub-field '{Camel(sub.Name)}' cannot be translatable.");
            fields.Add(BuildField(sub, subAttr));
        }
        return fields;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~MetadataScannerTests.Repeater_field_carries_nested_sub_field_schema|FullyQualifiedName~MetadataScannerTests.Non_repeater_field_has_null_sub_fields"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Domain/Metadata/Models/FieldMetadata.cs src/Struo.Infrastructure/Metadata/MetadataScanner.cs tests/Struo.Tests/Metadata/MetadataScannerTests.cs
git commit -m "feat: scan Repeater child objects into a nested sub-field schema"
```

---

### Task 2: Scanner fail-fast guards for Repeater

**Files:**
- Modify: `src/Struo.Infrastructure/Metadata/MetadataScanner.cs` (add `Repeater` to `NonTranslatableJsonInterfaces`)
- Test: `tests/Struo.Tests/Metadata/MetadataValidationTests.cs`

**Interfaces:**
- Consumes: `MetadataScanner.RepeaterAllowedInterfaces`, `BuildRepeaterChildFields` (Task 1).

- [ ] **Step 1: Write the failing tests**

Add to `MetadataValidationTests.cs` (nested private test entities + facts):

```csharp
[CmsCollection("RepeaterNotAList")]
private sealed class RepeaterNotAList
{
    [CmsField(Interface = FieldInterface.Repeater)] public string Faqs { get; set; } = "";
}

private sealed class RichChild
{
    [CmsField(Interface = FieldInterface.RichText)] public string Body { get; set; } = "";
}
[CmsCollection("RepeaterDisallowedSub")]
private sealed class RepeaterDisallowedSub
{
    [CmsField(Interface = FieldInterface.Repeater)] public List<RichChild> Rows { get; set; } = new();
}

private sealed class NestedChild
{
    [CmsField(Interface = FieldInterface.Repeater)] public List<RichChild> Inner { get; set; } = new();
}
[CmsCollection("RepeaterNested")]
private sealed class RepeaterNested
{
    [CmsField(Interface = FieldInterface.Repeater)] public List<NestedChild> Rows { get; set; } = new();
}

private sealed class TranslatableChild
{
    [CmsField(Interface = FieldInterface.Text, Translatable = true)] public string T { get; set; } = "";
}
[CmsCollection("RepeaterTranslatableSub")]
private sealed class RepeaterTranslatableSub
{
    [CmsField(Interface = FieldInterface.Repeater)] public List<TranslatableChild> Rows { get; set; } = new();
}

private sealed class EmptyChild { public string Bare { get; set; } = ""; }
[CmsCollection("RepeaterEmptyChild")]
private sealed class RepeaterEmptyChild
{
    [CmsField(Interface = FieldInterface.Repeater)] public List<EmptyChild> Rows { get; set; } = new();
}

private sealed class OkChild
{
    [CmsField(Interface = FieldInterface.Text)] public string A { get; set; } = "";
}
[CmsCollection("RepeaterTranslatableParent")]
private sealed class RepeaterTranslatableParent
{
    [CmsField(Interface = FieldInterface.Repeater, Translatable = true)] public List<OkChild> Rows { get; set; } = new();
}

[Fact]
public void Throws_when_repeater_is_not_a_list() =>
    ((Action)(() => MetadataScanner.ScanTypes([typeof(RepeaterNotAList)])))
        .Should().Throw<MetadataException>().WithMessage("*must be a List<T>*");

[Fact]
public void Throws_when_repeater_sub_field_interface_not_allowed() =>
    ((Action)(() => MetadataScanner.ScanTypes([typeof(RepeaterDisallowedSub)])))
        .Should().Throw<MetadataException>().WithMessage("*not allowed inside a Repeater*");

[Fact]
public void Throws_when_repeater_nested_in_repeater() =>
    ((Action)(() => MetadataScanner.ScanTypes([typeof(RepeaterNested)])))
        .Should().Throw<MetadataException>().WithMessage("*not allowed inside a Repeater*");

[Fact]
public void Throws_when_repeater_sub_field_translatable() =>
    ((Action)(() => MetadataScanner.ScanTypes([typeof(RepeaterTranslatableSub)])))
        .Should().Throw<MetadataException>().WithMessage("*cannot be translatable*");

[Fact]
public void Throws_when_repeater_child_has_no_cms_fields() =>
    ((Action)(() => MetadataScanner.ScanTypes([typeof(RepeaterEmptyChild)])))
        .Should().Throw<MetadataException>().WithMessage("*must declare at least one [CmsField]*");

[Fact]
public void Throws_when_repeater_parent_translatable() =>
    ((Action)(() => MetadataScanner.ScanTypes([typeof(RepeaterTranslatableParent)])))
        .Should().Throw<MetadataException>().WithMessage("*cannot be translatable*");
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~MetadataValidationTests.Throws_when_repeater"`
Expected: 5 of 6 already pass from Task 1's helper guards; `Throws_when_repeater_parent_translatable` FAILS (the parent-translatable guard isn't wired yet).

- [ ] **Step 3: Add `Repeater` to the non-translatable JSON set**

In `MetadataScanner.cs`, extend `NonTranslatableJsonInterfaces`:

```csharp
    private static readonly HashSet<FieldInterface> NonTranslatableJsonInterfaces =
    [
        FieldInterface.MultiSelect, FieldInterface.CheckboxGroup, FieldInterface.Tags,
        FieldInterface.KeyValue, FieldInterface.Files, FieldInterface.Repeater
    ];
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~MetadataValidationTests.Throws_when_repeater"`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Infrastructure/Metadata/MetadataScanner.cs tests/Struo.Tests/Metadata/MetadataValidationTests.cs
git commit -m "feat: fail-fast on invalid Repeater declarations at scan"
```

---

### Task 3: Persistence — Repeater maps to an `IsJson` text column

**Files:**
- Modify: `src/Struo.Infrastructure/Persistence/SqlSugarClientFactory.cs`
- Test: `tests/Struo.Tests/Persistence/StructuredColumnMappingTests.cs`

**Interfaces:**
- Produces: a `Repeater` `List<TChild>` property → `IsJson` + `text` column, round-tripping via SqlSugar.

- [ ] **Step 1: Extend the DDL test**

In `StructuredColumnMappingTests.cs`, add a Repeater child type and column to `StructColTestEntity`, and
assert it round-trips as `text`:

```csharp
    // add at file scope (top-level, before the class) or as a nested public type:
    public sealed class FaqRowDdl
    {
        [CmsField(Interface = FieldInterface.Text)] public string Q { get; set; } = "";
        [CmsField(Interface = FieldInterface.Textarea)] public string A { get; set; } = "";
    }
```

Add the property to `StructColTestEntity`:

```csharp
        [CmsField(Label = "Faqs", Interface = FieldInterface.Repeater)]
        public List<FaqRowDdl> Faqs { get; set; } = new();
```

Extend the `foreach` column list and the round-trip assertions:

```csharp
            foreach (var col in new[] { "Meta", "Attributes", "Gallery", "Faqs" })
```

and after the `Gallery` assertions:

```csharp
            row.Faqs = new List<FaqRowDdl> { new() { Q = "問題", A = "答案" } };
            // (set before Insertable; re-run the insert/read below already covers it —
            //  see full edit note)
```

> Implementation note: set `Faqs` on the `row` initializer alongside `Meta`/`Attributes`/`Gallery`,
> then after the existing `read.Gallery.Should().Equal(...)` add:
> ```csharp
> read.Faqs.Should().ContainSingle();
> read.Faqs[0].Q.Should().Be("問題");
> read.Faqs[0].A.Should().Be("答案");
> ```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~StructuredColumnMappingTests"`
Expected: FAIL — `Faqs` column is not `text` (SqlSugar maps the unknown `List<>` to a default/`varchar`
type or the DataType assertion fails).

- [ ] **Step 3: Add `Repeater` to the JSON-column set**

In `SqlSugarClientFactory.cs`, extend `JsonColumnInterfaces` and update its comment:

```csharp
    private static readonly HashSet<FieldInterface> JsonColumnInterfaces =
    [
        FieldInterface.MultiSelect, FieldInterface.CheckboxGroup, FieldInterface.Tags,
        FieldInterface.KeyValue, FieldInterface.Files, FieldInterface.Repeater
    ];
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~StructuredColumnMappingTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Infrastructure/Persistence/SqlSugarClientFactory.cs tests/Struo.Tests/Persistence/StructuredColumnMappingTests.cs
git commit -m "feat: map Repeater (List<child>) to a JSON text column"
```

---

### Task 4: `ItemService` — Repeater normalization + per-row validation

**Files:**
- Modify: `src/Struo.Application/Query/ItemService.cs`
- Test: `tests/Struo.Tests/Query/ItemServiceRepeaterTests.cs` (new)

**Interfaces:**
- Consumes: `FieldMetadata.Fields` (Task 1), the `IsJson` column (Task 3).
- Produces: cleaned `List<TChild>` on the entity; `QueryException` (→400) on any violation.

- [ ] **Step 1: Write the failing tests**

Create `tests/Struo.Tests/Query/ItemServiceRepeaterTests.cs` (mirror `ItemServiceFilesFieldTests` wiring):

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

public class ItemServiceRepeaterTests : IDisposable
{
    public sealed class Faq
    {
        [CmsField(Label = "Question", Interface = FieldInterface.Text, Required = true)]
        public string Question { get; set; } = "";

        [CmsField(Label = "Answer", Interface = FieldInterface.Textarea)]
        public string Answer { get; set; } = "";

        [CmsField(Label = "Category", Interface = FieldInterface.Select, MaxLength = 10)]
        [CmsOptions("general:General", "billing:Billing")]
        public string? Category { get; set; }
    }

    [SugarTable("repeater_thing")]
    [CmsCollection("RepeaterThing")]
    public sealed class RepeaterThing : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [CmsField(Label = "FAQs", Interface = FieldInterface.Repeater)]
        public List<Faq> Faqs { get; set; } = new();

        [CmsField(Label = "Required FAQs", Interface = FieldInterface.Repeater, Required = true)]
        public List<Faq> RequiredFaqs { get; set; } = new();
    }

    private readonly SqliteTestDatabase _file = new();
    private readonly ItemService _svc;

    public ItemServiceRepeaterTests()
    {
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        db.CodeFirst.InitTables<RepeaterThing>();
        db.CodeFirst.InitTables<Language>();
        LanguageSeeder.SeedAsync(db).GetAwaiter().GetResult();

        var types = new[] { typeof(RepeaterThing) };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["repeaterthing"] = typeof(RepeaterThing),
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

    private static object OneRequired => new { question = "r", answer = "", category = (string?)null };

    [Fact]
    public async Task Faqs_round_trip_in_order()
    {
        var body = Body(new
        {
            faqs = new[]
            {
                new { question = "問題一", answer = "答案", category = "general" },
                new { question = "Q2", answer = "", category = (string?)null },
            },
            requiredFaqs = new[] { OneRequired },
        });
        var created = await _svc.CreateAsync("repeaterthing", body);
        var read = await _svc.GetAsync("repeaterthing", created["id"]!.ToString()!);

        var faqs = ((IEnumerable<ItemServiceRepeaterTests.Faq>)read!["faqs"]!).ToList();
        faqs.Select(f => f.Question).Should().Equal("問題一", "Q2");
        faqs[0].Category.Should().Be("general");
    }

    [Fact]
    public async Task Fully_blank_rows_are_dropped()
    {
        var body = Body(new
        {
            faqs = new[]
            {
                new { question = "keep", answer = "", category = (string?)null },
                new { question = "  ", answer = "", category = (string?)null }, // all-blank -> dropped
            },
            requiredFaqs = new[] { OneRequired },
        });
        var created = await _svc.CreateAsync("repeaterthing", body);
        var read = await _svc.GetAsync("repeaterthing", created["id"]!.ToString()!);

        ((IEnumerable<ItemServiceRepeaterTests.Faq>)read!["faqs"]!).Should().ContainSingle();
    }

    [Fact]
    public async Task Missing_required_sub_field_is_rejected()
    {
        var body = Body(new
        {
            faqs = new[] { new { question = "", answer = "has answer", category = (string?)null } },
            requiredFaqs = new[] { OneRequired },
        });
        var act = () => _svc.CreateAsync("repeaterthing", body);
        await act.Should().ThrowAsync<QueryException>().WithMessage("*'question' is required*");
    }

    [Fact]
    public async Task Out_of_options_sub_field_value_is_rejected()
    {
        var body = Body(new
        {
            faqs = new[] { new { question = "q", answer = "", category = "mars" } },
            requiredFaqs = new[] { OneRequired },
        });
        var act = () => _svc.CreateAsync("repeaterthing", body);
        await act.Should().ThrowAsync<QueryException>().WithMessage("*not in its options*");
    }

    [Fact]
    public async Task Over_length_sub_field_value_is_rejected()
    {
        var body = Body(new
        {
            faqs = new[] { new { question = "q", answer = "", category = new string('x', 11) } },
            requiredFaqs = new[] { OneRequired },
        });
        var act = () => _svc.CreateAsync("repeaterthing", body);
        await act.Should().ThrowAsync<QueryException>().WithMessage("*exceeds maximum length 10*");
    }

    [Fact]
    public async Task Required_repeater_empty_is_rejected()
    {
        var body = Body(new { faqs = new object[0] }); // requiredFaqs omitted
        var act = () => _svc.CreateAsync("repeaterthing", body);
        await act.Should().ThrowAsync<QueryException>().WithMessage("Field 'requiredFaqs' is required.");
    }

    [Fact]
    public async Task Non_object_element_is_rejected_as_bad_request()
    {
        var body = Body(new { faqs = new[] { "not-an-object" }, requiredFaqs = new[] { OneRequired } });
        var act = () => _svc.CreateAsync("repeaterthing", body);
        await act.Should().ThrowAsync<QueryException>(); // STJ JsonException -> QueryException (400)
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~ItemServiceRepeaterTests"`
Expected: FAIL — no Repeater validation branch yet (blank rows kept, required/options/maxlength not enforced).

- [ ] **Step 3: Add the Repeater normalization branch**

In `ItemService.cs`, add a reusable blank check + the branch at the end of `Deserialize`, right after the
`Files` normalization loop (before `return entity;`):

```csharp
        // Repeater fields (List<TChild>) live on the parent entity as an ordered list of typed child
        // objects. Drop fully-blank rows (every sub-field null or whitespace string), enforce each
        // kept row's sub-field Required / Select-Radio option membership / MaxLength, and enforce the
        // parent Required as a non-empty list. Non-translatable only. A wrong-shaped element already
        // 400s at the deserialize guard.
        foreach (var field in meta.Fields.Where(f => f.Interface == FieldInterface.Repeater && !f.Translatable))
        {
            if (field.Fields is null) continue;
            if (!d.FieldToProperty.TryGetValue(field.Name, out var prop)) continue;
            var pi = d.EntityType.GetProperty(prop);
            if (pi is not { CanWrite: true }) continue;
            if (pi.GetValue(entity) is not System.Collections.IEnumerable rowsEnum) continue;

            var elementType = pi.PropertyType.IsGenericType
                ? pi.PropertyType.GetGenericArguments()[0]
                : typeof(object);
            var cleaned = (System.Collections.IList)Activator.CreateInstance(pi.PropertyType)!;

            var rowIndex = 0;
            foreach (var row in rowsEnum)
            {
                rowIndex++;
                if (row is null) continue;

                // Blank-row detection: every sub-field is null or a whitespace-only string.
                var allBlank = true;
                foreach (var sub in field.Fields)
                {
                    var v = ReadProp(row, sub.Name);
                    if (v is null) continue;
                    if (v is string sv && string.IsNullOrWhiteSpace(sv)) continue;
                    allBlank = false; break;
                }
                if (allBlank) continue;

                foreach (var sub in field.Fields)
                {
                    var v = ReadProp(row, sub.Name);
                    var sv = v as string;

                    if (sub.Required && (v is null || (sv is not null && string.IsNullOrWhiteSpace(sv))))
                        throw new QueryException(
                            $"Repeater field '{field.Name}' row {rowIndex}: '{sub.Name}' is required.");

                    if (sv is not null && sub.Options is { Count: > 0 } &&
                        !string.IsNullOrEmpty(sv) &&
                        !sub.Options.Any(o => string.Equals(o.Value, sv, StringComparison.Ordinal)))
                        throw new QueryException(
                            $"Repeater field '{field.Name}' row {rowIndex}: '{sub.Name}' value '{sv}' is not in its options.");

                    if (sv is not null && sub.MaxLength is > 0 && sv.Length > sub.MaxLength.Value)
                        throw new QueryException(
                            $"Repeater field '{field.Name}' row {rowIndex}: '{sub.Name}' exceeds maximum length {sub.MaxLength}.");
                }
                cleaned.Add(row);
            }

            if (field.Required && cleaned.Count == 0)
                throw new QueryException($"Field '{field.Name}' is required.");
            pi.SetValue(entity, cleaned);
        }
```

> Note: `ReadProp` (existing, case-insensitive) resolves the camelCase sub-field name to the child
> POCO's PascalCase property. `Activator.CreateInstance(pi.PropertyType)` builds a fresh
> `List<TChild>`; `IList.Add(row)` re-adds the same element instances (drop-only, no mutation).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~ItemServiceRepeaterTests"`
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Application/Query/ItemService.cs tests/Struo.Tests/Query/ItemServiceRepeaterTests.cs
git commit -m "feat: normalize + validate Repeater rows on write"
```

---

### Task 5: Sample `FaqItem` + `Article.Faqs` + migration

**Files:**
- Create: `samples/Struo.Sample.Blog/FaqItem.cs`
- Modify: `samples/Struo.Sample.Blog/Article.cs`
- Create: `db/migrations/004-article-repeater-column.sql`
- Test: `tests/Struo.Tests/Metadata/MetadataScannerTests.cs` (Article scans `faqs`)

**Interfaces:**
- Consumes: the scanner (Task 1), persistence (Task 3).

- [ ] **Step 1: Write the failing test**

If the suite has an Article-scans test file, add a fact; otherwise add to `MetadataScannerTests.cs`:

```csharp
[Fact]
public void Article_sample_has_a_repeater_faqs_field()
{
    var meta = MetadataScanner.ScanTypes([typeof(Struo.Sample.Blog.Article)])
        .Single(c => c.Name == "article");
    var faqs = meta.Fields.Single(f => f.Name == "faqs");
    faqs.Interface.Should().Be(FieldInterface.Repeater);
    faqs.Fields!.Select(f => f.Name).Should().Contain("question");
}
```

> If `Struo.Tests` does not already reference `Struo.Sample.Blog`, check an existing sample-scan test
> (memory notes "+1 Article scanner" tests exist for prior slices, so the reference is present).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~Article_sample_has_a_repeater_faqs_field"`
Expected: FAIL — `article` has no `faqs` field.

- [ ] **Step 3a: Create the child POCO**

`samples/Struo.Sample.Blog/FaqItem.cs`:

```csharp
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Sample.Blog;

public sealed class FaqItem
{
    [CmsField(Label = "Question", Interface = FieldInterface.Text, Required = true)]
    public string Question { get; set; } = "";

    [CmsField(Label = "Answer", Interface = FieldInterface.Textarea)]
    public string Answer { get; set; } = "";

    [CmsField(Label = "Category", Interface = FieldInterface.Select)]
    [CmsOptions("general:General", "billing:Billing")]
    public string? Category { get; set; }
}
```

- [ ] **Step 3b: Add the field to `Article`**

In `samples/Struo.Sample.Blog/Article.cs`, after the `Gallery` field (Sort = 11):

```csharp
    [CmsField(Label = "FAQs", Interface = FieldInterface.Repeater, Sort = 12, Group = "Content")]
    public List<FaqItem> Faqs { get; set; } = [];
```

- [ ] **Step 3c: Create the migration script**

`db/migrations/004-article-repeater-column.sql`:

```sql
-- Phase 7g+ slice 4: Article.Faqs (Repeater -> IsJson text column).
-- InitTables adds tables, not columns, so live/drifted DBs need this explicitly.
ALTER TABLE articles ADD COLUMN IF NOT EXISTS faqs text NOT NULL DEFAULT '[]';
```

- [ ] **Step 4: Run test + full backend suite to verify green**

Run: `dotnet build -warnaserror && dotnet test tests/Struo.Tests`
Expected: PASS — all backend tests including the new Repeater/scanner/DDL/service tests.

- [ ] **Step 5: Commit**

```bash
git add samples/Struo.Sample.Blog/FaqItem.cs samples/Struo.Sample.Blog/Article.cs db/migrations/004-article-repeater-column.sql tests/Struo.Tests/Metadata/MetadataScannerTests.cs
git commit -m "feat: add sample Repeater (FAQs) field to Article + migration"
```

---

### Task 6: Frontend — `fields?` type + `repeaterDef` registry entry

**Files:**
- Modify: `frontend/src/types/schema.ts`
- Modify: `frontend/src/lib/fieldTypes/types.ts` (no change needed if `FieldMeta` is imported from schema — verify)
- Modify: `frontend/src/lib/fieldTypes/registry.ts`
- Test: `frontend/src/lib/fieldTypes/registry.test.ts`

**Interfaces:**
- Consumes: backend `fields` in the schema JSON (Task 1).
- Produces: `registry.repeater` = `repeaterDef` (parse/serialize/listColumn) + `RepeaterField` component (Task 7).

- [ ] **Step 1: Write the failing test**

Add to `registry.test.ts`:

```ts
import { registry } from './registry'

describe('repeaterDef', () => {
  const field = { name: 'faqs', interface: 'repeater',
    fields: [{ name: 'question', interface: 'text' }, { name: 'answer', interface: 'textarea' }] } as never

  it('defaults to an empty array', () => {
    expect(registry.repeater.defaultValue(field)).toEqual([])
  })

  it('parses non-arrays to []', () => {
    expect(registry.repeater.parse(null, field)).toEqual([])
    expect(registry.repeater.parse([{ question: 'q' }], field)).toEqual([{ question: 'q' }])
  })

  it('serialize drops fully-blank rows', () => {
    const rows = [{ question: 'keep', answer: '' }, { question: '  ', answer: '' }]
    expect(registry.repeater.serialize(rows, field)).toEqual([{ question: 'keep', answer: '' }])
  })

  it('list column shows the count', () => {
    expect(registry.repeater.listColumn?.format([{ x: 1 }, { x: 2 }], field)).toBe('2 items')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && pnpm vitest run src/lib/fieldTypes/registry.test.ts`
Expected: FAIL — `registry.repeater` is `readonlyDef` (no array default / count column).

- [ ] **Step 3a: Add `fields?` to the schema type**

In `frontend/src/types/schema.ts`, inside `FieldMeta` after `options`:

```ts
  options?: FieldOption[] | null
  fields?: FieldMeta[] | null // Repeater child sub-field schema; present only for interface === "repeater"
  isSystem: boolean
```

- [ ] **Step 3b: Add the `repeaterDef` and wire it into the registry**

In `frontend/src/lib/fieldTypes/registry.ts`, import the component and define `repeaterDef`:

```ts
import RepeaterField from '../../components/fields/RepeaterField.vue'

const asItemCount: ListColumn = {
  format: (v) => (Array.isArray(v) ? `${v.length} items` : String(v ?? '')),
}

function isBlankRow(row: unknown, subFields: FieldMeta[]): boolean {
  if (!row || typeof row !== 'object') return true
  const r = row as Record<string, unknown>
  return subFields.every((f) => {
    const val = r[f.name]
    return val === null || val === undefined || (typeof val === 'string' && val.trim() === '')
  })
}

const repeaterDef: FieldTypeDef = {
  component: RepeaterField,
  defaultValue: () => [],
  parse: (raw) => (Array.isArray(raw) ? raw : []),
  serialize: (v, f) => {
    if (!Array.isArray(v)) return []
    const subs = f.fields ?? []
    return v.filter((row) => !isBlankRow(row, subs))
  },
  listColumn: asItemCount,
}
```

Then replace the `repeater: readonlyDef` line:

```ts
  keyValue: keyValueDef,
  repeater: repeaterDef,
  files: filesDef,
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && pnpm vitest run src/lib/fieldTypes/registry.test.ts`
Expected: PASS. (Task 7 creates `RepeaterField.vue`; if the import fails now, create a minimal stub —
but do Task 7 next so the component is real.)

- [ ] **Step 5: Commit**

```bash
git add frontend/src/types/schema.ts frontend/src/lib/fieldTypes/registry.ts frontend/src/lib/fieldTypes/registry.test.ts
git commit -m "feat(frontend): repeaterDef registry entry + fields schema type"
```

---

### Task 7: Frontend — `RepeaterField.vue`

**Files:**
- Create: `frontend/src/components/fields/RepeaterField.vue`
- Test: `frontend/src/components/fields/RepeaterField.test.ts` (new)

**Interfaces:**
- Consumes: `getFieldType` (registry), `field.fields` (Task 6). The registry↔RepeaterField import cycle is
  runtime-only (getFieldType is called in render, not at module init) — safe, the standard recursive
  component pattern.
- Produces: emits `update:modelValue` with the row array (blank rows kept in-editor; the registry
  `serialize` drops them on save).

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/fields/RepeaterField.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import RepeaterField from './RepeaterField.vue'
import type { FieldMeta } from '../../types/schema'

function field(over: Partial<FieldMeta> & { interface: string }): FieldMeta {
  return { name: 'faqs', label: 'FAQs', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over } as FieldMeta
}
const repeater = field({
  interface: 'repeater',
  fields: [
    field({ name: 'question', label: 'Question', interface: 'text' }),
    field({ name: 'answer', label: 'Answer', interface: 'text' }),
  ] as FieldMeta[],
})
const opts = { global: { plugins: [PrimeVue] } }

describe('RepeaterField', () => {
  it('renders one card per row', () => {
    const w = mount(RepeaterField, {
      props: { field: repeater, modelValue: [{ question: 'q1', answer: 'a1' }, { question: 'q2', answer: 'a2' }] },
      ...opts,
    })
    expect(w.findAll('.repeater-row')).toHaveLength(2)
  })

  it('adds a blank row', async () => {
    const w = mount(RepeaterField, { props: { field: repeater, modelValue: [] }, ...opts })
    await w.get('.repeater-add').trigger('click')
    const emitted = w.emitted('update:modelValue')?.at(-1)?.[0] as unknown[]
    expect(emitted).toHaveLength(1)
    expect(w.findAll('.repeater-row')).toHaveLength(1)
  })

  it('removes a row immutably', async () => {
    const w = mount(RepeaterField, {
      props: { field: repeater, modelValue: [{ question: 'q1', answer: 'a1' }, { question: 'q2', answer: 'a2' }] },
      ...opts,
    })
    await w.findAll('.repeater-remove')[0].trigger('click')
    const emitted = w.emitted('update:modelValue')?.at(-1)?.[0] as Array<Record<string, unknown>>
    expect(emitted).toHaveLength(1)
    expect(emitted[0].question).toBe('q2')
  })

  it('moves a row up', async () => {
    const w = mount(RepeaterField, {
      props: { field: repeater, modelValue: [{ question: 'q1', answer: 'a1' }, { question: 'q2', answer: 'a2' }] },
      ...opts,
    })
    await w.findAll('.repeater-up')[1].trigger('click') // move second row up
    const emitted = w.emitted('update:modelValue')?.at(-1)?.[0] as Array<Record<string, unknown>>
    expect(emitted.map((r) => r.question)).toEqual(['q2', 'q1'])
  })

  it('edits a sub-field', async () => {
    const w = mount(RepeaterField, {
      props: { field: repeater, modelValue: [{ question: 'q1', answer: 'a1' }] }, ...opts,
    })
    const input = w.findAll('.repeater-row input')[0]
    await input.setValue('edited')
    const emitted = w.emitted('update:modelValue')?.at(-1)?.[0] as Array<Record<string, unknown>>
    expect(emitted[0].question).toBe('edited')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && pnpm vitest run src/components/fields/RepeaterField.test.ts`
Expected: FAIL — the component does not exist.

- [ ] **Step 3: Create the component**

`frontend/src/components/fields/RepeaterField.vue`:

```vue
<script setup lang="ts">
import { ref, watch } from 'vue'
import Button from 'primevue/button'
import { getFieldType } from '../../lib/fieldTypes/registry'
import type { FieldMeta } from '../../types/schema'

defineOptions({ name: 'RepeaterField' })

type Row = Record<string, unknown>

const props = defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: Row[]): void }>()

function toRows(v: unknown): Row[] {
  return Array.isArray(v) ? (v as Row[]).map((r) => ({ ...(r ?? {}) })) : []
}

const rows = ref<Row[]>(toRows(props.modelValue))

// Re-derive rows only when the incoming model differs from our working array, so our own
// emits (edit/add/remove/move) don't clobber in-progress edits (incl. blank rows).
watch(
  () => props.modelValue,
  (v) => {
    if (JSON.stringify(v ?? []) !== JSON.stringify(rows.value)) rows.value = toRows(v)
  },
)

function subFields(): FieldMeta[] {
  return props.field.fields ?? []
}

function emptyRow(): Row {
  const r: Row = {}
  for (const f of subFields()) r[f.name] = getFieldType(f.interface).defaultValue(f)
  return r
}

function commit(next: Row[]): void {
  rows.value = next
  emit('update:modelValue', next)
}
function add(): void { commit([...rows.value, emptyRow()]) }
function removeAt(i: number): void { commit(rows.value.filter((_, idx) => idx !== i)) }
function moveUp(i: number): void {
  if (i <= 0) return
  const next = [...rows.value]
  ;[next[i - 1], next[i]] = [next[i], next[i - 1]]
  commit(next)
}
function moveDown(i: number): void {
  if (i >= rows.value.length - 1) return
  const next = [...rows.value]
  ;[next[i + 1], next[i]] = [next[i], next[i + 1]]
  commit(next)
}
function setSub(i: number, name: string, v: unknown): void {
  commit(rows.value.map((r, idx) => (idx === i ? { ...r, [name]: v } : r)))
}
</script>

<template>
  <div class="repeater-field">
    <div v-for="(row, i) in rows" :key="i" class="repeater-row">
      <div class="repeater-row__fields">
        <div v-for="sub in subFields()" :key="sub.name" class="repeater-subfield">
          <label class="repeater-subfield__label">{{ sub.label }}</label>
          <component
            :is="getFieldType(sub.interface).component"
            :field="sub"
            :model-value="row[sub.name]"
            :disabled="disabled || sub.readOnly"
            @update:model-value="(v: unknown) => setSub(i, sub.name, v)"
          />
        </div>
      </div>
      <div class="repeater-row__controls">
        <Button class="repeater-up" icon="pi pi-arrow-up" text :disabled="disabled || i === 0" @click="moveUp(i)" />
        <Button class="repeater-down" icon="pi pi-arrow-down" text
          :disabled="disabled || i === rows.length - 1" @click="moveDown(i)" />
        <Button class="repeater-remove" icon="pi pi-times" text :disabled="disabled" @click="removeAt(i)" />
      </div>
    </div>
    <p v-if="!rows.length" class="repeater-field__empty">No items</p>
    <Button class="repeater-add" icon="pi pi-plus" label="Add" size="small" :disabled="disabled" @click="add" />
  </div>
</template>

<style scoped>
.repeater-field { display: flex; flex-direction: column; gap: 12px; align-items: flex-start; }
.repeater-row {
  display: flex; gap: 12px; width: 100%;
  border: 1px solid var(--surface-border, #333); border-radius: 6px; padding: 12px;
}
.repeater-row__fields { display: flex; flex-direction: column; gap: 8px; flex: 1; }
.repeater-subfield { display: flex; flex-direction: column; gap: 4px; }
.repeater-subfield__label { font-size: 0.85em; opacity: 0.8; }
.repeater-row__controls { display: flex; flex-direction: column; gap: 4px; }
.repeater-field__empty { font-style: italic; opacity: 0.7; }
</style>
```

- [ ] **Step 4: Run tests + type-check + build to verify green**

Run: `cd frontend && pnpm vitest run src/components/fields/RepeaterField.test.ts && pnpm vue-tsc --noEmit && pnpm build`
Expected: PASS — all RepeaterField tests, `vue-tsc` clean (this enforces registry exhaustiveness), build
succeeds (the registry↔RepeaterField cycle resolves at runtime).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/fields/RepeaterField.vue frontend/src/components/fields/RepeaterField.test.ts
git commit -m "feat(frontend): RepeaterField editor (recursive sub-field dispatch + reorder)"
```

---

### Task 8: Verification baseline, live gate, docs, merge

**Files:**
- Modify: `docs/ROADMAP.md`
- (memory update happens outside the repo)

- [ ] **Step 1: Full backend + frontend verification baseline**

Run:
```bash
dotnet build -warnaserror && dotnet test tests/Struo.Tests
cd frontend && pnpm test && pnpm vue-tsc --noEmit && pnpm build
```
Expected: backend all green (361 prior + the new scanner/validation/DDL/service/sample tests);
frontend all green (228 prior + registry repeaterDef + RepeaterField tests); `vue-tsc` clean; build
succeeds (pre-existing >500 kB chunk advisory only). Record the exact counts.

- [ ] **Step 2: Live gate on real Postgres**

Start the API against live Postgres (per prior slices' recipe). Apply the migration first:
```bash
docker exec -i <pg-container> psql -U <user> -d <db> < db/migrations/004-article-repeater-column.sql
```
Then, authenticated as the bootstrap super-admin, exercise (API-level, matching prior slices):
1. Create an `article` with `faqs = [{question:"常見問題一", answer:"答", category:"general"}, {question:"Q2", answer:"", category:null}]` + a default-locale translation → **201**.
2. `GET` it → `faqs` reads back **in order**, exact UTF-8 (`常見問題一`), `category:"general"` preserved.
3. PUT reordered + a trailing all-blank row → the blank row is dropped, order persisted.
4. PUT a row with `question:""` → **400** `…'question' is required.`
5. PUT a row with `category:"mars"` → **400** `…not in its options.`
Confirm no `varchar(1)` regression (full child JSON round-trips). Note any backend fix needed (prior
slices' "SQLite-green ≠ Postgres-correct" class).

- [ ] **Step 3: Update ROADMAP**

Add a Phase 7g+ slice-4 "done & live-verified" bullet + a post-slice-4 verification baseline (mirror the
slice-3 entries), flip the table row 7g+ Repeater from ⬜ planned to ✅ done, and update "Next up" (Phase
7g+ is now complete; next is Phase 8 GraphQL). Link the spec + this plan.

- [ ] **Step 4: Commit docs**

```bash
git add docs/ROADMAP.md
git commit -m "docs: Phase 7g+ slice 4 done + live-verified (Repeater)"
```

- [ ] **Step 5: Merge to main**

```bash
git checkout main
git merge --no-ff phase-7g-plus-repeater -m "Merge Phase 7g+ slice 4: Repeater (repeatable child objects)"
```

---

## Self-Review

**Spec coverage:**
- §2/§2.1 persistence (List<POCO> via IsJson text) → Task 3. ✅
- §2.2 child POCO + sample → Task 5. ✅
- §3 `FieldMetadata.Fields` + frontend `fields?` → Task 1 + Task 6. ✅
- §3.1 allowed-interface whitelist → Task 1 (`RepeaterAllowedInterfaces`) + Task 2 (disallowed fail-fast). ✅
- §4 scanner recursion + all fail-fasts (non-List, disallowed sub, nested, translatable parent/sub, empty child) → Task 1 + Task 2. ✅
- §5 Deserialize normalization (drop-blank, required, options, maxlength, parent-required, bad-shape 400) → Task 4. ✅
- §6 projection as-is → covered implicitly (no code change; Task 4 round-trip test asserts it). ✅
- §7 frontend registry + RepeaterField (cards, add/remove/move, recursive dispatch) → Task 6 + Task 7. ✅
- §8 testing (scanner/DDL/service/frontend/live) → Tasks 1–8. ✅
- §9 non-goals → enforced by the whitelist (Task 1/2). ✅

**Placeholder scan:** No TBD/TODO; every code step has concrete code. The one prose note in Task 3
Step 1 gives an exact edit recipe (set `Faqs` on the initializer + three assertion lines). ✅

**Type consistency:** `FieldMetadata.Fields` (Task 1) = frontend `FieldMeta.fields` (Task 6) =
`field.Fields` read in `ItemService` (Task 4) = `field.fields` in `RepeaterField` (Task 7).
`RepeaterAllowedInterfaces` / `NonTranslatableJsonInterfaces` / `JsonColumnInterfaces` names match their
files. `getFieldType`, `defaultValue`, `serialize`, `listColumn` match `types.ts`. ✅
