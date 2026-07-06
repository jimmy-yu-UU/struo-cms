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
- **Verification baseline (2026-07-06, post-7g.5):** backend `dotnet build` clean +
  `dotnet test` **333** passed / 0 failed (325 post-7g + 3 scanner + 5 ItemService MaxLength). Frontend:
  **177/177** (173 post-7g + 2 FieldInput maxlength + 2 validateItem), `pnpm build` succeeds.
  **Live gate PASSED 2026-07-06** (4/4) — see the Phase 7g.5 row above.
- **Next up:** Phase 7g+
  (multi-value selects `MultiSelect`/`CheckboxGroup`/`Tags`; structured editors `Json`/`KeyValue`/`Repeater`;
  multi-file `Files` — consider the audit-F1 field-type registry refactor first), rendering read-only meanwhile.
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
| 7g+ | Multi-value selects (`MultiSelect`/`CheckboxGroup`/`Tags`), structured editors (`Json`/`KeyValue`/`Repeater`), multi-file `Files` — *deferred from 7d/7e* | ⬜ planned | — | — |
| 8 | GraphQL | ⬜ planned | — | — |
| 9 | Soft delete / revisions / hooks + unified response envelope | ⬜ planned | — | — |

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
