# StruoCMS — Roadmap & Phase Index

> **What this is:** a lightweight index of the phase progression and where each phase's
> design (spec) and TDD plan live. It is **not** the master specification.
>
> **Source of truth:** the StruoCMS master spec (§0–§18) condensed into
> [`/CLAUDE.md`](../CLAUDE.md) (§1 stack · §2 dependency rule · §17 execution rules).
> Each phase runs its own cycle: brainstorm → write-plan (TDD) → execute → verify (§17.1).
>
> Specs live in [`docs/superpowers/specs/`](superpowers/specs/); plans in
> [`docs/superpowers/plans/`](superpowers/plans/).

## Status at a glance

- **Done & merged to `main`:** Phases 0 → 6.9. Phase 6 (auth/session/SSO/RBAC/Redis) fully complete.
- **Phase 6c (SSO) done & live-verified:** external OIDC login (Entra ID / M365), email-only JIT
  provisioning (role-less → public floor), coexists with password login. Live gate passed on real
  Entra ID + Postgres + Redis (login → callback → cookie session → `/api/auth/me` returns the JIT
  user id).
- **Phase 7a (frontend foundation & auth) code-complete, live-smoke-pending:** separate Vue 3 SPA in
  `frontend/` (Vite + Pinia + Vue Router) authenticating against the .NET API; backend gained a
  default-off CORS policy + conditional cross-origin cookie mode. Automated gates are green
  (backend build/tests, frontend unit/component tests, frontend build, and a live Playwright E2E
  login → dashboard → logout). The spec's manual cross-origin HTTPS smoke (§9: Vite HTTPS + API
  HTTPS on distinct origins, real cross-origin cookie round-trip) is **user-driven and still
  pending** — see [`frontend/README.md`](../frontend/README.md) for the run recipe.
- **Phase 7b (collection lists) done & live-verified:** RBAC-aware collection nav
  (`CollectionNav`) and a generic `CollectionListView` (paginated/sortable PrimeVue DataTable driven
  by schema metadata), plus additive `/api/auth/me` permissions. Automated gates green (backend
  build/tests incl. `AuthMePermissionsTests`, frontend unit/component tests, frontend build). **Live
  gate PASSED 2026-07-02 on real Postgres + Redis:** dev API on `:5080` against live PG (`web-struo-cms-db`)
  + Redis; `GET /api/auth/me` for the bootstrap super-admin returned `{ isSuperAdmin: true, permissions: {} }`;
  `GET /api/items/article` returned live rows via the query DSL; Playwright E2E (`auth.spec.ts` +
  `collections.spec.ts`) 2/2 passed in Chromium (login → dashboard/logout; browse Content → Article →
  `/collections/article` → Status column). Article create is correctly gated by the i18n rule
  ("default-locale translation required") — expected, not a defect.
