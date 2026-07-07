# Phase 7g+ (slice 2) — Structured editors (`Json` + `KeyValue`) (design)

**Date:** 2026-07-07
**Status:** approved (brainstorm), pending implementation plan
**Scope:** the **second 7g+ slice** — light up the `Json` and `KeyValue` field interfaces, which have
rendered read-only since 7g.6. Following the slicing discipline (7d/7e/7f/7g/7g+ slice 1), this slice
is the *scalar structured-value* group only: `Repeater` (repeatable child objects) and multi-file
`Files` remain deferred (still read-only). Like slice 1 this **changes persistence** (a field now
stores a structured JSON value, not a scalar string), so it carries its own DDL consideration and its
own **live gate** on real Postgres.

---

## 1. Goal & positioning

7g.6 built a field-type registry so that adding a type is "one registry entry + one `*Field.vue` +
tests"; 7g+ slice 1 (multi-value selects) exercised that seam for *collection-of-values* fields via
SqlSugar JSON columns. This slice reuses the **exact same persistence mechanism** for two
*single structured value* interfaces:

- `Json` — an arbitrary JSON value (object, array, or scalar). CLR type `JsonElement?`.
- `KeyValue` — a string→string map. CLR type `Dictionary<string, string>`.

Both resolve to `ReadonlyField` today. Neither is option-bound; neither is free-form-item-based like
`Tags`. They are the smallest structured-editor increment that fully reuses slice 1's plumbing (JSON
column mapping + a write-path validation branch), which is why they are grouped together and
`Repeater`/`Files` are deferred.

Per the brainstorm, both fields are **non-translatable, non-sortable, non-searchable**, and
`MaxLength` (7g.5) **does not apply** — identical non-goal contract to slice 1. A structured
aggregate value has no meaningful cross-row sort/search semantics and translating a JSON blob is out
of scope (YAGNI).

## 2. Approach (decided)

**Approach A — real typed properties + SqlSugar JSON columns**, mirroring slice 1 (brainstorm options
B/C rejected for the same reasons recorded there).

- `Json` is a `JsonElement?` property; `KeyValue` is a `Dictionary<string,string>` property.
  `System.Text.Json` already deserializes the incoming JSON straight into these in
  `ItemService.Deserialize` (a `JsonElement?` captures whatever sub-tree the client sent; a
  `Dictionary<string,string>` binds a JSON object), and SqlSugar's JSON column support handles column
  (de)serialization both ways. Projection reflects the value out and the API serializes it back as
  real camelCase JSON for free. Satisfies CLAUDE.md §17.4 (all DB access via SqlSugar ORM, zero
  vendor SQL).
- Rejected **B** (`string` property holding raw JSON text): leaks serialization into the field
  contract, forces the consumer to re-parse, and breaks the "property type = value shape" model.
- Rejected **C for `KeyValue`** (`List<{Key,Value}>` array to guarantee ordering): the map semantic
  (`meta.author`) is what a headless consumer wants; `Dictionary` gives automatic key-uniqueness;
  System.Text.Json preserves insertion order in practice. Strict spec-guaranteed key ordering is not
  a requirement (brainstorm Q3 → A).
- Rejected **C for `Json`** (`Dictionary<string,object?>`): would forbid a top-level array or scalar;
  the user explicitly wanted arbitrary JSON (brainstorm Q2 → A, no top-level-object constraint).

The only new backend surface: two interfaces added to the JSON-column mapping convention, one
structured-value validation branch in `Deserialize`, and two sample fields. On the frontend: two
registry entries + two components.

## 3. Data model

| Interface | CLR property type | Stored (JSON) shape | API shape |
|---|---|---|---|
| `Json` | `JsonElement?` | any valid JSON (`{...}` / `[...]` / scalar) | the same JSON value, verbatim |
| `KeyValue` | `Dictionary<string, string>` | `{"seo-title":"…","author":"…"}` | a camelCase-*keyed*-verbatim JSON object |

**Note on `KeyValue` keys:** the dictionary *keys* are user data, not C# property names, so they are
**not** camelCased — they round-trip verbatim (`"seo-title"` stays `"seo-title"`). Only StruoCMS's own
property names follow the camelCase outbound convention. (This is standard `System.Text.Json`
behaviour: the `CamelCase` naming policy applies to POCO property names, not `Dictionary` keys, unless
`DictionaryKeyPolicy` is set — which StruoCMS does not set.)

**Empty/null representation:**
- `Json` — absent or explicit JSON `null` → C# `null` (a `Nullable<JsonElement>` binds JSON `null` to
  `null`). An empty object `{}` or empty array `[]` is a *present* value, not empty.
- `KeyValue` — absent → empty dictionary `{}`. (The property default is a non-null empty dictionary.)

## 4. Backend write path & scanning

