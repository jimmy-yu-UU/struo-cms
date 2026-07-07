# Phase 7g+ (slice 1) — Multi-value selects (MultiSelect / CheckboxGroup / Tags) (design)

**Date:** 2026-07-07
**Status:** approved (brainstorm), pending implementation plan
**Scope:** the **first 7g+ slice** — implement the three multi-value select interfaces that have
rendered read-only since 7g.6. Following the 7d/7e/7f/7g slicing discipline, this is the
*multi-value select* group only; structured editors (`Json`/`KeyValue`/`Repeater`) and multi-file
`Files` remain deferred (still read-only). This slice **changes persistence** (a field now stores an
array, not a scalar), so it carries its own DDL consideration and its own **live gate** on real
Postgres.

---

## 1. Goal & positioning

7g.6 built a field-type registry so that adding a type is "one registry entry + one `*Field.vue` +
tests". This slice is the first real exercise of that seam: it lights up `multiSelect`,
`checkboxGroup`, and `tags`, which today resolve to `ReadonlyField`.

All three hold a **collection of values** rather than a single scalar, which breaks the current
"one `[CmsField]` property → one scalar column" assumption. Two of them are **option-bound**
(the selected values must be a subset of the field's declared `[CmsOptions]`); the third (`tags`) is
**free-form** (arbitrary user-entered items, no option list).

Per the brainstorm, multi-value fields are **non-translatable** in this slice (they live on the
parent entity, like relations — not on the i18n translation sidecar). Instead of per-locale
translation, the display text is handled explicitly (see §3).

## 2. Approach (decided)

**Approach A — real typed list properties + SqlSugar JSON columns** (brainstorm option A; B/C
rejected).

- The field is a genuine list-typed CLR property. `System.Text.Json` already deserializes the
  incoming JSON array straight into it in `ItemService.Deserialize` (no change to that path), and
  SqlSugar's JSON column support handles column (de)serialization both ways — `jsonb` on Postgres,
  JSON-in-text on SQLite. Satisfies CLAUDE.md §17.4 ("all DB access via SqlSugar ORM, zero vendor
  SQL"). Projection reads the list back via reflection and the API serializes it out as a camelCase
  JSON array for free.
- Rejected **B** (`text` column + app-level JSON (de)serialize): reinvents what SqlSugar's JSON
  support already gives us, adds manual glue on both read and write, no advantage given Postgres is
  the supported runtime. Rejected **C** (`string` property holding raw JSON): leaks serialization
  into the field contract and breaks the "property type = value shape" model.

The only genuinely new backend surface is: one column-mapping branch (multi-value interfaces → JSON
column), one array-validation branch, an optional-label `[CmsOptions]` parser tweak, and a small
value type for tags. On the frontend: three registry entries + two/three components.

## 3. Data model

| Interface | CLR property type | Stored (JSON) shape | Display text |
|---|---|---|---|
| `MultiSelect` | `List<string>` | `["tech","ai"]` | resolved from `[CmsOptions]` label (label optional; falls back to value) |
| `CheckboxGroup` | `List<string>` | `["a","b"]` | as above |
| `Tags` (free-form) | `List<TagItem>` | `[{"value":"tech"},{"value":"ai","label":"人工智慧"}]` | `label ?? value` (per-item, optional) |

### `TagItem`

A new framework value type:

```csharp
namespace Struo.Domain.Metadata.Models;

public sealed record TagItem(string Value, string? Label = null);
```

Lives in `Struo.Domain` — a plain POCO record with no external package dependency, so it respects
§2 (Domain references nothing). It is the CLR shape for the `Tags` interface property; serialized
out camelCase as `{ "value": ..., "label": ... }` (label omitted/null when unset).

### The two "display text" mechanisms (user requirement)

The user asked that a multi-value item be able to carry a manually-specified display text, defaulting
to the raw value when unset. Because option-bound and free-form fields differ, this resolves to two
mechanisms:

1. **Option-bound (`MultiSelect`/`CheckboxGroup`) — declaration-level, via `[CmsOptions]`.** The
   label is made **optional** in the attribute syntax: `"value"` → label defaults to `value`;
   `"value:label"` → explicit label. Stored data stays `List<string>` (values only); the label is
   resolved from the field's options at display time. See §4.
2. **Free-form (`Tags`) — per-item, baked into the data.** Since there is no option list to resolve
   against, each stored tag carries its own optional `Label`. Display is `label ?? value`. (The
   scanner currently *permits* `[CmsOptions]` on `Tags` — it is in `OptionInterfaces` — but this
   slice's free-form design does not declare or validate against options for `Tags`; using options
   as tag autocomplete suggestions is out of scope.)

## 4. `[CmsOptions]` — label becomes optional

`MetadataScanner.ParseOptions` currently **requires** `value:label` (an entry with no `:` throws).
Change: an entry with no colon (or a blank label after the colon) sets `Label = Value`. An entry
with a leading colon (empty value) still throws (a value is mandatory).

- `FieldOption` already carries `(Value, Label)`, so downstream (`FieldMetadata.Options`, the
  `/schema` payload, the frontend `asOption` formatter) is unchanged — the label is simply always
  populated now.
- Backward compatible: existing `"draft:Draft"` declarations are unaffected.

## 5. Backend write path & scanning

- **Column-type mapping** (`SqlSugarClientFactory` EntityService hook): extend the existing
  content-bearing convention with a multi-value branch. When a property's `[CmsField]` interface ∈
  { `MultiSelect`, `CheckboxGroup`, `Tags` }, set `column.IsJson = true` (map to a JSON column). An
  explicit `[SugarColumn]` on the property still wins, matching the existing `ColumnDataType`
  override rule. This keeps the entity declaration to just `[CmsField]` + the list property, with no
  hand-written `[SugarColumn(IsJson = ...)]`. A DDL regression test asserts the column type on
  Postgres (and that SQLite round-trips), plus a versioned migration script in `db/migrations/` for
  live DBs (InitTables adds tables, not columns — same lesson as 7g).
- **Deserialize / validation** (`ItemService.Deserialize`, new multi-value branch that reads the
  deserialized list property; runs alongside the existing scalar required/maxlength loops):
  - `MultiSelect`/`CheckboxGroup`: value is `List<string>`; every element must be a member of the
    declared `[CmsOptions]` values → else 400 (`Field '{name}' has value '{v}' not in its options.`).
    `Required` → non-empty list (empty/absent → 400). Duplicate values de-duplicated (keep first).
  - `Tags`: value is `List<TagItem>`; each `Value` must be non-blank (→ 400); `Label` optional (blank
    label coerced to null). `Required` → non-empty list. De-duplicate by `Value` (keep first).
  - **`MaxLength` (7g.5) does NOT apply to multi-value fields** in this slice — it is a single-string
    limit. Two existing scanner facts already make this correct, and **need no change**: (a) the
    `MaxLength` 255-default is guarded by `prop.PropertyType == typeof(string)`
    (`MetadataScanner.cs:240`), so although `MultiSelect`/`CheckboxGroup`/`Tags` are listed in
    `ShortStringInterfaces` (added speculatively in 7g.5), a list-typed property never receives the
    255 default; (b) an explicitly-declared `MaxLength > 0` on a non-string property already
    fail-fasts at startup (`MetadataScanner.cs:234`), so declaring `MaxLength` on a multi-value field
    is rejected — no silent partial behaviour. A characterization test pins both.
- **Scanner** (`MetadataScanner`): multi-value fields are marked **not sortable** and **not
  searchable** (the query DSL does not filter/sort inside a JSON array — out of this slice's scope).
  `OptionInterfaces` **already contains** `MultiSelect`, `CheckboxGroup`, and `Tags`
  (`MetadataScanner.cs:15-19`), so `[CmsOptions]` on the option-bound interfaces already passes the
  option-interface check — **no set change needed**.
- **Projection**: unchanged — `Project` reflects the list property value out; the API serializes
  `List<string>` → JSON string array and `List<TagItem>` → JSON object array (camelCase).

## 6. Frontend (on the 7g.6 registry)

- **Registry entries** (`lib/fieldTypes/registry.ts`): replace the three `readonlyDef` entries for
  `multiSelect`/`checkboxGroup`/`tags` with real defs.
- **Components** (`components/fields/`), uniform contract (`field`, `modelValue`, `disabled` +
  `update:modelValue`):
  - `MultiSelectField.vue` — PrimeVue `MultiSelect` over `field.options`; for `multiSelect`.
  - `CheckboxGroupField.vue` — a group of PrimeVue `Checkbox` (multiple binding) over
    `field.options`; for `checkboxGroup`. (May share an option-bound base with `MultiSelectField` if
    it stays simple; two small components is acceptable.)
  - `TagsField.vue` — PrimeVue chips-style input where each chip's value is editable and can be
    expanded to enter an optional display text; for `tags`. Model value is `TagItem[]`.
- **Def contract**:
  - option-bound: `defaultValue: []`, `parse`: array of strings (`raw ?? []`), `serialize`: array of
    strings (drop blanks, de-dup).
  - tags: `defaultValue: []`, `parse`: array of `{value,label?}` (`raw ?? []`), `serialize`: drop
    items with blank `value`, coerce blank `label` to omitted, de-dup by `value`.
  - `listColumn.format`: option-bound → join resolved labels (value → its option label, fallback
    value) with `, `; tags → join `label ?? value` with `, `. (The `—` empty guard stays in
    `formatCell`; an empty array renders `—`.)
- The `FieldInterface` TS union already includes these three (they exist as `readonlyDef` today), so
  no union change; `vue-tsc` continues to enforce registry exhaustiveness.

## 7. Sample fields (for the live gate)

Add three fields to a sample entity (e.g. `Article` or `Category`) so the live gate has real
columns to exercise:

- a `MultiSelect` with `[CmsOptions]` (one option label omitted to prove the value-fallback),
- a `CheckboxGroup` with `[CmsOptions]`,
- a `Tags` field.

Each is a nullable/defaulted list property mapped to a JSON column. This adds one metadata-scan
assertion (the sample collection now exposes these fields with the right interface/eligibility).

## 8. Testing & verification

**Backend (unit + integration):**
- `[CmsOptions]` optional-label parsing: `"value"` → `Label == Value`; `"value:label"` → explicit;
  leading-colon still throws.
- multi-value JSON column DDL (Postgres `jsonb` / SQLite round-trip) — a DDL regression test.
- create + deep round-trip for each of the three interfaces (incl. a tag with a label and a tag
  without).
- option-membership validation → 400; `Required` empty-array → 400; Tags blank-`value` → 400.
- de-duplication (option-bound and tags) keeps first.
- scanner: multi-value fields are non-sortable/non-searchable; `MaxLength` on a multi-value field is
  rejected at startup.

**Frontend (unit + component):**
- registry dispatch: the three interfaces resolve to the new defs (not `readonlyDef`).
- def `parse`/`serialize` round-trips per interface (incl. tags label-drop and de-dup).
- `listColumn.format` output per interface (labels joined; empty → handled by the `—` guard).
- each `*Field.vue`: renders the correct control, emits `update:modelValue`, honours `disabled`;
  `TagsField` round-trips value + optional display text.

**Gates:** backend `dotnet build` clean (warnings-as-errors) + `dotnet test` all green (333 current +
new); frontend `pnpm test` all green (192 current + new), `pnpm vue-tsc` clean, `pnpm build` clean.

**Live gate (real Postgres + Redis, API-level — SQLite-green ≠ Postgres-correct):**
1. Create an item populating the `MultiSelect`, `CheckboxGroup`, and `Tags` fields (tags: one with a
   label, one without); expect 201.
2. `GET` the item back: all three arrays round-trip; the option-bound arrays are value strings; the
   tags array preserves `{value,label?}` with the label present on one and absent on the other; the
   JSON columns are read back correctly from `jsonb`.
3. Edit (PUT) the arrays (add/remove members, add a label to a previously label-less tag); expect
   200 and the change persisted.
4. Option-membership violation on a `MultiSelect` → 400 (not a 500 — the 7g bug-class guard).

## 9. Scope boundary / non-goals

- **Only the three multi-value select interfaces.** `Json`, `KeyValue`, `Repeater`, `Files`,
  `Hidden`, `Uuid` continue to render read-only (subsequent slices, each its own persistence design
  + live gate).
- **Non-translatable.** Multi-value fields live on the parent entity, not the i18n sidecar. No
  per-locale array support in this slice.
- **No querying inside arrays.** Multi-value fields are non-sortable and non-filterable; the query
  DSL is not extended to reach inside JSON arrays.
- **`MaxLength` is not applied to multi-value fields** (it remains a single-string limit). A max
  *item count* limit is not introduced in this slice (YAGNI; can be a later `[CmsField]` tweak).
- **Input flexibility for tags** (accepting a bare string and normalizing to `{value}`) is not
  required — the frontend always sends `{value, label?}` objects.
- The relations system (`relationInputKind`/`RelationInput`) is untouched — it is a separate axis
  from `FieldInterface`.
