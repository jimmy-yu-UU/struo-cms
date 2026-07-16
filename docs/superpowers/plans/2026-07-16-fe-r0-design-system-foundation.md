# FE-R0 Design-System Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the shared frontend foundation — OKLch design-token layer, a custom PrimeVue Aura preset, a dark/light theme engine, and an admin-UI i18n engine — that every later redesign slice (FE-R1..R7) builds on. No screen is visually rebuilt here.

**Architecture:** Two colour layers off one palette — a custom `definePreset(Aura)` drives PrimeVue components (primary→sky, surface→slate, `darkModeSelector: '.app-dark'`); a `theme.css` OKLch token layer drives non-PrimeVue layout. A Pinia `themeStore` + pure resolver + a no-flash inline script toggle `.app-dark` and persist `struo.theme`. `vue-i18n` (`legacy:false`) + a `uiLocaleStore` (separate from the content-locale `languageStore`) provide zh-TW/en with `struo.uiLocale` persistence.

**Tech Stack:** Vue 3.5, Pinia 3, PrimeVue 4.5 + `@primeuix/themes` 2 (`definePreset`), vue-i18n (to install), vitest + @vue/test-utils, TypeScript.

## Global Constraints

- **Pure frontend** — no backend/API/route/persistence change; no `dotnet` gate, no live Postgres gate.
- **Package versions never hand-authored** (CLAUDE.md §17.5) — install vue-i18n via `pnpm add vue-i18n` (latest the package manager resolves); never type a version string into `package.json` yourself.
- **PrimeVue stays** (CLAUDE.md §1) — re-skin via a custom Aura preset, do not replace components or adopt the prototype's SVG icon sheet (keep primeicons).
- **No code copied from the prototype** — it is a *visual* reference only.
- **Token source of truth** — the six core tokens use `brand-spec.md` OKLch values verbatim; status colours + shadows use the prototype palette values verbatim (given in Task 1).
- **`erasableSyntaxOnly`** is on — no `enum`, no parameter properties, no namespaces; use `type`/`interface`/const objects only.
- **`noUnusedLocals`/`noUnusedParameters`** are on — leave no unused bindings.
- **UI-locale store is distinct** from the existing content-locale `languageStore` — never conflate them.
- **Regression bar** — existing **339** frontend tests stay green; `pnpm build` (vue-tsc + vite) clean.
- Test runner: `pnpm test <path>` runs one file; `pnpm test` runs all; `pnpm build` is the type+build gate.

---

### Task 1: Design-token CSS + custom Aura preset

**Files:**
- Create: `frontend/src/assets/theme.css`
- Create: `frontend/src/theme/preset.ts`
- Test: `frontend/src/theme/preset.test.ts`

**Interfaces:**
- Produces: `export const struoPresetConfig` (the override object) and `export const StruoPreset` (result of `definePreset(Aura, struoPresetConfig)`) from `theme/preset.ts`. `theme.css` exports nothing (side-effect stylesheet).

- [ ] **Step 1: Write the failing test**

`frontend/src/theme/preset.test.ts`:
```ts
import { describe, it, expect } from 'vitest'
import { struoPresetConfig, StruoPreset } from './preset'

describe('StruoPreset', () => {
  it('config maps primary to the sky ramp', () => {
    expect(struoPresetConfig.semantic.primary[500]).toBe('{sky.500}')
    expect(struoPresetConfig.semantic.primary[50]).toBe('{sky.50}')
  })

  it('config maps surface to the slate ramp in light and dark', () => {
    expect(struoPresetConfig.semantic.colorScheme.light.surface[900]).toBe('{slate.900}')
    expect(struoPresetConfig.semantic.colorScheme.dark.surface[950]).toBe('{slate.950}')
  })

  it('StruoPreset is produced from definePreset', () => {
    expect(StruoPreset).toBeTruthy()
    expect(typeof StruoPreset).toBe('object')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm test src/theme/preset.test.ts`
Expected: FAIL — cannot resolve `./preset`.

- [ ] **Step 3: Write the token CSS**

