# Phase 7a — Frontend Foundation + Auth Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up a separate `frontend/` Vue 3 SPA that logs a user in against the existing API over a CORS boundary using the revocable cookie session, lands on an authenticated dashboard, and logs out.

**Architecture:** A monorepo-sibling Vue 3 + TypeScript SPA (Vite, PrimeVue, Pinia, Vue Router) talks to the .NET API. The API gets two additive, default-off backend changes: a configurable CORS policy and a cross-origin cookie mode (`SameSite=None; Secure`) gated on CORS being configured. The SPA layers `apiClient` (HTTP) → `authStore` (session state) → router guard → views, each a focused unit. Automated E2E runs through Vite's dev proxy (same-origin, deterministic); the true cross-origin HTTPS path is validated by a manual smoke.

**Tech Stack:** .NET 10 / ASP.NET Core (backend, unchanged except CORS+cookie); Vue 3, TypeScript, Vite, PrimeVue (Aura), Pinia, Vue Router, Vitest + Vue Test Utils, Playwright. Package manager: **pnpm**.

## Global Constraints

- **Package versions are NEVER inferred from model knowledge.** Install latest via the package manager itself: `pnpm add <pkg>` (frontend), `dotnet add package` (backend). Any version string in a file must be one the package manager produced — never hand-authored. (spec §1a, CLAUDE.md §17.5)
- **Backend changes are additive and default-off:** with `Struo:Cors:AllowedOrigins` empty, behavior and all 246 existing tests are unchanged. (spec §5)
- **Cross-origin cookie:** when CORS is configured, session cookie is `SameSite=None; Secure`; client always uses `credentials: 'include'`. `HttpOnly`, name `struo.session`, 8h sliding expiry unchanged. (spec §2, §5)
- **Auth endpoints (verified):** `POST /api/auth/login` body `{ email, password }` → `200 { data: { id } }` + `Set-Cookie`; `POST /api/auth/logout` → `204`; `GET /api/auth/me` → `200 { data: { id } }`. Envelope: `{ data }` / `{ error: { message } }`. (spec §3)
- **TDD-on-logic:** failing-test-first for `apiClient`, `authStore`, router guard; scaffold/layout verified via render + E2E. (spec §8)
- **Never swallow errors silently;** user-facing messages don't disclose which credential field was wrong. (spec §7)

---

### Task 1: Backend — configurable CORS + cross-origin cookie mode

**Files:**
- Modify: `src/Struo.Api/Auth/AuthWiring.cs` (cookie SameSite/secure gated on CORS config)
- Modify: `src/Struo.Api/Program.cs:24-52` (register + apply CORS policy)
- Create: `src/Struo.Api/Auth/CorsWiring.cs` (policy registration helper)
- Test: `tests/Struo.Tests/Api/CorsAndCookieTests.cs`

**Interfaces:**
- Consumes: existing `AddStruoAuth(IConfiguration, IWebHostEnvironment)`, `IWebHostEnvironment`, config key `Struo:Cors:AllowedOrigins` (string[]).
- Produces: `CorsWiring.AddStruoCors(IServiceCollection, IConfiguration) : IServiceCollection`, `CorsWiring.PolicyName` (const string `"StruoSpa"`), `CorsWiring.HasConfiguredOrigins(IConfiguration) : bool`, and `UseStruoCors(IApplicationBuilder, IConfiguration)` extension applying the policy only when origins are configured.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Struo.Tests/Api/CorsAndCookieTests.cs
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

public class CorsAndCookieTests
{
    private const string Origin = "https://admin.example.test";

    // A factory variant that enables cross-origin mode via config.
    private sealed class CorsApiFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteTestDatabase _db = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:DbType"] = "Sqlite",
                    ["Database:ConnectionString"] = _db.ConnectionString,
                    ["Struo:ContentAssemblies:0"] = "Struo.Sample.Blog",
                    ["Struo:Cors:AllowedOrigins:0"] = "https://admin.example.test"
                }));
        }
        protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) _db.Dispose(); }
    }

    [Fact]
    public async Task Cors_enabled_preflight_allows_configured_origin_with_credentials()
    {
        using var factory = new CorsApiFactory();
        var client = factory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Options, "/api/auth/me");
        req.Headers.Add("Origin", Origin);
        req.Headers.Add("Access-Control-Request-Method", "GET");
        var resp = await client.SendAsync(req);

        resp.Headers.GetValues("Access-Control-Allow-Origin").Should().Contain(Origin);
        resp.Headers.GetValues("Access-Control-Allow-Credentials").Should().Contain("true");
    }

    [Fact]
    public async Task Cors_enabled_login_sets_samesite_none_secure_cookie()
    {
        using var factory = new CorsApiFactory();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SqlSugar.ISqlSugarClient>();
            var hasher = scope.ServiceProvider.GetRequiredService<Struo.Application.Security.IPasswordHasher>();
            await db.Insertable(new Struo.Infrastructure.Identity.User
            {
                Id = Guid.CreateVersion7(), Email = "cors@b.com",
                Password = hasher.Hash("pw12345678"), IsActive = true
            }).ExecuteCommandAsync();
        }
        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { email = "cors@b.com", password = "pw12345678" });
        var setCookie = string.Join(";", resp.Headers.GetValues("Set-Cookie"));
        setCookie.ToLowerInvariant().Should().Contain("samesite=none");
        setCookie.ToLowerInvariant().Should().Contain("secure");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~CorsAndCookieTests`
Expected: FAIL — no `Access-Control-Allow-Origin` header / cookie is `samesite=lax` (missing `none`).

- [ ] **Step 3: Create the CORS wiring helper**

```csharp
// src/Struo.Api/Auth/CorsWiring.cs
namespace Struo.Api.Auth;

