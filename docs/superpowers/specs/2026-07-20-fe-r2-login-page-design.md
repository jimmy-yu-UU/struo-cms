# FE-R2 — Login page + config-driven branding (frontend redesign, slice 2)

> **Status:** design (brainstormed, approved 2026-07-20).
> **Slice of:** the **frontend admin redesign** (decomposed FE-R0..R7 in
> `docs/superpowers/specs/2026-07-16-fe-r0-design-system-foundation-design.md` §1). FE-R0 (design-system
> foundation) and FE-R1 (app shell) are done + merged. **This spec is slice 2 (FE-R2): the login page**,
> rebuilt from the design prototype, **plus** a small config-driven **branding** capability that the login
> page motivates and the shell/tab-title also consume.
> **Design-reference rule (user):** the prototype
> (`docs/struo-cms-frontend-design/struocms-admin-prototype.html`, `#screen-login`) is a *visual*
> reference only — **no code is copied from it**; the screen is rebuilt from the design.

## 0. Summary

FE-R2 rebuilds the current bare `LoginView.vue` (plain HTML inputs) into the prototype's login screen
using the FE-R0 foundation (PrimeVue controls re-skinned by the custom Aura preset + `theme.css` OKLch
tokens for custom layout), and introduces **config-driven branding** so the CMS name and logo are no
longer hard-coded to "StruoCMS".

Two things ship together because the login screen is the first place branding appears:

1. **Login page** — centred card: brand block, email + password (PrimeVue `Password`, `toggleMask`),
   primary "登入" button, and — **only when OIDC is enabled** — a divider + "使用 Microsoft 365 登入"
   button. Remember-me and forgot-password (decorative in the prototype, no backend) are **omitted**.
2. **Config-driven branding** — a new anonymous **`GET /api/config`** endpoint exposes
   `{ oidcEnabled, brandName, brandLogoUrl }`, sourced from backend **config only** (`appsettings` /
   env). The frontend loads it once at bootstrap into an `appConfigStore` consumed by the login page, the
   shell topbar, and `document.title`.

**Write side is out of scope (by decision).** Editing branding through an in-app admin UI would require a
whole **Site-Settings subsystem** (DB-backed settings store + management screen + write endpoint + RBAC)
and is deferred to its own future phase. `GET /api/config` is deliberately a **stable read contract**: a
later phase can swap its data source from `appsettings` to a DB store (with `appsettings` as the seed
default) **without any change to the frontend read side**.

