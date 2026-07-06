# Phase 7g.5 — Declared field max length: `[CmsField(MaxLength = n)]` (design)

**Date:** 2026-07-06
**Status:** approved (brainstorm), pending implementation plan
**Scope:** a small inserted slice, born from the 7g live-gate varchar(255) bug. Adds a CMS-layer
maximum-length declaration that flows attribute → metadata → schema API → backend write validation
(HTTP 400) → frontend input `maxlength` + client validation. **Deliberately does NOT touch DDL** —
DB column width stays entirely SqlSugar's concern (user decision). Multi-value selects, structured
editors, and multi-file `Files` remain deferred (7g+).

---

## 1. Goal & positioning

Give entity authors a declared, enforced input-length limit:

```csharp
[CmsField(Label = "Name", Required = true, MaxLength = 100)]
public string Name { get; set; } = string.Empty;
```

**`MaxLength` is a CMS-layer (UI + API validation) concept, fully decoupled from the database**
(user decision). The DB column width is governed by SqlSugar's own mechanisms
(`[SugarColumn(Length = n)]`, or its default `varchar(255)`); this phase changes no DDL and no
`SqlSugarClientFactory` logic. The default CMS limit (255) matches SqlSugar's default column width,
so in the out-of-the-box configuration validation (400) always fires before the database exception
(500) that the 7g live gate exposed.

Enforcement is **both ends** (user decision): the admin SPA gets the native `maxlength` attribute
plus a client-side validation message, and — because StruoCMS is headless and the API is called
directly by external programs — `ItemService` validates on write and rejects over-long values with
a clear 400, on both the parent-entity and translation-sidecar paths.

## 2. Semantics

- **Short-string interfaces** (`Text`, `Slug`, `Email`, `Url`, `Password`, `Color`, `Phone`, and
  the option-backed string interfaces `Select`/`MultiSelect`/`Radio`/`CheckboxGroup`/`Tags`):
  undeclared → **effective limit 255** (the SqlSugar default column width); declared → the declared
  value.
- **Content-bearing interfaces** (`RichText`, `Textarea`, `Markdown`, `Code`, `Json` — `text`
  columns since 7g): undeclared → **no limit**; an explicit `MaxLength` is still honored
  (meaningful for `Textarea`; for `RichText` it measures the stored HTML string — the guide will
  say so).
- **Unit:** UTF-16 code units — C# `string.Length`, JS `.length`, and the HTML `maxlength`
  attribute all count the same way, so front and back agree by construction (CJK = 1, emoji
  surrogate pair = 2 on both sides).
