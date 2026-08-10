# StruoCMS Frontend (Vue 3 SPA)

The admin single-page app: Vue 3 + TipTap, authenticating against the StruoCMS API (`Struo.Api`). Built
with Vite, Pinia, Vue Router, and Tailwind v4 + shadcn-vue — a migration off PrimeVue is underway, so
PrimeVue is still installed and still used by the screens not yet converted. See [chapter 2](../docs/guide/en/02-getting-started.md)
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
