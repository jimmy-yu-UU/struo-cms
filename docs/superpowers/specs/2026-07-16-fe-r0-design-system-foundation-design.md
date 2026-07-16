# FE-R0 — Design-system foundation (frontend redesign, slice 0)

> **Status:** design (brainstormed, approved 2026-07-16).
> **Slice of:** the **frontend admin redesign** — a full visual re-skin of the Vue admin SPA to the
> approved design prototype (`docs/struo-cms-frontend-design/struocms-admin-prototype.html` +
> `brand-spec.md`), plus the deferred **9c-fe** revision-history/revert UI. The redesign was decomposed
> into independent, individually-verifiable slices (see §1). **This spec is slice 0 (FE-R0): the shared
> design-system foundation** every later slice builds on. No screen is visually rebuilt here.
> **Design-reference rule (user):** the prototype is a *visual* reference only — **no code is copied from
> it**; the entire frontend is rebuilt from the design, not lifted.

## 0. Summary

FE-R0 establishes the frontend's shared foundation so every subsequent screen slice re-skins against one
consistent system:

1. **Design-token layer** — the six OKLch core tokens from `brand-spec.md` (`--bg` / `--surface` / `--fg`
   / `--muted` / `--border` / `--accent`) + status colours + shadow / radius / spacing / font scales,
   for **light** and **`.app-dark`**, in `src/assets/theme.css`. Consumed by non-PrimeVue layout code.
2. **Custom Aura preset** — `definePreset(Aura, …)` mapping PrimeVue's `primary` → sky and `surface` →
   slate, with `darkModeSelector: '.app-dark'`. Drives PrimeVue components. Same underlying palette as the
   token layer, so both flip together and stay visually unified.