public static class CorsWiring
{
    public const string PolicyName = "StruoSpa";

    public static string[] ConfiguredOrigins(IConfiguration config) =>
        config.GetSection("Struo:Cors:AllowedOrigins").Get<string[]>() ?? [];

    public static bool HasConfiguredOrigins(IConfiguration config) =>
        ConfiguredOrigins(config).Length > 0;

    public static IServiceCollection AddStruoCors(this IServiceCollection services, IConfiguration config)
    {
        var origins = ConfiguredOrigins(config);
        if (origins.Length == 0) return services; // default-off: no CORS registered

        services.AddCors(options => options.AddPolicy(PolicyName, policy =>
            policy.WithOrigins(origins)
                  .AllowCredentials()
                  .AllowAnyHeader()
                  .AllowAnyMethod()));
        return services;
    }

    public static IApplicationBuilder UseStruoCors(this IApplicationBuilder app, IConfiguration config)
    {
        if (HasConfiguredOrigins(config)) app.UseCors(PolicyName);
        return app;
    }
}
```

- [ ] **Step 4: Gate the cookie mode in AuthWiring**

In `src/Struo.Api/Auth/AuthWiring.cs`, replace the `securePolicy` block and the two cookie lines (`SecurePolicy` / `SameSite`) so cross-origin config flips them:

```csharp
// after: services.AddSingleton<DistributedCacheTicketStore>();
var crossOrigin = CorsWiring.HasConfiguredOrigins(config);

var securePolicy = (env.IsProduction() || crossOrigin)
    ? CookieSecurePolicy.Always
    : CookieSecurePolicy.SameAsRequest;
var sameSite = crossOrigin ? SameSiteMode.None : SameSiteMode.Lax;
```

Then in the `AddCookie` options set `options.Cookie.SecurePolicy = securePolicy;` (unchanged) and `options.Cookie.SameSite = sameSite;` (was hardcoded `Lax`).

- [ ] **Step 5: Register + apply CORS in Program.cs**

In `src/Struo.Api/Program.cs`, after `builder.Services.AddStruoAuth(...)` (line ~38) add:

```csharp
builder.Services.AddStruoCors(builder.Configuration);
```

And in the pipeline, immediately after `app.UseSerilogRequestLogging();` (line ~49), before `app.UseAuthentication();`:

```csharp
app.UseStruoCors(app.Configuration);
```

(`app.Configuration` is available on `WebApplication`.)

- [ ] **Step 6: Run the new tests + the full suite**

Run: `dotnet test --filter FullyQualifiedName~CorsAndCookieTests`
Expected: PASS (2 tests).

Run: `dotnet test`
Expected: **all previously-passing tests still pass** (default-off path unchanged), plus the 2 new tests. 0 failed / 0 skipped.

- [ ] **Step 7: Commit**

```bash
git add src/Struo.Api/Auth/CorsWiring.cs src/Struo.Api/Auth/AuthWiring.cs src/Struo.Api/Program.cs tests/Struo.Tests/Api/CorsAndCookieTests.cs
git commit -m "feat(api): configurable CORS + cross-origin cookie mode (default-off)"
```

---

### Task 2: Scaffold the `frontend/` SPA (pnpm, Vite, Vue 3 + TS, PrimeVue, Pinia, Router, Vitest)

**Files:**
- Create: `frontend/` (Vite scaffold: `package.json`, `pnpm-lock.yaml`, `vite.config.ts`, `tsconfig*.json`, `index.html`, `src/main.ts`, `src/App.vue`)
- Create: `frontend/.env.example`
- Create: `frontend/vitest.setup.ts`
- Create: `frontend/tests/smoke.test.ts` (toolchain smoke)
- Modify: `.gitignore` (ignore `frontend/node_modules`, `frontend/dist`, `frontend/coverage`)

**Interfaces:**
- Produces: a working pnpm project under `frontend/` where `pnpm test` and `pnpm build` succeed; `VITE_API_BASE_URL` env convention documented in `.env.example`.

- [ ] **Step 1: Scaffold with Vite (installs latest via pnpm)**

Run (from repo root, PowerShell):

```bash
pnpm create vite@latest frontend --template vue-ts
cd frontend
pnpm install
```

> Do NOT hand-edit versions into `package.json`. All versions come from pnpm.

- [ ] **Step 2: Add runtime + dev dependencies (latest, via pnpm)**

Run (inside `frontend/`):

```bash
pnpm add vue-router pinia primevue @primeuix/themes primeicons
pnpm add -D vitest @vue/test-utils jsdom @vitest/coverage-v8
```

- [ ] **Step 3: Configure Vite (dev proxy + Vitest)**

```typescript
// frontend/vite.config.ts
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  server: {
    port: 5173,
    // Same-origin dev/E2E: browser hits /api on the Vite origin, proxied to the API.
    proxy: { '/api': { target: 'http://localhost:5080', changeOrigin: true } },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./vitest.setup.ts'],
    coverage: { provider: 'v8', reportsDirectory: './coverage' },
  },
})
```

> If the API's dev HTTP port differs from `5080`, update `target` to match `launchSettings.json`.

```typescript
// frontend/vitest.setup.ts
// Placeholder for global test setup (PrimeVue config, etc.). Intentionally minimal for 7a.
export {}
```

- [ ] **Step 4: Document env + write the toolchain smoke test**

```bash
# frontend/.env.example
# Leave unset for same-origin dev/E2E via the Vite proxy (apiClient falls back to "/api").
# Set to the API origin for true cross-origin deployments, e.g. https://api.example.com
VITE_API_BASE_URL=
```

```typescript
// frontend/tests/smoke.test.ts
import { describe, it, expect } from 'vitest'

