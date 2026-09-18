# StruoCMS — Working Rules

The authoritative description of this repository — what it is, the core/sample boundary, architecture,
invariants, task playbooks, and prohibitions — lives in `AGENTS.md`. Read it first, every session.

## Rules for developing this template itself

These govern work on this repository's own codebase; they are not requirements `AGENTS.md` imposes on
downstream forks.

- **Failing test first.** Write the test before the implementation; a passing test suite is the
  acceptance gate, not a formality.
- **Package versions are never hand-authored**, except the bounded transitive-dependency `overrides:`
  ranges in `frontend/pnpm-workspace.yaml` and `docs/pnpm-workspace.yaml`, written for advisories a
  package hasn't picked up yet. Install via the package manager itself (`dotnet add package` for
  NuGet, `pnpm add <pkg>` for the frontend) and let it write the version string. NuGet versions are
  centralized in `Directory.Packages.props`.
- **Database behavior is verified against a live instance of the backend you are actually configured
  for, not just the SQLite test suite.** See `AGENTS.md`, "Verification", for the per-backend rule, the
  documented divergences, and how to configure a test connection.
- **Stay inside the requested scope.** Don't expand a task beyond what was asked.
- **When unsure about an architectural decision, stop and ask** rather than guessing.
- **Comments and docs state the current behavior only.** `StaleNarrativeConventionTests` fails
  `dotnet test` otherwise; the rule text is `docs/ai/conventions.md`, "Only the current state".

## The five standing gates

Run whichever of the five apply to your change, and all five before anything touching more than one of
`src/`, `frontend/`, and `docs/` — see `AGENTS.md`, "Verification", for the gate list, the CI split,
and the SQLite/PostgreSQL reasoning.

## See also

- `AGENTS.md` — start here.
- `docs/ai/architecture.md`, `docs/ai/conventions.md`, `docs/ai/task-playbooks.md` — the deeper
  reference set `AGENTS.md` points to.
- `docs/guide/en/` / `docs/guide/zh-TW/` — the bilingual user manual.
