# FE-R2 — Login Page + Config-Driven Branding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the bare `LoginView.vue` into the prototype's login screen, and make the CMS brand name + logo config-driven (login page, shell topbar, and browser tab title) via a new anonymous `GET /api/config` endpoint.

**Architecture:** A new anonymous `ConfigController` exposes `{ oidcEnabled, brandName, brandLogoUrl }` sourced from backend `appsettings`-bound `BrandingOptions` + existing `OidcOptions`. The frontend loads it once at bootstrap into an `appConfigStore` consumed by a shared `BrandMark` component (logo image or letter fallback), the rebuilt login page (PrimeVue controls + `theme.css` tokens, conditional M365 SSO button), the topbar, and `document.title`.

**Tech Stack:** .NET 10 / ASP.NET Core Controllers / SqlSugar (backend untouched except the new endpoint + options); Vue 3 + TypeScript + PrimeVue + Pinia + vue-i18n + vitest (frontend); xUnit + WebApplicationFactory (backend tests).

## Global Constraints

- **Design-reference rule:** the prototype (`docs/struo-cms-frontend-design/struocms-admin-prototype.html` `#screen-login`) is a *visual* reference only — **no code is copied**; rebuild from the design.
- **Package versions:** never hand-author; nothing new to install here (vue-i18n / PrimeVue / xUnit already present).
- **Outbound JSON = camelCase** (already globally configured).
- **All API responses go through the 9a envelope** (`EnvelopeResultFilter`) → `{ success, data }` / `{ success:false, error }`. `apiClient.get` auto-unwraps `data`.
- **Write side (in-app branding editing) is OUT OF SCOPE** — deferred to a future Site-Settings phase. `GET /api/config` is a stable read contract.
- **Omit vs prototype:** remember-me, forgot-password, demo caption (no backend support).
- **Backend defaults must not change current behaviour:** `BrandingOptions.Name` defaults to `"StruoCMS"`, `LogoUrl` to `null`.
- **Regression floor:** existing **387** frontend tests stay green; `pnpm build` clean; backend suite green.
- **This slice carries a `dotnet` gate + live Postgres gate** (new endpoint).

---

### Task 1: Backend — `BrandingOptions` + anonymous `GET /api/config`

**Files:**
- Create: `src/Struo.Application/Configuration/BrandingOptions.cs`
- Create: `src/Struo.Api/Controllers/ConfigController.cs`
- Modify: `src/Struo.Api/Program.cs:64` (register options after `AddStruoOidc`)
- Modify: `src/Struo.Api/appsettings.json` (documented sample block)
- Test: `tests/Struo.Tests/Api/ConfigEndpointTests.cs`

**Interfaces:**
- Consumes: existing `Struo.Application.Security.OidcOptions` (`.Enabled`); existing `EnvelopeResultFilter`.
- Produces: `GET /api/config` → envelope `{ success:true, data:{ oidcEnabled:bool, brandName:string, brandLogoUrl:string|null } }`. `BrandingOptions { const string SectionName="Branding"; string Name="StruoCMS"; string? LogoUrl=null; }`.

- [ ] **Step 1: Write the failing test**

Create `tests/Struo.Tests/Api/ConfigEndpointTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

[Collection("ApiIntegration")]
public class ConfigEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    [Fact]
    public async Task Config_is_anonymous_and_returns_defaults()
    {
        var client = _factory.CreateClient(); // no auth
        var response = await client.GetAsync("/api/config");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("oidcEnabled").GetBoolean().Should().BeFalse();
        data.GetProperty("brandName").GetString().Should().Be("StruoCMS");
        data.GetProperty("brandLogoUrl").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Config_reflects_configured_branding()
    {
        var f = _factory.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Branding:Name"] = "Acme Docs",
                ["Branding:LogoUrl"] = "https://cdn.example.com/logo.svg",
            })));
        var response = await f.CreateClient().GetAsync("/api/config");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("brandName").GetString().Should().Be("Acme Docs");
        data.GetProperty("brandLogoUrl").GetString().Should().Be("https://cdn.example.com/logo.svg");
    }

    [Fact]
    public async Task Config_reflects_oidc_enabled()
    {
        var f = _factory.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Oidc:Enabled"] = "true",
                ["Oidc:Authority"] = "https://login.example.com",
                ["Oidc:ClientId"] = "test-client",
                ["Oidc:ClientSecret"] = "test-secret",
            })));
        var response = await f.CreateClient().GetAsync("/api/config");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("data").GetProperty("oidcEnabled").GetBoolean().Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests --filter FullyQualifiedName~ConfigEndpointTests`
