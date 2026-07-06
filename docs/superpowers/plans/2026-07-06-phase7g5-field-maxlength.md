# Phase 7g.5 — Field MaxLength Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `[CmsField(MaxLength = n)]` — a CMS-layer input-length limit flowing attribute → metadata (effective value resolved once) → schema API → backend write validation (400) → frontend `maxlength` + client validation. No DDL change.

**Architecture:** The scanner (`MetadataScanner.BuildField`) resolves the *effective* limit into `FieldMetadata.MaxLength` (`int?`): declared value if > 0; else 255 for short-string interfaces; else null (unlimited) for content-bearing interfaces. Every consumer reads the resolved value. `ItemService` enforces on both write paths beside the existing Required checks (`QueryException` → 400). The SPA binds native `maxlength` and mirrors the rule in `validateItem`.

**Tech Stack:** .NET 10 / xUnit + AwesomeAssertions · Vue 3 + Vitest. No new packages.

**Spec:** `docs/superpowers/specs/2026-07-06-phase7g5-field-maxlength-design.md`

## Global Constraints

- Branch: `phase7g5-field-maxlength` off `main`.
- **No DDL/`SqlSugarClientFactory` change** (user decision): DB width stays SqlSugar's concern.
- Interface sets (exact): short-string = `Text, Slug, Email, Url, Password, Color, Phone, Select, MultiSelect, Radio, CheckboxGroup, Tags` (default 255); content-bearing = `RichText, Textarea, Markdown, Code, Json` (default unlimited). Explicit `MaxLength > 0` is honored on any string field.
- Unit = UTF-16 code units (`string.Length` / JS `.length`).
- Startup fail-fast (`MetadataException`): negative `MaxLength`; `MaxLength > 0` on a non-string CLR property.
- Backend error message format (exact): `Field '{name}' exceeds maximum length {max}.` (translation path appends ` for locale '{locale}'` — see Task 2).
- Length check runs AFTER RichText sanitization (the stored value is measured). Boundary: exactly-at-limit passes.
- Backend tests use AwesomeAssertions. Baselines: backend 325 green, frontend 173 green.
- TDD: failing test first for every behavior change.

---

### Task 1: Backend — attribute, metadata, scanner resolution + guards

**Files:**
- Modify: `src/Struo.Domain/Metadata/Attributes/CmsFieldAttribute.cs`
- Modify: `src/Struo.Domain/Metadata/Models/FieldMetadata.cs`
- Modify: `src/Struo.Infrastructure/Metadata/MetadataScanner.cs:208-238` (`BuildField`)
- Test: `tests/Struo.Tests/Metadata/` (extend the existing scanner test file — find it with `ls tests/Struo.Tests/Metadata/` and read a sibling test first to reuse its fixture idiom for test entity classes)

**Interfaces:**
- Consumes: existing `BuildField(PropertyInfo, CmsFieldAttribute)` returning `FieldMetadata` (also reused by the translation-entity path with `with { Translatable = true }`, so sidecar fields inherit resolution for free).
- Produces: `CmsFieldAttribute.MaxLength` (`int`, 0 = unset); `FieldMetadata.MaxLength` (`int?`, the EFFECTIVE limit). Tasks 2–3 read `FieldMetadata.MaxLength`/schema `maxLength` only.

- [ ] **Step 1: Write the failing tests**

