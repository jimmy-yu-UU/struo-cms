# Phase 7g+ (slice 3) — Multi-file `Files` field Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Light up the `Files` field interface (currently read-only) end-to-end — a `Files` field stores/returns an **ordered** list of file ids (`List<Guid>`), validated on write and edited in the SPA with a drag-reorderable, media-library-backed picker.

**Architecture:** `Files` is a `List<Guid>` property mapped to an `IsJson` + `text` column (the slice-1/2 `JsonColumnInterfaces` convention). All JSON we author is `System.Text.Json` (inbound whole-entity deserialize binds the guid-string array into `List<Guid>`; outbound projects the raw list); the only Newtonsoft touch is SqlSugarCore's internal `IsJson` column materialization, already load-bearing for the merged multi-value/KeyValue columns. The field mirrors the shipped scalar `File`/`Image` contract — store raw ids, project raw ids, frontend resolves each for display (one batched `filter[id][_in]` request). Frontend: one registry entry + a `FilesField.vue` (PrimeVue `OrderList` + an additive multi-select mode on the existing `MediaGrid`).

**Tech Stack:** .NET 10 / C# · SqlSugarCore 5.1.4.215 (`IsJson` columns) · System.Text.Json · xUnit + AwesomeAssertions · Vue 3 + PrimeVue 4.5 (`OrderList`, `Dialog`, `Button`, `InputText`) + Pinia · Vitest + `@vue/test-utils`.

## Global Constraints