- **Column-type mapping** (`SqlSugarClientFactory` EntityService hook): `Json` and `KeyValue` join the
  JSON-column convention that slice 1 established for the multi-value interfaces — i.e. the hook sets
  `column.IsJson = true; column.DataType = "text";`. The existing set (currently named
  `MultiValueInterfaces`, holding `MultiSelect`/`CheckboxGroup`/`Tags`) is broadened to also contain
  `Json` and `KeyValue`; it will be renamed to reflect "structured JSON value" rather than strictly
  "multi-value", and its comment updated. **`DataType = "text"` is mandatory** — `IsJson` alone leaves
  the CodeFirst length unset and Postgres makes it `varchar(1)`, truncating the payload with Npgsql
  22001 (the slice-1 live-gate finding, `3b4ab40`). An explicit `[SugarColumn]` on the property still
  wins.
  - *Interaction with `ContentBearingInterfaces`:* `Json` is already listed there (line 16), but that
    branch only fires inside `if (property.PropertyType == typeof(string))`. Since the canonical `Json`
    property is now `JsonElement?` (not `string`), it takes the new `IsJson`+`text` branch, which
    returns early. The pre-existing `Json`∈`ContentBearingInterfaces` entry stays harmless (it would
    only apply to a non-canonical `string`-typed `Json` field, which this slice does not introduce);
    it is left untouched to avoid unrelated churn.
  - A DDL regression test asserts the column type (`text` on Postgres, round-trip on SQLite), plus a
    versioned migration script in `db/migrations/` for live DBs (InitTables adds tables, not
    columns — same lesson as 7g / slice 1).