describe('toolchain', () => {
  it('runs vitest', () => {
    expect(1 + 1).toBe(2)
  })
})
```

Ensure `frontend/package.json` `scripts` has `"test": "vitest run"`, `"test:watch": "vitest"`, and the template's `"build": "vue-tsc -b && vite build"` (add `test`/`test:watch` if missing).

- [ ] **Step 5: Ignore build/dep artifacts**

Append to root `.gitignore`:

```
# Frontend
frontend/node_modules/
frontend/dist/
frontend/coverage/
```

- [ ] **Step 6: Verify the toolchain**

Run (inside `frontend/`):

```bash
pnpm test
pnpm build
```

Expected: `pnpm test` → 1 passed; `pnpm build` → succeeds (dist produced).

- [ ] **Step 7: Commit**

```bash
git add frontend .gitignore
git commit -m "chore(frontend): scaffold Vue 3 + TS SPA (Vite, PrimeVue, Pinia, Router, Vitest) via pnpm"
```

---

### Task 3: `apiClient` (TDD)

**Files:**
- Create: `frontend/src/api/apiClient.ts`
- Test: `frontend/src/api/apiClient.test.ts`

**Interfaces:**
- Produces:
  - `type ApiError = { message: string }`
  - `class ApiClient { constructor(baseUrl: string); setUnauthorizedHandler(fn: () => void): void; get<T>(path: string): Promise<T>; post<T>(path: string, body?: unknown): Promise<T> }`
  - `apiClient: ApiClient` (singleton, `baseUrl = import.meta.env.VITE_API_BASE_URL || '/api'`)
  - On non-2xx: throws `Error(error.message)`; on 401 additionally invokes the unauthorized handler.
  - `data` envelope is unwrapped: `{ data: X }` → returns `X`. `204`/empty body → returns `undefined as T`.

- [ ] **Step 1: Write the failing test**

```typescript
// frontend/src/api/apiClient.test.ts
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { ApiClient } from './apiClient'

function mockFetch(status: number, body: unknown) {
  return vi.fn().mockResolvedValue({
    status,
    ok: status >= 200 && status < 300,
    json: async () => body,
    text: async () => JSON.stringify(body),
  } as unknown as Response)
}