`frontend/src/assets/theme.css`:
```css
/* StruoCMS design tokens — source of truth: brand-spec.md (OKLch core) + prototype palette. */
:root {
  --bg: oklch(0.984 0.003 247.9);
  --surface: oklch(1 0 0);
  --fg: oklch(0.208 0.042 265.8);
  --muted: oklch(0.554 0.046 257.4);
  --border: oklch(0.929 0.013 255.5);
  --accent: oklch(0.588 0.158 241.9);
  --success: #16a34a;
  --warn: #d97706;
  --danger: #dc2626;
  --radius: 8px;
  --radius-lg: 12px;
  --speed: .15s;
  --sidebar-w: 260px;
  --shadow-1: 0 1px 2px color-mix(in srgb, #020617 6%, transparent);
  --shadow-2: 0 6px 24px color-mix(in srgb, #020617 9%, transparent);
  --shadow-3: 0 24px 64px color-mix(in srgb, #020617 24%, transparent);
  --font: system-ui, 'Segoe UI', 'Noto Sans TC', Roboto, sans-serif;
  --mono: ui-monospace, 'Cascadia Code', Consolas, monospace;
  color-scheme: light;
}
.app-dark {
  --bg: oklch(0.129 0.042 264.7);
  --surface: oklch(0.208 0.042 265.8);
  --fg: oklch(0.968 0.007 247.9);
  --muted: oklch(0.704 0.04 256.8);
  --border: oklch(0.279 0.041 260.0);
  --accent: oklch(0.746 0.16 232.7);
  --success: #4ade80;
  --warn: #fbbf24;
  --danger: #f87171;
  --shadow-1: 0 1px 2px rgb(0 0 0 / .45);
  --shadow-2: 0 6px 24px rgb(0 0 0 / .5);
  --shadow-3: 0 24px 64px rgb(0 0 0 / .65);
  color-scheme: dark;
}
html { font-family: var(--font); }
@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after { transition: none !important; animation: none !important; }
}
```

- [ ] **Step 4: Write the preset**

`frontend/src/theme/preset.ts`:
```ts
import { definePreset } from '@primeuix/themes'
import Aura from '@primeuix/themes/aura'

// primary -> sky, surface -> slate; same palette as assets/theme.css so both layers flip together.
export const struoPresetConfig = {
  semantic: {
    primary: {
      50: '{sky.50}', 100: '{sky.100}', 200: '{sky.200}', 300: '{sky.300}',
      400: '{sky.400}', 500: '{sky.500}', 600: '{sky.600}', 700: '{sky.700}',
      800: '{sky.800}', 900: '{sky.900}', 950: '{sky.950}',
    },
    colorScheme: {
      light: {
        surface: {
          0: '#ffffff', 50: '{slate.50}', 100: '{slate.100}', 200: '{slate.200}',
          300: '{slate.300}', 400: '{slate.400}', 500: '{slate.500}', 600: '{slate.600}',
          700: '{slate.700}', 800: '{slate.800}', 900: '{slate.900}', 950: '{slate.950}',
        },
      },
      dark: {
        surface: {
          0: '#ffffff', 50: '{slate.50}', 100: '{slate.100}', 200: '{slate.200}',
          300: '{slate.300}', 400: '{slate.400}', 500: '{slate.500}', 600: '{slate.600}',
          700: '{slate.700}', 800: '{slate.800}', 900: '{slate.900}', 950: '{slate.950}',
        },
      },
    },
  },
} as const

export const StruoPreset = definePreset(Aura, struoPresetConfig)
```

- [ ] **Step 5: Run test to verify it passes**

Run: `pnpm test src/theme/preset.test.ts`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/assets/theme.css frontend/src/theme/preset.ts frontend/src/theme/preset.test.ts
git commit -m "feat(frontend): design-token layer + custom Aura preset (FE-R0)"
```

---

### Task 2: `resolveInitialTheme` pure resolver

**Files:**
- Create: `frontend/src/theme/resolveInitialTheme.ts`
- Test: `frontend/src/theme/resolveInitialTheme.test.ts`

**Interfaces:**
- Produces: `export type ThemeMode = 'light' | 'dark'` and `export function resolveInitialTheme(): ThemeMode`.
- Reads `localStorage('struo.theme')`, else `matchMedia('(prefers-color-scheme: dark)')`, else `'light'`.

- [ ] **Step 1: Write the failing test**

`frontend/src/theme/resolveInitialTheme.test.ts`:
```ts
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { resolveInitialTheme } from './resolveInitialTheme'