- All DB access via SqlSugar ORM; zero vendor SQL (CLAUDE.md §17.4). `InitTables` is dev-only; live DBs get a migration script (InitTables adds tables, not columns).
- Domain references nothing external; persistence attributes live on entities only (§2). **No new NuGet/pnpm packages** in this slice (PrimeVue `OrderList` ships with the installed `primevue@4.5`).
- JSON handling we author uses **System.Text.Json** (user directive: prefer .NET built-in; Newtonsoft only where unavoidable — here, only SqlSugar's internal `IsJson` path).
- Outbound JSON is camelCase for DTO property names; a `Files` value is a JSON array of id strings, order preserved.
- `Files` is **non-translatable, non-sortable, non-searchable**; `MaxLength` (7g.5) does **not** apply.
- `Files`'s CLR type is `List<Guid>`. No existence validation on write (mirrors scalar `File`/`Image`).
- TDD: failing test first; frequent commits. Backend baseline **353** tests, frontend **217** — both must stay green plus the new tests.
- Frontend `FieldInterface` union already contains `files` — no union change; `vue-tsc` enforces registry exhaustiveness.

---

### Task 1: `Files` → JSON column convention + DDL/round-trip test

**Files:**
- Modify: `src/Struo.Infrastructure/Persistence/SqlSugarClientFactory.cs`
- Test: `tests/Struo.Tests/Persistence/StructuredColumnMappingTests.cs`

**Interfaces:**
- Consumes: `CmsFieldAttribute.Interface`, `FieldInterface.Files`.
- Produces: a `[CmsField(Interface=Files)]` `List<Guid>` property maps to an `IsJson` + `text` column; `List<Guid>` round-trips through SqlSugar in insertion order. The factory's `JsonColumnInterfaces` set gains `Files`.

- [ ] **Step 1: Add a `Files` field to the existing DDL test entity + assertions**

In `tests/Struo.Tests/Persistence/StructuredColumnMappingTests.cs`, add a `Gallery` property to `StructColTestEntity` (after `Attributes`, line 31):

```csharp
        [CmsField(Label = "Gallery", Interface = FieldInterface.Files)]
        public List<Guid> Gallery { get; set; } = new();
```

Extend the column-type loop to include `Gallery` (line 46):

```csharp
            foreach (var col in new[] { "Meta", "Attributes", "Gallery" })
```

Add an ordered round-trip assertion. Set the `Gallery` on the inserted row (inside the object initializer, after `Attributes`):

```csharp
                Gallery = new List<Guid>
                {
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Guid.Parse("22222222-2222-2222-2222-222222222222"),
                },
```

And after the existing `read.Attributes` assertion (line 63), assert order is preserved:

```csharp
            read.Gallery.Should().Equal(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("22222222-2222-2222-2222-222222222222"));
```

(`.Equal` asserts sequence equality **in order** — the load-bearing property for a gallery.)

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~StructuredColumnMappingTests"`
Expected: FAIL — `Files` is not yet in the JSON-column set, so `Gallery` (a `List<Guid>`) is not mapped to a JSON column; InitTables/insert throws or the read list is empty/wrong.

- [ ] **Step 3: Add `Files` to the JSON-column set**

In `src/Struo.Infrastructure/Persistence/SqlSugarClientFactory.cs`, add `FieldInterface.Files` to the `JsonColumnInterfaces` collection expression (the set currently holding `MultiSelect, CheckboxGroup, Tags, KeyValue`):

```csharp
    private static readonly HashSet<FieldInterface> JsonColumnInterfaces =
    [
        FieldInterface.MultiSelect, FieldInterface.CheckboxGroup, FieldInterface.Tags,
        FieldInterface.KeyValue, FieldInterface.Files
    ];
```

(No use-site change — the existing `mvField`/`JsonColumnInterfaces.Contains` branch already sets `column.IsJson = true; column.DataType = "text";`.)

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~StructuredColumnMappingTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Infrastructure/Persistence/SqlSugarClientFactory.cs tests/Struo.Tests/Persistence/StructuredColumnMappingTests.cs
git commit -m "feat: map Files (List<Guid>) to a JSON text column"
```

---

### Task 2: `Files` normalization + validation branch in `ItemService`

**Files:**
- Modify: `src/Struo.Application/Query/ItemService.cs` (`Deserialize` — new `Files` branch before `return entity;`)
- Test: `tests/Struo.Tests/Query/ItemServiceFilesFieldTests.cs`

**Interfaces:**
- Consumes: `FieldMetadata.{Interface,Name,Translatable,Required}`, `FieldInterface.Files`, `EntityDescriptor.FieldToProperty`, the existing `body.Deserialize` `JsonException → QueryException` guard.
- Produces: on both write paths, a `Files` field's `List<Guid>` is cleaned (drop `Guid.Empty`, de-dup keep-first preserving order); `Required` empty → 400 `Field '{name}' is required.`; a non-guid array element → 400 (via the existing guard). A valid list round-trips in order.

- [ ] **Step 1: Write the failing tests**

Create `tests/Struo.Tests/Query/ItemServiceFilesFieldTests.cs`:

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

public class ItemServiceFilesFieldTests : IDisposable
{
    [SugarTable("files_thing")]
    [CmsCollection("FilesThing")]
    public sealed class FilesThing : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [CmsField(Label = "Gallery", Interface = FieldInterface.Files)]
        public List<Guid> Gallery { get; set; } = new();

        [CmsField(Label = "Required Gallery", Interface = FieldInterface.Files, Required = true)]
        public List<Guid> RequiredGallery { get; set; } = new();
    }

    private readonly SqliteTestDatabase _file = new();
    private readonly ItemService _svc;

    public ItemServiceFilesFieldTests()
    {
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        db.CodeFirst.InitTables<FilesThing>();
        db.CodeFirst.InitTables<Language>();
        LanguageSeeder.SeedAsync(db).GetAwaiter().GetResult();

        var types = new[] { typeof(FilesThing) };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["filesthing"] = typeof(FilesThing),
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

    private static readonly string A = "11111111-1111-1111-1111-111111111111";
    private static readonly string B = "22222222-2222-2222-2222-222222222222";
    private static readonly string C = "33333333-3333-3333-3333-333333333333";

    [Fact]
    public async Task Gallery_round_trips_in_order()
    {
        var body = Body(new { gallery = new[] { A, B, C }, requiredGallery = new[] { A } });
        var created = await _svc.CreateAsync("filesthing", body);
        var read = await _svc.GetAsync("filesthing", created["id"]!.ToString()!);

        var gallery = ((IEnumerable<Guid>)read!["gallery"]!).Select(g => g.ToString()).ToList();
        gallery.Should().Equal(A, B, C);
    }

    [Fact]
    public async Task Duplicate_ids_are_deduplicated_keeping_first()
    {
        var body = Body(new { gallery = new[] { A, A, B }, requiredGallery = new[] { A } });
        var created = await _svc.CreateAsync("filesthing", body);
        var read = await _svc.GetAsync("filesthing", created["id"]!.ToString()!);

        var gallery = ((IEnumerable<Guid>)read!["gallery"]!).Select(g => g.ToString()).ToList();
        gallery.Should().Equal(A, B);
    }

    [Fact]
    public async Task Empty_guid_entries_are_dropped()
    {
        var body = Body(new { gallery = new[] { A, Guid.Empty.ToString(), B }, requiredGallery = new[] { A } });
        var created = await _svc.CreateAsync("filesthing", body);
        var read = await _svc.GetAsync("filesthing", created["id"]!.ToString()!);

        var gallery = ((IEnumerable<Guid>)read!["gallery"]!).Select(g => g.ToString()).ToList();
        gallery.Should().Equal(A, B);
    }

    [Fact]
    public async Task Required_empty_gallery_is_rejected()
    {
        var body = Body(new { gallery = new[] { A } }); // requiredGallery omitted
        var act = () => _svc.CreateAsync("filesthing", body);
        await act.Should().ThrowAsync<QueryException>()
            .WithMessage("Field 'requiredGallery' is required.");
    }

    [Fact]
    public async Task Non_guid_element_is_rejected_as_bad_request()
    {
        var body = Body(new { gallery = new[] { "not-a-guid" }, requiredGallery = new[] { A } });
        var act = () => _svc.CreateAsync("filesthing", body);
        await act.Should().ThrowAsync<QueryException>(); // STJ JsonException -> QueryException (400), not 500
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~ItemServiceFilesFieldTests"`
Expected: FAIL — no `Files` branch yet: dedup/empty-drop/required cases do not hold. (`Non_guid_element_is_rejected_as_bad_request` may already pass via the existing deserialize guard — that is fine; the other four must fail.)

- [ ] **Step 3: Implement the `Files` normalization branch**

In `src/Struo.Application/Query/ItemService.cs`, in `Deserialize`, immediately after the `KeyValue` `foreach` block (the one ending just before `return entity;`, ~line 815) and before `return entity;`, insert:

```csharp
        // Files fields (List<Guid>) live on the parent entity as an ordered list of file ids. Drop
        // Guid.Empty, de-duplicate keeping first (preserves order), and enforce Required as a non-empty
        // list. No existence check — mirrors the scalar File/Image field (a deleted file degrades to a
        // raw-id fallback in the picker). A non-guid array element already 400s at the deserialize
        // guard. Non-translatable only.
        foreach (var field in meta.Fields.Where(f => f.Interface == FieldInterface.Files && !f.Translatable))
        {
            if (!d.FieldToProperty.TryGetValue(field.Name, out var prop)) continue;
            var pi = d.EntityType.GetProperty(prop);
            if (pi is not { CanWrite: true }) continue;

            var ids = (pi.GetValue(entity) as IEnumerable<Guid>) ?? [];
            var seen = new HashSet<Guid>();
            var cleaned = new List<Guid>();
            foreach (var id in ids)
            {
                if (id == Guid.Empty) continue;
                if (seen.Add(id)) cleaned.Add(id); // de-dup, keep first (preserves order)
            }
            if (field.Required && cleaned.Count == 0)
                throw new QueryException($"Field '{field.Name}' is required.");
            pi.SetValue(entity, cleaned);
        }
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~ItemServiceFilesFieldTests"`
Expected: PASS (5/5).

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Application/Query/ItemService.cs tests/Struo.Tests/Query/ItemServiceFilesFieldTests.cs
git commit -m "feat: normalize + validate Files fields (dedup/empty-drop/required) on write"
```

---

### Task 3: Scanner fail-fast on `Translatable` JSON-column interfaces (review M4)

**Files:**
- Modify: `src/Struo.Infrastructure/Metadata/MetadataScanner.cs`
- Test: `tests/Struo.Tests/Metadata/MetadataScannerTests.cs`

**Interfaces:**
- Consumes: `FieldMetadata.{Translatable,Interface,Name}`, `FieldInterface.{MultiSelect,CheckboxGroup,Tags,KeyValue,Files}`.
- Produces: `ScanTypes`/`Scan` throws `MetadataException` at startup if any `MultiSelect`/`CheckboxGroup`/`Tags`/`KeyValue`/`Files` field is declared `Translatable`. `Json` is intentionally **not** caught.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Struo.Tests/Metadata/MetadataScannerTests.cs` (inside its existing test class; add `using Struo.Domain.Auditing;` and `using SqlSugar;` if absent — match neighbouring tests):

```csharp
    [SugarTable("bad_translatable_files")]
    [CmsCollection("BadTranslatableFiles")]
    private sealed class BadTranslatableFiles : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [CmsField(Label = "Gallery", Interface = FieldInterface.Files, Translatable = true)]
        public List<Guid> Gallery { get; set; } = new();
    }

    [SugarTable("ok_translatable_json")]
    [CmsCollection("OkTranslatableJson")]
    private sealed class OkTranslatableJson : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        // Json is intentionally NOT caught by the guard (a string that could be translatable later).
        [CmsField(Label = "Attributes", Interface = FieldInterface.Json, Translatable = true)]
        public string? Attributes { get; set; }
    }

    [Fact]
    public void Translatable_files_field_fails_fast()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(BadTranslatableFiles)]);
        act.Should().Throw<MetadataException>()
            .WithMessage("*Gallery*cannot be translatable*");
    }

    [Fact]
    public void Translatable_json_field_is_allowed()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(OkTranslatableJson)]);
        act.Should().NotThrow();
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~MetadataScannerTests.Translatable"`
Expected: FAIL — `Translatable_files_field_fails_fast` does not throw (no guard yet). (`Translatable_json_field_is_allowed` already passes.)

- [ ] **Step 3: Add the guarded set + validation loop**

In `src/Struo.Infrastructure/Metadata/MetadataScanner.cs`, add a set next to the existing `ShortStringInterfaces` (after line 29):

```csharp
    // 7g+ slice 3: JSON-column interfaces that live on the parent entity and cannot be translatable
    // (translating a structured aggregate is out of scope). A [CmsField(Translatable=true)] on any of
    // these fail-fasts at scan. Json is intentionally excluded (a string that could be translatable
    // in a later slice; non-translatable by convention here, but not fail-fasted).
    private static readonly HashSet<FieldInterface> NonTranslatableJsonInterfaces =
    [
        FieldInterface.MultiSelect, FieldInterface.CheckboxGroup, FieldInterface.Tags,
        FieldInterface.KeyValue, FieldInterface.Files
    ];
```

In `BuildCollection`, immediately after the translatable-fields merge (`foreach (var tf in translatableFields) fields.Add((++order, tf));`, line 142) and before the `var ordered = …` line, insert:

```csharp
        foreach (var (_, field) in fields)
            if (field.Translatable && NonTranslatableJsonInterfaces.Contains(field.Interface))
                throw new MetadataException(
                    $"Field '{field.Name}' uses interface '{field.Interface}', which cannot be translatable.");
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~MetadataScannerTests.Translatable"`
Expected: PASS (2/2).

- [ ] **Step 5: Run the full scanner suite to confirm no regression**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~MetadataScannerTests"`
Expected: PASS (existing collections declare no `Translatable` multi-value/KeyValue/Files fields, so none newly trip the guard).

- [ ] **Step 6: Commit**

```bash
git add src/Struo.Infrastructure/Metadata/MetadataScanner.cs tests/Struo.Tests/Metadata/MetadataScannerTests.cs
git commit -m "feat: fail-fast on Translatable multi-value/KeyValue/Files fields at scan (review M4)"
```

---

### Task 4: Sample `Gallery` field on `Article` + scanner assertion

**Files:**
- Modify: `samples/Struo.Sample.Blog/Article.cs`
- Test: `tests/Struo.Tests/Metadata/MetadataScannerTests.cs`

**Interfaces:**
- Consumes: the JSON-column convention (Task 1) and the write/validation path (Task 2).
- Produces: `Article` exposes `gallery` (`Files`, `List<Guid>`); non-required, non-sortable, non-searchable.

- [ ] **Step 1: Write the failing scanner test**

Add to `tests/Struo.Tests/Metadata/MetadataScannerTests.cs` (inside its existing test class):

```csharp
    [Fact]
    public void Article_exposes_files_field_with_correct_metadata()
    {
        var collections = MetadataScanner.ScanTypes([typeof(Struo.Sample.Blog.Article)]);
        var article = collections.Single(c => string.Equals(c.Name, "article", StringComparison.OrdinalIgnoreCase));

        var gallery = article.Fields.Single(f => f.Name == "gallery");
        gallery.Interface.Should().Be(FieldInterface.Files);
        gallery.Sortable.Should().BeFalse();
        gallery.Searchable.Should().BeFalse();
        gallery.Translatable.Should().BeFalse();
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~MetadataScannerTests.Article_exposes_files_field"`
Expected: FAIL — `Article` has no `gallery` field yet.

- [ ] **Step 3: Add the sample field**

In `samples/Struo.Sample.Blog/Article.cs`, add one property after `Meta` (line 49), before the `// --- relations ---` block (line 51):

```csharp
    [CmsField(Label = "Gallery", Interface = FieldInterface.Files, Sort = 11, Group = "Content")]
    public List<Guid> Gallery { get; set; } = [];
```

- [ ] **Step 4: Run the full backend suite to verify no regressions**

Run: `dotnet test tests/Struo.Tests`
Expected: PASS — the new scanner test passes and all existing tests stay green (existing `Article` creates omit `gallery` → it stays an empty list; it is not required, so no throw; the extra projected key does not break key-presence assertions).

- [ ] **Step 5: Commit**

```bash
git add samples/Struo.Sample.Blog/Article.cs tests/Struo.Tests/Metadata/MetadataScannerTests.cs
git commit -m "feat: add sample Files (gallery) field to Article"
```

---

### Task 5: `MediaGrid` additive multi-select mode

**Files:**
- Modify: `frontend/src/components/media/MediaGrid.vue`
- Modify: `frontend/src/components/media/MediaGrid.test.ts`

**Interfaces:**
- Consumes: `FileRow`.
- Produces: `MediaGrid` gains an additive `multiple?: boolean` + `selectedIds?: string[]` props and a `toggle` emit. In multiple mode a tile click emits `toggle` with its id (dialog stays open) and a tile is marked `is-selected` when its id is in `selectedIds`. The existing single-select mode (`selectable` + `selectedId` + `select`) is unchanged.

- [ ] **Step 1: Extend the MediaGrid tests**

In `frontend/src/components/media/MediaGrid.test.ts`, append inside the `describe('MediaGrid', …)` block:

```ts
  it('emits toggle with the id on click in multiple mode', async () => {
    const w = mount(MediaGrid, { props: { files, multiple: true, selectedIds: [] } })
    await w.findAll('.media-tile')[0].trigger('click')
    expect(w.emitted('toggle')?.[0]).toEqual(['f1'])
    expect(w.emitted('select')).toBeUndefined()
  })

  it('marks tiles whose id is in selectedIds (multiple mode)', () => {
    const w = mount(MediaGrid, { props: { files, multiple: true, selectedIds: ['f2'] } })
    expect(w.findAll('.media-tile')[1].classes()).toContain('is-selected')
    expect(w.findAll('.media-tile')[0].classes()).not.toContain('is-selected')
  })
```

- [ ] **Step 2: Run to verify they fail**

Run: `cd frontend && pnpm test -- media/MediaGrid`
Expected: FAIL — no `multiple`/`selectedIds`/`toggle` support yet (no `toggle` emitted; tiles not marked in multiple mode).

- [ ] **Step 3: Implement the multi-select mode**

Replace the `<script setup>` block of `frontend/src/components/media/MediaGrid.vue` (lines 1-10) with:

```vue
<script setup lang="ts">
import FileThumbnail, { type FileRow } from './FileThumbnail.vue'

const props = defineProps<{
  files: FileRow[]
  selectable?: boolean
  selectedId?: string | null
  multiple?: boolean
  selectedIds?: string[]
}>()
const emit = defineEmits<{ (e: 'select', id: string): void; (e: 'toggle', id: string): void }>()

function onClick(id: string): void {
  if (props.multiple) { emit('toggle', id); return }
  if (props.selectable) emit('select', id)
}
function isSelected(id: string): boolean {
  return props.multiple
    ? !!props.selectedIds?.includes(id)
    : props.selectable === true && props.selectedId === id
}
</script>
```

Update the tile's `:class` binding (line 19) to use `isSelected`:

```vue
      :class="{ 'is-selected': isSelected(f.id) }"
```

- [ ] **Step 4: Run to verify they pass**

Run: `cd frontend && pnpm test -- media/MediaGrid`
Expected: PASS (existing single-select tests + the two new multiple-mode tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/media/MediaGrid.vue frontend/src/components/media/MediaGrid.test.ts
git commit -m "feat(frontend): additive multi-select mode on MediaGrid"
```

---

### Task 6: `FilesField.vue` component

**Files:**
- Create: `frontend/src/components/fields/FilesField.vue`
- Test: `frontend/src/components/fields/FilesField.test.ts`

**Interfaces:**
- Consumes: PrimeVue `OrderList`/`Dialog`/`Button`/`InputText`, `MediaGrid` (multi-select mode from Task 5), `FileThumbnail`/`FileRow`, `itemsApi.list`, `useLanguageStore`.
- Produces: a component with the uniform contract (`{ field, modelValue, disabled }` props; `update:modelValue` emit with a `string[]` of ids in order). Resolves ids in one batched `filter[id][_in]` request; missing ids render a raw-id fallback row. Exposes `openDialog`, `toggle`, `removeAt`, `onReorder`, `currentIds` for testing/UX.

- [ ] **Step 1: Write the failing component tests**

Create `frontend/src/components/fields/FilesField.test.ts`:

```ts
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import FilesField from './FilesField.vue'
import { itemsApi } from '../../api/itemsApi'
import { useLanguageStore } from '../../stores/languageStore'
import type { FieldMeta } from '../../types/schema'

function field(over: Partial<FieldMeta> & { interface: string }): FieldMeta {
  return { name: 'gallery', label: 'Gallery', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over } as FieldMeta
}

const f1 = { id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1 }
const f2 = { id: 'f2', fileName: 'b.png', contentType: 'image/png', size: 2 }
const f3 = { id: 'f3', fileName: 'c.png', contentType: 'image/png', size: 3 }

const stubs = { OrderList: true, Dialog: true, Button: true, InputText: true, MediaGrid: true, FileThumbnail: true }

function setupStores() {
  const lang = useLanguageStore()
  lang.languages = [{ code: 'en', name: 'English', isDefault: true }]
}

describe('FilesField', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
  })

  it('resolves the model ids in order on mount', async () => {
    setupStores()
    // Return out of model order to prove the component re-orders to the model.
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [f2, f1], total: 2 })
    const w = mount(FilesField, { props: { field: field({ interface: 'files' }), modelValue: ['f1', 'f2'] }, global: { stubs } })
    await flushPromises()
    expect((w.vm as unknown as { currentIds: () => string[] }).currentIds()).toEqual(['f1', 'f2'])
  })

  it('keeps a missing id as a raw-id fallback row', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [f1], total: 1 }) // f2 gone
    const w = mount(FilesField, { props: { field: field({ interface: 'files' }), modelValue: ['f1', 'f2'] }, global: { stubs } })
    await flushPromises()
    expect((w.vm as unknown as { currentIds: () => string[] }).currentIds()).toEqual(['f1', 'f2'])
  })

  it('reorder emits the new id order', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [f1, f2], total: 2 })
    const w = mount(FilesField, { props: { field: field({ interface: 'files' }), modelValue: ['f1', 'f2'] }, global: { stubs } })
    await flushPromises()
    ;(w.vm as unknown as { onReorder: (v: unknown[]) => void }).onReorder([f2, f1])
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([['f2', 'f1']])
  })

  it('removeAt emits the shortened array immutably', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [f1, f2], total: 2 })
    const w = mount(FilesField, { props: { field: field({ interface: 'files' }), modelValue: ['f1', 'f2'] }, global: { stubs } })
    await flushPromises()
    ;(w.vm as unknown as { removeAt: (i: number) => void }).removeAt(0)
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([['f2']])
  })

  it('toggling an unselected file in the picker appends it last', async () => {
    setupStores()
    const listSpy = vi.spyOn(itemsApi, 'list')
    listSpy.mockResolvedValueOnce({ data: [f1], total: 1 })   // mount resolve
    const w = mount(FilesField, { props: { field: field({ interface: 'files' }), modelValue: ['f1'] }, global: { stubs } })
    await flushPromises()
    listSpy.mockResolvedValueOnce({ data: [f1, f3], total: 2 }) // dialog options
    await (w.vm as unknown as { openDialog: () => Promise<void> }).openDialog()
    await flushPromises()
    ;(w.vm as unknown as { toggle: (id: string) => void }).toggle('f3')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([['f1', 'f3']])
  })

  it('toggling an already-selected file removes it', async () => {
    setupStores()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [f1, f2], total: 2 })
    const w = mount(FilesField, { props: { field: field({ interface: 'files' }), modelValue: ['f1', 'f2'] }, global: { stubs } })
    await flushPromises()
    ;(w.vm as unknown as { toggle: (id: string) => void }).toggle('f1')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([['f2']])
  })
})
```

- [ ] **Step 2: Run to verify they fail**

Run: `cd frontend && pnpm test -- fields/FilesField`
Expected: FAIL — the component does not exist.

- [ ] **Step 3: Create `FilesField.vue`**

Create `frontend/src/components/fields/FilesField.vue`:

```vue
<script setup lang="ts">
import { ref, watch, onMounted } from 'vue'
import Dialog from 'primevue/dialog'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import OrderList from 'primevue/orderlist'
import MediaGrid from '../media/MediaGrid.vue'
import FileThumbnail, { type FileRow } from '../media/FileThumbnail.vue'
import { itemsApi } from '../../api/itemsApi'
import { useLanguageStore } from '../../stores/languageStore'
import type { FieldMeta } from '../../types/schema'