Expected: FAIL — `404 NotFound` (endpoint doesn't exist yet).

- [ ] **Step 3: Create `BrandingOptions`**

Create `src/Struo.Application/Configuration/BrandingOptions.cs`:

```csharp
namespace Struo.Application.Configuration;

/// <summary>Config-bound branding shown by the admin UI (login page, shell topbar, tab title).
/// Bound from the <c>Branding</c> configuration section. Defaults keep the stock "StruoCMS" identity.
/// The write side (in-app editing) is a future Site-Settings phase; this is deploy-time config only.</summary>
public sealed class BrandingOptions
{
    public const string SectionName = "Branding";

    public string Name { get; set; } = "StruoCMS";
    public string? LogoUrl { get; set; }
}
```

- [ ] **Step 4: Create `ConfigController`**

Create `src/Struo.Api/Controllers/ConfigController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Struo.Application.Configuration;
using Struo.Application.Security;

namespace Struo.Api.Controllers;

/// <summary>Anonymous public bootstrap config for the SPA: whether external OIDC login is available
/// and the deploy-time branding (name + optional logo URL). A dedicated controller keeps the route as
/// <c>api/config</c> (AuthController owns <c>api/auth</c>).</summary>
[ApiController]
[Route("api/config")]
public sealed class ConfigController : ControllerBase
{
    [AllowAnonymous]
    [HttpGet]
    public IActionResult Get(
        [FromServices] IOptions<OidcOptions> oidc,
        [FromServices] IOptions<BrandingOptions> branding)
        => Ok(new
        {
            oidcEnabled = oidc.Value.Enabled,
            brandName = branding.Value.Name,
            brandLogoUrl = branding.Value.LogoUrl,
        });
}
```

- [ ] **Step 5: Register `BrandingOptions` in `Program.cs`**

In `src/Struo.Api/Program.cs`, immediately after line 64 (`builder.Services.AddStruoOidc(builder.Configuration);`) add:

```csharp
    builder.Services.AddOptions<Struo.Application.Configuration.BrandingOptions>()
        .BindConfiguration(Struo.Application.Configuration.BrandingOptions.SectionName);
```

- [ ] **Step 6: Add sample block to `appsettings.json`**

In `src/Struo.Api/appsettings.json`, add a top-level section (sibling of the existing sections):

```json
  "Branding": {
    "Name": "StruoCMS",
    "LogoUrl": null
  }
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/Struo.Tests --filter FullyQualifiedName~ConfigEndpointTests`
Expected: PASS (3 tests).

- [ ] **Step 8: Commit**

```bash
git add src/Struo.Application/Configuration/BrandingOptions.cs src/Struo.Api/Controllers/ConfigController.cs src/Struo.Api/Program.cs src/Struo.Api/appsettings.json tests/Struo.Tests/Api/ConfigEndpointTests.cs
git commit -m "feat(api): anonymous GET /api/config (oidcEnabled + branding) (FE-R2)"
```

---

### Task 2: Frontend — `appConfigApi` + `appConfigStore`

**Files:**
- Create: `frontend/src/api/appConfigApi.ts`
- Create: `frontend/src/stores/appConfigStore.ts`
- Test: `frontend/src/api/appConfigApi.test.ts`
- Test: `frontend/src/stores/appConfigStore.test.ts`

**Interfaces:**
- Consumes: `apiClient.get` from `../api/apiClient`.
- Produces: `type AppConfig = { oidcEnabled: boolean; brandName: string; brandLogoUrl: string | null }`; `getAppConfig(): Promise<AppConfig>`; Pinia store `useAppConfigStore` with state `{ oidcEnabled:false, brandName:'StruoCMS', brandLogoUrl:null }`, getter `brandInitial: string`, action `load(): Promise<void>`.

- [ ] **Step 1: Write the failing tests**

Create `frontend/src/api/appConfigApi.test.ts`:

```ts
import { describe, it, expect, vi, afterEach } from 'vitest'
import { getAppConfig } from './appConfigApi'
import { apiClient } from './apiClient'

describe('appConfigApi', () => {
  afterEach(() => vi.restoreAllMocks())

  it('fetches /config and returns the parsed config', async () => {
    const payload = { oidcEnabled: true, brandName: 'Acme', brandLogoUrl: 'https://x/logo.svg' }
    const spy = vi.spyOn(apiClient, 'get').mockResolvedValue(payload)
    const result = await getAppConfig()
    expect(spy).toHaveBeenCalledWith('/config')
    expect(result).toEqual(payload)
  })
})
```

Create `frontend/src/stores/appConfigStore.test.ts`:

```ts
import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useAppConfigStore } from './appConfigStore'
import * as api from '../api/appConfigApi'

describe('appConfigStore', () => {
  beforeEach(() => setActivePinia(createPinia()))
  afterEach(() => vi.restoreAllMocks())

  it('has safe defaults', () => {
    const store = useAppConfigStore()
    expect(store.oidcEnabled).toBe(false)
    expect(store.brandName).toBe('StruoCMS')
    expect(store.brandLogoUrl).toBeNull()
  })

  it('load() populates state from the API', async () => {
    vi.spyOn(api, 'getAppConfig').mockResolvedValue({
      oidcEnabled: true, brandName: 'Acme', brandLogoUrl: 'https://x/logo.svg',
    })
    const store = useAppConfigStore()
    await store.load()
    expect(store.oidcEnabled).toBe(true)
    expect(store.brandName).toBe('Acme')
    expect(store.brandLogoUrl).toBe('https://x/logo.svg')
  })

  it('load() keeps defaults when the API fails', async () => {
    vi.spyOn(api, 'getAppConfig').mockRejectedValue(new Error('network'))
    const store = useAppConfigStore()
    await store.load()
    expect(store.brandName).toBe('StruoCMS')
    expect(store.oidcEnabled).toBe(false)
  })

  it('brandInitial is the upper-cased first character', () => {
    const store = useAppConfigStore()
    store.brandName = 'acme'
    expect(store.brandInitial).toBe('A')
    store.brandName = ''
    expect(store.brandInitial).toBe('')
  })
})
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd frontend && pnpm vitest run src/api/appConfigApi.test.ts src/stores/appConfigStore.test.ts`
Expected: FAIL — modules not found.

- [ ] **Step 3: Implement `appConfigApi.ts`**

Create `frontend/src/api/appConfigApi.ts`:

```ts
import { apiClient } from './apiClient'

export type AppConfig = {
  oidcEnabled: boolean
  brandName: string
  brandLogoUrl: string | null
}

export function getAppConfig(): Promise<AppConfig> {
  return apiClient.get<AppConfig>('/config')
}
```

- [ ] **Step 4: Implement `appConfigStore.ts`**

Create `frontend/src/stores/appConfigStore.ts`:

```ts
import { defineStore } from 'pinia'
import { getAppConfig } from '../api/appConfigApi'

export const useAppConfigStore = defineStore('appConfig', {
  state: () => ({
    oidcEnabled: false,
    brandName: 'StruoCMS',
    brandLogoUrl: null as string | null,
  }),
  getters: {
    brandInitial: (state): string => (state.brandName.charAt(0).toUpperCase() ?? ''),
  },
  actions: {
    async load(): Promise<void> {
      try {
        const cfg = await getAppConfig()
        this.oidcEnabled = cfg.oidcEnabled
        this.brandName = cfg.brandName
        this.brandLogoUrl = cfg.brandLogoUrl
      } catch {
        // config unavailable — keep safe defaults; branding/SSO degrade, password login still works
      }
    },
  },
})
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `cd frontend && pnpm vitest run src/api/appConfigApi.test.ts src/stores/appConfigStore.test.ts`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/api/appConfigApi.ts frontend/src/stores/appConfigStore.ts frontend/src/api/appConfigApi.test.ts frontend/src/stores/appConfigStore.test.ts
git commit -m "feat(frontend): appConfigApi + appConfigStore (branding + oidc bootstrap) (FE-R2)"
```

---

### Task 3: Frontend — shared `BrandMark.vue`

**Files:**
- Create: `frontend/src/components/shell/BrandMark.vue`
- Test: `frontend/src/components/shell/BrandMark.test.ts`

**Interfaces:**
- Consumes: `useAppConfigStore` (reads `brandName`, `brandLogoUrl`, `brandInitial`).
- Produces: `<BrandMark />` — renders `<img class="brand-logo" :src :alt>` when `brandLogoUrl` is truthy, else `<span class="mark">{{ brandInitial }}</span>`. No props (reads the store) — presentational.

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/shell/BrandMark.test.ts`:

```ts
import { describe, it, expect, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import BrandMark from './BrandMark.vue'
import { useAppConfigStore } from '../../stores/appConfigStore'

describe('BrandMark', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('renders the letter mark when no logo is configured', () => {
    const store = useAppConfigStore()
    store.brandName = 'Acme'
    store.brandLogoUrl = null
    const wrapper = mount(BrandMark)
    expect(wrapper.find('img.brand-logo').exists()).toBe(false)
    expect(wrapper.find('span.mark').text()).toBe('A')
  })

  it('renders the logo image when a logo URL is configured', () => {
    const store = useAppConfigStore()
    store.brandName = 'Acme'
    store.brandLogoUrl = 'https://cdn/logo.svg'
    const wrapper = mount(BrandMark)
    const img = wrapper.find('img.brand-logo')
    expect(img.exists()).toBe(true)
    expect(img.attributes('src')).toBe('https://cdn/logo.svg')
    expect(img.attributes('alt')).toBe('Acme')
    expect(wrapper.find('span.mark').exists()).toBe(false)
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && pnpm vitest run src/components/shell/BrandMark.test.ts`
Expected: FAIL — component not found.

- [ ] **Step 3: Implement `BrandMark.vue`**

Create `frontend/src/components/shell/BrandMark.vue`:

```vue
<script setup lang="ts">
import { storeToRefs } from 'pinia'
import { useAppConfigStore } from '../../stores/appConfigStore'

const appConfig = useAppConfigStore()
const { brandName, brandLogoUrl, brandInitial } = storeToRefs(appConfig)
</script>

<template>
  <img
    v-if="brandLogoUrl"
    class="brand-logo"
    :src="brandLogoUrl"
    :alt="brandName"
  />
  <span v-else class="mark">{{ brandInitial }}</span>
</template>

<style scoped>
.brand-logo {
  width: 32px;
  height: 32px;
  flex: none;
  border-radius: 8px;
  object-fit: contain;
}
.mark {
  width: 32px;
  height: 32px;
  flex: none;
  border-radius: 8px;
  background: var(--accent);
  color: #fff;
  display: grid;
  place-items: center;
  font-weight: 700;
  font-size: 1.05rem;
}
</style>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && pnpm vitest run src/components/shell/BrandMark.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/shell/BrandMark.vue frontend/src/components/shell/BrandMark.test.ts
git commit -m "feat(frontend): shared BrandMark (logo image or letter fallback) (FE-R2)"
```

---

### Task 4: Frontend — bootstrap config load + `apiBaseUrl` export

**Files:**
- Modify: `frontend/src/api/apiClient.ts:110` (export `apiBaseUrl`)
- Modify: `frontend/src/main.ts:35-39` (load config before mount + set `document.title`)
- Test: `frontend/src/api/apiClient.baseUrl.test.ts`

**Interfaces:**
- Produces: `export const apiBaseUrl: string` from `apiClient.ts` (= `import.meta.env.VITE_API_BASE_URL || '/api'`), reused by the SSO redirect in Task 5.
- Consumes: `useAppConfigStore().load()` (Task 2).

- [ ] **Step 1: Write the failing test**

Create `frontend/src/api/apiClient.baseUrl.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { apiBaseUrl } from './apiClient'

describe('apiBaseUrl', () => {
  it('defaults to /api when no env override is set', () => {
    expect(apiBaseUrl).toBe('/api')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && pnpm vitest run src/api/apiClient.baseUrl.test.ts`
Expected: FAIL — `apiBaseUrl` is not exported.

- [ ] **Step 3: Export `apiBaseUrl` and use it for the singleton**

In `frontend/src/api/apiClient.ts`, replace the final line (line 110):

```ts
export const apiClient = new ApiClient(import.meta.env.VITE_API_BASE_URL || '/api')
```

with:

```ts
export const apiBaseUrl = import.meta.env.VITE_API_BASE_URL || '/api'
export const apiClient = new ApiClient(apiBaseUrl)
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && pnpm vitest run src/api/apiClient.baseUrl.test.ts`
Expected: PASS.

- [ ] **Step 5: Wire config load into `main.ts`**

In `frontend/src/main.ts`: add the store import after line 15 (`import { useUiLocaleStore } ...`):

```ts
import { useAppConfigStore } from './stores/appConfigStore'
```

Then replace the bootstrap block (lines 35-39):

```ts
// Resolve any existing session before the router/guard runs, then mount.
auth.fetchCurrentUser().finally(() => {
  app.use(router)
  app.mount('#app')
})
```

with:

```ts
// Resolve the public app config (branding + oidc) and any existing session before mount,
// so the brand renders without a flash. Neither rejection blocks mounting.
const appConfig = useAppConfigStore(pinia)
Promise.allSettled([auth.fetchCurrentUser(), appConfig.load()]).finally(() => {
  document.title = appConfig.brandName
  app.use(router)
  app.mount('#app')
})
```

- [ ] **Step 6: Run the full frontend suite (regression) + build**

Run: `cd frontend && pnpm test && pnpm build`
Expected: all green; build clean. (`main.ts` has no unit test — it is exercised by the live gate in Task 7.)

- [ ] **Step 7: Commit**

```bash
git add frontend/src/api/apiClient.ts frontend/src/api/apiClient.baseUrl.test.ts frontend/src/main.ts
git commit -m "feat(frontend): export apiBaseUrl + load app config (branding/oidc) before mount (FE-R2)"
```

---

### Task 5: Frontend — i18n `login` namespace + rebuild `LoginView.vue`

**Files:**
- Modify: `frontend/src/locales/zh-TW.ts` (add `login` block)
- Modify: `frontend/src/locales/en.ts` (add `login` block)
- Rewrite: `frontend/src/views/LoginView.vue`
- Rewrite: `frontend/src/views/LoginView.test.ts`

**Interfaces:**
- Consumes: `useAuthStore().login` (existing), `useAppConfigStore` (Task 2: `oidcEnabled`, `brandName`), `BrandMark` (Task 3), `apiBaseUrl` (Task 4), `t('login.*')`.
- Produces: rebuilt login screen. No new exported symbols.

- [ ] **Step 1: Add the `login` namespace to both locale files**

In `frontend/src/locales/zh-TW.ts`, add after the `breadcrumb` block (before the closing `}`):

```ts
  login: {
    subtitle: '內容管理系統',
    email: '電子郵件',
    password: '密碼',
    submit: '登入',
    or: '或',
    ssoMicrosoft: '使用 Microsoft 365 登入',
    failed: '登入失敗,請稍後再試。',
  },
```

In `frontend/src/locales/en.ts`, add the symmetric block:

```ts
  login: {
    subtitle: 'Content management system',
    email: 'Email',
    password: 'Password',
    submit: 'Sign in',
    or: 'or',
    ssoMicrosoft: 'Sign in with Microsoft 365',
    failed: 'Login failed. Please try again.',
  },
```

- [ ] **Step 2: Write the failing tests**

Rewrite `frontend/src/views/LoginView.test.ts`:

```ts
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import PrimeVue from 'primevue/config'
import LoginView from './LoginView.vue'
import { useAuthStore } from '../stores/authStore'
import { useAppConfigStore } from '../stores/appConfigStore'
import { i18n } from '../i18n'

const push = vi.fn()
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))

const mountLogin = () => mount(LoginView, { global: { plugins: [i18n, PrimeVue] } })

describe('LoginView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    i18n.global.locale.value = 'zh-TW'
  })

  it('renders the configured brand name', () => {
    useAppConfigStore().brandName = 'Acme Docs'
    const wrapper = mountLogin()
    expect(wrapper.text()).toContain('Acme Docs')
  })

  it('calls authStore.login and navigates on success', async () => {
    const store = useAuthStore()
    const loginSpy = vi.spyOn(store, 'login').mockResolvedValue()
    const wrapper = mountLogin()
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
    const wrapper = mountLogin()
    await wrapper.find('input[type="email"]').setValue('a@b.com')
    await wrapper.find('input[type="password"]').setValue('bad')
    await wrapper.find('form').trigger('submit.prevent')
    await new Promise((r) => setTimeout(r, 0))
    expect(wrapper.text()).toContain('Invalid credentials.')
  })

  it('hides the SSO button when oidc is disabled', () => {
    useAppConfigStore().oidcEnabled = false
    const wrapper = mountLogin()
    expect(wrapper.find('[data-test="sso"]').exists()).toBe(false)
  })

  it('shows the SSO button and navigates on click when oidc is enabled', async () => {
    useAppConfigStore().oidcEnabled = true
    const original = window.location
    Object.defineProperty(window, 'location', { configurable: true, value: { href: '' } })
    try {
      const wrapper = mountLogin()
      const sso = wrapper.find('[data-test="sso"]')
      expect(sso.exists()).toBe(true)
      await sso.trigger('click')
      expect(window.location.href).toBe('/api/auth/login/oidc?returnUrl=/')
    } finally {
      Object.defineProperty(window, 'location', { configurable: true, value: original })
    }
  })
})
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `cd frontend && pnpm vitest run src/views/LoginView.test.ts`
Expected: FAIL — new brand/SSO assertions fail against the current bare view.

- [ ] **Step 4: Rewrite `LoginView.vue`**

Replace `frontend/src/views/LoginView.vue` entirely:

```vue
<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { storeToRefs } from 'pinia'
import InputText from 'primevue/inputtext'
import Password from 'primevue/password'
import Button from 'primevue/button'
import { useAuthStore } from '../stores/authStore'
import { useAppConfigStore } from '../stores/appConfigStore'
import { apiBaseUrl } from '../api/apiClient'
import BrandMark from '../components/shell/BrandMark.vue'

const email = ref('')
const password = ref('')
const error = ref('')
const submitting = ref(false)

const auth = useAuthStore()
const router = useRouter()
const { t } = useI18n()
const appConfig = useAppConfigStore()
const { brandName, oidcEnabled } = storeToRefs(appConfig)

async function onSubmit() {
  error.value = ''
  submitting.value = true
  try {
    await auth.login(email.value, password.value)
    router.push({ name: 'dashboard' })
  } catch (e) {
    error.value = e instanceof Error && e.message ? e.message : t('login.failed')
  } finally {
    submitting.value = false
  }
}

function onSso() {
  window.location.href = `${apiBaseUrl}/auth/login/oidc?returnUrl=/`
}
</script>

<template>
  <div class="login-wrap">
    <div class="login-card">
      <div class="brand">
        <BrandMark />
        <b>{{ brandName }}</b>
        <span class="caption">{{ t('login.subtitle') }}</span>
      </div>

      <form class="login-form" @submit.prevent="onSubmit">
        <div class="field">
          <label for="lg-email">{{ t('login.email') }}</label>
          <InputText
            id="lg-email"
            v-model="email"
            type="email"
            autocomplete="username"
            required
            autofocus
          />
        </div>

        <div class="field">
          <label for="lg-pw">{{ t('login.password') }}</label>
          <Password
            input-id="lg-pw"
            v-model="password"
            :feedback="false"
            toggle-mask
            :input-props="{ autocomplete: 'current-password', required: true }"
          />
        </div>

        <Button
          type="submit"
          class="btn-block"
          :loading="submitting"
          :label="t('login.submit')"
        />

        <p v-if="error" class="error" role="alert">{{ error }}</p>

        <template v-if="oidcEnabled">
          <div class="divider" role="separator" :aria-label="t('login.or')">{{ t('login.or') }}</div>
          <Button
            type="button"
            severity="secondary"
            class="btn-block sso-btn"
            data-test="sso"
            @click="onSso"
          >
            <svg width="17" height="17" viewBox="0 0 21 21" aria-hidden="true">
              <rect width="10" height="10" fill="#f25022" />
              <rect x="11" width="10" height="10" fill="#7fba00" />
              <rect y="11" width="10" height="10" fill="#00a4ef" />
              <rect x="11" y="11" width="10" height="10" fill="#ffb900" />
            </svg>
            <span>{{ t('login.ssoMicrosoft') }}</span>
          </Button>
        </template>
      </form>
    </div>
  </div>
</template>

<style scoped>
.login-wrap {
  min-height: 100dvh;
  display: grid;
  place-items: center;
  padding: 24px;
  background: var(--bg);
}
.login-card {
  width: min(420px, 100%);
  padding: 40px 36px 32px;
  display: grid;
  gap: 26px;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  box-shadow: var(--shadow-2);
}
.brand {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 10px;
}
.brand b {
  font-size: 1.05rem;
  letter-spacing: -0.01em;
  color: var(--fg);
}
.brand .caption {
  font-size: 0.8rem;
  color: var(--muted);
}
.login-form {
  display: grid;
  gap: 18px;
}
.field {
  display: grid;
  gap: 6px;
}
.field label {
  font-size: 0.8rem;
  font-weight: 500;
  color: var(--muted);
}
.field :deep(.p-inputtext),
.field :deep(.p-password) {
  width: 100%;
}
.field :deep(.p-password input) {
  width: 100%;
}
.btn-block {
  width: 100%;
  justify-content: center;
}
.sso-btn {
  gap: 8px;
}
.divider {
  display: flex;
  align-items: center;
  gap: 14px;
  color: var(--muted);
  font-size: 0.8rem;
  white-space: nowrap;
}
.divider::before,
.divider::after {
  content: '';
  flex: 1;
  height: 1px;
  background: var(--border);
}
.error {
  color: var(--danger);
  font-size: 0.85rem;
  margin: 0;
}
</style>
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `cd frontend && pnpm vitest run src/views/LoginView.test.ts src/locales`
Expected: PASS (LoginView + the symmetric-locale-keys test that now covers the `login` namespace).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/views/LoginView.vue frontend/src/views/LoginView.test.ts frontend/src/locales/zh-TW.ts frontend/src/locales/en.ts
git commit -m "feat(frontend): rebuild LoginView on design system + conditional M365 SSO + i18n (FE-R2)"
```

---

### Task 6: Frontend — `TheTopbar.vue` brand → config

**Files:**
- Modify: `frontend/src/components/shell/TheTopbar.vue:1-31` (import + brand content)
- Modify: `frontend/src/components/shell/TheTopbar.test.ts` (brand-name assertion + BrandMark stub)

**Interfaces:**
- Consumes: `useAppConfigStore` (Task 2), `BrandMark` (Task 3).
- Produces: topbar brand content driven by config. No structural/route/aria change.

- [ ] **Step 1: Update the test (add a brand-name assertion; stub BrandMark)**

In `frontend/src/components/shell/TheTopbar.test.ts`, add `BrandMark` to the `stubs` object:

```ts
const stubs = {
  UiLanguageSwitcher: { template: '<div class="stub-lang" />' },
  ThemeToggle: { template: '<div class="stub-theme" />' },
  UserMenu: { template: '<div class="stub-user" />' },
  BrandMark: { template: '<span class="stub-brandmark" />' },
}
```

Add this test inside the `describe` block:

```ts
  it('renders the configured brand name', async () => {
    const { useAppConfigStore } = await import('../../stores/appConfigStore')
    useAppConfigStore().brandName = 'Acme Docs'
    const wrapper = mount(TheTopbar, { global: { plugins: [i18n], stubs } })
    expect(wrapper.find('.brand-btn').text()).toContain('Acme Docs')
  })
```

- [ ] **Step 2: Run tests to verify the new one fails**

Run: `cd frontend && pnpm vitest run src/components/shell/TheTopbar.test.ts`
Expected: FAIL — the brand button still renders the hard-coded "StruoCMS", not "Acme Docs".

- [ ] **Step 3: Update `TheTopbar.vue`**

In `frontend/src/components/shell/TheTopbar.vue`, add imports to the `<script setup>` block (after line 7 `import UserMenu ...`):

```ts
import { storeToRefs } from 'pinia'
import { useAppConfigStore } from '../../stores/appConfigStore'
import BrandMark from './BrandMark.vue'
```

and after line 11 (`const { t } = useI18n()`):

```ts
const { brandName } = storeToRefs(useAppConfigStore())
```

Then replace the brand button's inner content (line 30):

```vue
      <span class="mark">S</span><b>StruoCMS</b>
```

with:

```vue
      <BrandMark /><b>{{ brandName }}</b>
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd frontend && pnpm vitest run src/components/shell/TheTopbar.test.ts`
Expected: PASS (all four tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/shell/TheTopbar.vue frontend/src/components/shell/TheTopbar.test.ts
git commit -m "feat(frontend): topbar brand reads config (BrandMark + brandName) (FE-R2)"
```

---

### Task 7: Verification gate (full suites + live Postgres smoke)

**Files:** none (verification only — evidence gate).

**Interfaces:** Consumes everything from Tasks 1-6.

- [ ] **Step 1: Backend build + full test suite**

Run: `dotnet build && dotnet test tests/Struo.Tests`
Expected: build succeeds; all tests pass (including the 3 new `ConfigEndpointTests`).

- [ ] **Step 2: Frontend full test suite + type-checked build**

Run: `cd frontend && pnpm test && pnpm build`
Expected: all green (≥ 387 + new tests); `vue-tsc` + `vite build` clean (pre-existing >500 kB chunk advisory only).

- [ ] **Step 3: Live Postgres run + Playwright login smoke**

Start the API against real Postgres (`ASPNETCORE_URLS` set so the Vite proxy target matches — per repo memory `:5080`) and the frontend dev server (`pnpm dev --host 127.0.0.1`). Using Playwright MCP, logged-out, verify:
1. Login page renders with the default brand ("StruoCMS") in **both** light and dark, no brand flash on load.
2. Restart the API with `Branding:Name` + `Branding:LogoUrl` configured → the custom name/logo appear on the **login page**, the **shell topbar** (after login), and the **browser tab title**.
3. `GET /api/config` (via browser network or direct) returns `{ oidcEnabled, brandName, brandLogoUrl }` in the envelope.
4. Invalid credentials → the `role="alert"` error shows the backend message.
5. Valid credentials (bootstrap admin `admin@admin.com`) → lands in the shell.
6. With OIDC disabled → no SSO block on the login page. (If OIDC is enabled in config, the SSO button appears and points at `/api/auth/login/oidc`; full external round-trip is not required in this gate.)

Record the pass/fail evidence.

- [ ] **Step 4: Final commit (if any smoke-driven fixes were needed)**

```bash
git add -A
git commit -m "test(frontend): FE-R2 live Postgres login + branding smoke evidence"
```

(If no fixes were needed, skip — the gate is evidence, not necessarily a code change.)

---

## Self-Review

**1. Spec coverage:**
- `GET /api/config` + `BrandingOptions` → Task 1. ✓
- `appConfigApi` + `appConfigStore` (defaults, load, brandInitial, graceful failure) → Task 2. ✓
- `BrandMark` (logo image / letter fallback) → Task 3. ✓
- Bootstrap load before mount + `document.title` + `apiBaseUrl` export → Task 4. ✓
- `LoginView` rebuild (PrimeVue Password toggleMask, conditional SSO, omit remember-me/forgot-password, error handling, theme via tokens) + `login` i18n namespace → Task 5. ✓
- `TheTopbar` brand → config → Task 6. ✓
- Acceptance gate (dotnet + frontend + live PG + Playwright, all 6 spec smoke points) → Task 7. ✓
- Out-of-scope items (Site-Settings write side, remember-me/forgot-password, logo upload, login-screen theme/lang toggles) → not implemented, consistent with spec §11. ✓

**2. Placeholder scan:** No TBD/TODO/"handle edge cases"/"similar to". Every code step shows complete code. ✓

**3. Type consistency:** `AppConfig { oidcEnabled, brandName, brandLogoUrl }` identical across Tasks 1 (JSON), 2 (type), and consumers. Store surface `{ oidcEnabled, brandName, brandLogoUrl, brandInitial, load() }` used consistently in Tasks 3/4/5/6. `apiBaseUrl` defined in Task 4, consumed in Task 5. `data-test="sso"` selector defined and asserted in Task 5. ✓
