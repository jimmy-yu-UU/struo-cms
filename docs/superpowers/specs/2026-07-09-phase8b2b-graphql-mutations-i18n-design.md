# Phase 8b.2b — GraphQL mutations (i18n / translations) (design)

**Date:** 2026-07-09
**Status:** approved (brainstorm); pending implementation plan
**Scope:** the third mutation slice and the **last** of the Phase 8b write series — typed GraphQL
**input** for translatable content, layered on the 8b.1 backbone and the 8b.2a structured inputs.
8b.2b covers the **`translations` input** (locale-keyed per-collection translatable fields) end to end,
**including translatable File/Image** (the per-locale OG image, Phase 5.6) which is simply a
translatable field inside the per-locale field-map. Advanced read querying (cross-relation / deep
filtering / multi-level nesting) remains a separate read-side slice (**8c**) and is not part of
Phase 8b. This slice adds to the `Struo.Api/GraphQl/` layer only — Domain / Application /
Infrastructure are **untouched** and gain **no new packages** (dependency rule §2). **`ItemService`
write logic is unchanged** — all translation validation is reused verbatim.

---

## 1. Goal & positioning

Phase 8b.1 proved the write pipeline (`createX`/`updateX`/`deleteX`, typed scalar own-fields + M2O FK
+ optimistic `version`) and 8b.2a extended it to the non-i18n structured/multi-value kinds (M2M,
File/Image, Files, MultiSelect/CheckboxGroup, Tags, Json/KeyValue, Repeater). Both deliberately kept
`AddWritableFields`'s `if (f.Translatable) continue;` guard, so **no translatable field appears in any
input yet** — every `createX` on a translation-gated collection (e.g. `Article`) therefore fails with
`BAD_USER_INPUT` "default locale required", because the required default-locale translation cannot be
expressed in GraphQL.

8b.2b closes that gap: adding a `[CmsCollection]` with a translation sidecar should also yield a typed
`translations` input, with no per-collection GraphQL code. The write pipeline already validates
translations end to end (`ItemService.SyncTranslationsAsync`: locale enabled, field ⊆ translatable
fields, required present, MaxLength after RichText sanitize, and on create the mandatory default-locale
translation). This slice is a **typed presentation layer over that pipeline**, not new write logic.

### 1.1 Decisions (locked in the brainstorm)

- **Typed per-collection translation fields input** (continues the 8b.1 / 8b.2a decision; rejects a
  generic `Any`-passthrough for the field-map). GraphQL has no native map type and locale codes are
  runtime config (not schema), so the `translations` input is a **list of `{locale, fields}` entries**,
  and `fields` is a **strongly-typed per-collection input** built from the collection's translatable
  fields:

  | Type | SDL | Notes |
  |---|---|---|
  | wrapper (per collection) | `input XTranslationInput { locale: String!, fields: XTranslationFieldsInput! }` | e.g. `ArticleTranslationInput` |
  | fields (per collection) | `input XTranslationFieldsInput { … }` | e.g. `ArticleTranslationFieldsInput`; one field per `f.Translatable`, **all nullable** |
  | the new input field | `translations: [XTranslationInput!]` | added to both `XCreateInput` and `XUpdateInput`, **only when `meta.Translation is not null`** |

  Each translatable field's SDL comes from the **existing** `SchemaTypeMapper.WritableInputSdl`
  (so a translatable `Image`/`File` → `ID`, a translatable `RichText`/`Text`/`Textarea` → `String`,
  etc.). All fields nullable; required-ness is validated server-side (parity with 8b.1/8b.2a).

