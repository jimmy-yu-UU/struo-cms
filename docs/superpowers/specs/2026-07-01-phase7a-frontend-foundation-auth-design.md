# StruoCMS — Phase 7a (Frontend Foundation + Auth) Design

> Date: 2026-07-01
> Source of truth: the StruoCMS master spec (§0–§18) + Phase 6a/6b/6c specs.
> Depends on Phase 6a (auth core — cookie session, Redis ticket store, `ICurrentUserAccessor`),
> 6b (RBAC), 6c (SSO / OIDC challenge), and 6.9 (convention-based collection discovery), all merged.
> Each phase runs its own brainstorm → write-plan (TDD) → execute → verify cycle (§17.1).

## 1. Goal & scope

Establish the **frontend admin application** as a separate, monorepo-sibling Vue 3 SPA and prove the
end-to-end **cross-origin authenticated session** against the existing API. Phase 7a is the *walking
skeleton*: a user opens the SPA, is redirected to a login page, signs in with email + password,
lands on an (empty) authenticated dashboard, and can log out — all over a CORS boundary using the
existing revocable Redis-backed cookie session. No content UI is built yet; 7a exists to lock the
scaffold, the auth plumbing, and the test toolchain that every later sub-phase (7b–7e) builds on.

**In scope**
- New `frontend/` folder (monorepo sibling to `src/`): a **separate app/deployable** with its own
  `package.json` + Vite build, versioned with the backend. Vue 3 + **TypeScript**, Vite, **PrimeVue**
  (Aura theme), **Pinia**, **Vue Router**. Package manager: **pnpm**.
- **`apiClient`** — thin HTTP wrapper: base URL from env, `credentials: 'include'`, camelCase JSON,
  centralized `401 → clear session + redirect to /login` handling.
- **`authStore` (Pinia)** — `user`, `isAuthenticated`, `login(email, password)`, `logout()`,
  `fetchCurrentUser()` (session bootstrap on app load).
- **Router + auth guard** — public `/login`; every other route protected. Guard redirects
  unauthenticated → `/login`, and authenticated users away from `/login` → `/`.
- **`AppShell`** — top bar (user menu + logout), side-nav placeholder, `<router-view>`.
- **`LoginView`** — email/password form → `authStore.login` → redirect to dashboard; inline error on 401.
- **`DashboardView`** — authenticated empty landing (placeholder for 7b nav/lists).
- **Backend delta (minimal, additive):** a **configurable CORS policy** and a **configurable
  cross-origin cookie mode**, both off by default so existing behavior and the 246 tests are unchanged
  (see §5). No new endpoints — 7a consumes the existing `POST /api/auth/login`, `POST /api/auth/logout`,
  `GET /api/auth/me`.
- **Testing:** Vitest + Vue Test Utils (unit/component) + Playwright (E2E), TDD-on-logic (see §8).

