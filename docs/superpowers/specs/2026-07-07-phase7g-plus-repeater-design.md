# Phase 7g+ (slice 4) — `Repeater` field: repeatable child objects (design)

**Date:** 2026-07-07
**Status:** approved (brainstorm); pending implementation plan
**Scope:** the **fourth and final 7g+ slice** — light up the `Repeater` field interface (a repeatable
group of sub-fields, e.g. an FAQ list `[{question, answer}, …]`), the **last** `FieldInterface` value
still rendering read-only since 7g.6. Completing it closes Phase 7g+. Following the slicing discipline
(7d/7e/7f/7g/7g+ slices 1–3), this slice **changes persistence** (a field now stores a JSON array of
child objects), so it carries its own DDL consideration and its own **live gate** on real Postgres.

---

## 1. Goal & positioning

The earlier 7g+ slices lit up the collection-shaped interfaces whose element is a *primitive or a
reference*: multi-value selects (`List<string>` / `List<TagItem>`, slice 1), structured `Json` +
`KeyValue` (slice 2), multi-file `Files` (`List<Guid>`, slice 3). `Repeater` is the remaining shape:
a collection whose element is a **structured child object with its own typed sub-fields**.

- `Repeater` — an **ordered** list of child objects, each conforming to a fixed sub-field schema.

`Repeater` resolves to `ReadonlyField` today. The design reuses the proven slice-1 JSON-column
persistence convention (`List<>` via `IsJson` + `text`) and the 7g.6 field-type registry (the child
row is a mini-form that dispatches each sub-field through the **same** `*Field.vue` components,
recursively). The only genuinely new surfaces are (a) a recursive sub-field schema on `FieldMetadata`
and (b) a `RepeaterField.vue` that renders/edits the array of child mini-forms.

Per the brainstorm, the field is **non-translatable, non-sortable, non-searchable**, and `MaxLength`
(7g.5) **does not apply** at the parent level — the same non-goal contract as slices 1–3. **Order is
meaningful and user-arrangeable**; `List<TChild>` preserves it and the editor lets the user move rows
up/down.

## 2. Approach (decided)

Three child-schema mechanisms were weighed in the brainstorm:

- **(A, chosen) Typed CLR child class** — the parent property is `List<TChild>` where `TChild` is a
  POCO whose sub-properties each carry `[CmsField]`. The scanner recurses into `TChild` to build the
  nested sub-field metadata.
- (B) Inline sub-field declaration via a new `[CmsRepeater("question:Text", …)]` attribute, stored as
  `List<Dictionary<string,object?>>` — rejected: stringly-typed, diverges from the `[CmsField]` idiom,
  weaker typing/validation.
- (C) Schema-less JSON array (a `Json` field constrained to an array) — rejected: no structured
  per-sub-field editing, which is the entire point of a Repeater; the existing `Json` field already
  covers free-form array data.

**Approach A** is the natural terminal design: it is type-safe, mirrors how `Tags` already stores a
list of typed POCOs (`List<TagItem>`), reuses the existing `BuildField` scanner logic and the frontend
field registry recursively, and reuses the existing per-field validation concepts (Required, options,
MaxLength) at the sub-field level.

### 2.1 Persistence — the proven `Tags` / `List<POCO>` path

- The entity property is `List<TChild>` (natural CLR type; preserves order).
- It maps to an `IsJson` + `text` column via the existing `JsonColumnInterfaces` convention in
  `SqlSugarClientFactory` (the set slices 1–3 populate): the EntityService hook sets
  `column.IsJson = true; column.DataType = "text";`. `DataType = "text"` is mandatory — `IsJson` alone
  leaves the CodeFirst length unset and Postgres makes it `varchar(1)`, truncating the payload (the
  slice-1 live-gate finding, `3b4ab40`).
- **Inbound / outbound JSON we author is `System.Text.Json`.** Inbound, STJ's whole-entity
  `Deserialize` binds the JSON array of child objects straight into `List<TChild>` (camelCase Web
  defaults map `question` → `Question`). Outbound, the API's STJ serializer emits the projected
  `List<TChild>` as a camelCase array of objects. The **only** Newtonsoft touch is SqlSugarCore 5.1.4's
  internal materialization of the `IsJson` column — the identical framework-internal path already
  load-bearing for the merged `Tags` column (`List<TagItem>`). Newtonsoft round-trips its own output
  consistently (it writes PascalCase to the DB text and reads PascalCase back); the DB storage casing
  is an internal detail invisible to the API contract, exactly as `Tags` already proves. `List<TChild>`
  is a POCO list, so the slice-2 `JsonElement?`-reads-back-disposed problem does **not** apply.
- Satisfies CLAUDE.md §17.4 (all DB access via SqlSugar ORM, zero vendor SQL).

### 2.2 The child object

`TChild` is an ordinary POCO — it is **not** a `[CmsCollection]`, has no PK, and is never an entity in
its own right; it exists only to be serialized into the parent's JSON column. Its sub-properties carry
`[CmsField]` with an interface drawn from the **allowed scalar set** (§3.1).