- **Startup fail-fast** (`MetadataException`, following the scanner's existing guard idiom):
  negative `MaxLength`, or `MaxLength` declared on a non-string CLR property.
- **Mismatch responsibility:** declaring `MaxLength = 500` while the column is still the SqlSugar
  default `varchar(255)` means values in 256–500 still fail at the DB. Aligning
  `[SugarColumn(Length = n)]` with `MaxLength` is the host developer's responsibility —
  `docs/guide/03-adding-a-collection.md` gains a short section stating exactly this.
- No opt-out of the 255 default for short-string interfaces (YAGNI): content that legitimately
  needs more belongs on a content-bearing interface or an explicit larger `MaxLength` (plus a
  wider column).

## 3. Architecture — effective value resolved once in the scanner (approach A)

The default rule is applied in **one place**: `MetadataScanner.BuildField` computes the *effective*
limit and bakes it into `FieldMetadata.MaxLength` (`int?`):

- attribute `MaxLength > 0` → that value (any string interface);
- attribute unset (0) + short-string interface → `255`;
- attribute unset + content-bearing interface → `null` (unlimited).

Every consumer — schema API, `ItemService`, the SPA — just reads the resolved value; nobody
re-implements "unset means 255". (Rejected approach B: store the raw declaration and let each
consumer apply defaults — three copies of the same rule that can drift.)

Pipeline touchpoints (all existing, no new architecture):

1. **`CmsFieldAttribute`** (Domain) — `public int MaxLength { get; set; }` (0 = unset; attributes
   cannot carry `int?`).
2. **`FieldMetadata`** (Domain) — `public int? MaxLength { get; init; }` (the effective value).
3. **`MetadataScanner.BuildField`** (Infrastructure) — resolution + the fail-fast guards, next to
   the existing `[CmsOptions]` guard. The translatable-field path reuses `BuildField`, so
   translation sidecar fields get the same resolution for free.
4. **Schema API** — `SchemaService` serializes `CollectionMetadata` records directly; `maxLength`
   is exposed automatically (camelCase). No controller change.
5. **`ItemService`** (Application) — in `Deserialize` (non-translatable) and
   `SyncTranslationsAsync` (translatable, per locale), beside the existing Required checks: for
   each field with `MaxLength` and a non-null string value, `value.Length > MaxLength` →
   `QueryException` (existing Program.cs mapping → 400) with a message naming the field and the
   limit, e.g. `field 'name' exceeds maximum length 100`. Runs **after** RichText sanitization
   (the stored value is what gets measured).
6. **Frontend** — `types/schema.ts` `FieldMeta` gains `maxLength?: number | null`;
   `FieldInput.vue` passes `:maxlength="field.maxLength ?? undefined"` to the `text` and
   `textarea` kinds (PrimeVue `InputText`/`Textarea` pass it through to the native attribute);
   `lib/validateItem.ts` adds the length rule beside the Required rule in both loops (shared +
   translatable), message mirroring the backend's. Rich text gets no `maxlength` binding (no
   native attribute on TipTap) — the backend check still covers explicit declarations.

## 4. Data flow

Unchanged write path; the only new behavior is a validation rule evaluated where Required already
is. Reads are unaffected. Existing stored data is unaffected (validation applies on write only).
Backward compatible: `maxLength` in the schema payload is additive; fields resolved to 255 today
were already de-facto capped at 255 by the column — the limit merely becomes visible and friendly.

## 5. Error handling & security

- Over-long write via API → **400** `{ error: { message: "field 'x' exceeds maximum length n" } }`
  (QueryException path), never a DB 22001 → 500, for every field whose effective limit is set.
- Client-side: typing is capped by native `maxlength`; programmatic/paste overflows surface the
  `validateItem` message on submit; server 400s still surface via the existing `serverError` path.
- No new security surface (validation only tightens accepted input).

## 6. Testing & live-gate

- **Backend (TDD):** scanner resolution matrix (declared / undeclared-short / undeclared-content /
  declared-on-content) + fail-fast guards (negative, non-string property); `ItemService` 400 on
  over-long for both paths, boundary pass at exactly the limit; schema endpoint exposes
  `maxLength`. Baseline 325 green + new.
- **Frontend:** `FieldInput` maxlength binding (text + textarea, absent for unlimited);
  `validateItem` length rule (shared + translatable, boundary cases); baseline 173 green + new.
- **Live gate (real PG + Redis + MinIO, API-level):** (1) POST a 256-char value into an
  undeclared Text field → 400 naming the field (the bug-class kill-shot: was a 500);
  (2) 255-char value → 201/200 round-trip; (3) explicit-MaxLength field enforces its declared
  limit; (4) over-long translatable field value (e.g. a long `zh-TW` title) → 400.

## 7. Out of scope (deferred)

- Any DDL/`varchar(n)` mapping from `MaxLength`, or automatic alignment/warnings between
  `MaxLength` and `[SugarColumn(Length)]` (user decision: the two knobs stay independent).
- Plain-text-length semantics for RichText (HTML-string length is what's measured).
- MinLength/regex/other validation rules (YAGNI until asked).
- 7g+: multi-value selects, structured editors, multi-file `Files`.