Add to the existing MetadataScanner test class (adapting fixture style to siblings). Test entities (place beside the file's existing test collections):

```csharp
[CmsCollection("maxLenSample")]
private sealed class MaxLenSample : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }
    [CmsField(Interface = FieldInterface.Text, MaxLength = 100)] public string Declared { get; set; } = string.Empty;
    [CmsField(Interface = FieldInterface.Text)] public string ShortDefault { get; set; } = string.Empty;
    [CmsField(Interface = FieldInterface.Textarea)] public string ContentDefault { get; set; } = string.Empty;
    [CmsField(Interface = FieldInterface.Textarea, MaxLength = 5000)] public string ContentDeclared { get; set; } = string.Empty;
}

[Fact]
public void MaxLength_resolution_matrix()
{
    var meta = MetadataScanner.Scan(new[] { typeof(MaxLenSample) }).Single();
    meta.Fields.Single(f => f.Name == "declared").MaxLength.Should().Be(100);
    meta.Fields.Single(f => f.Name == "shortDefault").MaxLength.Should().Be(255);
    meta.Fields.Single(f => f.Name == "contentDefault").MaxLength.Should().BeNull();
    meta.Fields.Single(f => f.Name == "contentDeclared").MaxLength.Should().Be(5000);
}
```

Fail-fast guards (separate invalid entities + tests):

```csharp
[CmsCollection("negativeMaxLen")]
private sealed class NegativeMaxLen : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }
    [CmsField(MaxLength = -1)] public string Name { get; set; } = string.Empty;
}

[CmsCollection("maxLenOnNonString")]
private sealed class MaxLenOnNonString : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }
    [CmsField(Interface = FieldInterface.Number, MaxLength = 10)] public int Count { get; set; }
}

[Fact]
public void Negative_MaxLength_fails_fast()
{
    var act = () => MetadataScanner.Scan(new[] { typeof(NegativeMaxLen) });
    act.Should().Throw<MetadataException>().WithMessage("*MaxLength*negative*");
}

[Fact]
public void MaxLength_on_non_string_property_fails_fast()
{
    var act = () => MetadataScanner.Scan(new[] { typeof(MaxLenOnNonString) });
    act.Should().Throw<MetadataException>().WithMessage("*MaxLength*string*");
}
```

(Adapt entity shape — PK attribute, `Scan` signature, collection-attribute arguments — to whatever the sibling tests in that file actually use; keep the assertions.)

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter "FullyQualifiedName~MetadataScanner"`
Expected: FAIL — `MaxLength` not defined on `CmsFieldAttribute`/`FieldMetadata` (compile error is the RED here; note it in the report).

- [ ] **Step 3: Implement**

`CmsFieldAttribute.cs` — after `Group`:

```csharp
    /// <summary>
    /// CMS-layer input-length limit (UTF-16 code units). 0 = unset: short-string interfaces
    /// default to 255 (matching SqlSugar's default column width), content-bearing interfaces
    /// are unlimited. Independent of the DB column width ([SugarColumn(Length = n)]).
    /// </summary>
    public int MaxLength { get; set; }
```

`FieldMetadata.cs` — after `Group`:

```csharp
    /// <summary>Effective CMS-layer max length resolved by the scanner; null = unlimited.</summary>
    public int? MaxLength { get; init; }
```

`MetadataScanner.cs` — add a static set near the top of the class (mirror the existing `OptionInterfaces` set idiom):

```csharp
    // 7g.5: interfaces whose undeclared MaxLength defaults to 255 (SqlSugar's default varchar width).
    // Content-bearing interfaces (RichText/Textarea/Markdown/Code/Json) default to unlimited.
    private static readonly HashSet<FieldInterface> ShortStringInterfaces =
    [
        FieldInterface.Text, FieldInterface.Slug, FieldInterface.Email, FieldInterface.Url,
        FieldInterface.Password, FieldInterface.Color, FieldInterface.Phone,
        FieldInterface.Select, FieldInterface.MultiSelect, FieldInterface.Radio,
        FieldInterface.CheckboxGroup, FieldInterface.Tags
    ];
```

In `BuildField`, before the `return new FieldMetadata` initializer (beside the `[CmsOptions]` guard):

```csharp
        if (attr.MaxLength < 0)
            throw new MetadataException(
                $"Field '{prop.DeclaringType?.Name}.{prop.Name}' has a negative MaxLength.");
        if (attr.MaxLength > 0 && prop.PropertyType != typeof(string))
            throw new MetadataException(
                $"Field '{prop.DeclaringType?.Name}.{prop.Name}' declares MaxLength but is not a string property.");

        int? maxLength = attr.MaxLength > 0
            ? attr.MaxLength
            : prop.PropertyType == typeof(string) && ShortStringInterfaces.Contains(attr.Interface)
                ? 255
                : null;
```

and in the initializer, after `Group = attr.Group,`:

```csharp
            MaxLength = maxLength,
```

- [ ] **Step 4: Run scanner tests**

Run: `dotnet test --filter "FullyQualifiedName~MetadataScanner"`
Expected: ALL PASS (3 new + existing).

- [ ] **Step 5: Full backend suite**

Run: `dotnet test`
Expected: all green (325 + 3 = 328). If any existing test asserts an exact `FieldMetadata` shape or schema JSON and now fails, update it to include the resolved `maxLength` — list every such adjustment in your report.

- [ ] **Step 6: Commit**

```bash
git add src/Struo.Domain/Metadata/Attributes/CmsFieldAttribute.cs src/Struo.Domain/Metadata/Models/FieldMetadata.cs src/Struo.Infrastructure/Metadata/MetadataScanner.cs tests/Struo.Tests/Metadata/
git commit -m "feat(metadata): [CmsField(MaxLength)] with scanner-resolved effective limit (7g.5)"
```

---

### Task 2: Backend — ItemService write validation (400)

**Files:**
- Modify: `src/Struo.Application/Query/ItemService.cs` (two spots: `Deserialize` ~line 683-689 required loop; `SyncTranslationsAsync` per-locale loop ~line 434-440)
- Test: `tests/Struo.Tests/Query/` — extend the existing ItemService write-path test file (sibling: `ItemServiceRichTextSanitizationTests.cs` — read it first and reuse its harness: real ItemService + SQLite test DB + sample entities)

**Interfaces:**
- Consumes: `FieldMetadata.MaxLength` (`int?`, effective; Task 1). Existing `QueryException` → 400 mapping in Program.cs (unchanged).
- Produces: over-long writes rejected with `QueryException` message `Field '{name}' exceeds maximum length {max}.` (+ ` for locale '{locale}'` on the translation path).

- [ ] **Step 1: Write the failing tests**

Use the sanitization test file's harness (real service, SQLite). The sample `ArticleTranslation.Title` is Text/translatable (effective 255 after Task 1); `Category.Name` is Text (effective 255, non-translatable path). Assertions to implement (adapt setup to the sibling harness verbatim-style):

```csharp
[Fact]
public async Task Create_rejects_overlong_non_translatable_string_with_400_class_error()
{
    var body = JsonBody(new { name = new string('x', 256) }); // category.name, effective 255
    var act = () => _service.CreateAsync("category", body);
    await act.Should().ThrowAsync<QueryException>()
        .WithMessage("Field 'name' exceeds maximum length 255.");
}

[Fact]
public async Task Create_accepts_string_exactly_at_limit()
{
    var body = JsonBody(new { name = new string('x', 255) });
    var created = await _service.CreateAsync("category", body); // must not throw
    created.Should().NotBeNull();
}

[Fact]
public async Task Create_rejects_overlong_translatable_string_per_locale()
{
    var body = JsonBody(new { translations = new Dictionary<string, object> {
        ["en"] = new { title = new string('x', 256), body = "<p>ok</p>" } } });
    var act = () => _service.CreateAsync("article", body);
    await act.Should().ThrowAsync<QueryException>()
        .WithMessage("Field 'title' exceeds maximum length 255 for locale 'en'.");
}
```

(`JsonBody` = however the sibling file builds its `JsonElement` request bodies — reuse it. If `article` create needs more required fields in the harness, supply them.)

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter "FullyQualifiedName~MaxLength"` (name your tests so this filter catches them, or use the test-class filter)
Expected: FAIL — over-long values currently pass validation (the first test throws nothing or a DB error; on SQLite it will NOT throw at all — that's the point).

- [ ] **Step 3: Implement**

`Deserialize` — immediately after the required-validation loop (line ~689), same style:

```csharp
        // Max length — CMS-layer limit (7g.5); the DB column width is SqlSugar's separate concern.
        // Runs after RichText sanitization so the stored value is what gets measured.
        foreach (var field in meta.Fields.Where(f => f.MaxLength is > 0 && !f.Translatable))
        {
            var pi = d.FieldToProperty.TryGetValue(field.Name, out var prop) ? d.EntityType.GetProperty(prop) : null;
            if (pi?.GetValue(entity) is string value && value.Length > field.MaxLength!.Value)
                throw new QueryException($"Field '{field.Name}' exceeds maximum length {field.MaxLength}.");
        }
```

`SyncTranslationsAsync` — immediately after the required loop inside the per-locale `foreach` (line ~440). Build the lookup once before the locale loop (beside where `requiredFields`/`allowed` are prepared):

```csharp
        var maxLengths = meta.Fields
            .Where(f => f.MaxLength is > 0)
            .ToDictionary(f => f.Name, f => f.MaxLength!.Value, StringComparer.OrdinalIgnoreCase);
```

then in the per-locale loop:

```csharp
            // Max length — CMS-layer limit (7g.5), measured after RichText sanitization.
            foreach (var (name, value) in fieldValues)
            {
                if (value is string sv && maxLengths.TryGetValue(name, out var max) && sv.Length > max)
                    throw new QueryException(
                        $"Field '{name}' exceeds maximum length {max} for locale '{locale}'.");
            }
```

- [ ] **Step 4: Run the new tests**

Run: same filter as Step 2. Expected: ALL PASS.

- [ ] **Step 5: Full backend suite**

Run: `dotnet test`
Expected: all green (328 + 3 = 331). Update-path coverage rides on `Deserialize`/`SyncTranslationsAsync` being shared by create and update — if the sibling harness makes an update test cheap (one more call), add one for the non-translatable path and count it in your report.

- [ ] **Step 6: Commit**

```bash
git add src/Struo.Application/Query/ItemService.cs tests/Struo.Tests/Query/
git commit -m "feat(items): enforce MaxLength on both write paths with 400 (7g.5)"
```

---

### Task 3: Frontend — schema type, input maxlength, client validation

**Files:**
- Modify: `frontend/src/types/schema.ts:6-21` (`FieldMeta`)
- Modify: `frontend/src/components/fields/FieldInput.vue:24-28`
- Modify: `frontend/src/lib/validateItem.ts`
- Test: `frontend/src/components/fields/FieldInput.test.ts`, `frontend/src/lib/validateItem.test.ts` (extend existing files; read them first, reuse their fixture builders)

**Interfaces:**
- Consumes: schema payload now carries `maxLength?: number | null` per field (Task 1, auto-exposed).
- Produces: `FieldMeta.maxLength?: number | null`; text/textarea inputs carry native `maxlength`; `validateItem` returns `` `${f.label} must be at most ${f.maxLength} characters.` `` for over-long values.

- [ ] **Step 1: Write the failing tests**

`FieldInput.test.ts` (reuse the file's mount/fixture idiom; PrimeVue InputText/Textarea render native elements that pass `maxlength` through):

```ts
it('binds maxlength on text inputs when the field declares one', () => {
  const w = mount(FieldInput, { props: { field: field({ interface: 'text', maxLength: 100 }), modelValue: '' } })
  expect(w.get('input').attributes('maxlength')).toBe('100')
})

it('omits maxlength when the field has none', () => {
  const w = mount(FieldInput, { props: { field: field({ interface: 'richText', maxLength: null }), modelValue: '' } })
  // richtext has no native input; use textarea kind for the no-limit case instead:
  const w2 = mount(FieldInput, { props: { field: field({ interface: 'textarea', maxLength: null }), modelValue: '' } })
  expect(w2.get('textarea').attributes('maxlength')).toBeUndefined()
})
```

(`field(...)` = the file's existing FieldMeta fixture helper — if none exists, build the object literal matching `FieldMeta` with all required keys.)

`validateItem.test.ts`:

```ts
it('rejects over-long shared values and accepts exactly-at-limit', () => {
  const meta = metaWith({ name: 'name', label: 'Name', interface: 'text', maxLength: 5, required: false })
  expect(validateItem(meta, modelWith({ shared: { name: 'x'.repeat(6) } }), 'en'))
    .toEqual({ name: 'Name must be at most 5 characters.' })
  expect(validateItem(meta, modelWith({ shared: { name: 'x'.repeat(5) } }), 'en')).toEqual({})
})

it('rejects over-long default-locale translatable values', () => {
  const meta = metaWith({ name: 'title', label: 'Title', interface: 'text', maxLength: 5, required: false, translatable: true })
  expect(validateItem(meta, modelWith({ translations: { en: { title: 'x'.repeat(6) } } }), 'en'))
    .toEqual({ title: 'Title must be at most 5 characters.' })
})
```

(`metaWith`/`modelWith` = the file's existing builders — reuse or inline equivalents.)

- [ ] **Step 2: Run to verify failure**

Run (in `frontend/`): `pnpm test -- FieldInput` and `pnpm test -- validateItem`
Expected: new tests FAIL (`maxlength` attribute absent; validateItem returns `{}`).

- [ ] **Step 3: Implement**

`types/schema.ts` — in `FieldMeta`, after `sort: number`:

```ts
  maxLength?: number | null // effective CMS-layer limit resolved by the backend scanner; null = unlimited
```

`FieldInput.vue` — the two bindings:

```html
  <InputText v-if="kind === 'text'" :model-value="(modelValue as string)" :disabled="isDisabled"
    :maxlength="field.maxLength ?? undefined" @update:model-value="update" />

  <Textarea v-else-if="kind === 'textarea'" :model-value="(modelValue as string)"
    :disabled="isDisabled" :rows="6" :maxlength="field.maxLength ?? undefined" @update:model-value="update" />
```

`validateItem.ts` — add beside `isEmpty`:

```ts
function tooLong(f: { maxLength?: number | null }, v: unknown): boolean {
  return typeof v === 'string' && f.maxLength != null && v.length > f.maxLength
}
```

and extend both loops:

```ts
  for (const f of shared) {
    if (f.required && isEmpty(model.shared[f.name])) errors[f.name] = `${f.label} is required.`
    else if (tooLong(f, model.shared[f.name])) errors[f.name] = `${f.label} must be at most ${f.maxLength} characters.`
  }
  const defaultValues = model.translations[defaultCode] ?? {}
  for (const f of translatable) {
    if (f.required && isEmpty(defaultValues[f.name])) errors[f.name] = `${f.label} is required.`
    else if (tooLong(f, defaultValues[f.name])) errors[f.name] = `${f.label} must be at most ${f.maxLength} characters.`
  }
```

- [ ] **Step 4: Run the two test files**

Run: `pnpm test -- FieldInput` and `pnpm test -- validateItem`
Expected: ALL PASS.

- [ ] **Step 5: Full frontend suite + build**

Run: `pnpm test` then `pnpm build`
Expected: all green (173 + 4 = 177); build succeeds (pre-existing chunk advisory only).

- [ ] **Step 6: Commit**

```bash
git add src/types/schema.ts src/components/fields/FieldInput.vue src/lib/validateItem.ts src/components/fields/FieldInput.test.ts src/lib/validateItem.test.ts
git commit -m "feat(frontend): maxlength binding + client-side length validation (7g.5)"
```

---

### Task 4: Docs + gates + merge readiness (controller-run)

**Files:**
- Modify: `docs/guide/03-adding-a-collection.md` (new short section)
- Modify: `docs/ROADMAP.md` (7g.5 row + baseline)

- [ ] **Step 1: Guide section**

Add to `docs/guide/03-adding-a-collection.md` (after the field-declaration examples; match the guide's tone):

```markdown
## Field max length (`MaxLength`)

`[CmsField(MaxLength = 100)]` sets the CMS-layer input limit: the admin form caps typing at 100
characters and the API rejects longer values with 400. Undeclared short-string fields (Text, Slug,
Email, Url, Password, Color, Phone, and option-backed interfaces) default to **255** — matching the
database default; content-bearing fields (RichText, Textarea, Markdown, Code, Json) are unlimited.
Lengths count UTF-16 code units (what `string.Length` and JavaScript `.length` return).

`MaxLength` is deliberately independent of the **database column width**, which SqlSugar controls
(`[SugarColumn(Length = n)]`, default `varchar(255)`). If you raise `MaxLength` above 255, also
widen the column, or values in between will still fail at the database:

```csharp
[CmsField(Label = "Summary", MaxLength = 500)]
[SugarColumn(Length = 500)]
public string Summary { get; set; } = string.Empty;
```
```

- [ ] **Step 2: Run every automated gate**

```bash
dotnet build        # clean, 0 warnings
dotnet test         # Task 2 count (expected 331)
# frontend/
pnpm test           # Task 3 count (expected 177)
pnpm build          # succeeds
```

- [ ] **Step 3: ROADMAP**

Update the 7g.5 row to code-complete (link spec + plan), add the post-7g.5 verification baseline with actual counts, and set "Next up" to the 7g.5 live gate then 7g+.

- [ ] **Step 4: Commit**

```bash
git add docs/guide/03-adding-a-collection.md docs/ROADMAP.md
git commit -m "docs: Phase 7g.5 code-complete (field MaxLength); gates green, live-gate pending"
```

- [ ] **Step 5: Live gate (operator-driven, real PG + Redis + MinIO)**

Same discipline as 7g (PowerShell + curl.exe, UTF-8 no-BOM payload files, cookie jar, `X-Struo-CSRF: 1` on mutations, API on :5080):

1. POST `category` with a 256-char `name` → **400** with `Field 'name' exceeds maximum length 255.` (was a 500 — the bug-class kill-shot).
2. POST `category` with a 255-char `name` → 201 and round-trips.
3. POST `article` with a 256-char `en` `title` → **400** `... for locale 'en'.` (translatable path; use a UTF-8 payload file).
4. Regression: a normal article create/read still works.

Record evidence, update the ROADMAP row to done & live-verified, then merge via superpowers:finishing-a-development-branch.