**Scope note vs earlier slices.** FE-R0/R1 were pure-frontend (no gate). FE-R2 adds a backend endpoint +
options, so it **does** carry a `dotnet` build/test gate and a **live Postgres gate** (the app is verified
running against real PG, per the project's "SQLite green ≠ PG correct" rule).

## 1. Chosen approach

**Single self-contained `LoginView.vue` + one small shared `BrandMark.vue`** (chosen over extracting a
`LoginCard` subcomponent — YAGNI for one screen — and over wrapping the card in PrimeVue `Card`, which
adds structure the lean prototype card doesn't need). The screen uses PrimeVue form controls
(`InputText` / `Password` / `Button`) for inputs and `theme.css` tokens for the custom layout (wrapper /
card / brand / divider) — the same two-layer pattern FE-R0/R1 established. `BrandMark.vue` is justified
as shared because it has **two** consumers (login page + topbar) with identical fallback logic.

## 2. Backend — `GET /api/config` + `BrandingOptions`

**`BrandingOptions`** (new, `Struo.Application/Configuration/` alongside existing options; bound from the
`Branding` config section, defaults keep current behaviour):

```csharp
public sealed class BrandingOptions
{
    public const string SectionName = "Branding";
    public string Name { get; set; } = "StruoCMS";
    public string? LogoUrl { get; set; }   // null → letter-mark fallback on the client
}
```

Registered in the API composition root with `services.AddOptions<BrandingOptions>().BindConfiguration(...)`
(no `ValidateOnStart` needed — both fields have safe defaults). `appsettings.json` gains a documented
`"Branding": { "Name": "StruoCMS", "LogoUrl": null }` sample block.

**Endpoint** — a dedicated `ConfigController` (keeps the route unambiguous; `AuthController` is already
`[Route("api/auth")]`, so hanging a `config` action off it would nest under `api/auth`):

```csharp
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
            oidcEnabled  = oidc.Value.Enabled,
            brandName    = branding.Value.Name,
            brandLogoUrl = branding.Value.LogoUrl,
        });
}
```

The 9a `EnvelopeResultFilter` wraps it → `{ "success": true, "data": { oidcEnabled, brandName,
brandLogoUrl } }`. Anonymous (pre-login) and safe (GET) — no CSRF, no auth. No DB, no migration.

## 3. Frontend — app-config store + branding

- **`src/api/appConfigApi.ts`** — `getAppConfig(): Promise<AppConfig>` via `apiClient.get('/config')`
  (`apiClient` auto-unwraps the envelope's `data`). `type AppConfig = { oidcEnabled: boolean; brandName:
  string; brandLogoUrl: string | null }`.
- **`src/stores/appConfigStore.ts`** (Pinia) — state seeded with safe defaults
  `{ oidcEnabled: false, brandName: 'StruoCMS', brandLogoUrl: null }`; getter `brandInitial` = first
  character of `brandName` (upper-cased, empty-safe); action `load()` → `getAppConfig()` on success, and
  on failure **silently keeps defaults** (branding/SSO degrade gracefully; password login is unaffected).
- **`src/main.ts`** — resolve config alongside the existing session resolve **before mount** (no brand
  flash): replace the lone `auth.fetchCurrentUser().finally(...)` with
  `Promise.allSettled([auth.fetchCurrentUser(), appConfig.load()]).finally(() => { document.title =
  appConfig.brandName; app.use(router); app.mount('#app') })`.
- **`src/components/shell/BrandMark.vue`** (new, shared) — props `{ name: string; logoUrl: string | null;
  initial: string }` (or reads the store directly — implementer's choice, kept presentational).
  Renders `<img :src="logoUrl" :alt="name" class="brand-logo">` when `logoUrl` is truthy; otherwise the
  existing coloured `<span class="mark">{{ initial }}</span>` placeholder. This preserves today's
  "coloured box + letter" look as the no-logo fallback, now driven by the brand name's first letter.

## 4. Frontend — `LoginView.vue` rebuild

Layout mirrors the prototype `#screen-login` (visual ref only):

```
.login-wrap  (min-height:100dvh; display:grid; place-items:center; padding:24px)
  └ .login-card (surface; width:min(420px,100%); shadow; radius-lg; padding; grid gap)
      ├ .brand (centred): <BrandMark/> + <b>{{ brandName }}</b> + <span.caption>內容管理系統</span>
      ├ <form.login-form @submit.prevent="onSubmit">
      │   ├ field: <label for=lg-email>{{ t('login.email') }}</label>
      │   │        <InputText id=lg-email type=email v-model=email autocomplete=username required autofocus>
      │   ├ field: <label for=lg-pw>{{ t('login.password') }}</label>
      │   │        <Password id=lg-pw v-model=password :feedback=false toggleMask
      │   │                  inputId=lg-pw :inputProps="{ autocomplete: 'current-password', required: true }">
      │   ├ <Button type=submit :loading=submitting :label="t('login.submit')" class="btn-block">
      │   └ <p v-if=error class=error role=alert>{{ error }}</p>
      └ <template v-if="appConfig.oidcEnabled">     ← SSO block, only when enabled
          ├ <div.divider role=separator :aria-label="t('login.or')">{{ t('login.or') }}</div>
          └ <Button type=button severity=secondary class="btn-block" @click=onSso>
               <MS 4-square logo svg aria-hidden> {{ t('login.ssoMicrosoft') }}
```

- **Omitted vs prototype:** remember-me checkbox, forgot-password link, the demo caption. (No backend
  support; would be dead UI.)
- **Theme:** no theme/language toggle on the login screen (those live in the shell). The global no-flash
  script + `themeStore.apply()` already set `.app-dark`, so the login page inherits dark/light
  automatically.
- Email `input[type="email"]` and the password control's inner `input[type="password"]` are preserved so
  the flow stays testable via those selectors.

## 5. Frontend — `TheTopbar.vue` update

Replace the hard-coded brand (`<span class="mark">S</span><b>StruoCMS</b>`) inside the existing
`button.brand-btn` with `<BrandMark/>` + `<b>{{ brandName }}</b>` sourced from `appConfigStore`. No change
to the button's routing, `aria-label`, or structure — only the brand content becomes config-driven.

## 6. Data flow

- **Bootstrap (once):** `appConfigStore.load()` → `{ oidcEnabled, brandName, brandLogoUrl }`; then
  `document.title = brandName`. Failure → defaults retained; app still works.
- **Branding render:** login page + topbar read `appConfigStore` reactively; `BrandMark` shows logo image
  or letter fallback.
- **Password login (unchanged):** `onSubmit` → `authStore.login(email, password)` → on success
  `router.push({ name: 'dashboard' })`; on failure set `error`.
- **SSO (only when `oidcEnabled`):** `onSso` → full-page navigation
  `window.location.href = \`${apiBaseUrl}/auth/login/oidc?returnUrl=/\`` (OIDC challenge is a redirect
  flow, not XHR). `apiClient` exports an `apiBaseUrl` const (= `import.meta.env.VITE_API_BASE_URL ||
  '/api'`) so the SSO target and XHR share one base.

## 7. Error handling

- **Login failure:** `ApiError.message` (backend "Invalid credentials.") shown in `role="alert"`; if the
  thrown error has no message, fall back to `t('login.failed')`.
- **Config fetch failure:** swallowed in `load()` → defaults (`oidcEnabled=false` hides the SSO block,
  `brandName='StruoCMS'`). Never blocks password login.
- **Double-submit:** button `:loading` + disabled while `submitting`.

## 8. i18n

New `login` namespace added to **both** `src/locales/zh-TW.ts` and `src/locales/en.ts` (the existing
symmetric-keys test enforces parity):

```ts
login: {
  subtitle: '內容管理系統',   // en: 'Content management system'
  email: '電子郵件',          // 'Email'
  password: '密碼',           // 'Password'
  submit: '登入',             // 'Sign in'
  or: '或',                   // 'or'
  ssoMicrosoft: '使用 Microsoft 365 登入',  // 'Sign in with Microsoft 365'
  failed: '登入失敗,請稍後再試。',           // 'Login failed. Please try again.'
}
```

Brand **name** is data (from config), not an i18n key. The "內容管理系統" subtitle is UI chrome → i18n.

## 9. Testing strategy (TDD)

**Backend (xUnit, WebApplicationFactory):**
- `GET /api/config` is reachable anonymously and returns `200` with the envelope shape
  `{ success:true, data:{ oidcEnabled, brandName, brandLogoUrl } }`.
- `oidcEnabled` reflects `OidcOptions.Enabled`; `brandName`/`brandLogoUrl` reflect `BrandingOptions`
  (default `"StruoCMS"` / `null` when unset; overridden values when configured).

**Frontend (vitest):**
- `appConfigApi`: returns parsed `{ oidcEnabled, brandName, brandLogoUrl }`.
- `appConfigStore`: `load()` success populates state; failure keeps defaults; `brandInitial` derives the
  upper-cased first character (empty-safe).
- `BrandMark`: renders `<img>` when `logoUrl` set (correct `src`/`alt`); renders letter `mark` when null.
- `LoginView`: renders brand name; submit calls `authStore.login(email, password)` and navigates to
  dashboard on success; shows the error on failure; **SSO block absent when `oidcEnabled=false`, present
  when `true`**; clicking SSO sets `window.location.href` to the OIDC path (mocked). (Rewrites the
  existing `LoginView.test.ts` for the new component; mounts with PrimeVue + i18n + pinia.)
- `TheTopbar`: renders the config brand name (update existing test's expectation from hard-coded
  "StruoCMS" to the store-provided value).

**Regression:** existing **387** frontend tests green; `pnpm build` (vue-tsc + vite) clean.

## 10. Acceptance gate

- `pnpm test` all green (387 + FE-R2 new/updated); `pnpm build` clean.
- `dotnet build` + backend tests green (new `/api/config` + `BrandingOptions` tests).
- **Live Postgres gate:** app run against real PG; `GET /api/config` returns branding + oidc; login page
  smoke via Playwright MCP (logged-out): (1) renders with default brand in light + dark, no flash;
  (2) a configured `Branding.Name`/`LogoUrl` shows through on login page **and** shell topbar **and** tab
  title; (3) invalid credentials show the error; (4) valid credentials land in the shell; (5) with OIDC
  disabled the SSO block is absent (with OIDC enabled, the button appears and points at the challenge —
  full external round-trip not required in the gate).

## 11. Out of scope (recorded)

- **Site-Settings subsystem** — in-app admin editing of branding (DB-backed settings store + management
  screen + write endpoint + RBAC). Its own future phase; `GET /api/config` is the stable read contract it
  will later back with a DB source.
- Remember-me and forgot-password flows (no backend; prototype-decorative).
- Uploading a brand logo through the media library (`brandLogoUrl` is a config-provided URL string for
  now).
- Theme-toggle / language-switcher on the login screen (they live in the shell).
- Dashboard / list / form / media / revisions rebuilds (FE-R3..R7).
