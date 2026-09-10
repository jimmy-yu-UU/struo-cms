# StruoCMS Frontend (Vue 3 SPA)

The admin single-page app: Vue 3 + TipTap, authenticating against the StruoCMS API (`Struo.Api`). Built
with Vite, Pinia, Vue Router, and Tailwind v4 + shadcn-vue. See [chapter 3](../docs/guide/en/03-getting-started.md)
for the full quick-start (starting the API alongside this SPA) and [chapter 14](../docs/guide/en/14-admin-spa-customization.md)
for customizing the admin UI.

## Install

```bash
pnpm install
```

## Dev server

```bash
pnpm dev
```

Starts Vite on `http://localhost:5173`. By default the SPA talks to the API through Vite's dev proxy
(`/api` → `http://localhost:5221`, configured in `vite.config.ts`), so the browser sees the API as
same-origin and no CORS setup is required. Start the API separately on port 5221 (chapter 2).

## Unit / component tests

```bash
pnpm test
```

Runs the Vitest suite (component + store + client tests, `jsdom` environment). This is separate from
`pnpm e2e` (Playwright) — see `e2e/README.md`.

## Build

```bash
pnpm build
```

Type-checks (`vue-tsc -b`) and produces a production bundle in `dist/`.

## E2E tests (Playwright)

```bash
pnpm e2e         # core project: framework-only specs
pnpm e2e:sample  # sample project: requires the Blog sample opted in
pnpm e2e:all     # both
```

Needs a running API and database; see `e2e/README.md` for prerequisites and setup, and
[chapter 15](../docs/guide/en/15-deployment-operations-testing.md) for what the three test layers cover
and what CI does and does not run.

## Manually verifying sample-only flows (e.g. revision history)

Some admin flows only exist on collections with `Revisions = true` — currently only the sample's
`Article` (`samples/Struo.Sample.Blog/Article.cs`), since none of the seven core collections opts in.
To exercise such a flow against this SPA (revision history, revert, etc.), opt the sample into the
running API first, following [chapter 16](../docs/guide/en/16-sample-walkthrough.md)'s "Opting it in".
Rebuild and start the API on `:5221` as usual (chapter 2), then `pnpm dev` here — no separate frontend
configuration is needed.

**This is a temporary, uncommitted opt-in — revert it as soon as you're done**, before committing
anything else. `dotnet test`'s `Host_project_has_no_project_reference_into_samples`
(`tests/Struo.Tests/Template/TemplateInvariantsTests.cs`) fails while the reference is in place — that is
expected, not a regression to chase — and passes again once it's reverted; confirm `git status` shows no
tracked diff at all before moving on (not just to `Struo.Api.csproj`) — chapter 16's
`Struo:ContentAssemblies` edit belongs in the gitignored Development file precisely so it never shows up
there. The host has no `ProjectReference` to the sample by default because it's an optional, detachable
demo, not shipped capability — see the core/sample boundary in `AGENTS.md`.