- **Deserialize / validation** (`ItemService.Deserialize`, a new structured branch that reads the
  deserialized property, running alongside the existing scalar required/maxlength loops and the slice-1
  multi-value loop; non-translatable fields only):
  - `Json` (`JsonElement?`): `Required` → the value must be present (`HasValue`); absent/`null` → 400
    `Field '{name}' is required.`. No format validation is possible or needed — the request body is
    already parsed JSON, so a `Json` field value is well-formed JSON by construction. `{}`/`[]` count
    as present.
  - `KeyValue` (`Dictionary<string,string>`): every key, trimmed, must be non-empty → else 400
    `Field '{name}' has an entry with an empty key.`. Values may be empty strings. Duplicate keys are
    already impossible (System.Text.Json's last-wins dictionary bind). `Required` → non-empty
    dictionary; empty/absent → 400 `Field '{name}' is required.`. (No de-dup or blank-value coercion
    step is required beyond the empty-key guard.)
  - **`MaxLength` (7g.5) does NOT apply.** As with slice 1, the `MaxLength` 255-default is guarded by
    `prop.PropertyType == typeof(string)`, and `JsonElement?`/`Dictionary<string,string>` are not
    strings, so they never receive the default; an explicitly-declared `MaxLength` on such a property
    already fail-fasts at startup (non-string guard). A characterization test pins this.
- **Scanner** (`MetadataScanner`): `Json`/`KeyValue` fields are **not sortable** and **not searchable**
  (the query DSL does not reach inside a JSON value — out of scope). These interfaces are **not**
  option-bound, so `[CmsOptions]` on them is not expected (existing option-interface validation is
  unaffected).
- **Projection**: unchanged — `Project` reflects the property value out; the API serializes
  `JsonElement?` → its JSON verbatim and `Dictionary<string,string>` → a JSON object.

## 5. Frontend (on the 7g.6 registry)

- **Registry entries** (`lib/fieldTypes/registry.ts`): replace the two `readonlyDef` entries for `json`
  and `keyValue` with real defs.
  - `json`: `defaultValue: null`, `parse: (raw) => raw ?? null` (the value is already a parsed JS
    value from the API), `serialize: (v) => v` (pass the JS value straight through; the blank-textarea
    → `null` coercion happens inside the component before it emits). `listColumn`: minified-JSON string,
    truncated.
  - `keyValue`: `defaultValue: {}`, `parse: (raw) => (raw && typeof raw === 'object' && !Array.isArray(raw) ? raw : {})`,
    `serialize`: build a plain object from the editor rows, dropping entries whose trimmed key is empty
    (last-wins on duplicate keys, matching the backend). `listColumn`: `k1: v1, k2: v2…` joined,
    truncated.
- **Components** (`components/fields/`), uniform contract (`field`, `modelValue`, `disabled` +
  `update:modelValue`):
  - `JsonField.vue` — a PrimeVue `Textarea`. Local text buffer, initialised from
    `JSON.stringify(modelValue, null, 2)` (empty string when `modelValue` is `null`/absent). On input,
    attempt `JSON.parse`: on success clear the error state and emit the parsed value; on failure show an
    inline invalid state + message and **do not emit** (so the last-valid model value is preserved). A
    blank/whitespace buffer emits `null`. (No CodeMirror/Ace — YAGNI; a validated `Textarea` is the
    minimal editor. A richer tree editor can be a later iteration.)
  - `KeyValueField.vue` — a row-based editor modelled on `TagsField.vue`: each row is a `key`
    `InputText` + a `value` `InputText` + a remove `Button`, with an "Add" `Button` below. Internally
    it holds an ordered `{ key, value }[]` working array (so a row may be temporarily blank or a key
    temporarily duplicated while typing); on every change it serializes to a plain object (dropping
    blank keys, last-wins) and emits. Immutable updates throughout (spread, no in-place mutation).
    Because the emitted model is an unordered object, the working-array order is component-local UI
    state; the editor re-derives it from the object on external model changes.
- The `FieldInterface` TS union already includes `json`/`keyValue` (they exist as `readonlyDef`
  today), so no union change; `vue-tsc` continues to enforce registry exhaustiveness.

## 6. Sample fields (for the live gate)

Add two non-required fields to `Article` so the live gate has real columns to exercise:

- `Attributes` — a `Json` field.
- `Meta` — a `KeyValue` field.

Both are mapped to JSON (`text`) columns by the convention. This adds one metadata-scan assertion (the
sample collection now exposes these fields with the right interface/eligibility) and requires a
migration script for pre-existing live DBs.

## 7. Testing & verification

**Backend (unit + integration):**
- structured JSON column DDL (Postgres `text` / SQLite round-trip) — extend the slice-1 DDL regression
  test (or add a sibling) covering `JsonElement?` and `Dictionary<string,string>` columns.
- `Json` create + round-trip: object, array, and scalar top-level values all persist and read back
  verbatim (incl. a non-ASCII string value for UTF-8).
- `KeyValue` create + round-trip: a multi-entry map (incl. a non-ASCII value and a `-`-containing key)
  reads back verbatim with keys un-camelCased.
- validation: `KeyValue` with a blank/whitespace key → 400; `Required` `Json` absent → 400; `Required`
  `KeyValue` empty → 400 (dedicated test collection so `Required` can be exercised without making a
  sample field required — same pattern slice 1 used with `MvThing`).
- scanner: `Json`/`KeyValue` fields are non-sortable/non-searchable; `MaxLength` on such a field is
  rejected at startup (characterization).

**Frontend (unit + component):**
- registry dispatch: `json`/`keyValue` resolve to the new defs (not `readonlyDef`); added to the
  list-eligible set.
- def `parse`/`serialize` round-trips: `json` passes values through and coerces non-objects sensibly;
  `keyValue` drops blank keys and builds a last-wins object; `listColumn.format` output per interface.
- `JsonField.vue`: renders a `Textarea`, initialises from the model, emits the parsed value on valid
  input, shows invalid state and suppresses emit on malformed input, emits `null` on blank; honours
  `disabled`.
- `KeyValueField.vue`: renders one row per entry + an Add control, adds/removes rows immutably, edits
  key/value immutably, serializes to a blank-key-dropped object; honours `disabled`.

**Gates:** backend `dotnet build` clean (warnings-as-errors) + `dotnet test` all green (344 current +
new); frontend `pnpm test` all green (205 current + new), `pnpm vue-tsc` clean, `pnpm build` clean.

**Live gate (real Postgres + Redis, API-level — SQLite-green ≠ Postgres-correct; UTF-8 via PowerShell
`Invoke-RestMethod` or a UTF-8 file, never Big5 curl):**
1. Create an `article` with `translations.en.title`, `attributes` = a JSON object containing a
   non-ASCII string, and `meta` = a map with a `-`-containing key and a non-ASCII value → expect 201.
2. `GET` the item back: `attributes` round-trips verbatim as real JSON (not a string); `meta`
   round-trips verbatim with keys un-camelCased and correct UTF-8 (verified by code point). Both read
   back from the `text` JSON columns (not truncated → the slice-1 `varchar(1)` bug-class is not
   reintroduced).
3. Edit (PUT): change `attributes` to a top-level array and add/remove a `meta` key → expect 200 and
   the change persisted on re-`GET`.
4. `PUT`/create with a `meta` entry whose key is blank → expect **400**
   `Field 'meta' has an entry with an empty key.` (not a 500 — the 7g bug-class guard).

## 8. Scope boundary / non-goals

- **Only `Json` and `KeyValue`.** `Repeater`, `Files`, `Hidden`, `Uuid` continue to render read-only
  (subsequent slices, each its own persistence design + live gate).
- **Non-translatable.** Both live on the parent entity, not the i18n sidecar.
- **No querying inside the value.** Non-sortable and non-filterable; the query DSL is not extended to
  reach inside a JSON value.
- **`MaxLength` is not applied** (it remains a single-string limit). No max key-count / max depth limit
  is introduced (YAGNI).
- **No schema constraint on `Json`.** A `Json` field accepts any valid JSON; there is no per-field JSON
  schema / shape validation in this slice.
- **No rich JSON tree editor.** The `Json` editor is a validated `Textarea`; a structured tree UI is a
  possible later iteration, not this slice.
- **No strict key-ordering guarantee for `KeyValue`.** Keys round-trip in insertion order in practice
  but this is not a spec guarantee (see §2).
- The relations system (`RelationInput`) and the multi-value select interfaces (slice 1) are
  untouched — separate axes.