describe('ApiClient', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('unwraps the data envelope on success', async () => {
    vi.stubGlobal('fetch', mockFetch(200, { data: { id: 'u1' } }))
    const c = new ApiClient('/api')
    const result = await c.get<{ id: string }>('/auth/me')
    expect(result).toEqual({ id: 'u1' })
  })

  it('sends credentials: include', async () => {
    const f = mockFetch(200, { data: {} })
    vi.stubGlobal('fetch', f)
    await new ApiClient('/api').get('/auth/me')
    expect(f).toHaveBeenCalledWith('/api/auth/me', expect.objectContaining({ credentials: 'include' }))
  })

  it('throws error.message on failure', async () => {
    vi.stubGlobal('fetch', mockFetch(401, { error: { message: 'Invalid credentials.' } }))
    const c = new ApiClient('/api')
    await expect(c.get('/auth/me')).rejects.toThrow('Invalid credentials.')
  })

  it('invokes the unauthorized handler on 401', async () => {
    vi.stubGlobal('fetch', mockFetch(401, { error: { message: 'x' } }))
    const c = new ApiClient('/api')
    const onUnauth = vi.fn()
    c.setUnauthorizedHandler(onUnauth)
    await expect(c.get('/auth/me')).rejects.toThrow()
    expect(onUnauth).toHaveBeenCalledOnce()
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm test apiClient`
Expected: FAIL — `Cannot find module './apiClient'`.

- [ ] **Step 3: Implement `apiClient.ts`**

```typescript
// frontend/src/api/apiClient.ts
export type ApiError = { message: string }

export class ApiClient {
  private onUnauthorized: (() => void) | null = null
  constructor(private readonly baseUrl: string) {}

  setUnauthorizedHandler(fn: () => void): void {
    this.onUnauthorized = fn
  }

  get<T>(path: string): Promise<T> {
    return this.request<T>('GET', path)
  }

  post<T>(path: string, body?: unknown): Promise<T> {
    return this.request<T>('POST', path, body)
  }

  private async request<T>(method: string, path: string, body?: unknown): Promise<T> {
    const res = await fetch(`${this.baseUrl}${path}`, {
      method,
      credentials: 'include',
      headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    })

    if (res.status === 401) this.onUnauthorized?.()

    if (!res.ok) {
      let message = `Request failed (${res.status})`
      try {
        const payload = await res.json()
        message = (payload?.error as ApiError)?.message ?? message
      } catch { /* non-JSON error body: keep default message */ }
      throw new Error(message)
    }

    if (res.status === 204) return undefined as T
    const text = await res.text()
    if (!text) return undefined as T
    const payload = JSON.parse(text)
    return (payload?.data ?? payload) as T
  }
}

export const apiClient = new ApiClient(import.meta.env.VITE_API_BASE_URL || '/api')
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm test apiClient`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/api/apiClient.ts frontend/src/api/apiClient.test.ts
git commit -m "feat(frontend): apiClient with envelope unwrap + 401 handler"
```

---

### Task 4: `authStore` (Pinia, TDD)

**Files:**
- Create: `frontend/src/stores/authStore.ts`
- Test: `frontend/src/stores/authStore.test.ts`

**Interfaces:**
- Consumes: `apiClient` (`get`/`post`) from Task 3.
- Produces (Pinia `useAuthStore`):
  - state: `user: { id: string } | null`
  - getter: `isAuthenticated: boolean`
  - actions: `login(email: string, password: string): Promise<void>`, `logout(): Promise<void>`, `fetchCurrentUser(): Promise<void>`
  - `login` throws on failure (leaving `user` null); `fetchCurrentUser` swallows errors → `user` null.

- [ ] **Step 1: Write the failing test**

```typescript
// frontend/src/stores/authStore.test.ts
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useAuthStore } from './authStore'
import { apiClient } from '../api/apiClient'

vi.mock('../api/apiClient', () => ({
  apiClient: { get: vi.fn(), post: vi.fn(), setUnauthorizedHandler: vi.fn() },
}))

describe('authStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('login success sets the user and isAuthenticated', async () => {
    vi.mocked(apiClient.post).mockResolvedValue({ id: 'u1' })
    vi.mocked(apiClient.get).mockResolvedValue({ id: 'u1' }) // follow-up fetchCurrentUser
    const store = useAuthStore()
    await store.login('a@b.com', 'pw')
    expect(store.user).toEqual({ id: 'u1' })
    expect(store.isAuthenticated).toBe(true)
    expect(apiClient.post).toHaveBeenCalledWith('/auth/login', { email: 'a@b.com', password: 'pw' })
  })

  it('login failure leaves user null and rethrows', async () => {
    vi.mocked(apiClient.post).mockRejectedValue(new Error('Invalid credentials.'))
    const store = useAuthStore()
    await expect(store.login('a@b.com', 'bad')).rejects.toThrow('Invalid credentials.')
    expect(store.isAuthenticated).toBe(false)
  })

  it('fetchCurrentUser sets user on 200', async () => {
    vi.mocked(apiClient.get).mockResolvedValue({ id: 'u9' })
    const store = useAuthStore()
    await store.fetchCurrentUser()
    expect(store.user).toEqual({ id: 'u9' })
  })

  it('fetchCurrentUser clears user on error (401)', async () => {
    vi.mocked(apiClient.get).mockRejectedValue(new Error('unauth'))
    const store = useAuthStore()
    await store.fetchCurrentUser()
    expect(store.user).toBeNull()
    expect(store.isAuthenticated).toBe(false)
  })

  it('logout clears the user', async () => {
    vi.mocked(apiClient.post).mockResolvedValue(undefined)
    const store = useAuthStore()
    store.user = { id: 'u1' }
    await store.logout()
    expect(store.user).toBeNull()
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm test authStore`
Expected: FAIL — `Cannot find module './authStore'`.

- [ ] **Step 3: Implement `authStore.ts`**

```typescript
// frontend/src/stores/authStore.ts
import { defineStore } from 'pinia'
import { apiClient } from '../api/apiClient'

export type CurrentUser = { id: string }

export const useAuthStore = defineStore('auth', {
  state: () => ({ user: null as CurrentUser | null }),
  getters: {
    isAuthenticated: (state) => state.user !== null,
  },
  actions: {
    async login(email: string, password: string): Promise<void> {
      // Login returns { id }; then confirm via /me for a canonical session.
      await apiClient.post<CurrentUser>('/auth/login', { email, password })
      await this.fetchCurrentUser()
    },
    async logout(): Promise<void> {
      try {
        await apiClient.post('/auth/logout')
      } finally {
        this.user = null
      }
    },
    async fetchCurrentUser(): Promise<void> {
      try {
        this.user = await apiClient.get<CurrentUser>('/auth/me')
      } catch {
        this.user = null // unauthenticated / no session
      }
    },
  },
})
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm test authStore`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/stores/authStore.ts frontend/src/stores/authStore.test.ts
git commit -m "feat(frontend): authStore (login/logout/fetchCurrentUser)"
```

---

### Task 5: Router + auth guard (TDD)

**Files:**
- Create: `frontend/src/router/index.ts`
- Create: `frontend/src/router/guard.ts` (pure guard function, unit-testable)
- Test: `frontend/src/router/guard.test.ts`

**Interfaces:**
- Consumes: `useAuthStore` (Task 4); views/layout (Task 6).
- Produces:
  - `authGuard(to, isAuthenticated): true | { name: string }` — pure function: unauthenticated + protected → `{ name: 'login' }`; authenticated + `to.name === 'login'` → `{ name: 'dashboard' }`; else `true`.
  - `router` (Vue Router instance) wiring `beforeEach` to `authGuard` using the store's `isAuthenticated`.
  - Routes: `/login` (name `login`, `meta.public = true`), `/` (`AppShell`) with child `''` (name `dashboard`).

- [ ] **Step 1: Write the failing test**

```typescript
// frontend/src/router/guard.test.ts
import { describe, it, expect } from 'vitest'
import { authGuard } from './guard'

describe('authGuard', () => {
  it('redirects unauthenticated users on protected routes to login', () => {
    expect(authGuard({ name: 'dashboard', meta: {} }, false)).toEqual({ name: 'login' })
  })

  it('allows unauthenticated users on public routes', () => {
    expect(authGuard({ name: 'login', meta: { public: true } }, false)).toBe(true)
  })

  it('redirects authenticated users away from login to dashboard', () => {
    expect(authGuard({ name: 'login', meta: { public: true } }, true)).toEqual({ name: 'dashboard' })
  })

  it('allows authenticated users on protected routes', () => {
    expect(authGuard({ name: 'dashboard', meta: {} }, true)).toBe(true)
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm test guard`
Expected: FAIL — `Cannot find module './guard'`.

- [ ] **Step 3: Implement the pure guard**

```typescript
// frontend/src/router/guard.ts
export type GuardTarget = { name: string | null | undefined; meta: { public?: boolean } }
export type GuardResult = true | { name: string }

export function authGuard(to: GuardTarget, isAuthenticated: boolean): GuardResult {
  const isPublic = to.meta.public === true
  if (!isAuthenticated && !isPublic) return { name: 'login' }
  if (isAuthenticated && to.name === 'login') return { name: 'dashboard' }
  return true
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm test guard`
Expected: PASS (4 tests).

- [ ] **Step 5: Wire the router (verified by build in Task 6, not a unit test)**

```typescript
// frontend/src/router/index.ts
import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '../stores/authStore'
import { authGuard } from './guard'
import AppShell from '../layouts/AppShell.vue'
import LoginView from '../views/LoginView.vue'
import DashboardView from '../views/DashboardView.vue'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/login', name: 'login', component: LoginView, meta: { public: true } },
    {
      path: '/',
      component: AppShell,
      children: [{ path: '', name: 'dashboard', component: DashboardView }],
    },
  ],
})

router.beforeEach((to) => {
  const auth = useAuthStore()
  return authGuard({ name: to.name as string, meta: to.meta }, auth.isAuthenticated)
})

export default router
```

> `index.ts` imports the views/layout created in Task 6, so it will not compile until Task 6 is done. That is expected — the guard unit tests in this task do not import `index.ts`, and `pnpm build` (Task 6 Step 6) is where the wiring is verified. Execute Task 6 next.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/router
git commit -m "feat(frontend): router + pure auth guard"
```

---

### Task 6: Views + AppShell (component tests)

**Files:**
- Create: `frontend/src/views/LoginView.vue`
- Create: `frontend/src/views/DashboardView.vue`
- Create: `frontend/src/layouts/AppShell.vue`
- Test: `frontend/src/views/LoginView.test.ts`
- Test: `frontend/src/layouts/AppShell.test.ts`

**Interfaces:**
- Consumes: `useAuthStore` (Task 4), `vue-router` (`useRouter`).
- Produces: `LoginView` (email/password form; submit → `authStore.login` → `router.push({ name: 'dashboard' })`; error → inline message). `AppShell` (top bar + logout button calling `authStore.logout` → `router.push({ name: 'login' })` + `<router-view>`). `DashboardView` (static heading "Dashboard").

- [ ] **Step 1: Write the failing LoginView test**

```typescript
// frontend/src/views/LoginView.test.ts
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import LoginView from './LoginView.vue'
import { useAuthStore } from '../stores/authStore'

const push = vi.fn()
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))

describe('LoginView', () => {
  beforeEach(() => { setActivePinia(createPinia()); vi.clearAllMocks() })

  it('calls authStore.login and navigates on success', async () => {
    const store = useAuthStore()
    const loginSpy = vi.spyOn(store, 'login').mockResolvedValue()
    const wrapper = mount(LoginView)
    await wrapper.find('input[type="email"]').setValue('a@b.com')
    await wrapper.find('input[type="password"]').setValue('pw')
    await wrapper.find('form').trigger('submit.prevent')
    await new Promise((r) => setTimeout(r, 0))
    expect(loginSpy).toHaveBeenCalledWith('a@b.com', 'pw')
    expect(push).toHaveBeenCalledWith({ name: 'dashboard' })
  })

  it('shows an error message when login fails', async () => {
    const store = useAuthStore()
    vi.spyOn(store, 'login').mockRejectedValue(new Error('Invalid credentials.'))
    const wrapper = mount(LoginView)
    await wrapper.find('input[type="email"]').setValue('a@b.com')
    await wrapper.find('input[type="password"]').setValue('bad')
    await wrapper.find('form').trigger('submit.prevent')
    await new Promise((r) => setTimeout(r, 0))
    expect(wrapper.text()).toContain('Invalid credentials.')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm test LoginView`
Expected: FAIL — `Cannot find module './LoginView.vue'`.

- [ ] **Step 3: Implement the views + shell**

```vue
<!-- frontend/src/views/LoginView.vue -->
<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '../stores/authStore'

const email = ref('')
const password = ref('')
const error = ref('')
const submitting = ref(false)
const auth = useAuthStore()
const router = useRouter()

async function onSubmit() {
  error.value = ''
  submitting.value = true
  try {
    await auth.login(email.value, password.value)
    router.push({ name: 'dashboard' })
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Login failed.'
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <div class="login">
    <h1>StruoCMS Admin</h1>
    <form @submit.prevent="onSubmit">
      <input type="email" v-model="email" placeholder="Email" required />
      <input type="password" v-model="password" placeholder="Password" required />
      <button type="submit" :disabled="submitting">Sign in</button>
      <p v-if="error" class="error" role="alert">{{ error }}</p>
    </form>
  </div>
</template>
```

```vue
<!-- frontend/src/views/DashboardView.vue -->
<script setup lang="ts"></script>
<template>
  <section>
    <h2>Dashboard</h2>
    <p>You are signed in. Collection management arrives in Phase 7b.</p>
  </section>
</template>
```

```vue
<!-- frontend/src/layouts/AppShell.vue -->
<script setup lang="ts">
import { useRouter } from 'vue-router'
import { useAuthStore } from '../stores/authStore'

const auth = useAuthStore()
const router = useRouter()

async function onLogout() {
  await auth.logout()
  router.push({ name: 'login' })
}
</script>

<template>
  <div class="shell">
    <header>
      <span class="brand">StruoCMS</span>
      <button type="button" class="logout" @click="onLogout">Log out</button>
    </header>
    <nav><!-- collection nav placeholder (Phase 7b) --></nav>
    <main><router-view /></main>
  </div>
</template>
```

> For 7a the native `input`/`button`/`form` elements keep the tests simple and the walking skeleton honest; PrimeVue components can replace them in later phases. Keep the `type="email"`/`type="password"`/`form`/`button.logout` selectors the tests and E2E rely on.

- [ ] **Step 4: Write + run the AppShell test**

```typescript
// frontend/src/layouts/AppShell.test.ts
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import AppShell from './AppShell.vue'
import { useAuthStore } from '../stores/authStore'

const push = vi.fn()
vi.mock('vue-router', () => ({ useRouter: () => ({ push }), RouterView: { template: '<div/>' } }))

describe('AppShell', () => {
  beforeEach(() => { setActivePinia(createPinia()); vi.clearAllMocks() })

  it('logout calls the store and routes to login', async () => {
    const store = useAuthStore()
    const logoutSpy = vi.spyOn(store, 'logout').mockResolvedValue()
    const wrapper = mount(AppShell, { global: { stubs: { RouterView: true } } })
    await wrapper.find('button.logout').trigger('click')
    await new Promise((r) => setTimeout(r, 0))
    expect(logoutSpy).toHaveBeenCalledOnce()
    expect(push).toHaveBeenCalledWith({ name: 'login' })
  })
})
```

Run: `pnpm test LoginView AppShell`
Expected: PASS (3 tests total).

- [ ] **Step 5: Run the full frontend suite**

Run: `pnpm test`
Expected: PASS — apiClient (4) + authStore (5) + guard (4) + LoginView (2) + AppShell (1) + smoke (1).

- [ ] **Step 6: Verify the build compiles (router wiring from Task 5 included)**

Run: `pnpm build`
Expected: succeeds.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/views frontend/src/layouts
git commit -m "feat(frontend): LoginView, DashboardView, AppShell + component tests"
```

---

### Task 7: App bootstrap (`main.ts`) — wire PrimeVue, Pinia, router, 401 handler, session bootstrap

**Files:**
- Modify: `frontend/src/main.ts`
- Modify: `frontend/src/App.vue` (reduce to `<router-view />`)

**Interfaces:**
- Consumes: `apiClient` (Task 3), `router` (Task 5), `useAuthStore` (Task 4).
- Produces: a mounted app that (a) registers PrimeVue Aura theme + Pinia + router, (b) wires `apiClient.setUnauthorizedHandler` to clear the store + route to `/login`, (c) calls `authStore.fetchCurrentUser()` before mount so the guard sees the resolved session.

- [ ] **Step 1: Implement `main.ts`**

```typescript
// frontend/src/main.ts
import { createApp } from 'vue'
import { createPinia } from 'pinia'
import PrimeVue from 'primevue/config'
import Aura from '@primeuix/themes/aura'
import 'primeicons/primeicons.css'
import App from './App.vue'
import router from './router'
import { apiClient } from './api/apiClient'
import { useAuthStore } from './stores/authStore'

const app = createApp(App)
const pinia = createPinia()
app.use(pinia)
app.use(PrimeVue, { theme: { preset: Aura } })

const auth = useAuthStore(pinia)
apiClient.setUnauthorizedHandler(() => {
  auth.user = null
  if (router.currentRoute.value.name !== 'login') router.push({ name: 'login' })
})

// Resolve any existing session before the router/guard runs, then mount.
auth.fetchCurrentUser().finally(() => {
  app.use(router)
  app.mount('#app')
})
```

```vue
<!-- frontend/src/App.vue -->
<script setup lang="ts"></script>
<template>
  <router-view />
</template>
```

- [ ] **Step 2: Verify tests + build still pass**

Run: `pnpm test`
Expected: PASS (all as before).

Run: `pnpm build`
Expected: succeeds.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/main.ts frontend/src/App.vue
git commit -m "feat(frontend): app bootstrap — PrimeVue/Pinia/router, 401 handler, session bootstrap"
```

---

### Task 8: Playwright E2E — login → dashboard → logout

**Files:**
- Create: `frontend/e2e/auth.spec.ts`
- Create: `frontend/playwright.config.ts`
- Modify: `frontend/package.json` (add `"e2e": "playwright test"` script)
- Create: `frontend/e2e/README.md` (documents the seeded dev user + how to run)

**Interfaces:**
- Consumes: the running API (dev) + Vite dev server (proxying `/api`). Uses a seeded dev admin (the dev bootstrap admin from `Auth:BootstrapAdmin`, or a user seeded via SQL before the run — documented in the README).

- [ ] **Step 1: Install Playwright (latest, via pnpm)**

Run (inside `frontend/`):

```bash
pnpm add -D @playwright/test
pnpm exec playwright install chromium
```

- [ ] **Step 2: Configure Playwright to boot Vite**

```typescript
// frontend/playwright.config.ts
import { defineConfig } from '@playwright/test'

export default defineConfig({
  testDir: './e2e',
  use: { baseURL: 'http://localhost:5173', trace: 'on-first-retry' },
  webServer: {
    command: 'pnpm dev',
    url: 'http://localhost:5173',
    reuseExistingServer: true,
    timeout: 60_000,
  },
})
```

> The API must be running (dev, HTTP on the port in `vite.config.ts` proxy) with a known user seeded. Document credentials in `e2e/README.md`. This E2E deliberately uses the same-origin proxy path; the true cross-origin HTTPS flow is the manual smoke in the verification gate.

- [ ] **Step 3: Write the E2E spec**

```typescript
// frontend/e2e/auth.spec.ts
import { test, expect } from '@playwright/test'

const EMAIL = process.env.E2E_EMAIL ?? 'admin@struo.local'
const PASSWORD = process.env.E2E_PASSWORD ?? 'change-me-please'

test('login → dashboard → logout', async ({ page }) => {
  // Visiting a protected route while unauthenticated redirects to login.
  await page.goto('/')
  await expect(page).toHaveURL(/\/login$/)

  await page.fill('input[type="email"]', EMAIL)
  await page.fill('input[type="password"]', PASSWORD)
  await page.click('button[type="submit"]')

  await expect(page).toHaveURL(/\/$/)
  await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible()

  await page.click('button.logout')
  await expect(page).toHaveURL(/\/login$/)
})
```

- [ ] **Step 4: Run the E2E**

Ensure the API is running with the seeded user, then run (inside `frontend/`):

```bash
pnpm e2e
```

Expected: 1 passed.

- [ ] **Step 5: Commit**

```bash
git add frontend/playwright.config.ts frontend/e2e frontend/package.json
git commit -m "test(frontend): Playwright E2E login → dashboard → logout"
```

---

### Task 9: Verification gate + docs

**Files:**
- Modify: `docs/ROADMAP.md` (mark Phase 7a done + link spec/plan)
- Create: `frontend/README.md` (run/build/test/e2e + env + cross-origin note)

- [ ] **Step 1: Backend suite**

Run: `dotnet build` — clean (warnings-as-errors). `dotnet test` — 246 prior + 2 new CORS tests, 0 failed / 0 skipped.

- [ ] **Step 2: Frontend suites**

Run (inside `frontend/`): `pnpm test` (all unit/component pass), `pnpm build` (succeeds), `pnpm e2e` (login→dashboard→logout passes).

- [ ] **Step 3: Manual cross-origin smoke (spec §9)**

Run the API over HTTPS with `Struo:Cors:AllowedOrigins` set to the Vite HTTPS origin, and Vite over HTTPS with `VITE_API_BASE_URL` pointing at the API origin. Confirm: login round-trips (cookie set + `/me` 200 cross-origin), logout → subsequent `/me` returns 401. Record evidence.

- [ ] **Step 4: Version-policy check (§1a)**

Confirm every version in `frontend/package.json` / `frontend/pnpm-lock.yaml` came from pnpm (no hand-authored versions); backend used `dotnet add package` if any package was added.

- [ ] **Step 5: Update docs + commit**

Write `frontend/README.md` (scripts, env, dev proxy vs cross-origin). Update `docs/ROADMAP.md` Phase 7a row to done with spec + plan links.

```bash
git add frontend/README.md docs/ROADMAP.md
git commit -m "docs: Phase 7a verification + frontend README + roadmap update"
```

---

## Self-Review

**Spec coverage:**
- §1 scaffold/stack → Task 2. `apiClient` → Task 3. `authStore` → Task 4. router+guard → Task 5. AppShell/Login/Dashboard → Task 6. `main.ts` bootstrap + 401 + session → Task 7. Testing (Vitest+VTU+Playwright) → Tasks 3–8.
- §1a version policy → Global Constraints + Tasks 2/8 (pnpm add) + Task 9 Step 4.
- §5 backend CORS + conditional cookie → Task 1.
- §6 data flow (boot/login/logout/401) → Tasks 4 + 7.
- §7 error handling (401 redirect, inline login error, no field disclosure) → Tasks 3, 6, 7.
- §9 verification gate (backend green, FE tests, E2E, manual cross-origin smoke) → Task 9.
- Out-of-scope items (OIDC button, tables, forms, files/i18n, rbac UI) correctly absent.

**Placeholder scan:** No TBD/TODO. Every code step shows complete code. The one cross-task ordering dependency (Task 5's `index.ts` imports Task 6 views) is called out explicitly with its resolution (guard unit tests don't import it; build verifies it in Task 6).

**Type consistency:** `ApiClient.get/post`, `setUnauthorizedHandler` used identically in Tasks 3/4/7. `useAuthStore` state `user: {id}|null`, getter `isAuthenticated`, actions `login/logout/fetchCurrentUser` consistent across Tasks 4/5/6/7. `authGuard(to, isAuthenticated)` signature consistent in Task 5. Route names `login`/`dashboard` consistent across Tasks 5/6/8. Selectors (`input[type=email]`, `input[type=password]`, `button[type=submit]`, `button.logout`, heading `Dashboard`) consistent across component tests (Task 6) and E2E (Task 8).
