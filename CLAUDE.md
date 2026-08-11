# StruoCMS — Working Rules

The authoritative description of this repository — what it is, the core/sample boundary, architecture,
invariants, task playbooks, and prohibitions — lives in `AGENTS.md`. Read it first, every session.

## Rules for developing this template itself

These govern work on this repository's own codebase; they are not requirements `AGENTS.md` imposes on
downstream forks.

- **Failing test first.** Write the test before the implementation; a passing test suite is the
  acceptance gate, not a formality.
- **Package versions are never hand-authored.** Install via the package manager itself
  (`dotnet add package` for NuGet, `pnpm add <pkg>` for the frontend) and let it write the version
  string. NuGet versions are centralized in `Directory.Packages.props`.
- **Database behavior is verified against a live instance of the backend you are actually configured
  for, not just the SQLite test suite** — SQLite passing is not evidence of correctness anywhere else.
  On PostgreSQL (the verified target) run the live-PG check; it is strongly recommended for every
  DB-behavior change. On any other backend, that backend needs its own equivalent live check — a green
  PostgreSQL run does not transfer. This is a robustness practice, **not** a CI gate: CI runs the
  SQLite suite only, deliberately, so that no single engine is privileged over DB replaceability.
  (`AGENTS.md`'s Verification section has the documented divergences and how to configure a test
  connection.)
- **Stay inside the requested scope.** Don't expand a task beyond what was asked.
- **When unsure about an architectural decision, stop and ask** rather than guessing.

## The five standing gates

The same checks CI runs on every push/PR: `dotnet build`, `dotnet test`, `pnpm test` and `pnpm build`
(the latter two from `frontend/`), plus `pnpm build` from `docs/` — the documentation site's build is the
manual's link gate, and it also checks that every chapter rendered with content, which `vitepress build`
does not. Run whichever apply to your change; run all five before anything touching more than one of the
three.

## See also

- `AGENTS.md` — start here.
- `docs/ai/architecture.md`, `docs/ai/conventions.md`, `docs/ai/task-playbooks.md` — the deeper
  reference set `AGENTS.md` points to.
- `docs/guide/en/` / `docs/guide/zh-TW/` — the bilingual user manual.
