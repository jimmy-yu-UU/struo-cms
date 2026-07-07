# Phase 7g+ (slice 2) — Structured editors (`Json` + `KeyValue`) (design)

**Date:** 2026-07-07
**Status:** approved (brainstorm) + revised after an empirical persistence probe (see §2.1); pending
implementation plan
**Scope:** the **second 7g+ slice** — light up the `Json` and `KeyValue` field interfaces, which have
rendered read-only since 7g.6. Following the slicing discipline (7d/7e/7f/7g/7g+ slice 1), this slice
is the *scalar structured-value* group only: `Repeater` (repeatable child objects) and multi-file
`Files` remain deferred (still read-only). Like slice 1 this **changes persistence** (a field now
stores a structured JSON value, not a plain scalar string), so it carries its own DDL consideration
and its own **live gate** on real Postgres.

---

## 1. Goal & positioning

7g.6 built a field-type registry so that adding a type is "one registry entry + one `*Field.vue` +
tests"; 7g+ slice 1 (multi-value selects) exercised that seam for *collection-of-values* fields via
SqlSugar JSON columns. This slice lights up two *single structured value* interfaces:

- `Json` — an arbitrary JSON value (object, array, or scalar).
- `KeyValue` — a string→string map.

Both resolve to `ReadonlyField` today. Neither is option-bound; neither is free-form-item-based like
`Tags`. They are the smallest structured-editor increment, which is why they are grouped together and
`Repeater`/`Files` are deferred.

Per the brainstorm, both fields are **non-translatable, non-sortable, non-searchable**, and
`MaxLength` (7g.5) **does not apply** — identical non-goal contract to slice 1. A structured
aggregate value has no meaningful cross-row sort/search semantics and translating a JSON blob is out
of scope (YAGNI).

## 2. Approach (decided)

**Approach A — real typed properties, persisted as JSON text**, mirroring slice 1's *external*
behaviour (arbitrary JSON in/out) but with the *internal* representation chosen to fit how SqlSugar
actually (de)serializes (see the probe in §2.1).

- `KeyValue` is a `Dictionary<string,string>` property mapped to an `IsJson` + `text` column exactly
  like slice 1's multi-value lists. SqlSugar's JSON-column support round-trips a BCL dictionary
  cleanly; `System.Text.Json` binds the incoming JSON object straight into it in
  `ItemService.Deserialize`; projection reflects it out and the API serializes it back as a JSON
  object.
- `Json` is a **`string?` property holding the raw JSON text**, mapped to a plain `text` column
  (**not** `IsJson`). On write, the field key is stripped from the body before the whole-entity
  deserialize (the existing `StripKeys` pattern used for M2M keys and `translations`) and the property
  is set to the element's raw text; on read, `Project` **parses the stored string back into a
  `JsonElement`** so the API emits real structured JSON, not a quoted string.
- Satisfies CLAUDE.md §17.4 (all DB access via SqlSugar ORM, zero vendor SQL).

### 2.1 Why `Json` is a raw string, not `JsonElement?` (empirical finding)

The brainstorm settled on `JsonElement?` + `IsJson`. A persistence probe (SqlSugarCore 5.1.4.215,
which depends on **Newtonsoft.Json 13.0.2** and uses it to materialize `IsJson` columns) disproved
that choice:

- **Write worked:** a `JsonElement?` serialized to correct JSON text in the column.
- **Read was broken:** SqlSugar materialized the column back into a `System.Text.Json.JsonElement`
  whose backing `JsonDocument` was already disposed — any subsequent use (e.g. the API serializing it)
  threw `InvalidOperationException: Operation is not valid due to the current state of the object`
  (`JsonElementConverter.Write`). Routing `IsJson` through a custom System.Text.Json `ISerializeService`
  did **not** fix it — SqlSugar's `IsJson` read path does not use the serialize service for column
  materialization. So `JsonElement?` via `IsJson` is not viable on this stack.
- **A raw `string?` in a plain `text` column round-trips perfectly** (verified: object/array/nested,
  non-ASCII values intact by code point), and `Dictionary<string,string>` via `IsJson` `text`
  round-trips cleanly (a plain BCL type Newtonsoft handles natively both ways). Hence the split
  representation above. The **external contract is unchanged** — the API still accepts and returns
  arbitrary JSON for a `Json` field; only the internal storage/materialization differs.

Rejected alternatives (unchanged from the brainstorm plus the probe): `object?`/`JsonNode?` (Newtonsoft
materializes them into `JObject`/`JArray` or disposed elements that the STJ API boundary then
mis-serializes); `Dictionary<string,object?>` for `Json` (would forbid a top-level array or scalar,
which the user explicitly wanted).

## 3. Data model

| Interface | CLR property type | Column | Stored text | API shape |
|---|---|---|---|---|
| `Json` | `string?` (raw JSON text) | plain `text`, nullable (**not** `IsJson`) | the raw JSON, verbatim (`{...}` / `[...]` / scalar) | the same JSON value, parsed back to structured JSON on projection |
| `KeyValue` | `Dictionary<string, string>` | `IsJson` + `text` | `{"seo-title":"…","author":"…"}` | a JSON object with keys verbatim |