defineOptions({ name: 'FilesField' })

const props = defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: string[]): void }>()

const langStore = useLanguageStore()

type Row = FileRow & { missing?: boolean }

function toIds(v: unknown): string[] {
  return Array.isArray(v) ? v.map(String) : []
}

const rows = ref<Row[]>([])
const dialogOpen = ref(false)
const options = ref<FileRow[]>([])
const search = ref('')
const loadError = ref('')

function currentIds(): string[] {
  return rows.value.map((r) => r.id)
}

async function resolve(ids: string[]): Promise<void> {
  if (ids.length === 0) { rows.value = []; return }
  let found: FileRow[] = []
  try {
    const res = await itemsApi.list('file', {
      page: 0,
      rows: ids.length,
      filter: { id: { op: '_in', value: ids.join(',') } },
      locale: langStore.defaultCode || undefined,
    })
    found = res.data as unknown as FileRow[]
  } catch {
    found = []
  }
  const byId = new Map(found.map((f) => [f.id, f]))
  // Preserve model order; a missing id becomes a raw-id fallback row.
  rows.value = ids.map(
    (id) => byId.get(id) ?? ({ id, fileName: id, contentType: '', size: 0, missing: true } as Row),
  )
}

// Re-resolve only when the incoming model differs from our current order, so our own
// emits (reorder/add/remove) don't clobber the working rows.
watch(
  () => props.modelValue,
  (v) => {
    const incoming = toIds(v)
    if (JSON.stringify(incoming) !== JSON.stringify(currentIds())) resolve(incoming)
  },
)
onMounted(() => resolve(toIds(props.modelValue)))