- **Phase 7c (item detail + create/edit/delete forms) done & live-verified (API-level):**
  schema-driven create/edit/delete forms for scalar fields (`ItemForm`/`ItemFormView`/`FieldInput`),
  i18n locale tabs backed by an additive `GET /api/languages` endpoint, `apiClient` put/delete +
  `itemsApi` CRUD, and `authStore.canWrite`/`canDelete` gating wired into the collection-list route.
  Automated gates green: backend `dotnet build` clean + `dotnet test` 262/262; frontend `pnpm test`
  86/86 and `pnpm build` succeeds. **Live gate PASSED 2026-07-02 on real Postgres (`web-struo-cms-db`)
  + Redis:** creating an `article` without a default-locale translation is rejected (400 "default
  locale 'en' required"); creating with `en`+`zh-TW` returns 201 and `GET /api/items/article/{id}`
  round-trips both locales with correct UTF-8 (`繁中標題`/`繁中內文`); edit (PUT) 200, delete 204,
  GET-after 404. **The live gate surfaced and fixed a real Postgres-only bug** (commit `049a8fa`):
  an empty translatable Guid FK (per-locale OG image) hit `Guid.Parse("")` in the translation-sync
  path → HTTP 500 on create; empty/blank now coerces to null (+3 regression tests). *Known follow-up
  (Phase 7d / not 7c scope):* the collection **list** shows translatable columns (Title/SEO) as "—"
  because the list endpoint does not overlay translations and spec §0 defers translated columns; the
  full Playwright UI create→edit→delete flow (`items.spec.ts`) therefore can't identify a row by title
  and is deferred with translated columns. The i18n CRUD contract itself is verified above at the API level.
- **Phase 7d (relation editing) done & live-verified (real PG+Redis):** *sliced to relations only* —
  schema-driven editing for `Dropdown` (M2O), `TagSelect` (M2M), `TreeSelect` (self-ref, cycle-guarded) + a
  read-only `RelatedList` (inbound), via a generic `RelationPicker` + `RelationInput` dispatcher; the collection
  **list** now renders translatable columns (fixes the 7c "—"); a full UI create→edit→delete E2E
  (`relations.spec.ts`) authored. Frontend built on the existing backend write path (M2O FK scalar, M2M
  `SyncM2MAsync`, deep expansion, list translation overlay) + a sample `Article↔Tag` M2M. Merged to main
  (`dbe9bf0`, --no-ff). **LIVE GATE PASSED 2026-07-03** (live Postgres `web-struo-cms-db` + Redis, API-level,
  all 11 steps): create category/tag + article with `category`+`tags`; deep round-trip (M2O+M2M inflate); edit
  category A→B (M2O update); edit tags [T1]→[T1,T2]→[T2] (M2M junction replace); RelatedList inbound
  `filter[categoryId][_eq]` returns the article with its translated title; list translation overlay; delete→404.
  **The live gate surfaced & fixed 3 real backend bugs (all "SQLite-green ≠ Postgres-correct")** — see the
  verification-baseline note. File/Image upload, TipTap, multi-value selects, and structured editors are
  deferred (render read-only) to a later sub-phase (7e+).
- **Phase 7e (media library + File/Image pickers) done & live-verified (PG+Redis+MinIO):**
  *sliced to files only* — a dedicated `/media` Media Library view (browse via `GET /api/items/file`, drag-drop
  bulk upload via `POST /api/files`, per-file delete + Title/Alt edit) plus **select-only** `File`/`Image` field
  pickers (reuse the shared `MediaGrid`; store a scalar `Guid?` like SEO's `OgImageId`, so the form save path is
  unchanged). Frontend was the bulk (Phase 5 file API + `File` CMS collection already existed); one sample field
  added (`Article.HeroImageId`). Final review caught & fixed a Postgres-only empty-`Guid?`→`""`-on-update 500
  (frontend `''`→`null`, scoped to file/image). **Live gate on real PG+Redis+MinIO PASSED** (login; upload→MinIO with
  dimension extraction; `/api/items/file` list; create article w/ `heroImageId` + round-trip; **clear→`null`→200**
  (the empty-`Guid?` fix, confirmed: old `""` payload 500s); presigned thumbnail 302; delete→204→picker raw-id
  fallback) and surfaced **1 backend fix** (`ade382f`): uploads now default `Status="published"` (dimensions are
  extracted synchronously — `draft` gated nothing), and `GET /api/files/{id}` + `/content` now serve **any** status
  to an authenticated caller (cookie or bearer, probed via `AuthenticateAsync`) while anonymous stays published-only,
  so the admin backend can preview draft/archived files. Backend **272/272**, frontend 147/147, `pnpm build` clean.
- **Phase 7f (TipTap rich text + server-side HTML sanitization) done & live-verified (real PG+Redis+MinIO, 2026-07-06):**
  *sliced to the first rich-text slice* — a real TipTap WYSIWYG editor (`RichTextInput.vue`: basic formatting — bold/
  italic/strike, H2/H3, lists, blockquote, code block, link (http/https/mailto), hr, undo/redo — plus **inline images**
  that reuse the Phase 7e media library, stored as a base-independent relative `src` + `data-file-id`), replacing the
  `Textarea` fallback via the existing `FieldInput` dispatcher with **no save-path change**. Critically it also closes the
  **CLAUDE.md §8 gap**: a new `IHtmlSanitizer` port (Application) + `GanssHtmlSanitizer` (Infrastructure, `HtmlSanitizer`
  NuGet package, namespace `Ganss.Xss`) sanitizes every `RichText` value **on write** (both the translation-sidecar and
  parent-entity paths in `ItemService`), with an allowlist mirroring the editor output and blank documents coerced to
  `null`. Automated gates green: backend `dotnet build` clean + `dotnet test` **283/283**; frontend `pnpm test` **157/157**
  and `pnpm build` succeeds. **Live gate PASSED 2026-07-06 on real Postgres (`web-struo-cms-db`) + Redis + MinIO**
  (bootstrap super-admin, API-level): (1) **stored-XSS** — an article `body` carrying `<script>`/`onclick`/`javascript:`
  href/`<iframe>`/`data:` img src/inline `style` reads back as `<p>ok</p><p>y</p><a rel="noopener noreferrer">bad</a><img><p>styled</p>`
  (every vector stripped, legit text + `rel=noopener` kept); (2) **media image** — upload → published file, article `body`
  round-trips `<img src="/api/files/{id}/content" data-file-id="{id}" alt="cat">` with the `<script>` stripped, and
  `GET /api/files/{id}/content` returns **302**; (3) **i18n** — `en` + `zh-TW` titles/bodies round-trip with correct UTF-8
  and `<strong>` preserved. (One non-reproducing transient 500 on a first locale-scoped read at startup; every subsequent
  read — with and without `?locale=` — returned 200, so treated as a first-request warmup blip, not a defect.) Advanced
  rich text (tables/align/colour), multi-value selects, structured editors, and multi-file `Files` remain deferred to 7g+.
- **Phase 7g (advanced rich text) done & live-verified (real PG+Redis+MinIO, 2026-07-06):** the second rich-text slice — the TipTap
  editor gains **basic tables** (insert 3×3 with header row, add/delete rows and columns, toggle header row,
  delete table — no cell merge / column resize), **text alignment** (left/center/right/justify on paragraphs +
  headings), **text colour** (10-swatch palette + free colour picker + clear, via `TextStyle`+`Color`), and
  **sub/superscript** (mutually exclusive via `excludes` — TipTap does NOT exclude them by default), with the
  `GanssHtmlSanitizer` allowlist extended **in lockstep**: tags `table thead tbody tr th td sub sup span` + the
  `style` attribute carrying **exactly two CSS properties** (`color`, `text-align`; every other property and
  `url()`/`expression()` values stripped per-property by Ganss); `colspan`/`rowspan`/`colwidth` deliberately
  rejected. New presentational `RichTextColorMenu`/`RichTextTableMenu` components emit events; the parent runs
  the editor commands. No save-path or `ItemService` change — the existing write-path sanitization picks up the
  new allowlist automatically. Automated gates green (see baseline below). **Live gate PASSED 2026-07-06**
  (API-level, real Postgres `web-struo-cms-db` + Redis + MinIO): (1) round-trip preservation — table with header
  row + `text-align: center`/`right` + colour span (hex normalized to `rgba()`) + sub/sup all read back intact;
  (2) hostile payload — `position:fixed`, `background:url(…)`, `color: expression(…)`, `colspan="5"`, plus the
  full 7f vector suite, all stripped while legit text and `text-align` survive; (3) i18n — `en` + `zh-TW` bodies
  with the new formatting round-trip with correct UTF-8. **The live gate surfaced & fixed 1 real backend bug**
  (`3aaaca9`, the SQLite-green ≠ Postgres-correct class again): SqlSugar CodeFirst mapped every string column to
  `varchar(255)` on Postgres, so a realistic rich-text body failed with Npgsql 22001 → 500; the
  `SqlSugarClientFactory` EntityService hook now maps content-bearing field interfaces
  (`RichText`/`Textarea`/`Markdown`/`Code`/`Json`) to `text` columns (explicit `[SugarColumn]` still wins), with
  a DDL regression test and versioned migration scripts in `db/migrations/` (incl. a retroactive script for the
  audit-D2 `version` columns, which live DBs provisioned before that merge are missing — `InitTables` adds
  tables, not columns). Spec:
  [spec](superpowers/specs/2026-07-06-phase7g-advanced-richtext-design.md) · plan:
  [plan](superpowers/plans/2026-07-06-phase7g-advanced-richtext.md).
- **Phase 7g.5 (declared field max length) done & live-verified (real PG+Redis+MinIO, 2026-07-06):** `[CmsField(MaxLength = n)]` —
  a **CMS-layer** input-length limit deliberately decoupled from the DB column width (which stays SqlSugar's
  concern via `[SugarColumn(Length = n)]`; the guide documents the alignment responsibility). The scanner
  resolves the **effective** limit once into `FieldMetadata.MaxLength` (`int?`): declared value wins; undeclared
  short-string interfaces (Text/Slug/Email/Url/Password/Color/Phone + option-backed) default to **255**
  (matching SqlSugar's default `varchar(255)`, so validation fires before the DB); undeclared content-bearing
  interfaces are unlimited. Fail-fast at startup on negative MaxLength or MaxLength on a non-string property.
  Enforced **both ends**: `ItemService` rejects over-long values on both write paths with 400
  (`Field '{name}' exceeds maximum length {max}.`, `+ for locale '{locale}'` on translations, measured after
  RichText sanitization, exactly-at-limit passes); the SPA binds native `maxlength` on text/textarea and
  mirrors the rule in `validateItem`. Unit = UTF-16 code units on both sides. Spec:
  [spec](superpowers/specs/2026-07-06-phase7g5-field-maxlength-design.md) · plan:
  [plan](superpowers/plans/2026-07-06-phase7g5-field-maxlength.md). **Live gate PASSED 2026-07-06**
  (API-level, real Postgres + Redis + MinIO, 4/4, no fixes needed): 256-char `category.name` → **400**
  `Field 'name' exceeds maximum length 255.` (the 7g bug-class kill-shot — was a 500); 255-char boundary →
  201 + exact round-trip; 256-char `en` title → **400** `... for locale 'en'.`; normal article regression
  intact. *Known residual (final-review minor, deferred):* string fields with interfaces outside both sets
  (`Hidden`, `Uuid`, and the 7g+ `KeyValue`/`Repeater`/`Files`) resolve to no CMS limit while their columns
  stay `varchar(255)` — the old 500 remains possible there (negligible exposure today; option: broaden the
  default to "any non-content-bearing string → 255" as a follow-up one-liner).
- **Verification baseline (2026-07-06, post-7g.6):** frontend `pnpm test` **192/192** passed (177 post-7g.5
  − 8 retired `fieldInputKind` tests + 2 `fieldTypes/types` + 7 registry + 13 `fieldComponents` + 1 new
  `ItemForm` error-jump), `pnpm build` succeeds (pre-existing >500 kB chunk advisory only), `vue-tsc` clean
  (this is what enforces registry exhaustiveness). **Backend untouched** — no `src/**` change on the branch,
  so `dotnet test` stays **333** from post-7g.5. No live gate (frontend-only, no persistence change).
- **Verification baseline (2026-07-06, post-7g.5):** backend `dotnet build` clean +
  `dotnet test` **333** passed / 0 failed (325 post-7g + 3 scanner + 5 ItemService MaxLength). Frontend:
  **177/177** (173 post-7g + 2 FieldInput maxlength + 2 validateItem), `pnpm build` succeeds.
  **Live gate PASSED 2026-07-06** (4/4) — see the Phase 7g.5 row above.
- **Phase 7g.6 (frontend field-type registry + lazy i18n tabs) done — pure refactor, no live gate:** the
  audit's F1+F2+F3 pre-work for 7g+. Per-field-type behaviour (component dispatch, parse/default,
  serialize/coercion, list-column eligibility + formatting) is now a single `lib/fieldTypes/` registry keyed
  by `FieldInterface` (`Record<…>` gives compile-time exhaustiveness; unknown interfaces degrade to read-only),
  the empty-`Guid?` `''`→`null` coercion is single-homed in the file/image def (F2), and `ItemForm` mounts
  only the active locale's translatable fields (F3, with an error→default-locale jump). Zero behaviour change
  for F1/F2 (existing parse/serialize/list/dispatch tests stayed green as characterization); only `ItemForm`'s
  test changed for the deliberate lazy-mount behaviour. No backend/DDL/live-gate (frontend-only). Adding a new
  field type is now one registry entry + one `*Field.vue` + tests.
- **Phase 7g+ slice 1 (multi-value selects) done & live-verified (real PG, 2026-07-07):** the first 7g+ slice —
  `MultiSelect`/`CheckboxGroup` (option-bound, values ⊆ `[CmsOptions]`, stored `List<string>`) and `Tags`
  (free-form, stored `List<TagItem>` where `TagItem = {Value, Label?}` carries an optional per-item display
  text, `label ?? value`). `[CmsOptions]` label is now **optional** (bare `"value"` → label = value; empty
  value still fail-fasts). Values persist via a SqlSugar `IsJson` **`text`** column (the value-shape = the
  CLR property type; System.Text.Json deserializes the array natively); `ItemService.Deserialize` validates
  option membership / non-blank tags / `Required`-non-empty, de-duplicates (keep first), and coerces blank
  tag labels to null — **both** write paths, 400 not 500. Frontend: three registry entries + `MultiSelectField`/
  `CheckboxGroupField`/`TagsField` on the 7g.6 registry (list columns join resolved labels / `label ?? value`).
  Non-translatable, non-sortable, non-searchable (§9). **Live gate PASSED 4/4 on real Postgres** (create with all
  three fields incl. a tag with & without label → exact UTF-8 round-trip verified by code point — `人工智慧` =
  U+4EBA U+5DE5 U+667A U+6167; PUT edit incl. the `amer` value-fallback option; `mars` → **400** "not in its
  options", not 500). **The live gate surfaced & fixed 1 real backend bug** (`3b4ab40`, the SQLite-green ≠
  Postgres-correct class again): SqlSugar `IsJson` leaves the CodeFirst length unset → Postgres made the column
  `varchar(1)` (SQLite ignores declared length, so the round-trip test passed), truncating any real JSON payload
  with Npgsql 22001; the multi-value convention now also sets `DataType = "text"`, with a DDL type assertion added
  and a `db/migrations/001-article-multivalue-columns.sql` (lowercase `text` columns) for pre-existing DBs (the
  live DB boots clean after it). Spec:
  [spec](superpowers/specs/2026-07-07-phase7g-plus-multivalue-selects-design.md) · plan:
  [plan](superpowers/plans/2026-07-07-phase7g-plus-multivalue-selects.md).
- **Verification baseline (2026-07-07, post-7g+ slice 1):** backend `dotnet build -warnaserror` clean +
  `dotnet test` **344** passed / 0 failed (343 post-7g.6-backend-work + 1 CmsOptions empty-value guard; the
  suite also gained TagItem/mapping/multi-value/scanner tests net of the count). Frontend `pnpm test` **205**
  (192 post-7g.6 + 4 option-bound + 4 tags + registry round-trip/format + label-drop), `pnpm vue-tsc` clean,
  `pnpm build` succeeds. **Live gate PASSED 2026-07-07** (4/4 + 1 backend fix `3b4ab40`) — see the row above.
- **Phase 7g+ slice 2 (structured editors: `Json` + `KeyValue`) done & live-verified (real PG, 2026-07-07):**
  the second 7g+ slice — `Json` (arbitrary JSON) and `KeyValue` (string→string map). **A persistence probe
  overturned the brainstorm's `JsonElement?` choice:** SqlSugarCore 5.1.4 materializes `IsJson` columns with
  Newtonsoft, and a `System.Text.Json.JsonElement?` reads back **disposed** (`JsonElementConverter.Write`
  throws). So `Json` is a **`string?` holding raw JSON text** in a **plain `text` column (not `IsJson`)**:
  `ItemService.Deserialize` strips the `Json` field key before the whole-entity deserialize (the existing
  `StripKeys` path) and sets the property to `element.GetRawText()`; `Project` parses the stored string to a
  **fresh** `JsonElement` so the API emits real structured JSON. `KeyValue` is a `Dictionary<string,string>`
  via an `IsJson` **`text`** column (Newtonsoft round-trips a BCL dict cleanly); validated on write — blank
  key → 400, `Required` empty → 400 (dictionary **keys are user data, NOT camelCased** — they round-trip
  verbatim). A non-string map value now also maps to 400 (JsonException → QueryException), not 500. Frontend:
  `JsonField` (validated `Textarea` — malformed input suppresses the emit) + `KeyValueField` (row-based
  key/value) on the 7g.6 registry; sample `Article.Attributes` (Json) + `Article.Meta` (KeyValue).
  Non-translatable, non-sortable, non-searchable; `MaxLength` N/A (`Json` is content-bearing → unlimited).
  **Live gate PASSED 4/4 on real Postgres, no backend fixes:** create 201; `attributes` reads back as a real
  JSON object (`人工智慧` exact by code point) not a string; `meta` keys verbatim incl. a mixed-case `MyKey`
  (proves no camelCasing) and `標題` correct; PUT `attributes` → top-level array + added `meta` key persisted;
  blank `meta` key → 400 (not 500). The slice-1 `varchar(1)` class was **not** reintroduced (full JSON
  round-trips through the `text` columns). Spec:
  [spec](superpowers/specs/2026-07-07-phase7g-plus-structured-editors-design.md) · plan:
  [plan](superpowers/plans/2026-07-07-phase7g-plus-structured-editors.md).
- **Verification baseline (2026-07-07, post-7g+ slice 2):** backend `dotnet build -warnaserror` clean +
  `dotnet test` **353** passed / 0 failed (344 post-slice-1 + StructuredColumnMapping + ItemServiceJsonField(3)
  + ItemServiceKeyValue(4 incl. the final-review bad-value-type test) + scanner + the final-review fixes).
  Frontend `pnpm test` **217** (205 post-slice-1 + 4 JsonField + 4 KeyValueField + 4 registry round-trip/format),
  `pnpm vue-tsc` clean, `pnpm build` succeeds. **Live gate PASSED 2026-07-07** (4/4, no backend fixes) — see
  the row above. A whole-branch review + one fix batch (bad-JSON-value-type → 400, verbatim mixed-case-key
  test, defensive Json projection guard) preceded the merge.
- **Phase 7g+ slice 3 (multi-file `Files`) done & live-verified (real PG + MinIO, 2026-07-07):**
  the third 7g+ slice — `Files` is an **ordered** list of file references (`List<Guid>`, a gallery),
  mirroring the shipped scalar `File`/`Image` contract (store raw ids, project raw ids, frontend
  resolves each for display). Persists via the slice-1 `IsJson` **`text`** column convention (`Files`
  added to `JsonColumnInterfaces`); **all JSON we author is System.Text.Json** (inbound whole-entity
  deserialize binds the guid-string array into `List<Guid>`, outbound projects the raw list) — the only
  Newtonsoft touch is SqlSugar's internal `IsJson` materialization, and `List<Guid>` (a value-type list)
  has none of the slice-2 disposed-`JsonElement?` problem. `ItemService.Deserialize` gained a `Files`
  normalization branch — drops `Guid.Empty`, de-duplicates keeping first (preserving order), and enforces
  `Required` as a non-empty list (→ 400); a non-guid array element → 400 via the pre-existing slice-2
  `JsonException → QueryException` guard (no new code). **No existence validation** (a deleted file
  degrades to a raw-id fallback in the picker, matching the scalar contract). Frontend: `FilesField.vue`
  (PrimeVue `OrderList` drag-reorder + one batched `filter[id][_in]` resolve + missing-id fallback rows)
  and an additive multi-select mode on `MediaGrid` (`multiple`/`selectedIds`/`@toggle`; `FilePicker`
  untouched); sample `Article.Gallery`. Non-translatable, non-sortable, non-searchable; `MaxLength` N/A.
  This slice also **actioned the slice-2 review's deferred M4 item**: a scanner startup fail-fast on
  `Translatable = true` for the JSON-column interfaces (`MultiSelect`/`CheckboxGroup`/`Tags`/`KeyValue`/
  `Files`; `Json` intentionally excluded). **Live gate PASSED 12/12 on real Postgres + MinIO, no backend
  fixes:** upload 3 files → create article with `gallery=[id1,id2,id3]` (201) → reads back **in order** →
  PUT reorder+drop → `[id3,id1]` → PUT duplicate → de-duplicated keep-first `[id1,id3]` → non-guid
  element → **400** (not 500) → delete a referenced file → the id still round-trips in the gallery (the
  raw-id fallback contract). The slice-1 `varchar(1)` class was **not** reintroduced (full guids
  round-trip through the `text` column). Spec:
  [spec](superpowers/specs/2026-07-07-phase7g-plus-multifile-files-design.md) · plan:
  [plan](superpowers/plans/2026-07-07-phase7g-plus-multifile-files.md).
- **Verification baseline (2026-07-07, post-7g+ slice 3):** backend `dotnet build -warnaserror` clean +
  `dotnet test` **361** passed / 0 failed (353 post-slice-2 + 5 ItemServiceFilesField + 2 scanner
  fail-fast + 1 Article scanner; the StructuredColumnMapping DDL fact was extended in place, count
  unchanged). Frontend `pnpm test` **228** (217 post-slice-2 + 2 MediaGrid multi-select + 6 FilesField +
  3 registry), `pnpm vue-tsc` clean, `pnpm build` succeeds (pre-existing >500 kB chunk advisory only).
  **Live gate PASSED 2026-07-07** (12/12, no backend fixes) — see the row above. Migration
  `003-article-files-column.sql` applied to the live (drifted) DB (`gallery text NOT NULL DEFAULT '[]'`).
- **Phase 7g+ slice 4 (`Repeater`) done & live-verified (real PG, 2026-07-08) — completes Phase 7g+:**
  the **last** field interface — `Repeater` is an ordered `List<TChild>` where `TChild` is a POCO of
  `[CmsField]` sub-properties (the **lean scalar** sub-field set: text/number/boolean/date families +
  `Select`/`Radio`; RichText/File/multi-value/nested-Repeater fail-fast at scan). Persisted via the
  slice-1 `IsJson`+`text` convention (the proven `Tags` `List<TagItem>` path; DB storage is Newtonsoft-
  internal, API in/out is System.Text.Json camelCase). `FieldMetadata` gained a nullable self-recursive
  `Fields` (the child sub-field schema); the scanner recurses the `List<T>` element type and fail-fasts
  every out-of-contract declaration (non-`List<T>`, disallowed sub-interface incl. nested `Repeater`,
  translatable parent/sub, empty child). `ItemService.Deserialize` drops fully-blank rows (every sub-field
  null/whitespace-string; numeric/bool `0`/`false` keeps the row) and validates each kept row's sub-field
  Required / `Select`-`Radio` option membership / `MaxLength` (→400), plus parent `Required` as a non-empty
  list; a wrong-shaped element →400 via the existing deserialize guard. Projection is unchanged (the CLR
  list emits as-is). Frontend: `RepeaterField.vue` renders a card list (add/remove/move-up/down, immutable
  updates) that recursively dispatches each sub-field through the field registry (`getFieldType`), wired via
  `repeaterDef` (`repeater` was `readonlyDef`). Non-translatable, non-sortable, non-searchable; parent
  `MaxLength` N/A. **A final whole-branch review surfaced & fixed 1 issue** (`a295e8c`): the scanner accepted
  any single-arg `IEnumerable<T>`, so an interface-typed property (`IList<T>`) booted then 500'd on write
  (`Activator.CreateInstance` on an interface) — the `List<T>` detection is now tightened to
  `typeof(List<>)`, so it fail-fasts at scan with a truthful message. **Live gate PASSED 5/5 on real
  Postgres (`web-struo-cms-db`), no backend fixes:** create an article with `faqs`=[常見問題一/general, Q2]
  → 201; read back **in order** with CJK exact by code point + `category` preserved (the slice-1 `varchar(1)`
  class was **not** reintroduced); PUT reorder + a trailing all-blank row → `[Q2, 常見問題一]` (blank dropped);
  a missing required sub-field → **400** `'question' is required`; an out-of-options `Select` value → **400**
  `not in its options`; cleanup DELETE 204 → GET 404. Migration `004-article-repeater-column.sql` applied to
  the live DB (`faqs text NOT NULL DEFAULT '[]'`). Spec:
  [spec](superpowers/specs/2026-07-07-phase7g-plus-repeater-design.md) · plan:
  [plan](superpowers/plans/2026-07-07-phase7g-plus-repeater.md).
- **Verification baseline (2026-07-08, post-7g+ slice 4):** backend `dotnet build -warnaserror` clean +
  `dotnet test` **378** passed / 0 failed (361 post-slice-3 + 2 scanner recursion + 6 scanner fail-fast +
  7 ItemService Repeater + 1 sample scanner + 1 final-review interface-typed guard; the
  StructuredColumnMapping DDL fact was extended in place). Frontend `pnpm test` **237** (228 post-slice-3 +
  5 RepeaterField + 4 registry repeaterDef; net of 3 re-pointed `'repeater'`-as-readonly-stand-in
  assertions), `pnpm vue-tsc` clean, `pnpm build` succeeds (pre-existing >500 kB chunk advisory only).
  **Live gate PASSED 2026-07-08** (5/5, no backend fixes) — see the row above.
- **Phase 8 (GraphQL — read-only delivery API) done & live-verified (real PG, 2026-07-08):** a HotChocolate v16
  read-only GraphQL API at `/graphql`. A metadata-driven `ITypeModule` builds, per discovered `[CmsCollection]`,
  a strongly-typed `X` object type + `XList {items,total}` + `XFilterInput` + two root queries (`x(id)`,
  pluralised `xs(filter,sort,limit,offset,search,locale)`) — so adding a `[CmsCollection]` yields a typed GraphQL
  surface for free. `FieldInterface`→SDL (Number by CLR type, Tags→`[TagItem!]`, Json/KeyValue→`Any`, Repeater→
  recursive `[XFieldItem!]`, File/Image→`ID`+resolved `File`, Files→`[ID!]`+`[File!]`); typed filter (own scalars +
  M2O FK + and/or); `sort:[String!]` tokens; offset/`total` pagination; optional `locale` + `translations`.
  **Single-level relations reuse the existing `deep` expander** (selection→`DeepSpec`); File/Image/Files resolve to
  `File` nodes via a chunked batch DataLoader (N+1-safe, MaxLimit-chunked so large id sets don't silently truncate).
  RBAC + query-whitelist are **reused verbatim** through an Api-owned `IGraphQlDataSource` adapter over `ItemService`
  (**Domain/Application/Infrastructure untouched; only `HotChocolate.AspNetCore` added**); an `IErrorFilter` maps
  domain exceptions → `code` (FORBIDDEN/NOT_FOUND/BAD_USER_INPUT/CONFLICT/INTERNAL_SERVER_ERROR, masked); introspection
  + Nitro IDE dev-only; max-execution-depth rule. Built subagent-driven (11 tasks, Sonnet impl + Opus review each) +
  final whole-branch review. **Review/live-gate caught & fixed real bugs SQLite+fake-dict tests missed:** a v16
  "fully-populated input dict" filter-explosion (every filtered query was silently wrong), a `translations`-silently-
  empty type-guard, a File-batch `MaxLimit` silent-truncation, an aliased-relation duplicate-key crash, and (live gate)
  **Repeater sub-fields casting a POCO child to a dict → HC0053** (fixed: resolve from dict-or-POCO). **Live gate PASSED
  on real Postgres** (`web-struo-cms-db`): all JSON-text columns round-trip (no `varchar(1)`), CJK exact by code point
  (`人工智慧`=U+4EBA U+5DE5 U+667A U+6167; KeyValue key `標題`=U+6A19 U+984C), File resolution, M2O+M2M relations,
  create/read/delete, FORBIDDEN/BAD_USER_INPUT/missing-null. Backend `dotnet test` **449**, 0 warnings; frontend
  untouched (237). Spec: [spec](superpowers/specs/2026-07-08-phase8-graphql-design.md) · plan:
  [plan](superpowers/plans/2026-07-08-phase8-graphql.md). *Deferred to follow-ups:* mutations (8b), cross-relation &
  multi-level (depth>1) nesting, typed Select/Radio enums, plus polish minors (long→IntFilter operand, Date filter operand).
- **Phase 8b.1 (GraphQL mutations — backbone) done & live-verified (real Postgres, 2026-07-08):** a strongly-typed GraphQL
  **write** surface layered on the Phase 8 read schema — `createX(input, locale)` / `updateX(id, input, locale)` /
  `deleteX(id)` per discovered `[CmsCollection]`, generated from metadata (mirrors the read `ITypeModule`). Per collection
  two `InputObjectType`s: `XCreateInput` (writable **scalar own-fields + M2O FK**, all nullable) and `XUpdateInput` (same +
  `version: Long` optimistic-concurrency token). Resolvers convert the typed input to a `JsonElement` and delegate to the
  existing `ItemService.CreateAsync/UpdateAsync/DeleteAsync` via new write methods on the Api-owned `IGraphQlDataSource`
  adapter (**Domain/Application/Infrastructure untouched, no new packages, no backend write-logic change**); create/update
  **re-read via `GetAsync`** using the mutation's selection set so the returned node behaves exactly like a query result
  (relations expand, `translations` present). Errors reuse the Phase 8 `StruoErrorFilter` (FORBIDDEN / BAD_USER_INPUT /
  NOT_FOUND / CONFLICT / masked INTERNAL_SERVER_ERROR); RBAC (`CanWrite`/`CanDelete` + `RequireSuperAdminForAdminOnly`) and
  CSRF (POST `/graphql` behind `CsrfProtectionMiddleware`) are inherited unchanged. **Scope fence:** scalar + M2O FK +
  `version` only; **translatable own-fields are excluded from the inputs** (typed `translations` input is 8b.2), and M2M /
  File/Image/Files / multi-value / Json / KeyValue / Repeater inputs are **deferred to 8b.2**. Built subagent-driven (Sonnet
  impl + Opus review per task). **Review caught two real bugs the plan's assumptions missed** — both the v16-HotChocolate
  **null-backfill** class (the same root cause as the Phase 8 filter-explosion): the coerced input dict backfills every unsent
  optional field with `null`, so (1) **update** partial-merge would have silently nulled untouched columns, and (2) **create**
  would clobber entity defaults (`status="draft"`→null) / make non-nullable value-type scalars uncreatable — both fixed by a
  `SentFieldsOnly` helper that reads the argument literal so only client-sent fields reach the body (verified for inline-literal
  **and** `$variable` forms). Automated gates green: `dotnet build -warnaserror` **0 warnings** + `dotnet test` **488** (449
  Phase-8 baseline + 39 mutation tests). Frontend untouched (237). Spec:
  [spec](superpowers/specs/2026-07-08-phase8b-graphql-mutations-design.md) · plan:
  [plan](superpowers/plans/2026-07-08-phase8b-graphql-mutations.md). **Live gate PASSED 2026-07-08 on real Postgres
  (`web-struo-cms-db`), 9/9, no backend fixes** (driven via `Category` — non-translatable `name` + self-ref `parentId` M2O
  FK + `version`; `Article` create is gated by the default-locale-translation rule, confirming it needs 8b.2): `createCategory`
  → id + version 0; **CJK** `分類一` round-trips code-point-exact (U+5206 U+985E U+4E00); create with `parentId` re-reads
  `parent { name }`; **partial-merge update** of only `name`+`version` left the untouched `parentId` intact (version 0→1) —
  the null-backfill fix proven on PG; stale `version` → **CONFLICT**; empty required `name` → **BAD_USER_INPUT**; anonymous
  mutation → **FORBIDDEN**; `deleteCategory` → `true` then re-query → `null`; `createArticle` (translatable) →
  **BAD_USER_INPUT** "default locale required" (expected — 8b.2). The create null-backfill fix's defaulted-field case can't be
  exercised on the sample entities (the only defaulted non-translatable scalar, `Article.Status`, sits behind the default-locale
  gate) and stays covered by the inline-and-`$variable` unit tests. *Deferred:* **8b.2** (translatable
  `translations` + M2M + File/Image/Files + multi-value + Json/KeyValue + Repeater inputs) and **8c** (advanced read querying:
  cross-relation / deep filtering / multi-level nesting) — both remain, unchanged by this slice.
- **Phase 8b.2a (GraphQL mutations — structured, non-i18n) done & live-verified (real Postgres, 2026-07-09):** typed
  GraphQL **input** for the non-i18n deferred field kinds, layered on the 8b.1 backbone — M2M relation (`[ID!]`),
  scalar File/Image own-field (`ID`), Files (`[ID!]`), MultiSelect/CheckboxGroup (`[String!]`), Tags
  (`[TagItemInput!]`), Json/KeyValue (`Any`), and Repeater (`[XFieldItemInput!]`, the item input type built **once**
  per field in `Build` and referenced by name in both create+update inputs). All schema generation in
  `CollectionSchemaBuilder`/`SchemaTypeMapper`/`StruoTypeModule`; the input dict flows through the **unchanged**
  `MutationInputMapper.ToJsonElement` into the shape `ItemService` already validates (**Domain/App/Infra untouched, no
  new packages, ItemService/mapper write logic unchanged**). **The one new behaviour** is a **recursive
  `SentFieldsOnly`** (`MutationResolvers`): HotChocolate v16 null-backfills unsent fields of dict-runtime
  `InputObjectType`s at **every** depth (8b.1 pruned only the top level), so a Repeater/Tags nested item would carry
  backfilled `null`s (and a non-nullable value-type sub-field would 500 on the child-POCO deserialize); the recursion
  prunes against the request literal at all depths, and `Any` (Json/KeyValue) is a **provable no-op** (never
  backfilled). Translatable own-fields + typed `translations` input + translatable File/Image stay fenced to **8b.2b**
  (`AddWritableFields` keeps `if (f.Translatable) continue;`). Built subagent-driven (Sonnet impl + Opus review per
  task; final whole-branch Opus review READY-TO-MERGE Yes). Automated gates green: `dotnet build -warnaserror` **0
  warnings** + `dotnet test` **500** (490 8b.1 baseline + 10 new: ~5 SchemaTypeMapper + 3 schema + ~5 execution incl.
  recursive-prune inline & `$variable`). Frontend untouched (237). Spec:
  [spec](superpowers/specs/2026-07-09-phase8b2a-graphql-mutations-structured-design.md) · plan:
  [plan](superpowers/plans/2026-07-09-phase8b2a-graphql-mutations-structured.md). **Live gate PASSED 2026-07-09 on real
  Postgres (`web-struo-cms-db`) + Redis + MinIO, no core backend fixes.** Because every 8b.2a structured field lives on
  the translation-gated `Article`, the gate drove REST-create-base → **`updateArticle` sets all kinds** → read back:
  MultiSelect/CheckboxGroup/Tags(CJK `人工智慧` code-point-exact + `{value}`-only tag → `label:null` via recursive
  prune)/Json(nested, CJK)/KeyValue(`MyKey` **verbatim mixed-case, not camelCased** + CJK key `標題`)/Files/Image
  (resolved `File` node)/Repeater(CJK `常見問題一` + `Select` option + omitted `answer`→`""` POCO default via recursive
  prune)/M2M(linked + re-read); the `varchar(1)` class was **not** reintroduced (full JSON through `text` columns);
  **partial-merge** (update `status` only) left every unsent structured/M2M field untouched; M2M `[]` cleared the
  junction; Repeater-required / Repeater-option / M2M-nonexistent-id → **BAD_USER_INPUT**; `createArticle` without
  translations → **BAD_USER_INPUT** "default locale 'en' required" (confirms the 8b.2b boundary); delete → re-query
  `null`. **One known limitation** (accepted, documented in spec §8): an **empty object key** in a Json/KeyValue `Any`
  value passed as a **variable** is rejected by HotChocolate's `AnyType` variable coercion (`ArgumentException`)
  **before** the resolver → masked `INTERNAL_SERVER_ERROR` (REST gives a clean 400); inherent to the `Any`
  representation, fails safely (no write). *Deferred:* **8b.2b** (translatable `translations` + translatable File/Image
  inputs) and **8c** (advanced read querying) remain, unchanged by this slice.
- **Phase 8b.2b (GraphQL mutations — i18n / translations) done & live-verified (real Postgres, 2026-07-09):** the third and
  **last** mutation slice — a typed GraphQL `translations` **input** on every translation-gated collection's
  `createX`/`updateX`, completing the Phase 8b write series. Per collection (only when `meta.Translation is not null`) the
  builder emits, **built once in `Build()`** and referenced by name in both inputs: `XTranslationInput { locale: String!,
  fields: XTranslationFieldsInput! }` + `XTranslationFieldsInput` (one input field per translatable own-field, all nullable,
  SDL via the existing `WritableInputSdl` — so a translatable `Image`/`File` → `ID` = the per-locale OG image, RichText/Text/
  Textarea → `String`); `AddWritableFields` gains `translations: [XTranslationInput!]` and **keeps** its
  `if (f.Translatable) continue;` (translatable fields ride only inside `translations`). **The one new runtime behaviour** is
  `MutationResolvers.FoldTranslations` — an immutable fold of the GraphQL list `[{locale,fields}]` into the locale-keyed
  object `{ "<locale>": {fields} }` that `ItemService.SyncTranslationsAsync` consumes; it runs **after** the recursive
  `SentFieldsOnly` prune (8b.2a, which strips HC v16 null-backfill at every depth incl. the nested `fields` object) and
  **before** `MutationInputMapper.ToJsonElement` (mapper unchanged). Locale codes + field keys serialize **verbatim**
  (`JsonSerializerDefaults.Web` leaves `DictionaryKeyPolicy` null), so `zh-TW` stays `zh-TW`. **Read side stays
  `[Translation!]{locale,fields:Any!}` (accepted asymmetry; typing it is a separate read enhancement).** All validation/errors
  reused verbatim from `ItemService` (unknown/disabled locale, field ⊆ translatable, required present, MaxLength after
  RichText sanitize, create-requires-default-locale) → `BAD_USER_INPUT`/`CONFLICT`/`FORBIDDEN`. **Domain/App/Infra untouched,
  no new packages, `ItemService`/`MutationInputMapper` write logic unchanged.** No new sample entity/fields (the real
  `ArticleTranslation` already carries title/body/seo*/`seoOgImageId`). Built subagent-driven (Sonnet impl + Opus review per
  task; final whole-branch Opus review READY-TO-MERGE Yes, SPEC ✅, 0 Critical/Important). Automated gates green:
  `dotnet build -warnaserror` **0 warnings** + `dotnet test` **513** (500 8b.2a baseline + 13 new: 1 name-helper + 5 schema +
  1 fold + 6 execution). Frontend untouched (237). Spec:
  [spec](superpowers/specs/2026-07-09-phase8b2b-graphql-mutations-i18n-design.md) · plan:
  [plan](superpowers/plans/2026-07-09-phase8b2b-graphql-mutations-i18n.md). **Live gate PASSED 2026-07-09 on real
  Postgres (`web-struo-cms-db`) + Redis + MinIO, 22/22, no backend fixes:** `createArticle` en+zh-TW **succeeds** (the
  8b.1/8b.2a default-locale-400 boundary now passable); `zh-TW` title `你好` code-point-exact (U+4F60 U+597D); `body` RichText
  `<script>`/`onclick` stripped on the translation-sidecar path; per-locale `seoOgImageId` (F1/F2) round-trips; **article-level
  partial-merge** (update `status` only, no `translations` key) leaves both locales fully intact; edit-one-locale leaves the
  other untouched; recursive-prune title-only `$variable` update succeeds (no backfill crash); missing-default-locale /
  unknown-locale / missing-required-`title` → `BAD_USER_INPUT`; delete → re-query `null`. **Semantic note (documented, not a
  defect):** the repo's translation sync is **delete-then-insert per locale** (replace-per-locale), so a locale entry with a
  subset of fields clears the omitted ones within that locale — identical to REST; the partial-merge guarantee is at the
  **article level**, and the recursive prune's role here is preventing a spurious 400/500 from v16 null-backfill, not
  preserving unsent sub-fields. *Deferred:* typed read-side `translations` (separate read enhancement) and **8c** (advanced
  read querying) remain.
- **Phase 8c.1 (GraphQL advanced read querying — cross-relation filter + sort) done & live-verified (real PG, 2026-07-09):** the
  first 8c slice — cross-relation (dotted-path) **filtering** over many-to-one relations, multi-hop, plus
  verified/tested/documented cross-relation **sort** — both by reusing engine/validation machinery already built for
  the REST side (`RelationFilterResolver.RewriteAsync`, `QueryValidator`/`RelationPath.Parse`,
  `SqlSugarItemRepository.RelationOrderExpr`); **no new engine code**. `CollectionSchemaBuilder.BuildFilterInput` adds,
  per M2O relation, a nested `{Target}FilterInput` field referenced **by name** (mirrors the existing self-referential
  `and`/`or` pattern, so `CategoryFilterInput.parent: CategoryFilterInput` self-resolves without a build loop) alongside
  the existing FK `IdFilter` field. `FilterInputTranslator` becomes metadata-aware: it recurses into M2O-relation-named
  keys, accumulating a dotted path prefix (`category.name`, `category.parent.name`), emitting the same
  `FilterNode`/`ComparisonFilter` shape as before — own-field and cross-relation comparisons mix under one implicit
  AND, flowing unchanged into `QueryValidator` → `RelationFilterResolver`. **Sort required no schema change** —
  `sort: [String!]` tokens already threaded through `ParseSort` → `QueryModel.Sort` → `RelationOrderExpr`; this slice
  adds the execution-spy regression test locking `["category.name", "-category.parent.name"]` into two ordered
  `SortToken`s (asc/desc). **Api-only** (`Struo.Api/GraphQl` + tests); `Struo.Domain`/`Struo.Application`/
  `Struo.Infrastructure` untouched, no new NuGet packages. **Caveats (documented, not addressed in this slice):**
  cross-relation sort's `RelationOrderExpr` is a correlated raw ORDER-BY subquery whose SQL shape is
  PostgreSQL/SQLite-specific (audit D9) — unverified on MySQL/SqlServer/Oracle; **sort across a to-many relation is
  rejected** (`BAD_USER_INPUT`); relation paths are depth-capped by `StruoQueryOptions.MaxRelationDepth` (default 5).
  *Deferred to 8c.2:* to-many (O2M/M2M) cross-relation filter, nested-list `filter/sort/limit/offset` arguments,
  multi-level (depth > 1) relation **nesting/expansion**. `dotnet build -warnaserror` **0 warnings** + `dotnet test`
  **526** (513 8b.2b baseline + 13 new: translator dotted-flattening + schema nested-filter-input + query-builder
  metadata-threading + execution-spy filter/multi-hop/AND + the sort-token execution-spy test, plus engine-reuse
  coverage). Frontend untouched (237). Spec:
  [spec](superpowers/specs/2026-07-09-phase8c1-graphql-cross-relation-read-design.md) · plan:
  [plan](superpowers/plans/2026-07-09-phase8c1-graphql-cross-relation-read.md). **Live gate PASSED 2026-07-09 on real
  Postgres (`web-struo-cms-db`) + Redis, 7/7, no backend fixes:** cross-relation CJK filter `category.name eq 資訊8c1`
  (資=U+8CC7 訊=U+8A0A) → the linked article, code-point-exact; multi-hop `category.parent.name eq GateRoot8c1` → the
  same article (server-side `id IN` resolution, independent of the depth-1 read limit); **discrimination** — filter
  `category.name eq GateRoot8c1` → total 0 (the article is NOT in that category, proving the filter actually filters,
  not a spy no-op); `sort:["category.name"]` asc + `["-category.name"]` desc ordered on real PG (the D9 raw-SQL
  ORDER-BY subquery); empty match → empty list; **negatives via the real `QueryValidator`/`RelationPath` →
  `StruoErrorFilter`**: to-many sort `tags.name` → `BAD_USER_INPUT` "Sort across to-many relations is not supported",
  unknown relation `nosuchrel.name` → `BAD_USER_INPUT` "Unknown relation 'nosuchrel'". Final whole-branch review (opus)
  READY-TO-MERGE Yes, 0 Critical/Important.
- **Phase 8c.2 (GraphQL to-many cross-relation filter) done & live-verified (real PG, 2026-07-13):**
  the second 8c slice — cross-relation (dotted-path) **filtering** over one-to-many and many-to-many relations
  (ANY/EXISTS semantics), multi-hop including mixed-kind (e.g. M2O→O2M, O2M→M2M), by relaxing the 8c.1 M2O-only guard
  at two Api-only call sites: `CollectionSchemaBuilder.BuildFilterInput` now emits a nested `{Target}FilterInput` for
  O2M/M2M relations too (previously M2O-only), and `CollectionResolvers.RelationTargets` now recognises O2M/M2M so
  the metadata-aware `FilterInputTranslator` flows the to-many relation key into the same dotted `FieldPath` it
  already built for M2O — unchanged downstream into `QueryValidator` → `RelationFilterResolver` (**no new engine
  code**). **Api-only** (`Struo.Api/GraphQl` + tests); `Struo.Domain`/`Struo.Application`/`Struo.Infrastructure`
  untouched, no new NuGet packages, no sample change. De-risked with a **restored M2M cross-relation filter REST
  integration test** (real engine, SQLite, `CrossRelationFilterTests`) proving the engine's M2M hop still resolves
  correctly end-to-end, plus O2M/M2M schema tests and execution-spy tests confirming the dotted path reaches the
  query builder for to-many relations, single-hop and multi-hop mixed-kind, plus a translator characterization test.
  `dotnet build -warnaserror` **0 warnings** + `dotnet test` **534** (526 8c.1 baseline + 8 new: 2 schema
  nested-filter-input + 4 execution-spy filter/multi-hop-mixed-kind + 1 translator characterization + 1 restored M2M
  engine integration test). Frontend untouched (237). Spec:
  [spec](superpowers/specs/2026-07-09-phase8c2-graphql-tomany-relation-filter-design.md) · plan:
  [plan](superpowers/plans/2026-07-09-phase8c2-graphql-tomany-relation-filter.md). **Live gate PASSED 8/8 on real
  Postgres (`web-struo-cms-db`) + Redis, 2026-07-13, no backend fixes:** M2M `articles(filter:{tags:{name:{eq}}})`
  → the tagged article, control article absent (real filtering, total 1); CJK tag `人工智慧` code-point-exact
  (U+4EBA U+5DE5 U+667A U+6167); O2M `categories(filter:{articles:{status:{eq:"published"}}})` → the child category;
  O2M self-ref `categories(filter:{children:{name:{eq}}})` → the root; multi-hop mixed-kind
  `categories(filter:{articles:{category:{name:{eq}}}})` (O2M→M2O) → the child; discrimination (bogus tag value →
  total 0, proves real filtering not a spy no-op); empty match → empty list, no errors. **Nuance (documented, not a
  defect):** an unknown relation in a typed nested filter is rejected by **HotChocolate v16 query validation**
  ("The specified input object field `nosuchrel` does not exist") — earlier than 8c.1's dotted-string →
  `RelationPath.Parse` → `BAD_USER_INPUT` path; the typed nested-input surface makes an unknown relation a schema
  error, a strictly stronger guard (both are correct rejections). *Deferred to 8c.3:*
  multi-level (depth > 1) relation **nesting/expansion** and nested-list `filter/sort/limit/offset` arguments —
  engine work spanning Domain/App/Infra, not Api-only. Sort across to-many relations remains rejected (unchanged
  from 8c.1).
- **Verification baseline (2026-07-09, post-8c.2):** backend `dotnet build -warnaserror` clean (0 warnings) +
  `dotnet test` **534** passed / 0 failed / 0 skipped (526 8c.1 baseline + 8 new, see bullet above). Frontend
  untouched, `pnpm test` still **237**. **Live gate PASSED 8/8** on real Postgres (`web-struo-cms-db`) + Redis
  (2026-07-13, no backend fixes) — see the Phase 8c.2 row above.
- **Phase 8c.3a (GraphQL advanced read querying — multi-level (depth>1) relation nesting/expansion) done &
  live-verified (real PG, 2026-07-13, 7/7, no backend fixes):** the third 8c slice — relation **expansion** (not filtering) can now
  recurse past a single hop: `DeepRelationSpec` gains a recursive `DeepSpec? Deep` (Domain), so a relation node can
  itself carry a nested `DeepSpec` of further relation nodes to expand, to any depth up to the existing
  `StruoQueryOptions.MaxRelationDepth` (default 5). **Application** — `QueryParser`'s REST `deep` JSON envelope now
  parses **nested** objects (`deep:{category:{deep:{parent:{}}}}`) into the recursive spec (the flat query-string
  form, `?deep=a,b`, stays depth-1 by design — it has no syntax for nesting); `ItemService` validates nesting
  **depth** (≤ `MaxRelationDepth`) and **each level's relation names** against the metadata graph, independent of
  result-set size, before the query runs → over-depth or an unknown relation at any level is `BAD_USER_INPUT` (400).
  **Infrastructure** — `RelationExpander` recurses per level, expanding **breadth-first and batched** (one query per
  relation-node per level, not per row — an explicit N+1-safe regression test locks the query count). **Api** —
  GraphQL's selection-set walker now builds a **nested** `DeepSpec` from the query's actual selection tree, so a
  selection like `article { category { parent { name } } }` resolves the full chain; HotChocolate's
  `max-execution-depth` stays **12** (unchanged) and there is **no GraphQL schema-shape change** (still `deep`
  expansion under the hood, just recursive now). **Behaviour change vs. 8c.1/8c.2:** the old relation-**count** cap
  is now a nesting-**depth** cap — many sibling relations at depth 1 are allowed; only how deep a single chain nests
  is bounded. **Not in this slice (deferred to 8c.3b):** nested-list `filter/sort/limit/offset` **arguments** on
  related list fields — `DeepRelationSpec.Limit` exists but is **unused**; a nested relation **list** currently
  returns **all** rows, unfiltered/unsorted/unpaginated. No new NuGet packages; no sample-entity change. Spec:
  [spec](superpowers/specs/2026-07-13-phase8c3a-multilevel-relation-nesting-design.md) · plan:
  [plan](superpowers/plans/2026-07-13-phase8c3a-multilevel-relation-nesting.md). **Live gate PASSED 2026-07-13 on
  real Postgres (`web-struo-cms-db`) + Redis, 7/7, no backend fixes:** (C2) GraphQL depth-3 M2O chain
  `article→category→parent→parent` resolves with **CJK code-point-exact** at every nested level (孫 U+5B6B → 子
  U+5B50 → 根 U+6839); (C3) GraphQL depth-2 O2M `category→children`; (C4) mixed-kind `category→articles(O2M)→
  category(M2O)`; (C6) self-referential cycle `category.parent.parent`; (C7) GraphQL over-depth (6 self-ref hops)
  **rejected** — **nuance (documented, not a defect):** on GraphQL a self-referential over-depth chain is caught by
  HotChocolate's cyclic-coordinate-depth rule (`HC0087`) **before** it reaches `ItemService`, so the engine's
  `MaxRelationDepth` cap is shadowed on that path; (C7b) the engine depth cap is therefore verified via **REST** —
  a depth-6 nested `deep` envelope returns **400 `Relation nesting too deep (depth 6); the maximum is 5.`** (the
  `ItemService.ValidateDeepTree` message, confirming the Task-3 cap fires on real PG regardless of result-set size);
  (C8) REST nested envelope `deep:{category:{deep:{parent:{deep:{parent:{}}}}}}` resolves the full chain, CJK exact.
  No SQLite-green ≠ Postgres-correct bug surfaced — the recursive engine, validation, and envelope nesting all
  worked on real Postgres first try. Both over-depth rejections (HC0087 on GraphQL, `BAD_USER_INPUT`/400 on REST)
  are correct.
- **Verification baseline (2026-07-13, post-8c.3a):** backend `dotnet build -warnaserror` clean (0 warnings) +
  `dotnet test` **549** passed / 0 failed / 0 skipped (534 8c.2 baseline + 15 new: recursive `DeepRelationSpec` +
  REST envelope nesting + depth/name validation + `RelationExpander` recursion + N+1 batching invariant + GraphQL
  selection-tree recursion + a final-review backfill covering depth>1 recursion through an M2M level). Frontend
  untouched, `pnpm test` still **237**. **Live gate PASSED 7/7** on real Postgres (`web-struo-cms-db`) + Redis
  (2026-07-13, no backend fixes) — see the Phase 8c.3a row above.
- **Phase 8c.3b (GraphQL advanced read querying — nested-list `filter/sort/limit/offset` arguments) done &
  live-verified (real PG, 2026-07-13, 18/18, no backend fixes):** the fourth 8c slice — completes the 8c.3 pair 8c.3a started. A related-list field
  (**to-many only: O2M/M2M**) can now be **shaped**, not just selected/expanded: `filter` (full cross-relation
  dotted-path predicate on the target, reusing the 8c.1/8c.2 `RelationFilterResolver`/`FilterInputTranslator`
  engine), `sort` (own-field-only, multi-key, asc/`-`desc), `limit` (per-parent top-N; omitted or ≤ 0 → all
  rows, an explicit positive value is capped at `MaxLimit` — no implicit default/truncation), and `offset`
  (per-parent skip, ≥ 0, applied before `limit`; negative rejected) — at **every** nesting level up
  to `MaxRelationDepth`. **Domain** — `DeepRelationSpec` gains `FilterNode? Filter`,
  `IReadOnlyList<SortField>? Sort`, `int? Offset` (the pre-existing `Limit` field goes from parsed-but-unused
  to actually consumed). **Application** — `QueryParser.ParseDeepObject` reads the four new envelope keys;
  `ItemService.ValidateDeepTree` rejects args on an **M2O** relation (`BAD_USER_INPUT`), whitelist-validates
  a nested `filter`'s field/relation paths against the **target collection** via `QueryValidator`/
  `RelationPath`, rejects a relation/dotted nested-`sort` token (own-field only), and rejects negative
  `limit`/`offset`; `IItemRepository` gains `QueryWhereInFilteredAsync` (batched "WHERE prop IN values AND
  extraFilter"; a `null` filter is byte-for-byte the pre-8c.3b `QueryWhereInAsync` path, so existing callers
  are unaffected). **Infrastructure** — `RelationExpander.ExpandAsync` gains an `IRelationFilterResolver`
  dependency and a `locale` parameter; a nested `filter` is rewritten via
  `RelationFilterResolver.RewriteAsync` and **pushed into the existing batched query** (ANDed onto the O2M
  reverse-FK `IN` / M2M junction-target `IN`, adding **no** new query), while `sort`/`limit`/`offset` are
  applied **in-memory, per parent group**, before recursing into a further `Deep` (trim-before-recurse: a
  deeper level only expands the rows that survived this level's limit). **Execution strategy A (hybrid,
  chosen over two rejected alternatives — B: full SQL push-down via `ROW_NUMBER() OVER (PARTITION BY …)`,
  rejected as raw vendor SQL forbidden by §17.4 and PG/SQLite-shaped per audit D9; C: per-parent queries,
  rejected as reintroducing N+1):** the N+1-safe batched invariant from 8c.3a is preserved and its test
  **extended** — follow-up query count still equals the relation-node count of the tree, unchanged by row
  counts or by whether args are present. **Api (GraphQL)** — `CollectionSchemaBuilder` declares
  `filter: {Target}FilterInput`, `sort: [String!]`, `limit: Int`, `offset: Int` on O2M/M2M relation fields
  only (**M2O fields get no arguments at all**); `CollectionResolvers.BuildDeep` reads a selection's argument
  values (both inline-literal and `$variable` shapes) into the `DeepRelationSpec` via the same
  `FilterInputTranslator`/`GraphQlQueryBuilder.ParseSort` 8c.1/8c.2 already use. **Back-compat:** an all-null
  arg spec is byte-for-byte the 8c.3a behaviour (characterization tests green); **omitting `limit` still
  returns all rows** (no implicit default/truncation) — only an explicit `limit` paginates. No new NuGet
  packages; no `samples/*` change. Spec:
  [spec](superpowers/specs/2026-07-13-phase8c3b-nested-list-args-design.md) · plan:
  [plan](superpowers/plans/2026-07-13-phase8c3b-nested-list-args.md). **Live gate PASSED 2026-07-13 on real
  Postgres (`web-struo-cms-db`) + Redis, 18/18, no backend fixes:** nested O2M + M2M `filter` narrows
  (discriminating); cross-relation dotted `tags.name` filter (REST **and** GraphQL); per-parent **independent**
  `limit` (catA 3→2 and catB 1→1 in one page); `offset`+`limit` (skip a, take b); `sort` asc/desc strict
  (ASCII); CJK nested-`filter` value round-trips **code-point-exact** (甲 = U+7532); CJK nested-`sort` is
  deterministic and does not error (observed order `乙丙甲` = current-culture collation, confirming the in-memory
  `IComparable`/`String.CompareTo` note — deterministic, not a defect); M2O-args / bad nested-filter-field /
  relation-path nested-`sort` all → `BAD_USER_INPUT` (400); arg-less nested = all rows (8c.3a back-compat).
  **The deferred GraphQL nested `sort`+`offset` items are now live-confirmed:** nested `sort`+`offset` inline
  **and** nested `sort`/`filter` via `$variable` all resolve through the real `/graphql` endpoint. No
  SQLite-green ≠ Postgres-correct bug surfaced — the engine, validation, push-down, and GraphQL arg-reading
  all worked on real Postgres first try. (All initial script failures were harness-only PowerShell quirks —
  single-element-array unwrap + `byte[]` pipeline-unroll — not product defects.) **Remaining known items
  (non-blocking):** the REST query-string `?deep=a,b` flat form stays depth-1 with no args (by design — no
  nesting syntax); a nested list has no `total`/`hasMore` metadata (bare `[Target!]`); a nested `limit` >
  `MaxLimit` clamp is unit-trivial and was not separately exercised on live (only ≤ MaxLimit row counts
  seeded). The implemented `limit` semantics (omitted/≤0 → all rows; explicit positive → capped at
  `MaxLimit`) supersede the spec's §3.4 wording (ambiguous on this point) — this ROADMAP entry and the guide
  are the live contract; the spec is a historical planning artifact.
- **Verification baseline (2026-07-13, post-8c.3b, live-verified):** backend `dotnet build -warnaserror`
  clean (0 warnings) + `dotnet test` **573** passed / 0 failed / 0 skipped (549 8c.3a baseline + 24 new:
  `DeepRelationSpec` filter/sort/offset fields + envelope parse + recursive arg validation +
  `QueryWhereInFilteredAsync` + `RelationExpander` filter push-down/in-memory sort-limit-offset + N+1
  invariant extended with args + GraphQL schema args on O2M/M2M-only + `BuildDeep` arg-reading + nested-list
  execution round-trips + a review backfill locking `_starts_with`/`_ends_with` match-direction semantics).
  Frontend untouched, `pnpm test` still **237**. **Live gate PASSED 18/18** on real Postgres
  (`web-struo-cms-db`) + Redis (2026-07-13, no backend fixes) — see the Phase 8c.3b row above.
- **Phase 9 decomposed into four independent slices** (soft delete / revisions / lifecycle hooks +
  unified response envelope were bundled; each is its own brainstorm → plan → execute → verify cycle):
  **9b soft delete (done, below)**, 9a unified response envelope, 9c revisions, 9d lifecycle hooks. 9b was
  taken first (highest product value, backend self-contained, builds on the existing `OnDelete`/Restrict +
  audit infrastructure).
- **Phase 9b (soft delete — REST + GraphQL, backend-only slice) done & live-verified (real PG, 2026-07-14):**
  per-collection soft delete. A collection opts in by having its entity implement a new
  `ISoftDeletable { DateTime? DeletedAt; Guid? DeletedBy; }` (Domain marker, mirrors `IAuditable` — one
  source of truth, no attribute flag); the scanner derives `CollectionMetadata.SoftDelete` from the
  interface. **Enforcement is a SqlSugar global query filter** (`QueryFilter.AddTableFilter<ISoftDeletable>(e
  => e.DeletedAt == null)` on the request-scoped client): every read path — list, get-by-id, deep relation
  expansion, cross-relation id-resolution, M2M existence, inbound-Restrict — excludes trashed rows **by
  default** with no per-path code (the "floor"). Only the top-level `QueryAsync`/`GetAsync` accept a
  `DeletedFilter { Exclude, Only, With }` mode that lifts it (via `.ClearFilter<ISoftDeletable>()` + an
  `Only` `DeletedAt IS NOT NULL` conditional); `SoftDeleteAsync`/`RestoreAsync` use `Updateable` (not
  filtered) so they locate trashed rows by id. `DELETE` marks (`DeletedAt`/`DeletedBy` stamped from
  `ICurrentUserAccessor.GetCurrentUserId()`, same actor as audit); `?purge=true` / `deleteX(purge:true)` hard-
  removes (today's path, existence-check widened to `With` so an already-trashed row can be purged); `POST
  .../{id}/restore` / `restoreX(id)` reverts. Reads gate `?deleted=only|with` (REST) / `deleted: ONLY|WITH`
  (GraphQL, new `DeletedFilter` enum + arg on the top-level list query) behind the collection's **delete**
  permission (`IPermissionService.CanDelete`) → 403/`FORBIDDEN`. **REST + GraphQL full parity**;
  Domain/Application engine reused by both surfaces (GraphQL via the existing `IGraphQlDataSource` seam); the
  response envelope stays `{ data }` / `{ error }` (unifying it is slice 9a). Built subagent-driven (8 tasks,
  Sonnet impl + per-task review, opus on the risk-bearing tasks + the whole-branch review). Sample `Article`
  and `Category` opt in; migration `db/migrations/005-soft-delete-columns.sql` adds `deletedat timestamp` /
  `deletedby uuid` (lowercase unquoted, matching CodeFirst). `dotnet build -warnaserror` **0 warnings** +
  `dotnet test` **599** (573 8c.3b baseline + 26 new incl. a final-review batch locking deep-expansion /
  `QueryWhereIn` exclusion / non-soft-collection-unaffected / per-query ClearFilter scoping / REST 403).
  Frontend untouched (**237**); the Vue trash/restore/purge UI is deferred to **9b-fe**. **Live gate PASSED
  2026-07-14 on real Postgres (`web-struo-cms-db`) + Redis + MinIO** (REST 12/13 — the one miss a default-list
  pagination artifact on the populated dev DB, disproven by a post-restore GET-by-id success; GraphQL 9/9):
  create → soft-delete → default list excludes → `?deleted=only`+`sort=title` shows it (the decisive check —
  `Only` conditional + `ClearFilter` composing with the D9 raw ORDER-BY translatable-sort subquery on real PG)
  → `?deleted=with` GET returns the trashed row with **CJK code-point-exact** (`軟刪測` = U+8EDF U+522A U+6E2C)
  → restore → visible again → deep-expand of a soft-deleted M2O parent yields null → purge → 404 even with
  `?deleted=with`; `?deleted=banana` → 400; GraphQL parity end-to-end with CJK (`類別` = U+985E U+5225).
  **The live gate surfaced & fixed 1 real Postgres-only bug** (`b76f453`, the SQLite-green ≠ Postgres-correct
  class): `restore`/`soft-delete` set the nullable columns to NULL via an **untyped** SqlSugar parameter →
  Npgsql inferred `text` → PG `42804` "column deletedat is timestamp but expression is text" (SQLite's dynamic
  typing accepted it, so the SQLite suite was green); fixed with the entity-typed
  `.SetColumns(it => new T { DeletedAt = null, DeletedBy = null })` overload (its expression resolver assigns
  the correct `DbType` to the null) + an `ISoftDeletable` generic constraint. Spec:
  [spec](superpowers/specs/2026-07-13-phase9b-soft-delete-design.md) · plan:
  [plan](superpowers/plans/2026-07-14-phase9b-soft-delete.md).
  **Known/deferred:** no `OnDelete.Restrict` relation exists among the sample entities (Article→Category and
  Category→Parent are both `SetNull`), so the "inbound-Restrict counts live references only" claim is proven at
  the **primitive** level (a trashed row is invisible to the batched `QueryWhereInAsync` the Restrict check
  uses — unit-locked) but has no end-to-end 409 demo; a real M2M-onto-trashed-target case likewise needs a
  soft-deletable M2M target (Tag isn't one). Both are documented, not defects.
- **Phase 9b-fe (soft-delete admin UI — frontend-only slice) done & live-verified (real PG, 2026-07-14):**
  the Vue admin UI on top of the 9b backend. The generic collection list (`CollectionListView`) gains an
  **Active / Trash** segmented switch (PrimeVue `SelectButton`, rendered only when the collection is
  soft-deletable **and** the user has delete permission) and a per-row **actions column** (Active → **Delete**;
  Trash → **Restore** + **Delete permanently**), all gated on `canDelete` (backend `403`/`404` remain the real
  guard). Soft-vs-hard delete and all confirm copy live in one pure helper (`lib/deleteAction.ts`): a
  soft-deletable collection soft-deletes with a light "move to trash" confirm; a non-soft collection
  hard-deletes with the unchanged irreversible confirm; **Restore** has no confirm; **purge** uses a strong
  confirm. `itemsApi` gained `deleted` (list mode, `exclude` omitted from the query string), `remove(…, {purge})`,
  and `restore`; `buildListQuery` threads the `deleted` mode; the item form's delete is now soft-delete-aware.
  Feedback reuses the existing inline-error + list-reload pattern (**no** `ToastService`). **The `/api/schema`
  endpoint already emitted `softDelete` per collection — no backend change of any kind.** Built subagent-driven
  (7 tasks, Sonnet impl + per-task review, Opus on the two view tasks + the whole-branch review; final review
  READY-TO-MERGE, 0 Critical / 0 Important). Automated gate: `pnpm test` **261** (237 baseline + 24 new),
  `vue-tsc` clean, `pnpm build` succeeds. **Live gate PASSED 2026-07-14 on real Postgres (`web-struo-cms-db`) +
  Redis + MinIO** (Playwright `frontend/e2e/trash.spec.ts`, 1/1): create → search-isolate → inline soft-delete →
  row leaves Active → Trash shows it → Restore → back in Active → soft-delete again → Trash → Delete permanently →
  gone. **Two test-only live-gate fixes** (validated by the green run, not product defects): the sample `Article`
  `Body` is a **non-required RichText** (TipTap, no `<textarea>`) so the E2E skips it (Title, required, suffices);
  and the populated dev DB means a new row isn't on list page 1, so the E2E **search-isolates** by the searchable
  Title (search state persists across the Active/Trash switch). Spec:
  [spec](superpowers/specs/2026-07-14-phase9b-fe-soft-delete-ui-design.md) · plan:
  [plan](superpowers/plans/2026-07-14-phase9b-fe-soft-delete-ui.md).
- **Phase 9a (unified response envelope — REST, backend-only slice) done & live-verified (real PG),
  2026-07-14:** every REST `/api/*` JSON response now carries one envelope — success
  `{ success:true, data, meta? }` (`meta` list-only, offset-based `{ total, limit, offset }`) and error
  `{ success:false, error:{ code, message, details? } }` (`details` only on `VALIDATION`). Enveloping is
  **centralized in `Struo.Api/Http/`** and controllers were simplified to return raw data / a `PagedResult`
  marker / `ApiResults.Fail(...)`: an `EnvelopeResultFilter` (`IAlwaysRunResultFilter`) wraps successful
  `ObjectResult`s (expanding `PagedResult`→`data`+`meta`) and converts a bare `NotFound()` into a `NOT_FOUND`
  error envelope while leaving `204`/`FileResult`/`RedirectResult` untouched (idempotent — it never
  double-wraps a result already carrying an envelope); a `StruoExceptionHandler` (`IExceptionHandler`)
  **replaced the inline `try/catch` middleware** in `Program.cs`, mapping domain exceptions → status + error
  envelope (401 `UNAUTHORIZED` for anonymous vs 403 `FORBIDDEN` for authenticated `PermissionDeniedException`,
  404 `NOT_FOUND`, 409 `CONFLICT`, 400 `BAD_USER_INPUT`, masked 500 `INTERNAL_SERVER_ERROR`) — placed **before**
  the CSRF/permission middleware so it also envelopes their exceptions; and an
  `InvalidModelStateResponseFactory` emits `VALIDATION` (400) with `details:[{field,message}]`. The `error.code`
  taxonomy is **symmetric with the GraphQL `StruoErrorFilter`**; **GraphQL keeps its own `{ data, errors }`
  envelope untouched**. **Domain/Application/Infrastructure untouched, no new NuGet packages, no DB migration.**
  Built subagent-driven (5 impl tasks, Sonnet impl + Opus review per task; Task 5 the atomic success
  switch-over reviewed with **zero issues**). Automated gate: `dotnet build -warnaserror` **0 warnings** +
  `dotnet test` **634** (599 baseline + 35 new: envelope-types + filter + exception-handler unit tests +
  error/success integration tests); 5 existing tests re-pointed to read under `data` (pure envelope
  re-pointing, no behaviour/assertion change). Frontend **untouched** (261) — the current SPA is
  backward-compatible (`apiClient` unwraps `data ?? payload`, reads `error.message`; `itemsApi.list` reads
  `res.data`+`res.meta.total`); explicit `apiClient`/`ApiError` alignment (branch on `success`, surface
  `code`/`details`) is deferred to **9a-fe**. Spec:
  [spec](superpowers/specs/2026-07-14-phase9a-unified-response-envelope-design.md) · plan:
  [plan](superpowers/plans/2026-07-14-phase9a-unified-response-envelope.md). **Live gate PASSED 2026-07-14 on
  real Postgres (`web-struo-cms-db`) + Redis + MinIO, 11/11, no fixes:** list → `{success,data,meta{total,limit,
  offset}}`; category create → 201 `{success,data}` with CJK `類別9a` code-point-exact (U+985E U+5225) + `version`;
  get → `{success,data}` (no `meta`); unknown id → 404 `NOT_FOUND`; malformed JSON → 400 `VALIDATION` + `details`;
  `?deleted=banana` → 400 `BAD_USER_INPUT`; anonymous read of `user` → 401 `UNAUTHORIZED`; stale `version` update →
  409 `CONFLICT`; `GET /api/files/{id}/content` → **302** to a MinIO presigned URL (**not enveloped**, `content-type`
  absent, no JSON body); `DELETE ...?purge=true` → **bare 204**, empty body; `/api/schema` now `{success,data}`.
  No SQLite-green ≠ Postgres-correct bug surfaced — the filter, exception handler, validation factory, and
  controller simplifications all worked on real Postgres first try. **The final whole-branch review (opus)
  surfaced & fixed 1 real bug** (`ad38cbd`) the per-task gates + live gate missed (the live 404 check
  asserted `code` only, not `message`): `[ApiController]`'s built-in `ClientErrorResultFilter` (order −2000)
  rewrites a bare `NotFound()` into a `ProblemDetails` **before** `EnvelopeResultFilter` runs, so a 404's
  `error.message` leaked the literal `"Microsoft.AspNetCore.Mvc.ProblemDetails"`; fixed with
  `ApiBehaviorOptions.SuppressMapClientErrors = true` (bare 4xx now reaches the filter's already-tested
  `StatusCodeResult` branch → clean `"Resource not found."`) + hardening `Message()` to trust only string
  bodies, verified by a **real-pipeline** `WebApplicationFactory` integration test (RED reproduced the leak →
  GREEN). Final `dotnet test` **635**, 0 warnings.
- **Next up:** **Phase 9b + 9b-fe done & live-verified** (real PG); **9a done, live-gate pending**. Remaining
  Phase 9 slices — **9a-fe** (frontend envelope alignment), **9c** (revisions), **9d** (lifecycle hooks) —
  remain, user's call on order.
  The Phase 8b GraphQL **write** series (8b.1 backbone → 8b.2a structured non-i18n → **8b.2b i18n**) remains **complete**.
  Phase 6.9 resolved the framework-vs-host decision (see "Open architectural decisions" below):
  `Struo.Api` is a reusable base template with convention-based collection discovery. The
  `IPermissionService` port is backed by real RBAC (`RbacPermissionService` + per-request
  snapshot); the allow-all stub is out of the live DI graph.
- **Verification baseline (2026-07-06, post-7g):** backend `dotnet build` clean
  (warnings-as-errors, 0 warnings) + `dotnet test` **325** passed / 0 failed / 0 skipped (316 post-audit-remediation
  + 7 sanitizer allowlist tests + 1 value-level CSS validation test from the final review + 1 DDL text-column
  regression test from the live-gate fix; suite includes the real-PG integration tests). Frontend: **173/173**
  unit/component (161 post-audit + 2 align/sub-sup + 5 colour menu + 5 table menu), `pnpm build` succeeds
  (pre-existing >500 kB chunk-size advisory only). **Live gate PASSED 2026-07-06** (3/3 checks + 1 backend fix
  `3aaaca9`) — see the Phase 7g row above.
- **Verification baseline (2026-07-03, post-7f):** backend `dotnet build` clean (warnings-as-errors);
  `dotnet test` **283** passed / 0 failed / 0 skipped (272 post-7e + 9 sanitizer allowlist tests + 2 RichText
  write-path sanitization tests). Frontend: **157/157** unit/component (147 post-7e + 5 image-url helpers + 4
  RichTextInput editor + 1 image-insert), `pnpm build` succeeds (pre-existing >500 kB chunk-size advisory only).
  Phase 7f live-gate **PASSED 2026-07-06** on real Postgres + Redis + MinIO (stored-XSS strip + media-image round-trip
  + i18n body — see the Phase 7f row above). (Prior post-7d baseline retained below for history.)
- **Verification baseline (2026-07-03, post-7d + live-gate fixes):** backend `dotnet build` clean
  (warnings-as-errors); `dotnet test` **269** passed / 0 failed / 0 skipped (262 prior + 1 sample M2M scan +
  6 across the 3 live-gate fixes). Frontend: **119/119** unit/component (86 prior + 33 for 7d relations),
  `pnpm build` succeeds. **Live gate PASSED on real Postgres + Redis** (Phase 7d relations CRUD/M2M/RelatedList/
  translated list, API-level). **Three live-gate backend fixes** (all SQLite-green ≠ Postgres-correct):
  (1) `a0f02bb` — `ItemService.UpdateAsync` merged only `[CmsField]`, silently dropping M2O/tree relation FK
  updates (editing a relation via PUT did nothing); (2) `26b1a39` — O2M relation metadata exposed
  `foreignKey: null` and the query whitelist rejected FK columns, so `RelatedList` never queried; now a
  collection is filterable by its declared M2O relation FKs and O2M exposes its reverse FK; (3) `df1f1b6` —
  the filter translator stringified every value, so uuid/`Guid` columns (incl. the `id` PK) hit Postgres 42883
  `operator does not exist: uuid = text`; now `ConditionalModel.CSharpTypeName` is set from the column CLR type.
  (Prior 2026-07-02 baseline: backend 262/262, frontend 86/86.)
  Phase 7a's live Playwright E2E (login → dashboard → logout) and Phase 7b's live browse E2E
  (`auth.spec.ts` + `collections.spec.ts`, 2/2 in Chromium) previously passed against the dev API on
  live Postgres + Redis; Phase 7c's live gate PASSED at the API level (create/edit/delete + i18n
  reject/round-trip on live Postgres+Redis — see Phase 7c row above). DB/auth
  features are gated on live Postgres+Redis (SQLite-green ≠ Postgres-correct); Phase 6c OIDC
  round-trip previously verified against live Entra ID.

## Phases

| Phase | Title | Status | Spec (design) | Plan (TDD) |
|---|---|:---:|---|---|
| 0 | Foundation / 地基 (Clean Architecture, DI, SqlSugar, Serilog, health, audit AOP, Scalar) | ✅ | [spec](superpowers/specs/2026-06-25-phase0-foundation-design.md) | [plan](superpowers/plans/2026-06-25-phase0-foundation.md) |
| 1 | Metadata core (`[Cms*]` attributes + startup scanner, cached) | ✅ | [spec](superpowers/specs/2026-06-25-phase1-metadata-core-design.md) | [plan](superpowers/plans/2026-06-25-phase1-metadata-core.md) |
| 2 | Generic CRUD + query DSL (whitelist-validated, no ORM leak) | ✅ | [spec](superpowers/specs/2026-06-26-phase2-crud-dsl-design.md) | [plan](superpowers/plans/2026-06-26-phase2-crud-dsl.md) |
| 3a | Relations foundation (M2O / M2M descriptors, relationship graph) | ✅ | [spec](superpowers/specs/2026-06-26-phase3a-relations-foundation-design.md) | [plan](superpowers/plans/2026-06-26-phase3a-relations-foundation.md) |
| 3b | Cross-relation query (deep expansion, cross-relation filter/sort) | ✅ | [spec](superpowers/specs/2026-06-26-phase3b-cross-relation-query-design.md) | [plan](superpowers/plans/2026-06-26-phase3b-cross-relation-query.md) |
| 4 | i18n (translation sidecars, per-locale read/write, locale guards) | ✅ | [spec](superpowers/specs/2026-06-26-phase4-i18n-design.md) | [plan](superpowers/plans/2026-06-26-phase4-i18n.md) |
| 5 | Files (upload, dimension extraction, local + S3/MinIO storage, references) | ✅ | [spec](superpowers/specs/2026-06-27-phase5-files-design.md) | [plan](superpowers/plans/2026-06-27-phase5-files.md) |
| 5.5 | Identity & schema alignment (Guid/UUIDv7 PKs via `AuditableEntity`) — *inserted* | ✅ | [spec](superpowers/specs/2026-06-29-phase5.5-identity-uuid-alignment-design.md) | [plan](superpowers/plans/2026-06-29-phase5.5-identity-uuid-alignment.md) |
| 5.6 | Multilingual SEO (`SeoTranslation` sidecar base; `ISeoMeta` retired; per-locale OG image) — *inserted* | ✅ | [spec](superpowers/specs/2026-06-29-phase5.6-multilingual-seo-design.md) | [plan](superpowers/plans/2026-06-29-phase5.6-multilingual-seo.md) |
| 6 | Auth / session / SSO / RBAC / Redis — *decomposed into 6a/6b/6c* | ✅ | — | — |
| 6a | Authentication core (User collection, Argon2id, cookie+Redis session, bearer token) | ✅ done (live PG+Redis verified) | [spec](superpowers/specs/2026-06-30-phase6a-auth-core-design.md) · [plan](superpowers/plans/2026-06-30-phase6a-auth-core.md) · [guide](guide/02-authentication.md) | — |
| 6b | Collection-based authorization / RBAC (per-collection rules incl. public read) | ✅ done (live PG verified) | [spec](superpowers/specs/2026-06-30-phase6b-rbac-design.md) | [plan](superpowers/plans/2026-06-30-phase6b-rbac.md) |
| 6c | SSO (external OIDC identity providers) | ✅ done (live-verified: Entra+PG+Redis) | [spec](superpowers/specs/2026-07-01-phase6c-sso-design.md) · [guide](guide/02-authentication.md) | [plan](superpowers/plans/2026-07-01-phase6c-sso.md) |
| 6.9 | Convention-based collection discovery (framework/host decoupling) — *inserted* | ✅ | [spec](superpowers/specs/2026-07-01-phase6.9-convention-collection-discovery-design.md) | [plan](superpowers/plans/2026-07-01-phase6.9-convention-collection-discovery.md) |
| 7 | Vue 3 + PrimeVue + TipTap admin SPA — *decomposed into 7a/…* | ⬜ in progress | — | — |
| 7a | Frontend foundation & auth (Vue 3 SPA scaffold, `apiClient`, `authStore`, router guard, login/dashboard shell, cross-origin CORS+cookie mode) | ⬜ code-complete, live-smoke-pending | [spec](superpowers/specs/2026-07-01-phase7a-frontend-foundation-auth-design.md) | [plan](superpowers/plans/2026-07-01-phase7a-frontend-foundation-auth.md) |
| 7b | Collection lists (RBAC-aware nav, generic paginated/sortable `CollectionListView`, additive `/api/auth/me` permissions) | ✅ done (live-verified: PG+Redis) | [spec](superpowers/specs/2026-07-02-phase7b-collection-lists-design.md) | [plan](superpowers/plans/2026-07-02-phase7b-collection-lists.md) |
| 7c | Item detail + create/edit/delete forms (scalar fields, i18n locale tabs, additive `GET /api/languages`) | ✅ done (live-verified API-level: PG+Redis i18n CRUD) | [spec](superpowers/specs/2026-07-02-phase7c-item-forms-design.md) | [plan](superpowers/plans/2026-07-02-phase7c-item-forms.md) |
| 7d | Relation editing (`Dropdown`/`TagSelect`/`TreeSelect` + read-only `RelatedList`) + list translated columns + full UI CRUD E2E — *sliced to relations only* | ✅ done (live-verified: PG+Redis — relations CRUD + M2M replace + RelatedList + translated list; **+3 live-gate backend fixes**) | [spec](superpowers/specs/2026-07-03-phase7d-relations-design.md) | [plan](superpowers/plans/2026-07-03-phase7d-relations.md) |
| 7e | Media Library + File/Image field pickers (dedicated `/media` view: browse + drag-drop bulk upload + delete/edit; select-only `File`/`Image` pickers in forms) — *sliced to files only* | ✅ done (live-verified: PG+Redis+MinIO — upload/list/pick/clear + presigned thumbnails; **+1 live-gate backend fix: upload-publishes + auth-aware file serving**) | [spec](superpowers/specs/2026-07-03-phase7e-media-library-file-pickers-design.md) | [plan](superpowers/plans/2026-07-03-phase7e-media-library-file-pickers.md) |
| 7f | TipTap rich text (basic formatting + inline images) + server-side HTML sanitization (`IHtmlSanitizer`/`GanssHtmlSanitizer`, write-path, both entity + translation paths) — *sliced to the first rich-text slice* | ✅ done (live-verified: PG+Redis+MinIO — stored-XSS strip + media-image round-trip + i18n) | [spec](superpowers/specs/2026-07-03-phase7f-richtext-tiptap-design.md) | [plan](superpowers/plans/2026-07-03-phase7f-richtext-tiptap.md) |
| 7g | Advanced rich text (basic tables / text-align / colour / sub-superscript) + sanitizer allowlist extended in lockstep (`style` limited to `color`+`text-align`) — *second rich-text slice, deferred from 7f* | ✅ done (live-verified: PG+Redis+MinIO — round-trip + hostile-CSS + i18n; **+1 live-gate backend fix: content-bearing interfaces → `text` columns**) | [spec](superpowers/specs/2026-07-06-phase7g-advanced-richtext-design.md) | [plan](superpowers/plans/2026-07-06-phase7g-advanced-richtext.md) |
| 7g.5 | Declared field max length (`[CmsField(MaxLength = n)]` → metadata/schema → backend 400 validation → frontend `maxlength`; CMS-layer only, decoupled from DB width — *no DDL*) — *inserted; born from the 7g live-gate varchar(255) bug* | ✅ done (live-verified: PG+Redis+MinIO — 256→400 kill-shot + boundary + translatable + regression, 4/4, no fixes) | [spec](superpowers/specs/2026-07-06-phase7g5-field-maxlength-design.md) | [plan](superpowers/plans/2026-07-06-phase7g5-field-maxlength.md) |
| 7g.6 | Frontend field-type registry (`lib/fieldTypes/*`, keyed by `FieldInterface`, TS-exhaustive, unknown→read-only) + empty-`Guid?` coercion single-homed (F2) + lazy i18n tabs (F3) — *pure refactor, no new field types, no backend* | ✅ done (frontend gates green; no live gate — no server/persistence change) | [spec](superpowers/specs/2026-07-06-phase7g6-field-type-registry-design.md) | [plan](superpowers/plans/2026-07-06-phase7g6-field-type-registry.md) |
| 7g+.1 | Multi-value selects (`MultiSelect`/`CheckboxGroup` option-bound `List<string>`; `Tags` free-form `List<TagItem>` w/ optional per-item display label) + `[CmsOptions]` optional label + `IsJson`→`text` column — *first 7g+ slice* | ✅ done (live-verified: real PG — 3-field CRUD + exact-UTF-8 tag-label round-trip + `mars`→400; **+1 live-gate backend fix: `IsJson`→`text`, was `varchar(1)`**) | [spec](superpowers/specs/2026-07-07-phase7g-plus-multivalue-selects-design.md) | [plan](superpowers/plans/2026-07-07-phase7g-plus-multivalue-selects.md) |
| 7g+.2 | Structured editors `Json` (`string?` raw JSON in plain `text` — `JsonElement?` reads back disposed via SqlSugar/Newtonsoft, so strip-on-write + parse-on-project) + `KeyValue` (`Dictionary<string,string>` via `IsJson` `text`, keys verbatim not camelCased) — *second 7g+ slice* | ✅ done (live-verified: real PG — object/array/scalar round-trip + exact-UTF-8 + mixed-case-key verbatim + blank-key→400; **no backend fixes**; +1 pre-merge review fix: bad-value-type→400) | [spec](superpowers/specs/2026-07-07-phase7g-plus-structured-editors-design.md) | [plan](superpowers/plans/2026-07-07-phase7g-plus-structured-editors.md) |
| 7g+.3 | Multi-file `Files` (`List<Guid>` ordered gallery via `IsJson` `text`; mirrors scalar File/Image raw-id contract; STJ in/out; drag-reorder `OrderList` + batched `filter[id][_in]` resolve + missing-id fallback; additive `MediaGrid` multi-select) + scanner `Translatable` fail-fast (review M4) — *third 7g+ slice* | ✅ done (live-verified: real PG+MinIO — order round-trip + reorder/drop + dedup keep-first + non-guid→400 + deleted-file fallback, 12/12, no backend fixes) | [spec](superpowers/specs/2026-07-07-phase7g-plus-multifile-files-design.md) | [plan](superpowers/plans/2026-07-07-phase7g-plus-multifile-files.md) |
| 7g+.4 | Structured editor `Repeater` (repeatable child objects: `List<TChild>` of `[CmsField]` sub-props, lean scalar sub-field set, `IsJson`→`text`; scanner recurses `FieldMetadata.Fields` + fail-fasts invalid declarations; `ItemService` drops blank rows + validates sub-field Required/options/MaxLength; recursive `RepeaterField.vue`) — *the last 7g+ slice; completes Phase 7g+* | ✅ done (live-verified: real PG — order round-trip + reorder/drop-blank + sub-field required 400 + options 400 + CJK, 5/5, no backend fixes; **+1 final-review fix: interface-typed collection fail-fasts at scan**) | [spec](superpowers/specs/2026-07-07-phase7g-plus-repeater-design.md) | [plan](superpowers/plans/2026-07-07-phase7g-plus-repeater.md) |
| 8 | GraphQL (read-only delivery API: metadata-driven dynamic schema via HotChocolate; typed per-collection queries + filter/sort/offset-pagination/i18n; single-level relations via existing `deep`; File/Image/Files resolve to `File` nodes via batched DataLoader; RBAC/whitelist reused through an Api-owned `IGraphQlDataSource` adapter — Application untouched) | ✅ done (live-verified: real PG — all JSON-text columns round-trip + CJK exact + File resolution + FORBIDDEN/BAD_USER_INPUT; **+1 live-gate backend fix: Repeater sub-fields resolve from POCO children**) | [spec](superpowers/specs/2026-07-08-phase8-graphql-design.md) | [plan](superpowers/plans/2026-07-08-phase8-graphql.md) |
| 8b.1 | GraphQL mutations (backbone): typed `createX`/`updateX`/`deleteX` per collection — scalar own-fields + M2O FK + optimistic `version`; input→`JsonElement`→`ItemService` via the Api-owned adapter (Domain/App/Infra untouched); create/update re-read; RBAC/CSRF/error-filter reused — *first mutation slice* | ✅ done (live-verified: real PG 9/9 — create/CJK/M2O-re-read/partial-merge/CONFLICT/BAD_USER_INPUT/FORBIDDEN/delete, no backend fixes; **2 review fixes: v16 null-backfill on update + create**; `dotnet test` **490**, 0 warnings) | [spec](superpowers/specs/2026-07-08-phase8b-graphql-mutations-design.md) | [plan](superpowers/plans/2026-07-08-phase8b-graphql-mutations.md) |
| 8b.2a | GraphQL mutations (structured, non-i18n): typed inputs for M2M (`[ID!]`) + File/Image (`ID`) + Files (`[ID!]`) + MultiSelect/CheckboxGroup (`[String!]`) + Tags (`[TagItemInput!]`) + Json/KeyValue (`Any`) + Repeater (`[XFieldItemInput!]`); recursive `SentFieldsOnly` prunes v16 null-backfill in nested inputs; ItemService/mapper unchanged — *first structured mutation slice* | ✅ done (live-verified: real PG+Redis+MinIO — all kinds round-trip via updateArticle incl. CJK + KeyValue verbatim keys + recursive-prune + partial-merge + M2M clear + validation→BAD_USER_INPUT, no core fixes; 1 documented known limitation: empty Any object key → masked 500) | [spec](superpowers/specs/2026-07-09-phase8b2a-graphql-mutations-structured-design.md) | [plan](superpowers/plans/2026-07-09-phase8b2a-graphql-mutations-structured.md) |
| 8b.2b | GraphQL mutations (i18n): typed `translations` input (`[XTranslationInput!]` of `{locale, fields: XTranslationFieldsInput}`, built once in `Build()`; translatable File/Image per-locale OG image → `ID`) + immutable list→locale-keyed `FoldTranslations` after recursive prune; ItemService/mapper unchanged; read side stays `Any` — *third & last mutation slice, completes Phase 8b writes* | ✅ done (live-verified: real PG+Redis+MinIO 22/22 — createArticle now succeeds + CJK exact + translation-path sanitize + per-locale OG image + article-level partial-merge + negatives→BAD_USER_INPUT, no backend fixes; `dotnet test` **513**, 0 warnings) | [spec](superpowers/specs/2026-07-09-phase8b2b-graphql-mutations-i18n-design.md) | [plan](superpowers/plans/2026-07-09-phase8b2b-graphql-mutations-i18n.md) |
| 8c.1 | GraphQL advanced read querying (cross-relation filter + sort): M2O cross-relation (dotted-path) filtering, multi-hop, via nested `{Target}FilterInput` fields + metadata-aware `FilterInputTranslator`; cross-relation sort verified/tested/documented (no schema change, reuses `RelationOrderExpr`) — *first 8c slice, read-side, parallel to 8b* | ✅ done (live-verified: real PG — CJK filter + multi-hop + discrimination + sort + BAD_USER_INPUT negatives, 7/7, no backend fixes) | [spec](superpowers/specs/2026-07-09-phase8c1-graphql-cross-relation-read-design.md) | [plan](superpowers/plans/2026-07-09-phase8c1-graphql-cross-relation-read.md) |
| 8c.2 | GraphQL to-many cross-relation filter (O2M/M2M nested `{Target}FilterInput`, ANY/EXISTS, multi-hop mixed-kind; relaxes the 8c.1 M2O-only guard in `BuildFilterInput` + `RelationTargets`; engine/validator reused) — *second 8c slice* | ✅ done (live-verified: real PG — M2M/O2M/self-ref/multi-hop mixed-kind/CJK/discrimination/empty, 8/8, no fixes) | [spec](superpowers/specs/2026-07-09-phase8c2-graphql-tomany-relation-filter-design.md) | [plan](superpowers/plans/2026-07-09-phase8c2-graphql-tomany-relation-filter.md) |
| 8c.3a | GraphQL advanced read querying (multi-level (depth>1) relation **nesting/expansion**): recursive `DeepRelationSpec`/`DeepSpec` (Domain); REST `deep` envelope nesting + depth/name validation (Application); batched recursive `RelationExpander` (Infrastructure, N+1-safe); GraphQL selection-tree recursion (Api); relation-**count** cap → nesting-**depth** cap — *third 8c slice, engine work across Domain/App/Infra* | ✅ done (live-verified: real PG 7/7 — depth-3 M2O/depth-2 O2M/mixed-kind/self-ref cycle/CJK exact/over-depth reject/REST envelope, no fixes) | [spec](superpowers/specs/2026-07-13-phase8c3a-multilevel-relation-nesting-design.md) | [plan](superpowers/plans/2026-07-13-phase8c3a-multilevel-relation-nesting.md) |
| 8c.3b | GraphQL advanced read querying (nested-list `filter/sort/limit/offset` **arguments** on to-many related list fields; `filter` pushed to SQL via `RelationFilterResolver`, `sort/limit/offset` applied in-memory per parent group; N+1-safe invariant extended; M2O gets no args, nested sort is own-field-only) — *fourth 8c slice, completes the 8c.3 pair with 8c.3a* | ✅ done (live-verified: real PG 18/18, no fixes) | [spec](superpowers/specs/2026-07-13-phase8c3b-nested-list-args-design.md) | [plan](superpowers/plans/2026-07-13-phase8c3b-nested-list-args.md) |
| 9 | Soft delete / revisions / hooks + unified response envelope — *decomposed into 9a/9b/9c/9d* | ⬜ in progress | — | — |
| 9b | Soft delete (per-collection `ISoftDeletable` opt-in; SqlSugar global query-filter floor; `DELETE`=mark / `?purge=true`=remove / `restore`; `?deleted=exclude\|only\|with` gated by delete perm; REST + GraphQL parity) — *backend-only; Vue UI deferred to 9b-fe* | ✅ done (live-verified: real PG — soft/only/restore/purge + CJK + `?deleted=only`×D9-sort + deep-expansion exclusion; **+1 live-gate fix: typed-NULL PG 42804**) | [spec](superpowers/specs/2026-07-13-phase9b-soft-delete-design.md) | [plan](superpowers/plans/2026-07-14-phase9b-soft-delete.md) |
| 9a | Unified response envelope (every REST `/api/*` JSON response → `{success,data,meta?}` / `{success:false,error:{code,message,details?}}`; centralized `EnvelopeResultFilter` + `StruoExceptionHandler` + `InvalidModelStateResponseFactory` + `ApiResults.Fail`; machine-readable `error.code` symmetric with GraphQL; 204/binary/redirect not enveloped) — *backend-only; frontend explicit alignment deferred to 9a-fe* | ✅ done (live-verified: real PG — list meta / get / create / VALIDATION+details / BAD_USER_INPUT / 401 / CONFLICT / 404 / 302-file-not-enveloped / bare-204, 11/11, no fixes) | [spec](superpowers/specs/2026-07-14-phase9a-unified-response-envelope-design.md) | [plan](superpowers/plans/2026-07-14-phase9a-unified-response-envelope.md) |
| 9c | Revisions | ⬜ planned | — | — |
| 9d | Lifecycle hooks | ⬜ planned | — | — |
| 9b-fe | Soft delete admin UI (Vue Active/Trash switch on the collection list + inline soft-delete/restore/purge actions column; `itemsApi` `deleted`/`purge`/`restore`; soft-delete-aware item-form confirm) — *frontend-only; consumes the 9b API; `/api/schema` already emits `softDelete`* | ✅ done (live-verified: real PG — full delete→trash→restore→purge UI loop via Playwright `trash.spec.ts`) | [spec](superpowers/specs/2026-07-14-phase9b-fe-soft-delete-ui-design.md) | [plan](superpowers/plans/2026-07-14-phase9b-fe-soft-delete-ui.md) |

> The 5.5 and 5.6 phases were inserted between Phase 5 and Phase 6 as principled refinements
> (identity model alignment, then SEO model), not feature additions to the planned scope.

## Open architectural decisions (not blocking, recorded here so they aren't lost)

- **Framework vs. host separation — RESOLVED (Phase 6.9).** StruoCMS is a reusable **base template**:
  clone it, add `[CmsCollection]` classes, get CRUD APIs. `Struo.Api` no longer names any content
  type — metadata is discovered by convention (framework assembly + host assembly + the
  `Struo:ContentAssemblies` config list). Adding a collection requires no edit to `Program.cs`. See
  [`docs/guide/03-adding-a-collection.md`](guide/03-adding-a-collection.md).
- **Permission granularity for Phase 6.** `IPermissionService` is collection-level and takes no
  user argument (ambient). RBAC may need the current user/roles resolved inside the service (via
  `ICurrentUserAccessor`) or a signature change. Settle this during the Phase 6 brainstorm.