describe('resolveInitialTheme', () => {
  beforeEach(() => { localStorage.clear() })
  afterEach(() => { vi.restoreAllMocks() })

  it('honours a saved dark preference', () => {
    localStorage.setItem('struo.theme', 'dark')
    expect(resolveInitialTheme()).toBe('dark')
  })

  it('honours a saved light preference', () => {
    localStorage.setItem('struo.theme', 'light')
    expect(resolveInitialTheme()).toBe('light')
  })

  it('falls back to system dark when nothing is saved', () => {
    vi.spyOn(window, 'matchMedia').mockReturnValue({ matches: true } as MediaQueryList)
    expect(resolveInitialTheme()).toBe('dark')
  })

  it('defaults to light when nothing saved and system is light', () => {
    // global vitest.setup stub returns matches:false
    expect(resolveInitialTheme()).toBe('light')
  })

  it('ignores an unknown saved value', () => {
    localStorage.setItem('struo.theme', 'weird')
    expect(resolveInitialTheme()).toBe('light')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm test src/theme/resolveInitialTheme.test.ts`
Expected: FAIL — cannot resolve `./resolveInitialTheme`.

- [ ] **Step 3: Write the implementation**

`frontend/src/theme/resolveInitialTheme.ts`:
```ts
export type ThemeMode = 'light' | 'dark'

export function resolveInitialTheme(): ThemeMode {
  try {
    const saved = localStorage.getItem('struo.theme')
    if (saved === 'dark' || saved === 'light') return saved
    if (typeof window !== 'undefined' && typeof window.matchMedia === 'function') {
      return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
    }
  } catch {
    /* localStorage/matchMedia unavailable — fall through to the default */
  }
  return 'light'
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm test src/theme/resolveInitialTheme.test.ts`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/theme/resolveInitialTheme.ts frontend/src/theme/resolveInitialTheme.test.ts
git commit -m "feat(frontend): resolveInitialTheme resolver (FE-R0)"
```

---

### Task 3: `themeStore`

**Files:**
- Create: `frontend/src/stores/themeStore.ts`
- Test: `frontend/src/stores/themeStore.test.ts`

**Interfaces:**
- Consumes: `resolveInitialTheme`, `ThemeMode` from `../theme/resolveInitialTheme`.
- Produces: `export const useThemeStore` — state `{ mode: ThemeMode }`; getter `isDark: boolean`; actions `apply(): void`, `set(mode: ThemeMode): void`, `toggle(): void`.

- [ ] **Step 1: Write the failing test**

`frontend/src/stores/themeStore.test.ts`:
```ts
import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useThemeStore } from './themeStore'

describe('themeStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
    document.documentElement.classList.remove('app-dark')
  })

  it('toggle switches mode, applies the class, and persists', () => {
    const s = useThemeStore()
    s.set('light')
    expect(s.isDark).toBe(false)
    expect(document.documentElement.classList.contains('app-dark')).toBe(false)
    s.toggle()
    expect(s.mode).toBe('dark')
    expect(s.isDark).toBe(true)
    expect(document.documentElement.classList.contains('app-dark')).toBe(true)
    expect(localStorage.getItem('struo.theme')).toBe('dark')
  })

  it('set applies immediately and persists', () => {
    const s = useThemeStore()
    s.set('dark')
    expect(document.documentElement.classList.contains('app-dark')).toBe(true)
    expect(localStorage.getItem('struo.theme')).toBe('dark')
    s.set('light')
    expect(document.documentElement.classList.contains('app-dark')).toBe(false)
    expect(localStorage.getItem('struo.theme')).toBe('light')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm test src/stores/themeStore.test.ts`
Expected: FAIL — cannot resolve `./themeStore`.

- [ ] **Step 3: Write the implementation**

`frontend/src/stores/themeStore.ts`:
```ts
import { defineStore } from 'pinia'
import { resolveInitialTheme, type ThemeMode } from '../theme/resolveInitialTheme'

export const useThemeStore = defineStore('theme', {
  state: () => ({ mode: resolveInitialTheme() as ThemeMode }),
  getters: {
    isDark: (state): boolean => state.mode === 'dark',
  },
  actions: {
    apply(): void {
      document.documentElement.classList.toggle('app-dark', this.mode === 'dark')
      try {
        localStorage.setItem('struo.theme', this.mode)
      } catch {
        /* localStorage unavailable — class is still applied in-memory */
      }
    },
    set(mode: ThemeMode): void {
      this.mode = mode
      this.apply()
    },
    toggle(): void {
      this.set(this.mode === 'dark' ? 'light' : 'dark')
    },
  },
})
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm test src/stores/themeStore.test.ts`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/stores/themeStore.ts frontend/src/stores/themeStore.test.ts
git commit -m "feat(frontend): themeStore (dark/light toggle + persistence) (FE-R0)"
```

---

### Task 4: `resolveInitialUiLocale` pure resolver

**Files:**
- Create: `frontend/src/theme/resolveInitialUiLocale.ts`
- Test: `frontend/src/theme/resolveInitialUiLocale.test.ts`

**Interfaces:**
- Produces: `export type UiLocale = 'zh-TW' | 'en'`, `export const DEFAULT_UI_LOCALE: UiLocale`, `export function resolveInitialUiLocale(): UiLocale`.

- [ ] **Step 1: Write the failing test**

`frontend/src/theme/resolveInitialUiLocale.test.ts`:
```ts
import { describe, it, expect, beforeEach } from 'vitest'
import { resolveInitialUiLocale, DEFAULT_UI_LOCALE } from './resolveInitialUiLocale'

describe('resolveInitialUiLocale', () => {
  beforeEach(() => { localStorage.clear() })

  it('defaults to zh-TW', () => {
    expect(DEFAULT_UI_LOCALE).toBe('zh-TW')
    expect(resolveInitialUiLocale()).toBe('zh-TW')
  })

  it('honours a saved en preference', () => {
    localStorage.setItem('struo.uiLocale', 'en')
    expect(resolveInitialUiLocale()).toBe('en')
  })

  it('honours a saved zh-TW preference', () => {
    localStorage.setItem('struo.uiLocale', 'zh-TW')
    expect(resolveInitialUiLocale()).toBe('zh-TW')
  })

  it('ignores an unknown saved value', () => {
    localStorage.setItem('struo.uiLocale', 'ja')
    expect(resolveInitialUiLocale()).toBe('zh-TW')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm test src/theme/resolveInitialUiLocale.test.ts`
Expected: FAIL — cannot resolve `./resolveInitialUiLocale`.

- [ ] **Step 3: Write the implementation**

`frontend/src/theme/resolveInitialUiLocale.ts`:
```ts
export type UiLocale = 'zh-TW' | 'en'

export const DEFAULT_UI_LOCALE: UiLocale = 'zh-TW'

export function resolveInitialUiLocale(): UiLocale {
  try {
    const saved = localStorage.getItem('struo.uiLocale')
    if (saved === 'zh-TW' || saved === 'en') return saved
  } catch {
    /* localStorage unavailable — fall through to the default */
  }
  return DEFAULT_UI_LOCALE
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm test src/theme/resolveInitialUiLocale.test.ts`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/theme/resolveInitialUiLocale.ts frontend/src/theme/resolveInitialUiLocale.test.ts
git commit -m "feat(frontend): resolveInitialUiLocale resolver (FE-R0)"
```

---

### Task 5: Install vue-i18n + locale packs + i18n instance

**Files:**
- Modify: `frontend/package.json` (via `pnpm add`, not by hand)
- Create: `frontend/src/locales/zh-TW.ts`
- Create: `frontend/src/locales/en.ts`
- Create: `frontend/src/i18n/index.ts`
- Test: `frontend/src/locales/locales.test.ts`

**Interfaces:**
- Consumes: `resolveInitialUiLocale` from `../theme/resolveInitialUiLocale`.
- Produces: default-exported message objects from `locales/zh-TW.ts` and `locales/en.ts`; `export const i18n` (a `createI18n` instance, `legacy:false`) from `i18n/index.ts`.

- [ ] **Step 1: Install vue-i18n**

Run: `cd frontend && pnpm add vue-i18n`
Expected: `package.json` gains a `vue-i18n` dependency at the version the package manager resolves (do not edit the version by hand).

- [ ] **Step 2: Write the failing test**

`frontend/src/locales/locales.test.ts`:
```ts
import { describe, it, expect } from 'vitest'
import zhTW from './zh-TW'
import en from './en'

function keyPaths(obj: Record<string, unknown>, prefix = ''): string[] {
  return Object.entries(obj).flatMap(([k, v]) =>
    v !== null && typeof v === 'object'
      ? keyPaths(v as Record<string, unknown>, `${prefix}${k}.`)
      : [`${prefix}${k}`],
  )
}

describe('locale packs', () => {
  it('zh-TW and en expose symmetric key sets', () => {
    expect(keyPaths(zhTW).sort()).toEqual(keyPaths(en).sort())
  })

  it('carry the seeded common namespace', () => {
    expect(zhTW.common.logout).toBe('登出')
    expect(en.common.logout).toBe('Log out')
  })
})
```

- [ ] **Step 3: Run test to verify it fails**

Run: `pnpm test src/locales/locales.test.ts`
Expected: FAIL — cannot resolve `./zh-TW`.

- [ ] **Step 4: Write the locale packs**

`frontend/src/locales/zh-TW.ts`:
```ts
export default {
  common: {
    save: '儲存',
    cancel: '取消',
    delete: '刪除',
    confirm: '確認',
    search: '搜尋',
    loading: '載入中…',
    logout: '登出',
  },
  theme: {
    light: '淺色',
    dark: '深色',
    toggle: '切換主題',
  },
  lang: {
    label: '介面語言',
    'zh-TW': '繁體中文',
    en: 'English',
  },
}
```

`frontend/src/locales/en.ts`:
```ts
export default {
  common: {
    save: 'Save',
    cancel: 'Cancel',
    delete: 'Delete',
    confirm: 'Confirm',
    search: 'Search',
    loading: 'Loading…',
    logout: 'Log out',
  },
  theme: {
    light: 'Light',
    dark: 'Dark',
    toggle: 'Toggle theme',
  },
  lang: {
    label: 'Interface language',
    'zh-TW': '繁體中文',
    en: 'English',
  },
}
```

- [ ] **Step 5: Write the i18n instance**

`frontend/src/i18n/index.ts`:
```ts
import { createI18n } from 'vue-i18n'
import zhTW from '../locales/zh-TW'
import en from '../locales/en'
import { resolveInitialUiLocale } from '../theme/resolveInitialUiLocale'

export const i18n = createI18n({
  legacy: false,
  locale: resolveInitialUiLocale(),
  fallbackLocale: 'en',
  messages: { 'zh-TW': zhTW, en },
})
```

- [ ] **Step 6: Run test to verify it passes**

Run: `pnpm test src/locales/locales.test.ts`
Expected: PASS (2 tests).

- [ ] **Step 7: Commit**

```bash
git add frontend/package.json frontend/pnpm-lock.yaml frontend/src/locales frontend/src/i18n
git commit -m "feat(frontend): vue-i18n engine + zh-TW/en locale packs (FE-R0)"
```

---

### Task 6: `uiLocaleStore`

**Files:**
- Create: `frontend/src/stores/uiLocaleStore.ts`
- Test: `frontend/src/stores/uiLocaleStore.test.ts`

**Interfaces:**
- Consumes: `i18n` from `../i18n`; `resolveInitialUiLocale`, `UiLocale` from `../theme/resolveInitialUiLocale`.
- Produces: `export const useUiLocaleStore` — state `{ locale: UiLocale }`; action `set(locale: UiLocale): void` (updates `i18n.global.locale.value`, `document.documentElement.lang`, persists `struo.uiLocale`).

- [ ] **Step 1: Write the failing test**

`frontend/src/stores/uiLocaleStore.test.ts`:
```ts
import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useUiLocaleStore } from './uiLocaleStore'
import { i18n } from '../i18n'

describe('uiLocaleStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
    i18n.global.locale.value = 'zh-TW'
    document.documentElement.removeAttribute('lang')
  })

  it('set updates the i18n locale, <html lang>, and persists', () => {
    const s = useUiLocaleStore()
    s.set('en')
    expect(s.locale).toBe('en')
    expect(i18n.global.locale.value).toBe('en')
    expect(document.documentElement.getAttribute('lang')).toBe('en')
    expect(localStorage.getItem('struo.uiLocale')).toBe('en')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm test src/stores/uiLocaleStore.test.ts`
Expected: FAIL — cannot resolve `./uiLocaleStore`.

- [ ] **Step 3: Write the implementation**

`frontend/src/stores/uiLocaleStore.ts`:
```ts
import { defineStore } from 'pinia'
import { i18n } from '../i18n'
import { resolveInitialUiLocale, type UiLocale } from '../theme/resolveInitialUiLocale'

export const useUiLocaleStore = defineStore('uiLocale', {
  state: () => ({ locale: resolveInitialUiLocale() as UiLocale }),
  actions: {
    set(locale: UiLocale): void {
      this.locale = locale
      i18n.global.locale.value = locale
      document.documentElement.setAttribute('lang', locale)
      try {
        localStorage.setItem('struo.uiLocale', locale)
      } catch {
        /* localStorage unavailable — locale is still applied in-memory */
      }
    },
  },
})
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm test src/stores/uiLocaleStore.test.ts`
Expected: PASS (1 test).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/stores/uiLocaleStore.ts frontend/src/stores/uiLocaleStore.test.ts
git commit -m "feat(frontend): uiLocaleStore (admin-UI locale, separate from content languageStore) (FE-R0)"
```

---

### Task 7: Wire main.ts + index.html no-flash script + AppShell i18n proof

**Files:**
- Modify: `frontend/src/main.ts`
- Modify: `frontend/index.html`
- Modify: `frontend/src/layouts/AppShell.vue`
- Modify: `frontend/src/layouts/AppShell.test.ts`

**Interfaces:**
- Consumes: `StruoPreset` (`theme/preset`), `i18n` (`i18n`), `useThemeStore` (`stores/themeStore`), `useUiLocaleStore` (`stores/uiLocaleStore`), `resolveInitialUiLocale` (`theme/resolveInitialUiLocale`).
- Produces: fully wired app — PrimeVue on the custom preset with dark selector, i18n installed, theme + UI-locale applied before mount; `AppShell` renders its logout label via `t('common.logout')`.

- [ ] **Step 1: Update the AppShell test (add i18n plugin + i18n-render assertion)**

Replace `frontend/src/layouts/AppShell.test.ts` with:
```ts
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import AppShell from './AppShell.vue'
import { useAuthStore } from '../stores/authStore'
import { i18n } from '../i18n'

const push = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
  useRoute: () => ({ path: '/collections/article' }),
  RouterView: { template: '<div/>' },
}))

const mountShell = () =>
  mount(AppShell, { global: { plugins: [i18n], stubs: { RouterView: true, CollectionNav: true } } })

describe('AppShell', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    i18n.global.locale.value = 'zh-TW'
  })

  it('logout calls the store and routes to login', async () => {
    const store = useAuthStore()
    const logoutSpy = vi.spyOn(store, 'logout').mockResolvedValue()
    const wrapper = mountShell()
    await wrapper.find('button.logout').trigger('click')
    await new Promise((r) => setTimeout(r, 0))
    expect(logoutSpy).toHaveBeenCalledOnce()
    expect(push).toHaveBeenCalledWith({ name: 'login' })
  })

  it('renders the logout label via i18n and reacts to locale', async () => {
    const wrapper = mountShell()
    expect(wrapper.find('button.logout').text()).toContain('登出')
    i18n.global.locale.value = 'en'
    await wrapper.vm.$nextTick()
    expect(wrapper.find('button.logout').text()).toContain('Log out')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm test src/layouts/AppShell.test.ts`
Expected: FAIL — the i18n render test fails (button text is still the hard-coded "Log out" only in English; the zh-TW assertion `登出` fails) because AppShell does not use i18n yet.

- [ ] **Step 3: Update AppShell to use i18n**

In `frontend/src/layouts/AppShell.vue`, add `useI18n` to the script and swap the logout label. Script `<script setup lang="ts">` gains:
```ts
import { useI18n } from 'vue-i18n'
```
and after the other store/router setup:
```ts
const { t } = useI18n()
```
In the template, change the logout button's text from `Log out` to `{{ t('common.logout') }}` (keep `class="logout"` and the `@click="onLogout"` handler unchanged):
```html
<button type="button" class="logout" @click="onLogout">{{ t('common.logout') }}</button>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm test src/layouts/AppShell.test.ts`
Expected: PASS (2 tests).

- [ ] **Step 5: Add the no-flash script + lang to index.html**

In `frontend/index.html`, set `<html lang="zh-TW">` and add, as the first child of `<head>`:
```html
<script>try{var s=localStorage.getItem('struo.theme');if(s?s==='dark':matchMedia('(prefers-color-scheme: dark)').matches)document.documentElement.classList.add('app-dark')}catch(e){}</script>
```

- [ ] **Step 6: Wire main.ts**

Replace `frontend/src/main.ts` with:
```ts
import { createApp } from 'vue'
import { createPinia } from 'pinia'
import PrimeVue from 'primevue/config'
import ConfirmationService from 'primevue/confirmationservice'
import 'primeicons/primeicons.css'
import './assets/theme.css'
import App from './App.vue'
import router from './router'
import { apiClient } from './api/apiClient'
import { useAuthStore } from './stores/authStore'
import { StruoPreset } from './theme/preset'
import { i18n } from './i18n'
import { useThemeStore } from './stores/themeStore'
import { useUiLocaleStore } from './stores/uiLocaleStore'
import { resolveInitialUiLocale } from './theme/resolveInitialUiLocale'

const app = createApp(App)
const pinia = createPinia()
app.use(pinia)
app.use(PrimeVue, { theme: { preset: StruoPreset, options: { darkModeSelector: '.app-dark' } } })
app.use(i18n)
app.use(ConfirmationService)

useThemeStore(pinia).apply()
useUiLocaleStore(pinia).set(resolveInitialUiLocale())

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

- [ ] **Step 7: Run the full suite + build gate**

Run: `pnpm test`
Expected: all green — 339 prior + the FE-R0 additions (theme/preset/resolvers/stores/locales/AppShell).

Run: `pnpm build`
Expected: vue-tsc + vite build succeed (pre-existing >500 kB chunk advisory only).

- [ ] **Step 8: Manual visual smoke (not automated)**

Start the dev server (`cd frontend && pnpm dev --host 127.0.0.1`) and confirm:
1. Dark/light initialises from `localStorage('struo.theme')` / system preference with **no load flash**.
2. In devtools, set `localStorage['struo.uiLocale']='en'` and reload → the AppShell logout label renders "Log out"; set `'zh-TW'` → "登出".
(No API/persistence change, so no live Postgres gate.)

- [ ] **Step 9: Commit**

```bash
git add frontend/src/main.ts frontend/index.html frontend/src/layouts/AppShell.vue frontend/src/layouts/AppShell.test.ts
git commit -m "feat(frontend): wire preset + theme + i18n into app bootstrap; AppShell i18n proof (FE-R0)"
```

---

## Self-Review

**1. Spec coverage:**
- Token layer (spec §3) → Task 1 (`theme.css`). ✓
- Custom Aura preset (spec §4) → Task 1 (`preset.ts`). ✓
- Theme engine: resolver + store + no-flash script (spec §5) → Tasks 2, 3, 7 (index.html). ✓
- i18n engine: install + packs + instance + store (spec §6) → Tasks 5, 6; resolver Task 4. ✓
- Integration/wiring (spec §7) → Task 7 (main.ts, AppShell string swap). ✓
- Error handling (spec §8) → try/catch in resolvers (Tasks 2, 4) + stores (Tasks 3, 6); fallbackLocale (Task 5); unknown-value tests (Tasks 2, 4). ✓
- Testing strategy (spec §9) → each task's tests + Task 7 full-suite/build + manual smoke. ✓
- Acceptance gate (spec §10) → Task 7 Steps 7–8. ✓
- Out of scope (spec §11) — no tasks build toggle/switcher UI, no screen rebuild, no third locale, no icon-sheet. ✓

**2. Placeholder scan:** No TBD/TODO; every code step shows complete content; no "similar to Task N". The spec's illustrative `…` values were resolved to concrete values in Task 1 (status colours as prototype hex, shadows verbatim). ✓

**3. Type consistency:** `ThemeMode` defined in Task 2, consumed in Task 3. `UiLocale`/`DEFAULT_UI_LOCALE` defined in Task 4, consumed in Tasks 5–6. `i18n` produced in Task 5, consumed in Tasks 6–7. `StruoPreset`/`struoPresetConfig` produced in Task 1, consumed in Task 7. `useThemeStore.apply/set/toggle`, `useUiLocaleStore.set` names consistent between definitions and call sites (main.ts). ✓