```csharp
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

// on Article (sample):
[CmsField(Label = "FAQs", Interface = FieldInterface.Repeater, Sort = 12, Group = "Content")]
public List<FaqItem> Faqs { get; set; } = [];
```

API contract (camelCase): `"faqs": [{ "question": "...", "answer": "...", "category": "general" }]`.

## 3. Metadata model change

`FieldMetadata` gains a nullable, self-recursive sub-field list carrying the Repeater child schema
(null for every non-Repeater field):

```csharp
public sealed record FieldMetadata
{
    // …existing members…
    /// <summary>Repeater child-object sub-field schema; null = not a Repeater.</summary>
    public IReadOnlyList<FieldMetadata>? Fields { get; init; }
}
```

The frontend `FieldMeta` type mirrors it with `fields?: FieldMeta[]`. This is the only model-layer
addition.

### 3.1 Allowed sub-field interfaces (scanner whitelist)

The **lean scalar set** (brainstorm Q2). A new `MetadataScanner.RepeaterAllowedInterfaces`:

```
Text, Textarea, Markdown, Code, Slug, Email, Url, Color, Phone,
Number, Slider, Rating,
Boolean, Checkbox,
Date, Time, DateTime,
Select, Radio            // with [CmsOptions]
```

Deliberately **excluded** (fail-fast at scan): `RichText` (would need per-row server-side
sanitization), `File`/`Image`/`Files` (would need per-row file resolution), `MultiSelect`/
`CheckboxGroup`/`Tags`/`KeyValue`/`Json` (structured-inside-structured), `Repeater` (no nesting),
`Password`/`Hidden`/`Uuid`/`Divider` (no meaning inside a repeater row).

## 4. Scanner (`MetadataScanner`)

When `attr.Interface == Repeater`:

1. The property must be a `List<T>` (generic, assignable to `IEnumerable`, exactly one generic
   argument, `T` a `class`) — else `MetadataException` (`Repeater field '{name}' must be a List<T> of a
   child object type.`).
2. **Recurse:** for each `[CmsField]` sub-property of `T`, call the existing `BuildField` (this reuses
   `[CmsOptions]` parsing, the MaxLength-255 default for short strings, etc.). The resulting list
   becomes the parent `FieldMetadata.Fields`. `T` must declare ≥1 `[CmsField]` sub-field — else
   `MetadataException`.
3. Each sub-field's `Interface` must be ∈ `RepeaterAllowedInterfaces`, else fail-fast:
   `Repeater field 'faqs' sub-field 'answer' uses interface 'File', which is not allowed inside a Repeater.`
   (A nested `Repeater` sub-field is caught by this same check — `Repeater ∉ RepeaterAllowedInterfaces`.)
4. A sub-field with `[CmsField(Translatable = true)]` fail-fasts (translating a JSON-embedded child is
   out of scope): `Repeater field 'faqs' sub-field '…' cannot be translatable.`
5. Add `Repeater` to the existing `NonTranslatableJsonInterfaces` set so a `Translatable = true`
   **parent** Repeater fail-fasts at scan (reuses the slice-3 M4 mechanism).

The recursion terminates because `Repeater` is not itself an allowed sub-field interface.

## 5. `ItemService` — Deserialize normalization branch

`List<TChild>` binds natively via the whole-entity `Deserialize` (no key-strip needed, unlike `Json`);
a wrong-shaped element (e.g. a string where an object is expected, or a sub-field of the wrong JSON
type) throws `JsonException`, already caught and mapped to **400** by the existing deserialize guard.

A new Repeater normalization branch (structured like the Files/Tags branches), non-translatable only:

1. Read the child `IList` off the property.
2. **Drop fully-blank rows.** A row is dropped **iff every** sub-field value is `null` **or** a
   whitespace-only string. A row containing any non-null non-string value (a number, a bool, a date) is
   **kept** (the user must remove such a row explicitly). This rule is crisp and predictable; the
   frontend mirrors it.
3. For each **kept** row, validate its sub-fields:
   - **Required:** a required sub-field that is `null`/blank → 400
     (`Repeater field 'faqs' row 2: 'question' is required.`).
   - **Option membership (Select/Radio):** a non-null value ∉ the sub-field's options → 400
     (`Repeater field 'faqs' row 2: 'category' value 'x' is not in its options.`).
   - **MaxLength:** a string sub-field longer than its effective MaxLength → 400
     (`Repeater field 'faqs' row 2: 'question' exceeds maximum length 255.`).
4. **Parent Required:** after dropping blank rows, an empty list → 400
   (`Field 'faqs' is required.`).
5. Write the cleaned list back onto the entity.

Because the allowed set excludes `RichText` and `File`/`Image`, this branch needs **no** recursive HTML
sanitization and **no** per-row file resolution — keeping the slice tight.

## 6. Projection (`Project`)