- **`locale: String!`, `fields: XTranslationFieldsInput!`** are the two non-nullable input fields — an
  entry with no locale or no fields is meaningless, and `ItemService` would reject it anyway; rejecting
  at the schema is stricter-but-consistent (mirrors 8b.2a's `TagItemInput.value: String!`).

- **The one new runtime behaviour is a list→object fold** (§4): GraphQL sends
  `translations: [{locale, fields}]`; `ItemService.SyncTranslationsAsync` consumes the locale-keyed
  object `{ "en": {…}, "zh-TW": {…} }`. A small pure helper folds the pruned list into that object
  before serialization; `MutationInputMapper` stays a field-agnostic serializer.

- **Read-side stays as-is (asymmetric, accepted YAGNI).** The read side projects
  `translations: [Translation!]` with `fields: Any!` (Phase 8). Typing the read side is a separate
  Phase 8 read enhancement, out of 8b.2b scope. A round-trip is therefore "typed in, `Any` out" — it
  works; it is not symmetric. Recorded here so it isn't mistaken for an oversight.

### 1.2 Non-goals (this slice)

- **Typing the read-side `translations`** — deferred (separate read enhancement).
- **Advanced read querying** (cross-relation filtering, nested filter/sort/pagination, multi-level
  nesting) — deferred to **8c**, unrelated to mutations.
- **`payload`-wrapped user errors / bulk mutations / optimistic delete** — unchanged from 8b.1.
- **New sample entity/fields** — none needed. `ArticleTranslation` (a `SeoTranslation`) already carries
  the full live-gate surface: `title` (Text, Required), `body` (RichText — exercises translation-path
  sanitize), `seoTitle` (Text), `seoMetaDescription` (Textarea), and `seoOgImageId` (**Image → ID**,
  the per-locale OG image = translatable File/Image).

## 2. Layer & wiring

Same layer as 8b.1/8b.2a — `Struo.Api/GraphQl/`. Changed files:

```
src/Struo.Api/GraphQl/
  CollectionSchemaBuilder.cs   // build XTranslationInput + XTranslationFieldsInput once in Build();
                               //   AddWritableFields: emit `translations: [XTranslationInput!]` when meta.Translation != null
  MutationResolvers.cs         // fold the pruned `translations` list -> locale-keyed object before ToJsonElement
  (SchemaTypeMapper.cs)        // only if a name helper is added (XTranslationInput / XTranslationFieldsInput names)
```

No change to `MutationInputMapper.cs` (stays the dumb serializer), `GraphQlDataSource.cs`,
`ItemService`, `Program.cs`, or any file outside `Struo.Api/GraphQl/`. No DI, package, or DDL change.

## 3. Input type generation & field mapping

### 3.1 Per-collection types (built once, referenced by both inputs)

Mirrors 8b.2a's Repeater-item-input rule: `XTranslationInput` and `XTranslationFieldsInput` are built
**once per collection in `Build`** (before `BuildCreateInput`/`BuildUpdateInput`), guarded by
`meta.Translation is not null`, and referenced by name in both inputs. Building them inside
`AddWritableFields` would double-register (create and update both call it) and break the schema build.

- **`XTranslationFieldsInput`** (e.g. `ArticleTranslationFieldsInput`) — one input field per
  `f.Translatable` own-field (skipping `Hidden`/`ReadOnly`/`IsSystem`, same as `AddWritableFields`),
  SDL via `SchemaTypeMapper.WritableInputSdl(f.Interface, clr)`, **all nullable**. RuntimeType
  `IReadOnlyDictionary<string, object?>`.
- **`XTranslationInput`** (e.g. `ArticleTranslationInput`) — `{ locale: String!, fields:
  XTranslationFieldsInput! }`. RuntimeType `IReadOnlyDictionary<string, object?>`.

The translatable field set is what the scanner already permits as translatable (scalars, number
families, boolean, date/time, `File`/`Image`, and `Json`; the multi-value/`Files`/`Tags`/`KeyValue`/
`Repeater` interfaces fail-fast on `Translatable=true` at scan — 7g+ slice 3 M4 — so they can never
appear here). `WritableInputSdl` already covers every interface that can reach this point; an included
interface with no mapping would be a startup fail-fast (asserted by a schema-generation test), mirroring
the read/write sides.

### 3.2 `AddWritableFields` extension

Unchanged for own-fields (`if (f.Translatable) continue;` **stays** — translatable fields are carried
by `translations`, never as top-level own-fields). After the existing own-field + relation loops, add:

```
if (meta.Translation is not null)
    config.Fields.Add(new InputFieldConfiguration(
        "translations", null, TypeReference.Parse($"[{XTranslationInputName(meta.Name)}!]")));
```

So `translations` appears in both `XCreateInput` and `XUpdateInput` for translation-gated collections,
and is absent for collections with no sidecar.

### 3.3 Name helpers (`SchemaTypeMapper`)

`XTranslationInputName(collection)` = `Pascal(collection) + "TranslationInput"`;
`XTranslationFieldsInputName(collection)` = `Pascal(collection) + "TranslationFieldsInput"`.

## 4. The one new runtime behaviour: list → locale-keyed fold

HotChocolate coerces `translations` to `List<IReadOnlyDictionary<string,object?>>`, each element
`{ "locale": "...", "fields": { … } }`. `ItemService.SyncTranslationsAsync` reads
`body.translations` as a **locale-keyed object** `{ "<locale>": { <field>: <value> } }`. A small pure
helper folds one into the other, **after** recursive `SentFieldsOnly` (§5) so only client-sent
sub-fields survive, and **before** `MutationInputMapper.ToJsonElement`:

- input: the pruned top-level dict (may contain a `translations` key whose value is a list of entry
  dicts).
- output: a **new** dict (immutable transform — CLAUDE coding-style §immutability) identical to the
  input except `translations`, if present, is replaced by `{ entry.locale : entry.fields }`.
- last-writer-wins on a duplicate locale in the list (degenerate input; `ItemService` would otherwise
  see one object key anyway). A non-string/absent `locale`, or a malformed entry, folds to what
  `ItemService` then rejects with `BAD_USER_INPUT` — no new error path.
- `translations` absent → helper is a no-op (create still requires it via `ItemService`; update stays
  partial-merge).

`MutationInputMapper.ToJsonElement` then serializes the folded dict, producing exactly the REST
`translations` body shape. This helper lives in `MutationResolvers` next to `SentFieldsOnly` (a
private static `FoldTranslations`), called by both `ResolveCreate` and `ResolveUpdate` in the
`SentFieldsOnly(...)` → `FoldTranslations(...)` → `ToJsonElement(...)` order; `MutationInputMapper`
is untouched.

## 5. Prune / validation / errors (reused, near-zero new code)

- **Recursive `SentFieldsOnly` (8b.2a) already handles the nesting.** `translations` is a
  `ListValueNode`; each entry is an `ObjectValueNode` with a nested `fields` `ObjectValueNode`. The
  recursion prunes at every depth, so a client that sends only `{ locale, fields: { title } }` does
  **not** get `body`/`seoOgImageId`/… backfilled to `null` (which would spuriously clear or 400 an
  untouched translatable field). Verified for inline literals **and** `$variable` forms (as in 8b.1/
  8b.2a). The fold runs after the prune, so it folds already-pruned entries.
- **Validation fully reused** from `ItemService.SyncTranslationsAsync`: unknown/disabled locale, field
  not translatable, required translation field missing, MaxLength (after RichText sanitize), RichText
  sanitize on write, and on **create** the mandatory default-locale translation.
- **Errors** reuse `StruoErrorFilter`: all the above → `BAD_USER_INPUT`; stale `version` → `CONFLICT`;
  unknown id on update → `null` (REST 404 parity); RBAC → `FORBIDDEN`. No new error codes.
- **`createX` on a translation-gated collection now succeeds** when the input supplies the default
  locale — the behaviour 8b.1/8b.2a live gates could not reach (they confirmed the 400 boundary).

## 6. RBAC / CSRF / concurrency (all inherited, zero code)

Unchanged from 8b.1: `ItemService` enforces `CanWrite` + `RequireSuperAdminForAdminOnly`; optimistic
`version` on `XUpdateInput` drives the repository compare-and-swap; `POST /graphql` is behind
`CsrfProtectionMiddleware`; create/update re-read via `GetAsync` with the mutation's selection set
(relations + `translations` resolvable) and best-effort fall back to the write result on a
read-denied re-read.

## 7. Testing strategy (TDD — failing test first, §17.2)

In `tests/Struo.Tests` (SQLite for automated tests), gated finally on real Postgres.

1. **Schema generation (unit).** `ArticleCreateInput`/`ArticleUpdateInput` now contain
   `translations: [ArticleTranslationInput!]`; `ArticleTranslationInput` (`locale: String!`,
   `fields: ArticleTranslationFieldsInput!`) and `ArticleTranslationFieldsInput` (one nullable field
   per translatable field, incl. `seoOgImageId: ID`) are emitted; **non-translatable own-fields keep
   their 8b.2a top-level input shape** and no translatable field leaks to the top level; a collection
   with no sidecar has no `translations` input; an unmapped included interface → startup fail-fast; SDL
   snapshot updated.
2. **Fold helper (unit).** list `[{locale,fields}]` → `{ locale: fields }`; absent `translations` →
   no-op; duplicate locale → last-writer-wins; input dict not mutated (returns a new dict).
3. **Execution / integration (HotChocolate executor + SQLite).**
   - `createArticle` with `translations` incl. the default locale → **succeeds** (the 8b.1/8b.2a 400
     boundary is now passable) → re-read returns the translations.
   - `updateArticle` adds a new locale; edits an existing locale; **partial-merge** (send one scalar +
     `version`, no `translations`) leaves translations untouched.
   - **Recursive prune regression:** an entry sending only `title` (not `body`/`seoOgImageId`) → unsent
     sub-fields fall to their translation-entity defaults and do **not** 400 (inline literal **and**
     `$variable`).
   - Missing required translation field → `BAD_USER_INPUT`; unknown/disabled locale → `BAD_USER_INPUT`;
     create missing the default locale → `BAD_USER_INPUT`; RichText in a translatable field is sanitized
     on write.
4. **Live gate (real Postgres).** `createArticle` with `en` + `zh-TW` (CJK exact by code point), `body`
   RichText carrying an XSS vector → stripped on read, `seoOgImageId` differing per locale → each
   round-trips (resolved `File` on read); `updateArticle` partial-merge does not clear untouched
   locales/fields; create missing the default locale → `BAD_USER_INPUT`. Evidence recorded per the
   project live-gate convention (SQLite-green ≠ Postgres-correct).

**Verification baseline continuity:** backend `dotnet build -warnaserror` clean (0 warnings) +
`dotnet test` all green (500 baseline + new 8b.2b tests). Frontend untouched (237).

## 8. Risks & open points

- **Nested null-backfill in `fields`** — already addressed by the recursive `SentFieldsOnly` shipped in
  8b.2a; covered by the inline-and-`$variable` prune regression tests and the live gate. The one new
  code path (the fold) runs after the prune and is unit-tested in isolation.
- **Read/write asymmetry** (typed input, `Any` read) — accepted §1.1; a future read enhancement can
  type the read side symmetrically.
- **`Any` known limitation (8b.2a §8) does not apply here** — `translations` fields are typed, not
  `Any`, so the empty-object-key `AnyType` variable-coercion masking is not on this path (a translatable
  `Json` field's value is still `Any` and inherits that documented limitation, unchanged).
- **Deferred scope** (typed read-side translations; 8c advanced read querying) recorded so it isn't
  lost; each is a clean follow-up on the pipeline this slice completes.