function commit(next: Row[]): void {
  rows.value = next
  emit('update:modelValue', next.map((r) => r.id))
}
function onReorder(next: Row[]): void {
  commit([...next])
}
function removeAt(i: number): void {
  commit(rows.value.filter((_, idx) => idx !== i))
}

async function openDialog(): Promise<void> {
  dialogOpen.value = true
  await loadOptions()
}
async function loadOptions(): Promise<void> {
  loadError.value = ''
  try {
    const res = await itemsApi.list('file', {
      page: 0,
      rows: 50,
      search: search.value || undefined,
      locale: langStore.defaultCode || undefined,
    })
    options.value = res.data as unknown as FileRow[]
  } catch (e) {
    loadError.value = e instanceof Error ? e.message : 'Failed to load files.'
  }
}
function toggle(id: string): void {
  const existing = rows.value.find((r) => r.id === id)
  if (existing) {
    commit(rows.value.filter((r) => r.id !== id))
    return
  }
  const file = options.value.find((f) => f.id === id)
  if (file) commit([...rows.value, file]) // append -> new files go last
}

watch(search, loadOptions)
defineExpose({ openDialog, toggle, removeAt, onReorder, currentIds, resolve })
</script>

<template>
  <div class="files-field">
    <OrderList
      v-if="rows.length"
      :model-value="rows"
      data-key="id"
      :disabled="disabled"
      @update:model-value="(v: Row[]) => onReorder(v)"
    >
      <template #item="{ item, index }">
        <div class="files-row">
          <FileThumbnail v-if="!item.missing" :file="item" />
          <span class="files-row__name">{{ item.missing ? item.id : item.fileName }}</span>
          <Button class="files-remove" icon="pi pi-times" text :disabled="disabled" @click="removeAt(index)" />
        </div>
      </template>
    </OrderList>
    <p v-else class="files-field__empty">No files selected</p>

    <Button class="files-add" label="Select files" size="small" :disabled="disabled" @click="openDialog" />

    <Dialog v-model:visible="dialogOpen" modal header="Select files" :style="{ width: '60rem' }">
      <p v-if="loadError" class="error" role="alert">{{ loadError }}</p>
      <InputText v-model="search" placeholder="Search files…" class="files-field__search" />
      <MediaGrid :files="options" multiple :selected-ids="currentIds()" @toggle="toggle" />
      <template #footer>
        <Button label="Done" @click="dialogOpen = false" />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.files-field {
  display: flex;
  flex-direction: column;
  gap: 12px;
  align-items: flex-start;
}
.files-row {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
}
.files-row :deep(.file-thumb) {
  width: 48px;
  height: 48px;
  flex: none;
}
.files-row__name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.files-field__empty {
  color: var(--text);
  font-style: italic;
}
.files-field__search {
  display: block;
  margin: 8px 0 12px;
  width: 100%;
}
</style>
```

- [ ] **Step 4: Run to verify they pass**

Run: `cd frontend && pnpm test -- fields/FilesField`
Expected: PASS (6/6).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/fields/FilesField.vue frontend/src/components/fields/FilesField.test.ts
git commit -m "feat(frontend): FilesField editor (OrderList reorder + media multi-select)"
```