3. **Theme engine** — a Pinia `themeStore` + pure resolvers + a no-flash inline script in `index.html`
   (dark/light from `localStorage('struo.theme')`, falling back to `prefers-color-scheme`). **No toggle
   UI** (that lands in FE-R1's topbar).
4. **Admin-UI i18n engine** — `vue-i18n` (`legacy: false`), locale packs `zh-TW` + `en` (default `zh-TW`,
   fallback `en`), a `uiLocaleStore` persisting `struo.uiLocale`, **fully separate from the existing
   content-locale `languageStore`**. Seeds shell/common strings only. **No language-switcher UI** (FE-R1).

**Pure frontend.** No backend, API, route-structure, or persistence change → **no `dotnet` gate, no live
Postgres gate**. The app stays functional throughout: after R0, existing (not-yet-rebuilt) screens adopt
the new Aura preset — their look shifts slightly but nothing breaks, and all existing tests stay green.

## 1. Redesign decomposition (context — the master slicing)

Approved slicing; each slice is its own spec → plan → execute → verify cycle. Order: foundation first,
then screen-by-screen, revisions UI last (it depends on the shell + form design language being in place).

| Slice | Scope |
|---|---|
| **FE-R0 (this spec)** | Design-system foundation: token layer + custom Aura preset + theme engine + admin-UI i18n engine. |
| FE-R1 | App shell: topbar (brand / UI-language switcher / theme toggle / user menu) + collapsible sidebar + mobile drawer + breadcrumbs + toasts. Replaces the current bare `AppShell`. |
| FE-R2 | Login page (email/password + M365 SSO) per prototype. |
| FE-R3 | Dashboard (stat cards / recent updates / locale coverage / quick actions — data sources TBD at R3 brainstorm). |
| FE-R4 | Collection list (page-head + toolbar search/status filter + DataTable + paginator + Active/Trash switch, preserving 9b-fe). |
| FE-R5 | Item form (locale tabs with translation-completeness dots + translatable fields + side column, integrating all existing field types). |
| FE-R6 | Media library (upload / filter / grid·list toggle / file-detail dialog). |
| FE-R7 (**9c-fe**) | Revision-history + revert UI (consumes the existing 9c REST API), newly designed in the established design language (the prototype has no revisions screen). |

The topbar's theme-toggle button and UI-language switcher are **built in FE-R1**; FE-R0 only builds the
engines they call. Per-screen string extraction into i18n keys happens as each slice is rebuilt.

## 2. Chosen approach — `definePreset(Aura)` + OKLch CSS token layer

Approach A (chosen over "pure CSS-variable override" and "fully custom preset"): strongest control,
robust dark switching via `darkModeSelector`, aligned with the official PrimeVue theming path, minimal
blast radius on the existing 339 tests. `brand-spec.md` mandates PrimeVue Aura semantics; CLAUDE.md §1
mandates PrimeVue; the prototype itself annotates every control with `data-pv` Aura mappings — so real
PrimeVue components re-skinned via a custom preset is the natural fit, **not** a hand-rolled component set.

**Two layers, one palette.** PrimeVue components take their look from the preset's sky/slate scales;
custom layout (shell / sidebar / topbar / cards, built in later slices) takes it from the `theme.css`
OKLch tokens. Both derive from the same colours, so light/dark flip in lockstep and read as one system.

**Icons.** Keep the existing **primeicons** dependency (already mapped by Aura). The prototype's bespoke
inline SVG symbol sheet is **not** adopted — the redesign is about tokens and layout, not swapping icon
libraries. A later slice may add specific icons if a screen needs them.

## 3. Design-token layer — `src/assets/theme.css`

Global CSS custom properties, source-of-truth = `brand-spec.md` OKLch values. For non-PrimeVue layout.

```css
:root {
  /* six core (light) */
  --bg: oklch(0.984 0.003 247.9);     --surface: oklch(1 0 0);
  --fg: oklch(0.208 0.042 265.8);     --muted: oklch(0.554 0.046 257.4);
  --border: oklch(0.929 0.013 255.5); --accent: oklch(0.588 0.158 241.9);
  /* status */
  --success: <green-600 oklch>; --warn: <amber-600 oklch>; --danger: <red-600 oklch>;
  /* scales */
  --radius: 8px; --radius-lg: 12px; --speed: .15s; --sidebar-w: 260px;
  --shadow-1: …; --shadow-2: …; --shadow-3: …;
  --font: system-ui,'Segoe UI','Noto Sans TC',Roboto,sans-serif;
  --mono: ui-monospace,'Cascadia Code',Consolas,monospace;
  color-scheme: light;
}
.app-dark {
  --bg: oklch(0.129 0.042 264.7);     --surface: oklch(0.208 0.042 265.8);
  --fg: oklch(0.968 0.007 247.9);     --muted: oklch(0.704 0.04 256.8);
  --border: oklch(0.279 0.041 260.0); --accent: oklch(0.746 0.16 232.7);
  /* dark status colours + shadow overrides */
  color-scheme: dark;
}
@media (prefers-reduced-motion: reduce) { /* disable theme transitions */ }
```

Exact status-colour and shadow OKLch values are filled from `brand-spec.md` / the prototype's palette
during implementation (light + dark). Spacing/radius/motion match the brand spec (4px base, 8px radius,
120–180ms ease-out).

## 4. Custom Aura preset — `src/theme/preset.ts`

```ts
import { definePreset } from '@primeuix/themes'
import Aura from '@primeuix/themes/aura'

export const StruoPreset = definePreset(Aura, {
  semantic: {
    primary: { /* sky 50–950 */ },
    colorScheme: {
      light: { surface: { /* slate scale */ } /* + primary semantic mappings */ },
      dark:  { surface: { /* slate scale */ } /* + primary semantic mappings */ },
    },
  },
})
```

Applied in `main.ts`:
`app.use(PrimeVue, { theme: { preset: StruoPreset, options: { darkModeSelector: '.app-dark' } } })`.

## 5. Theme engine

**`src/theme/resolveInitialTheme.ts`** (pure, unit-testable): read `localStorage('struo.theme')` → if
`'dark'`/`'light'`, use it; else `matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'`.

**`src/stores/themeStore.ts`** (Pinia):
- state `mode: 'light' | 'dark'` (init from `resolveInitialTheme()`); getter `isDark`.
- `toggle()` / `set(mode)` → update mode then `apply()`.
- `apply()` → toggle `<html>.app-dark`, write `localStorage('struo.theme')`.

**`index.html`** no-flash inline script in `<head>` before mount (sets `.app-dark` on `<html>` early):
```html
<script>try{var s=localStorage.getItem('struo.theme');
if(s?s==='dark':matchMedia('(prefers-color-scheme: dark)').matches)
document.documentElement.classList.add('app-dark')}catch(e){}</script>
```

**Flow.** `main.ts` calls `themeStore.apply()` after the store is created (idempotent with the inline
script). FE-R1's toggle button calls `themeStore.toggle()`. When the user has never chosen explicitly
(no localStorage value), a `prefers-color-scheme` change is followed; once chosen, the choice wins.

## 6. Admin-UI i18n engine

**Install:** `pnpm add vue-i18n` (latest via the package manager; never hand-authored — §17.5).

**`src/locales/zh-TW.ts` / `src/locales/en.ts`** — nested-namespace message objects; R0 seeds only
common/shell strings (each screen adds its own keys when rebuilt). The two files' **key structure must be
symmetric** (missing keys fall back to `en`).

```ts
export default {
  common: { save:'儲存', cancel:'取消', delete:'刪除', confirm:'確認', search:'搜尋',
            loading:'載入中…', logout:'登出' /* … */ },
  theme:  { light:'淺色', dark:'深色', toggle:'切換主題' },
  lang:   { label:'介面語言', 'zh-TW':'繁體中文', en:'English' },
}
```

**`src/theme/resolveInitialUiLocale.ts`** (pure): `localStorage('struo.uiLocale')` if `'zh-TW'`/`'en'`,
else `'zh-TW'`.

**`src/i18n/index.ts`:**
`createI18n({ legacy:false, locale: resolveInitialUiLocale(), fallbackLocale:'en', messages:{ 'zh-TW':zhTW, en } })`.

**`src/stores/uiLocaleStore.ts`** (Pinia; **completely separate from content-locale `languageStore`**):
- state `locale: 'zh-TW' | 'en'`.
- `set(locale)` → set `i18n.global.locale`, set `document.documentElement.lang`, write `struo.uiLocale`.

FE-R1's language switcher calls `uiLocaleStore.set(...)`. Components use `const { t } = useI18n()` and
`{{ t('common.save') }}`. R0 converts `AppShell`'s existing hard-coded strings (e.g. "Log out") to keys
as a working proof the engine is wired end-to-end (visual rebuild of the shell stays in FE-R1).

## 7. Integration & wiring — `main.ts`

```ts
app.use(pinia)
app.use(PrimeVue, { theme: { preset: StruoPreset, options: { darkModeSelector: '.app-dark' } } })
app.use(i18n)
app.use(ConfirmationService)
useThemeStore(pinia).apply()
useUiLocaleStore(pinia).set(resolveInitialUiLocale())
// existing auth resolve → router → mount unchanged
import './assets/theme.css'
```

**Files added:** `assets/theme.css`, `theme/preset.ts`, `theme/resolveInitialTheme.ts`,
`theme/resolveInitialUiLocale.ts`, `stores/themeStore.ts`, `stores/uiLocaleStore.ts`, `i18n/index.ts`,
`locales/zh-TW.ts`, `locales/en.ts`.
**Files changed:** `main.ts` (wiring), `index.html` (no-flash script + `lang`), `AppShell.vue` (swap the
few hard-coded strings to i18n keys — visual rebuild deferred to FE-R1).
**Not touched:** backend, any API, route structure, all other views/components' logic.

## 8. Error handling

- Theme/UI-locale resolvers wrap `localStorage`/`matchMedia` access defensively (private-mode / SSR-less
  guards) and fall back to defaults (`light` was not chosen → system; UI locale → `zh-TW`); the inline
  script is already `try/catch`-wrapped.
- vue-i18n `fallbackLocale: 'en'` covers any missing `zh-TW` key; the symmetric-keys test prevents silent
  gaps at build time.
- An unknown persisted value (e.g. a hand-edited `struo.uiLocale: 'ja'`) is ignored → default.

## 9. Testing strategy (TDD)

**Unit (vitest, new):**
- `resolveInitialTheme`: localStorage `dark`/`light` honoured; no value → `matchMedia` fallback (mocked).
- `resolveInitialUiLocale`: localStorage hit honoured; no value → `zh-TW`; unknown value → `zh-TW`.
- `themeStore`: `toggle`/`set` → correct `<html>.app-dark`, `localStorage('struo.theme')` written,
  `isDark` correct.
- `uiLocaleStore`: `set` → `i18n.global.locale` changes, `document.documentElement.lang` updates,
  `struo.uiLocale` written.
- `locales`: zh-TW vs en **symmetric key sets** (recursive key comparison — guards against missed
  translations).
- `preset`: `StruoPreset` is producible via `definePreset` and carries primary/surface semantic overrides
  (shallow — does not deep-assert Aura internals).
- `AppShell`: seeded strings render via i18n (switching locale changes the text).

**Regression:** existing **339** frontend tests stay green; `pnpm build` (vue-tsc + vite) clean
(pre-existing >500 kB chunk advisory only).

**Manual visual smoke (not automated):** run dev; confirm (1) dark/light initialises correctly from
localStorage / system preference with no load flash; (2) switching `struo.uiLocale` toggles the AppShell
seeded strings between zh-TW and en.

## 10. Acceptance gate

- `pnpm test` all green (339 + R0 new tests); `pnpm build` clean.
- Backend untouched → **no `dotnet` gate, no live Postgres gate** (no persistence/API change).
- Manual theme/UI-locale smoke passes.

## 11. Out of scope (recorded so they aren't lost)

- Any screen's visual rebuild (shell / login / dashboard / list / form / media → FE-R1–R6).
- The theme-toggle button and language-switcher UI (→ FE-R1).
- Per-screen string extraction into i18n keys (each slice does its own as it is rebuilt).
- A third UI language (日本語 in the prototype) — add a locale pack later; the engine supports it.
- The 9c-fe revision UI (→ FE-R7).
- Adopting the prototype's bespoke SVG icon sheet (keep primeicons).
