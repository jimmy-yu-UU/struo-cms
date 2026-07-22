# StruoCMS — Working Rules (condensed)

## Purpose (§0 — read first)
StruoCMS is a **reusable, headless CMS *template***, not a finished product. It ships **only core
framework/system capability** (metadata-driven collections, identity/RBAC, files/media, revisions,
i18n, site-settings, the query DSL, REST + GraphQL, the admin SPA shell). It contains **no business
content models** — downstream teams fork this template and add their own collections + migrations for
their actual project. Therefore:
- `samples/Struo.Sample.Blog` (Article/Tag/Category/…) is a **demo only** — it shows *how* to define
  collections; it is NOT part of the shipped core and is deleted on fork. Never treat sample tables
  (`articles`/`tags`/`categories`/`article_*`) as core.
- **Core = `src/Struo.*` framework + `FrameworkEntityTypes.All`** (languages/files/file_translations/
  users/roles/permissions/user_roles/revisions/site_settings). If unsure whether something is core or
  sample, it's core only if it lives in `src/Struo.*`.
- `db/migrations/` carries **core-only** schema (`001-core-baseline.sql` = the prod bootstrap). Sample
  schema is built by dev `InitTables` from sample entities; downstream forks write their own migrations.
- When adding capability, ask "does every downstream CMS need this, or is it business-specific?"
  Business-specific → belongs in the sample or downstream, not core (YAGNI at the template level).

## Stack (§1)
.NET 10 / C# latest · SqlSugarCore · DB: **PostgreSQL** (runtime, supported) + SQLite (tests only);
MySQL/SqlServer/Oracle are type-mapped but **unverified/experimental** (raw ORDER-BY subqueries and
literal-coercion assumptions are PG/SQLite-shaped — see audit D9) ·
ASP.NET Core **Controllers** · Serilog · Scalar (Mars theme, axios) · Redis (Phase 6) ·
Vue 3 + PrimeVue + TipTap (Phase 7). Outbound JSON = camelCase.

## Dependency rule (§2)
Domain → nothing · Application → Domain · Infrastructure → Application+Domain ·
Api → Application+Infrastructure. Framework code never references `samples/*`.
Domain stays free of external packages; persistence attributes live on entities only.

## Execution rules (§17)
1. Phase-by-phase: brainstorm → write-plan (TDD) → execute → verify. Specs in
   `docs/superpowers/specs/`, plans in `docs/superpowers/plans/`.
2. TDD: failing test first; acceptance = verification gate with evidence.
3. YAGNI: stay inside the current phase's scope.
4. All DB access via SqlSugar ORM; zero vendor SQL. InitTables dev-only.
5. Package versions are NEVER inferred from model knowledge — install latest via the package
   manager itself (`dotnet add package` for NuGet, `pnpm add <pkg>` for frontend). Any version
   string in a file must be one the package manager produced, never hand-authored from memory.
   NuGet versions centralized in `Directory.Packages.props`.
6. Metadata scanned at startup and cached; no per-request reflection (Phase 1+).
7. Query DSL never leaks ORM internals; field/relation paths are whitelist-validated (Phase 2+).
8. RichText is server-side sanitized (Phase 6/7).
9. When unsure about an architecture decision, stop and ask.