**Note on `KeyValue` keys:** the dictionary *keys* are user data, not C# property names, so they are
**not** camelCased — they round-trip verbatim (`"seo-title"` stays `"seo-title"`). Only StruoCMS's own
DTO property names follow the camelCase outbound convention.

**Note on `Json` value serialization:** the API's `System.Text.Json` (web defaults) escapes non-ASCII
to `\uXXXX` in the emitted JSON — this is valid JSON that decodes to the original code points (same as
every prior phase's UTF-8 handling); it is not corruption.

**Empty/null representation:**
- `Json` — absent or explicit JSON `null` → stored `null` (SQL NULL); projected as JSON `null`. An
  empty object `{}` or empty array `[]` is a *present* value, not empty.
- `KeyValue` — absent → empty dictionary `{}` (the property default is a non-null empty dictionary).

## 4. Backend write path & scanning

- **Column-type mapping** (`SqlSugarClientFactory` EntityService hook):
  - `KeyValue` joins the JSON-column convention slice 1 established (the set currently named
    `MultiValueInterfaces` → broadened + renamed to reflect "structured JSON value"; the hook sets
    `column.IsJson = true; column.DataType = "text";`). `DataType = "text"` is mandatory — `IsJson`
    alone leaves the CodeFirst length unset and Postgres makes it `varchar(1)`, truncating the payload
    (the slice-1 live-gate finding, `3b4ab40`).
  - `Json` needs **no new mapping branch**: it is a `string?` property, and `Json` is **already** in
    `ContentBearingInterfaces` (line 16), whose existing branch widens an undeclared `string`
    interface to `text`; the existing NRT branch maps `string?` → nullable. So a `string?` `Json`
    property already gets a nullable `text` column. (An explicit `[SugarColumn]` still wins, as
    everywhere.)
- **Write path** (`ItemService.Deserialize`):
  - **Strip `Json` field keys before the whole-entity deserialize.** A `Json` value in the body is an
    arbitrary JSON object/array/scalar; binding it into a `string` property would make
    `System.Text.Json` throw. So the `Json` field names join the existing strip set (alongside M2M
    keys and `translations`). After the entity is deserialized, for each `Json` field: if the original
    body has the (camelCase) key and its `ValueKind != Null`, set the property to
    `element.GetRawText()`; otherwise leave it `null`. (Setter uses `FieldToProperty` +
    reflection, matching the existing branches.)
  - **`KeyValue` validation** (a new structured branch, running alongside the existing
    required/maxlength loops and the slice-1 multi-value loop; non-translatable fields only): every
    key, trimmed, must be non-empty → else 400 `Field '{name}' has an entry with an empty key.`.
    Values may be empty strings. Duplicate keys are already impossible (System.Text.Json last-wins
    dictionary bind). `Required` → non-empty dictionary; empty/absent → 400
    `Field '{name}' is required.`.
  - **`Json` `Required`** is handled by the **existing generic required loop** (line 699): a `string?`
    property that is `null` (absent or explicit JSON `null`) trips `value is null` → 400
    `Field '{name}' is required.`. A present `{}`/`[]`/scalar is a non-null, non-whitespace string, so
    it passes. **No `Json`-specific required code is added** (DRY). No format validation is needed —
    the raw text comes from an already-parsed body element, so it is well-formed by construction.
  - **`MaxLength` (7g.5) does NOT apply.** The 255-default is guarded by `PropertyType == typeof(string)`.
    `KeyValue` is a `Dictionary` (never gets the default). `Json` **is** a `string`, so to keep the
    non-goal exact, the scanner's `MaxLength` default must **exclude** the `Json` interface (it is a
    content-bearing structured value, not a short string). A characterization test pins that a `Json`
    field resolves to `MaxLength == null` (unlimited) and that an explicit `MaxLength` on a `KeyValue`
    (non-string) property fail-fasts at startup.
- **Scanner** (`MetadataScanner`): `Json`/`KeyValue` fields are **not sortable** and **not searchable**
  (the query DSL does not reach inside a JSON value — out of scope). Neither is option-bound, so
  `[CmsOptions]` on them is not expected.
- **Projection** (`ItemService.Project`): `KeyValue` is unchanged — the reflected `Dictionary` serializes
  as a JSON object. `Json` is **special-cased**: the reflected value is a raw JSON `string`; project it
  as a parsed `JsonElement` (`JsonSerializer.Deserialize<JsonElement>(raw)` — a self-contained,
  non-disposed element, unlike the SqlSugar-materialized one) so the API emits structured JSON; a
  `null` stored value projects as `null`.

## 5. Frontend (on the 7g.6 registry)

Unchanged in intent from the brainstorm — the wire contract the frontend sees is still "arbitrary JSON
for `json`, an object for `keyValue`".

