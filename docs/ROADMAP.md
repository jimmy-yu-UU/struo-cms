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
- **Phase 7c (item detail + create/edit/delete forms) code-complete, automated gates green,
  live-gate user-driven/pending:** schema-driven create/edit/delete forms for scalar fields
  (`ItemForm`/`ItemFormView`/`FieldInput`), i18n locale tabs backed by an additive
  `GET /api/languages` endpoint, `apiClient` put/delete + `itemsApi` CRUD, and
  `authStore.canWrite`/`canDelete` gating wired into the collection-list route. Automated gates
  green: backend `dotnet build` clean + `dotnet test` 259/259 (257 prior + 2 new
  `LanguagesEndpointTests`); frontend `pnpm test` 86/86 and `pnpm build` succeeds. **Live gate
  (real Postgres + Redis, `pnpm dev`, Playwright E2E create/edit/delete + manual i18n round-trip
  confirming a default-locale translation is required and `en`+`zh-TW` both persist) has not been
  run yet — it is user-driven per the Phase 7a §9 / 7b precedent** and remains pending.
- **Next up:** Phase 7d (relation pickers, File/Image upload controls, TipTap rich text, multi-value
  selects) — all explicitly deferred by Phase 7c, which scoped itself to scalar fields only.
  Phase 6.9 resolved the framework-vs-host decision (see "Open architectural decisions" below):
  `Struo.Api` is a reusable base template with convention-based collection discovery. The
  `IPermissionService` port is backed by real RBAC (`RbacPermissionService` + per-request
  snapshot); the allow-all stub is out of the live DI graph.
- **Verification baseline (2026-07-02):** backend `dotnet build` clean (warnings-as-errors);
  `dotnet test` 259 passed / 0 failed / 0 skipped (257 prior + 2 `LanguagesEndpointTests`).
  Frontend: 86/86 unit/component tests passed, `pnpm build` succeeds. Phase 7a's live Playwright E2E
  (login → dashboard → logout) and Phase 7b's live browse E2E (`auth.spec.ts` + `collections.spec.ts`,
  2/2 in Chromium) previously passed against the dev API on live Postgres + Redis; Phase 7c's live
  gate (E2E create/edit/delete + i18n round-trip) is still pending — see Phase 7c row above. DB/auth
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
| 7c | Item detail + create/edit/delete forms (scalar fields, i18n locale tabs, additive `GET /api/languages`) | ⬜ code-complete, automated gates green, live-gate user-driven/pending | [spec](superpowers/specs/2026-07-02-phase7c-item-forms-design.md) | [plan](superpowers/plans/2026-07-02-phase7c-item-forms.md) |
| 7d | Relation pickers, File/Image upload, TipTap rich text, multi-value selects | ⬜ planned | — | — |
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