**Out of scope (deferred)**
- OIDC login button / SSO redirect from the SPA (mechanism already exists server-side via
  `GET /api/auth/login/oidc`; 7a's design must not preclude adding a button, but it is not built here).
- Dynamic collection navigation, data tables, query DSL wiring (7b).
- Create/edit forms, field widgets, relation pickers (7c).
- TipTap rich text, file manager, i18n translation editing (7d).
- User/role/permission management UI (7e).
- Production hosting/CD pipeline for the SPA; containerization.
- Enriching `GET /api/auth/me` beyond its current `{ id }` payload (see §3 open note).

## 1a. Package version policy — ABSOLUTE CONSTRAINT

**Package versions MUST NEVER be inferred from the assistant's own knowledge.** Every dependency —
both npm (frontend) and NuGet (backend) — is installed at its **latest** version *through the package
manager itself*, never by hand-writing a version string guessed from memory.

- Frontend: add deps via `pnpm add <pkg>` (resolves to latest by default); let pnpm write the resolved
  versions into `package.json` / `pnpm-lock.yaml`. Do not type a version literal into `package.json`
  from memory.
- Backend: `dotnet add package <pkg>` (latest), versions centralized in `Directory.Packages.props`
  (existing rule §17.5). Never hardcode a version.
- The plan and execution must *run the installer* to obtain versions; any version number appearing in a
  file must have been produced by the package manager, not authored from assumed knowledge.

This is a hard gate: work that hardcodes assumed versions is rejected at verification.

## 2. Decisions locked in brainstorming

| Topic | Decision |
|---|---|
| SPA hosting | **Fully separate app**, API-only backend + **CORS**. Chosen over "served as static files by Struo.Api" and over a server-rendered MPA. Keeps the backend a pure API; the SPA is an independent deployable. |
| Repo layout | **Monorepo sibling folder `frontend/`** (not a separate git repo). Own `package.json`/build; versioned with the backend. |
| Auth strategy | **Cross-origin cookie session** — reuse the existing Redis-backed revocable cookie. Chosen over client-side bearer tokens (XSS-exposed, and the existing bearer is permanent, not a session). Browser sends the `struo.session` cookie automatically; OIDC redirect stays server-driven. |
| Cross-origin requirements | Backend CORS with `AllowCredentials` + a fixed allow-list of SPA origins from config; session cookie `SameSite=None; Secure` **when cross-origin mode is enabled**. Client uses `credentials: 'include'`. Dev runs both Vite and the API over **HTTPS** (required for `SameSite=None; Secure`). |
| Language | **TypeScript** throughout the SPA. |
| UI kit / theme | **PrimeVue**, Aura preset theme. |
| State / routing | **Pinia** + **Vue Router**. |
| Testing | **Vitest + Vue Test Utils** (unit/component) + **Playwright** (E2E). Failing-test-first applied rigorously to *logic* (authStore, apiClient 401 handling, router guard); scaffold/layout verified via render + E2E rather than literal RED-first. |

## 3. Existing API surface consumed (verified against `AuthController`)

| Method | Route | Auth | Request | Success | Failure |
|---|---|---|---|---|---|
| POST | `/api/auth/login` | anonymous | `{ email, password }` | `200 { data: { id } }` + sets `struo.session` cookie | `401 { error: { message } }` |
| POST | `/api/auth/logout` | cookie/bearer | — | `204` (signs out cookie, revokes ticket) | `401` |
| GET | `/api/auth/me` | cookie/bearer | — | `200 { data: { id } }` | `401` |

- Login body field is **`email`**, not username.
- `GET /api/auth/me` currently returns **only the user id**. That is sufficient for 7a's session
  bootstrap (presence of a 200 = authenticated; the id seeds `authStore.user`). Enriching this payload
  (email/name/roles for the user menu) is deferred; the store models `user` as an object so a richer
  payload later is non-breaking.
- API response envelope is `{ data }` on success / `{ error: { message } }` on failure (matches the
  house style). `apiClient` unwraps `data` and surfaces `error.message`.

## 4. Frontend architecture

Small, single-purpose units communicating through narrow interfaces:

```
frontend/
  src/
    api/apiClient.ts        # fetch wrapper: baseURL, credentials:'include', envelope unwrap, 401 hook
    stores/authStore.ts     # Pinia: user, isAuthenticated, login/logout/fetchCurrentUser
    router/index.ts         # routes + auth guard
    layouts/AppShell.vue    # top bar + nav placeholder + <router-view>
    views/LoginView.vue     # email/password form
    views/DashboardView.vue # empty authenticated landing
    main.ts                 # app bootstrap: PrimeVue, Pinia, router, then fetchCurrentUser()
  tests/                    # Vitest unit/component specs (co-located or here)
  e2e/                      # Playwright specs
  package.json, vite.config.ts, tsconfig.json, .env.example
```

**Boundaries**
- `apiClient` knows HTTP + the envelope; it does not know about routing (it exposes an injectable
  `onUnauthorized` callback that the app wires to the router).
- `authStore` knows session state; it calls `apiClient` and never touches the DOM.
- Views/layout know presentation; they call the store, never `apiClient` directly.

## 5. Backend changes (additive, default-off)

Two small, config-gated additions to `Struo.Api`. Both default to today's behavior so the existing
246 tests (same-origin, HTTP test host, `SameSite=Lax`) stay green.

1. **CORS** — new `Struo:Cors:AllowedOrigins` (string array, default empty). When non-empty, register a
   named CORS policy with `.WithOrigins(...)`, `.AllowCredentials()`, `.AllowAnyHeader()`,
   `.AllowAnyMethod()` and apply it in the pipeline. When empty, no CORS middleware is added (unchanged).
2. **Cross-origin cookie mode** — the cookie currently uses `SameSite=Lax` with
   `SecurePolicy = Always (prod) / SameAsRequest (else)`. A cross-origin browser session requires
   `SameSite=None; Secure`. Because `None` mandates `Secure` (breaks the HTTP test host and same-origin
   HTTP dev), this is gated: when `Struo:Cors:AllowedOrigins` is non-empty (i.e. cross-origin is
   intended), set `Cookie.SameSite = None` and `SecurePolicy = Always`; otherwise keep the current
   `Lax`/policy. This keeps `AuthWiring` the single source of truth for the cookie and leaves all tests
   on the default path.

No controller/endpoint changes. `HttpOnly=true`, `struo.session` name, 8h sliding expiry all unchanged.

## 6. Data flow

- **Boot:** `main.ts` mounts the app, then `authStore.fetchCurrentUser()` → `GET /api/auth/me`.
  200 → `user` set, `isAuthenticated=true`. 401 → store cleared. The router guard reads the resolved
  state to render `AppShell` or redirect to `/login`.
- **Login:** `LoginView` submits → `authStore.login(email, password)` → `POST /api/auth/login`.
  200 → backend sets `struo.session` cookie → store calls `fetchCurrentUser()` (or seeds from the login
  `id`) → navigate to `/`. 401 → inline error, no field-level disclosure.
- **Authenticated requests:** every `apiClient` call sends `credentials: 'include'`; the browser attaches
  the cookie cross-origin (allowed by CORS `AllowCredentials`).
- **Logout:** `authStore.logout()` → `POST /api/auth/logout` (revokes the Redis ticket) → clear store →
  route to `/login`.

## 7. Error handling

- **401 anywhere** (via `apiClient.onUnauthorized`) → clear `authStore` → redirect to `/login`.
- **Login failure** → inline form error ("Invalid credentials."), no indication of which field was wrong.
- **Network / 5xx** → user-friendly PrimeVue toast; detailed error only to the console in dev. Never
  swallow errors silently (global rule).
- **CORS/session misconfig** (cookie not sent) surfaces as a persistent redirect-to-login loop; the
  verification gate's manual HTTPS smoke exists specifically to catch this before sign-off.

## 8. Testing strategy (Vitest + Vue Test Utils + Playwright)

**TDD (failing-test-first) on logic:**
- `authStore`: login success sets user; login 401 leaves unauthenticated + surfaces error; logout clears
  state; `fetchCurrentUser` 200/401 branches. (Mock `apiClient`.)
- `apiClient`: `credentials:'include'` is set; envelope `data` unwrap; `error.message` surfaced; a 401
  invokes the `onUnauthorized` hook.
- Router guard: unauthenticated → `/login`; authenticated → target; authenticated on `/login` → `/`.

**Component tests:**
- `LoginView`: submit calls store; error state renders; empty-field validation.
- `AppShell`: logout action calls `authStore.logout`.

**E2E (Playwright):** the walking-skeleton flow against a running API + Vite — visit a protected route →
redirected to `/login` → enter seeded credentials → land on dashboard → logout → back to `/login`.
(Uses a seeded dev user; documented in the plan.)

Scaffold/layout markup is verified via render + the E2E flow, not literal RED-first.

## 9. Verification gate (acceptance)

- `dotnet build` clean (warnings-as-errors) and `dotnet test` **246+ passed / 0 failed / 0 skipped**
  (CORS + conditional-cookie change covered by a test that asserts default behavior is unchanged and that
  enabling `Struo:Cors:AllowedOrigins` flips the cookie to `SameSite=None; Secure`).
- Frontend unit + component tests pass (Vitest).
- Playwright E2E `login → dashboard → logout` passes.
- **Manual live smoke:** Vite (HTTPS) + API (HTTPS) on different origins; real cross-origin cookie login
  round-trip succeeds; logout revokes the session (a subsequent `/api/auth/me` returns 401).
- **Version-policy check (§1a):** every dependency version in `package.json`/`pnpm-lock.yaml` was
  produced by `pnpm add` (not hand-authored); no version string was guessed from assistant knowledge.

## 10. Risks & mitigations

- **`SameSite=None; Secure` requires HTTPS in dev** → document Vite HTTPS + API HTTPS setup in the plan;
  gate the cookie change behind CORS config so nobody accidentally breaks HTTP dev/tests.
- **CORS + credentials pitfalls** (wildcard origin forbidden with credentials) → explicit origin
  allow-list only; covered by the manual smoke.
- **Frontend TDD friction on pure UI** → scope RED-first to logic; verify UI via render + E2E.
- **`/api/auth/me` thin payload** → model `user` as an object now so an enriched payload later is
  non-breaking; user-menu labels can start from the id/email echo.