- **Registry entries** (`lib/fieldTypes/registry.ts`): replace the two `readonlyDef` entries for `json`
  and `keyValue` with real defs.
  - `json`: `defaultValue: null`, `parse: (raw) => raw ?? null` (the API returns a parsed JS
    value), `serialize: (v) => v` (send the JS value straight through; the blank-textarea → `null`
    coercion happens inside the component). `listColumn`: minified-JSON string, truncated.
  - `keyValue`: `defaultValue: {}`,
    `parse: (raw) => (raw && typeof raw === 'object' && !Array.isArray(raw) ? raw : {})`,
    `serialize`: build a plain object from the editor rows, dropping entries whose trimmed key is empty
    (last-wins on duplicate keys, matching the backend). `listColumn`: `k1: v1, k2: v2…`, truncated.
- **Components** (`components/fields/`), uniform contract (`field`, `modelValue`, `disabled` +
  `update:modelValue`):
  - `JsonField.vue` — a PrimeVue `Textarea`. Local text buffer, initialised from
    `JSON.stringify(modelValue, null, 2)` (empty string when `modelValue` is `null`/absent). On input,
    attempt `JSON.parse`: on success clear the error state and emit the parsed value; on failure show an
    inline invalid state + message and **do not emit** (last-valid model value preserved). A
    blank/whitespace buffer emits `null`. (No CodeMirror/Ace — YAGNI; a validated `Textarea` is the
    minimal editor.)
  - `KeyValueField.vue` — a row-based editor modelled on `TagsField.vue`: each row is a `key`
    `InputText` + a `value` `InputText` + a remove `Button`, with an "Add" `Button` below. Internally
    it holds an ordered `{ key, value }[]` working array (rows may be temporarily blank or a key
    temporarily duplicated while typing); on every change it serializes to a plain object (dropping
    blank keys, last-wins) and emits. Immutable updates throughout. The working-array order is
    component-local UI state, re-derived from the object on external model changes.
- The `FieldInterface` TS union already includes `json`/`keyValue`, so no union change; `vue-tsc`
  continues to enforce registry exhaustiveness.
- **Registry test caveat:** `registry.test.ts` currently uses `getFieldType('json').component` as its
  read-only baseline (line 25); once `json` is a real def, that reference must move to a still-deferred
  interface (e.g. `repeater`).

## 6. Sample fields (for the live gate)

Add two non-required fields to `Article` so the live gate has real columns:

- `Attributes` — a `Json` field (`string?`, `text` column).
- `Meta` — a `KeyValue` field (`Dictionary<string,string>`, `IsJson` `text` column).

This adds one metadata-scan assertion (the sample collection now exposes these fields with the right
interface/eligibility) and requires a migration script for pre-existing live DBs.

## 7. Testing & verification

**Backend (unit + integration):**
- column DDL: a `Json` field maps to a plain nullable `text` column (not `IsJson`); a `KeyValue` field
  maps to an `IsJson` `text` column. Round-trip via actual insert+read on SQLite (extend/sibling the
  slice-1 `MultiValueColumnMappingTests`): `KeyValue` (`Dictionary<string,string>` incl. a `-`-keyed,
  non-ASCII-valued entry) reads back verbatim; a raw-JSON `string` column stores/reads verbatim.
- `Json` create + round-trip through `ItemService`: object, array, and scalar top-level values persist
  and project back as structured JSON (incl. a non-ASCII string value verified by code point); a
  `null`/absent value projects as `null`.
- `KeyValue` create + round-trip: a multi-entry map (incl. a non-ASCII value and a `-`-containing key)
  projects back verbatim with keys un-camelCased.
- validation: `KeyValue` with a blank/whitespace key → 400; `Required` `Json` absent → 400; `Required`
  `KeyValue` empty → 400 (dedicated test collection so `Required` can be exercised without making a
  sample field required — the `MvThing` pattern from slice 1).
- scanner: `Json`/`KeyValue` are non-sortable/non-searchable; a `Json` field resolves to
  `MaxLength == null`; an explicit `MaxLength` on a `KeyValue` property fail-fasts at startup.

**Frontend (unit + component):**
- registry dispatch: `json`/`keyValue` resolve to the new defs (not `readonlyDef`); added to the
  list-eligible set; the read-only baseline assertion updated off `json`.
- def `parse`/`serialize`: `json` passes values through and coerces nullish to `null`; `keyValue` drops
  blank keys and builds a last-wins object; `listColumn.format` output per interface.
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
   back from their `text` columns (not truncated → the slice-1 `varchar(1)` bug-class is not
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
- **`MaxLength` is not applied** (it remains a single-string limit). No max key-count / max depth limit.
- **No schema constraint on `Json`.** Any valid JSON is accepted; no per-field JSON schema validation.
- **No rich JSON tree editor.** The `Json` editor is a validated `Textarea`.
- **No strict key-ordering guarantee for `KeyValue`.** Keys round-trip in insertion order in practice
  but this is not a spec guarantee.
- The relations system (`RelationInput`) and the multi-value select interfaces (slice 1) are
  untouched — separate axes.
