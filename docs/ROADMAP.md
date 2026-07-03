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
- **Next up:** Phase 7e+ (File/Image/Files upload + TipTap rich text; then multi-value selects + structured
  editors) — all deferred from 7d, rendering read-only meanwhile.
  Phase 6.9 resolved the framework-vs-host decision (see "Open architectural decisions" below):
  `Struo.Api` is a reusable base template with convention-based collection discovery. The
  `IPermissionService` port is backed by real RBAC (`RbacPermissionService` + per-request
  snapshot); the allow-all stub is out of the live DI graph.
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
| 7e+ | File/Image/Files upload, TipTap rich text, multi-value selects (`MultiSelect`/`CheckboxGroup`/`Tags`), structured editors (`Json`/`KeyValue`/`Repeater`) — *deferred from 7d* | ⬜ planned | — | — |
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