---

### Task 7: Wire `files` into the field-type registry

**Files:**
- Modify: `frontend/src/lib/fieldTypes/registry.ts`
- Modify: `frontend/src/lib/fieldTypes/registry.test.ts`

**Interfaces:**
- Consumes: `FilesField`.
- Produces: `registry.files` becomes a real def (`defaultValue []`; `parse` coerces to a `string[]`; `serialize` drops blank/empty entries and de-duplicates keeping first, order preserved; `listColumn` null — not list-eligible, mirroring scalar `file`/`image`).

- [ ] **Step 1: Extend the registry tests**

In `frontend/src/lib/fieldTypes/registry.test.ts`:

(a) Confirm the read-only baseline still points at a genuinely read-only interface. It currently uses `repeater` (after slice 2). Leave it as `repeater` — verify the assertion reads:

```ts
  it('falls back to the read-only def for unknown interfaces', () => {
    expect(getFieldType('somethingNew').component).toBe(getFieldType('repeater').component)
    expect(getFieldType('somethingNew').listColumn).toBeNull()
  })
```

(b) Append these tests inside the `describe('field-type registry', …)` block:

```ts
  it('files default is an empty array and parse coerces to string[]', () => {
    const f = field({ interface: 'files' })
    expect(getFieldType('files').defaultValue(f)).toEqual([])
    expect(getFieldType('files').parse(undefined, f)).toEqual([])
    expect(getFieldType('files').parse(['a', 'b'], f)).toEqual(['a', 'b'])
    expect(getFieldType('files').parse('x', f)).toEqual([])
  })

  it('files serialize drops blanks and de-duplicates keeping first (order preserved)', () => {
    const f = field({ interface: 'files' })
    expect(getFieldType('files').serialize(['a', '', ' ', 'a', 'b'], f)).toEqual(['a', 'b'])
    expect(getFieldType('files').serialize('nope', f)).toEqual([])
  })

  it('files is not list-eligible', () => {
    expect(getFieldType('files').listColumn).toBeNull()
  })
```

