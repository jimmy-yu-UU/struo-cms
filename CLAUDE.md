# StruoCMS — Working Rules (condensed)

## Stack (§1)
.NET 10 / C# latest · SqlSugarCore · multi-DB (default PostgreSQL, tests SQLite) ·
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
   manager itself (`dotnet add package` for NuGet, `npm install <pkg>` for frontend). Any version
   string in a file must be one the package manager produced, never hand-authored from memory.
   NuGet versions centralized in `Directory.Packages.props`.
6. Metadata scanned at startup and cached; no per-request reflection (Phase 1+).
7. Query DSL never leaks ORM internals; field/relation paths are whitelist-validated (Phase 2+).
8. RichText is server-side sanitized (Phase 6/7).
9. When unsure about an architecture decision, stop and ask.
