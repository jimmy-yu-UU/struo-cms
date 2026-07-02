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
- **Phase 7b (collection lists) code-complete, live-gate-pending:** RBAC-aware collection nav
  (`CollectionNav`) and a generic `CollectionListView` (paginated/sortable PrimeVue DataTable driven
  by schema metadata), plus additive `/api/auth/me` permissions. Automated gates are green (backend
  build/tests incl. `AuthMePermissionsTests`, frontend unit/component tests, frontend build). The
  live browse E2E (`collections.spec.ts`, requires a running dev API + seeded admin + ≥1 `article`
  row) and the live Postgres+Redis RBAC gate (super-admin vs. limited-editor `/api/auth/me` +
  nav + list pagination/sort) are **user-driven and still pending** — see
  [`frontend/e2e/README.md`](../frontend/e2e/README.md) for the seed recipe.
- **Next up:** run the Phase 7b live browse E2E + live Postgres/Redis RBAC gate, then continue with
  Phase 7c (item detail view + create/edit forms). Phase 6.9 resolved the framework-vs-host
  decision (see "Open architectural decisions" below): `Struo.Api` is a reusable base template with
  convention-based collection discovery. The `IPermissionService` port is backed by real RBAC
  (`RbacPermissionService` + per-request snapshot); the allow-all stub is out of the live DI graph.
- **Verification baseline (2026-07-02):** backend `dotnet build` clean (warnings-as-errors);
  `dotnet test` 257 passed / 0 failed / 0 skipped (255 prior + `AuthMePermissionsTests`).
  Frontend: 44/44 unit/component tests passed, `pnpm build` succeeds. Phase 7a's live Playwright E2E
  (login → dashboard → logout) previously passed live; Phase 7b's live browse E2E
  (`collections.spec.ts`) and the live Postgres+Redis RBAC gate are pending (user-driven — see
  Phase 7b row above). DB/auth features are gated on live Postgres+Redis (SQLite-green ≠
  Postgres-correct); Phase 6c OIDC round-trip previously verified against live Entra ID.

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
| 7b | Collection lists (RBAC-aware nav, generic paginated/sortable `CollectionListView`, additive `/api/auth/me` permissions) | ⬜ code-complete, live-gate-pending | [spec](superpowers/specs/2026-07-02-phase7b-collection-lists-design.md) | [plan](superpowers/plans/2026-07-02-phase7b-collection-lists.md) |
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