(Do **not** add `files` to any `LIST_ELIGIBLE` set — it stays not-eligible like `file`/`image`.)

- [ ] **Step 2: Run to verify they fail**

Run: `cd frontend && pnpm test -- fieldTypes/registry`
Expected: FAIL — `files` still resolves to `readonlyDef` (default `''`, parse passes through).

- [ ] **Step 3: Wire the def into the registry**

In `frontend/src/lib/fieldTypes/registry.ts`, add the import near the other field imports (after line 19):

```ts
import FilesField from '../../components/fields/FilesField.vue'
```

Add a `filesDef` near `keyValueDef` (after line 133):

```ts
const filesDef: FieldTypeDef = {
  component: FilesField,
  defaultValue: () => [],
  parse: (raw) => (Array.isArray(raw) ? raw.map(String) : []),
  serialize: (v) => {
    if (!Array.isArray(v)) return []
    const seen = new Set<string>()
    const out: string[] = []
    for (const x of v) {
      const s = String(x)
      if (s.trim() === '' || seen.has(s)) continue
      seen.add(s)
      out.push(s)
    }
    return out
  },
  listColumn: null,
}
```

Replace the `files: readonlyDef,` entry (line 167) with:

```ts
  files: filesDef,
```

(Leave `repeater`, `hidden`, `uuid` as `readonlyDef`. `getFieldType`'s fallback still returns `readonlyDef` for unknown interfaces — unchanged.)

- [ ] **Step 4: Run the registry tests + typecheck**

Run: `cd frontend && pnpm test -- fieldTypes/registry && pnpm vue-tsc --noEmit`
Expected: PASS and typecheck clean (registry still exhaustive over `FieldInterface`).

- [ ] **Step 5: Run the full frontend suite + build**

Run: `cd frontend && pnpm test && pnpm build`
Expected: all tests green (217 + new), build succeeds (pre-existing >500 kB chunk-size advisory only).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/lib/fieldTypes/registry.ts frontend/src/lib/fieldTypes/registry.test.ts
git commit -m "feat(frontend): wire Files into the field-type registry"
```

---

### Task 8: Full gates + live gate (real Postgres + MinIO) + migration + docs

**Files:**
- Create: `db/migrations/003-article-files-column.sql`
- Modify: `docs/ROADMAP.md`
- Modify: `C:\Users\YuJimmy\.claude\projects\D--dotnet-struo-cms\memory\MEMORY.md` + a new memory file (post-verification)

**Interfaces:**
- Consumes: everything above.
- Produces: a migration for the new `article` column; recorded verification baseline + live-gate result.

- [ ] **Step 1: Run the complete automated gates**

Run: `dotnet build -warnaserror && dotnet test tests/Struo.Tests`
Expected: build clean; **353 + new** tests pass / 0 fail.

Run: `cd frontend && pnpm test && pnpm vue-tsc --noEmit && pnpm build`
Expected: **217 + new** tests pass; typecheck clean; build succeeds.

- [ ] **Step 2: Write the migration script**

Create `db/migrations/003-article-files-column.sql`:

```sql
-- Phase 7g+ slice 3 — multi-file `gallery` column on the sample `Article` collection.
--
-- CONTEXT: SqlSugar `InitTables` creates missing TABLES but never adds columns to an existing table.
-- Live databases provisioned before this merge already have an `articles` table, so the new `gallery`
-- field must be added here. A freshly provisioned database gets it from CodeFirst and does not need
-- this script.
--
-- COLUMN TYPE — `text`:
--   * `gallery` (Files field): an `IsJson` `text` column holding a serialized JSON array of file ids
--     (List<Guid> => ["<guid>","<guid>"]). `text`, NOT the `IsJson` default `varchar(1)` (which
--     truncates on Postgres — the slice-1 finding), and NOT NULL with a `'[]'` default (the CLR
--     property is `= []`, so an empty gallery serializes to "[]").
--
-- Identifiers are LOWERCASE and unquoted (SqlSugar emits unquoted identifiers; Postgres folds them to
-- lowercase — existing columns are `status`, `publishedat`, `regions`, `attributes`, `meta`, ...).
-- Idempotent.

ALTER TABLE articles ADD COLUMN IF NOT EXISTS gallery text NOT NULL DEFAULT '[]';
```

- [ ] **Step 3: Start the dev API against live Postgres + Redis + MinIO**

Follow the recipe prior slices used (see the slice-1/slice-2 live-gate notes): run the API with
`dotnet run --no-launch-profile --project src/Struo.Api --urls http://localhost:5221` against the live
`web-struo-cms-db` Postgres + Redis:6379 + MinIO, authenticating as the bootstrap super-admin
(`appsettings.Development.json`, gitignored). Apply `db/migrations/003-article-files-column.sql` to the
live DB (or provision fresh). Record which was used. API responses are enveloped `{ data: … }`
(create id = `.data.id`); cookie mutations need an `X-Struo-CSRF` header (any value); use PowerShell
`Invoke-RestMethod` + a `WebSession`, never Big5 curl.

- [ ] **Step 4: Live-gate checks (API-level, UTF-8 via PowerShell `Invoke-RestMethod` or a UTF-8 file)**

1. Upload 3 files (`POST /api/files`) → capture 3 published file ids `id1, id2, id3`.
2. Create an `article` with `translations.en.title` and `gallery = [id1, id2, id3]` → expect **201**.
3. `GET /api/items/article/{id}` → `gallery` reads back as a JSON array `[id1, id2, id3]` in the **same
   order**, read from the `text` column (not truncated → the slice-1 `varchar(1)` bug-class is not
   reintroduced).
4. `PUT` the item with `gallery = [id3, id1]` (reorder + drop id2) → expect **200**; re-`GET` confirms
   the new order `[id3, id1]` and that id2 is gone.
5. `PUT` with a duplicate id (`gallery = [id1, id1, id3]`) → **200**; re-`GET` shows de-duplicated
   `[id1, id3]` (keep-first).
6. Create with a `gallery` containing a **non-guid string** (`["not-a-guid"]`) → expect **400** (not a
   500 — the 7g bug-class guard).
7. `Required` empty: temporarily add `Required = true` to a probe field, or use a dedicated `FilesThing`
   route if exposed → an empty/absent required `Files` → **400** `Field '{name}' is required.` (If no
   required `Files` collection is live-exposed, this is already covered by `ItemServiceFilesFieldTests`
   at the unit level — record that.)
8. Delete one referenced file (`DELETE /api/files/{id2}` after re-adding it) then `GET` the article: the
   `gallery` array still round-trips the deleted id unchanged through the API (frontend picker shows a
   raw-id fallback — verify in the SPA if convenient, else confirm the API contract).

Fix any Postgres-only issue surfaced (SQLite-green ≠ Postgres-correct); re-run until green.

- [ ] **Step 5: Update the roadmap**

In `docs/ROADMAP.md`: add a Phase 7g+ (slice 3) status row (✅ done + live-verified) with the final test
counts, add the `7g+.3` table row, and update the trailing 7g+ "remaining" row (structured/multi-file
editors now = `Repeater` **only**; `Files` done).

- [ ] **Step 6: Commit + finish the branch**

```bash
git add docs/ROADMAP.md db/migrations/003-article-files-column.sql
git commit -m "docs: Phase 7g+ slice 3 done + live-verified (multi-file Files field)"
```

Then use the `superpowers:finishing-a-development-branch` skill to merge the slice-3 branch into `main`
(`--no-ff`), and record a new memory file + `MEMORY.md` pointer capturing the outcome (the `List<Guid>`
+ `IsJson` text shape; the scalar-mirroring raw-id contract + batched `_in` resolve; the review-M4
Translatable fail-fast now actioned; any live-gate fixes).

---

## Self-Review

**1. Spec coverage:**
- §2 approach (`List<Guid>` via `IsJson` text; STJ in/out, Newtonsoft only SqlSugar-internal) → Task 1. ✅
- §2.1 mirror scalar contract (raw ids, no inflation) → Task 2 (projection unchanged) + Task 6 (frontend batched `_in` resolve). ✅
- §3 data model (`List<Guid>`, order-preserving, `[]` default) → Tasks 1, 2, 4. ✅
- §4 column mapping (add to `JsonColumnInterfaces`) → Task 1; write normalization (drop empty, dedup keep-first, Required) → Task 2; non-guid → 400 via existing guard → Task 2 (test); MaxLength N/A (not a string) → no code, noted; non-sortable/searchable → Task 4 assertion; projection generic → Task 2 (no code, verified by round-trip test). ✅
- §4.1 Translatable fail-fast (review M4; Json excluded) → Task 3. ✅
- §5 frontend registry + component + MediaGrid multi-select → Tasks 5, 6, 7 (registry baseline caveat → Task 7 step 1a). ✅
- §6 sample field → Task 4. ✅
- §7 tests (backend + frontend) + live gate → Tasks 1–8. ✅
- §8 non-goals (only `Files`; non-translatable; no querying; no MaxLength; no inflation; no existence check; reserved relation enums untouched) → respected; no task introduces any of them.

**2. Placeholder scan:** No TBD/TODO/"handle edge cases"/"similar to". Every code step shows full code. Task 8 live-gate steps are procedural (manual verification) with exact payloads and expected status codes — appropriate for a live gate, not placeholders.

**3. Type consistency:** `Files` = `List<Guid>` consistently across Tasks 1–4 and the migration (Task 8). The factory set `JsonColumnInterfaces` (Task 1) and the scanner set `NonTranslatableJsonInterfaces` (Task 3) are distinct, correctly-named sets in different files. Error strings match between Task 2's tests and impl (`Field '{name}' is required.`) and between Task 3's test (`*cannot be translatable*`) and impl (`… which cannot be translatable.`). Frontend: `filesDef` (Task 7) references `FilesField` (Task 6); `MediaGrid` multi-select props/emit (`multiple`/`selectedIds`/`toggle`, Task 5) match `FilesField`'s usage (Task 6); the `_in` filter shape (`{ op: '_in', value: ids.join(',') }`, Task 6) matches the backend `QueryOperator.In` comma-join contract. `currentIds`/`toggle`/`removeAt`/`onReorder` exposed in Task 6 match the Task 6 tests. Registry `files` not added to any list-eligible set (Task 7) matches `listColumn: null`.