The Repeater field value is the CLR `List<TChild>`, placed into the projection dictionary **as-is**; the
outbound serializer (STJ Web / camelCase) serializes each POCO sub-property camelCased →
`[{question, answer, category}]`. No special projection code is required (identical to `Tags`). Sub-field
metadata names are `Camel(prop.Name)`, so they align with the camelCased output keys.

## 7. Frontend

- **`types.ts`:** `FieldMeta` gains `fields?: FieldMeta[]`; `ALL_FIELD_INTERFACES` already contains
  `repeater`.
- **Registry (`registry.ts`):** replace the `repeater: readonlyDef` placeholder with a real
  `repeaterDef` → `RepeaterField.vue`:
  - `defaultValue: () => []`
  - `parse`: array-ize (`Array.isArray(raw) ? raw : []`)
  - `serialize`: drop fully-blank rows, mirroring the backend rule (every sub-field `null`/blank string)
  - `listColumn`: render the count (e.g. `3 items`)
- **`RepeaterField.vue`:** renders the array as a list of **cards**, each card a mini-form iterating
  `field.fields` and dispatching each sub-field through `getFieldType(sub.interface).component`
  — recursively reusing the existing `*Field.vue` components. Row controls: **Add row**, **Remove row**,
  **Move up / Move down** (plain buttons — not PrimeVue `OrderList`, whose drag model does not suit a
  per-row mini-form). The backend guarantees no `repeater` sub-field, so the recursion cannot loop.
  A watch/clobber guard mirrors `FilesField`/`KeyValueField`: emit `update:modelValue` with a new array
  on every mutation (immutable update; no in-place mutation, per coding-style).
- **Validation:** frontend validation stays light (required-indicator + a `validate` in `repeaterDef`
  that mirrors the sub-field Required check per row for an inline message); the backend is the source of
  truth and returns 400.

## 8. Testing

**Backend (`dotnet test`):**
- **Scanner** (`MetadataScannerTests` / `MetadataValidationTests`): builds nested `Fields` from the child
  POCO; fail-fast on (a) non-`List<T>` property, (b) a disallowed sub-field interface, (c) a nested
  `Repeater` sub-field, (d) a `Translatable` parent Repeater, (e) a `Translatable` sub-field, (f) a
  child POCO with zero `[CmsField]` sub-fields.
- **DDL** (`StructuredColumnMappingTests`): the Repeater column maps to `text` (extend the existing fact
  in place).
- **`ItemServiceRepeaterTests`:** create round-trip preserves row order and sub-field values; blank rows
  dropped; sub-field Required missing → 400; Select/Radio option membership violation → 400; sub-field
  MaxLength exceeded → 400; parent Required with an all-blank list → 400; a non-object array element →
  400 (deserialize guard); update replaces the list.

**Frontend (`pnpm test` + `vue-tsc`):**
- `RepeaterField`: add / remove / move-up / move-down / edit a sub-field; recursive dispatch renders the
  correct sub-field components.
- Registry: `parse`/`serialize` round-trip; blank-row drop; count list-column format.

**Live gate (real Postgres):** create an `article` with `faqs` (multiple rows incl. a `Select`
sub-field) → 201 → `GET` reads them back **in order** with exact UTF-8 (e.g. `常見問題`); PUT to
reorder + drop a blank row → persisted order; a missing required sub-field → 400; an out-of-options
`Select` value → 400. Confirm the slice-1 `varchar(1)` class is **not** reintroduced (full child JSON
round-trips through the `text` column). Apply `db/migrations/004-article-repeater-column.sql` to the
live (drifted) DB first.

## 9. Non-goals (YAGNI)

Nested `Repeater`; `RichText` / `File` / `Image` / multi-value / `KeyValue` sub-fields; min/max row-count
constraints; per-sub-field i18n; persisted expand/collapse state. Each could become its own later slice
if a real need arises.

## 10. Files touched (anticipated)

- `src/Struo.Domain/Metadata/Models/FieldMetadata.cs` — add `Fields`.
- `src/Struo.Infrastructure/Metadata/MetadataScanner.cs` — Repeater recursion + whitelist + fail-fasts;
  add `Repeater` to `NonTranslatableJsonInterfaces`.
- `src/Struo.Infrastructure/Persistence/SqlSugarClientFactory.cs` — add `Repeater` to
  `JsonColumnInterfaces`.
- `src/Struo.Application/Query/ItemService.cs` — Repeater normalization/validation branch in
  `Deserialize`.
- `samples/Struo.Sample.Blog/` — `FaqItem` POCO + `Article.Faqs`.
- `db/migrations/004-article-repeater-column.sql` — `faqs text NOT NULL DEFAULT '[]'`.
- `frontend/src/lib/fieldTypes/types.ts` + `registry.ts` — `fields?` + `repeaterDef`.
- `frontend/src/components/fields/RepeaterField.vue` — new.
- `frontend/src/types/schema.ts` — `FieldMeta.fields?`.
- Tests as listed in §8.
