# FE-R1 App Shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the bare `AppShell` with the prototype's full application shell — topbar (brand, UI-language switcher, theme toggle, user menu), collapsible sidebar with mobile drawer, breadcrumbs, and a Toast host.

**Architecture:** A grid `AppShell.vue` coordinator composes small, single-purpose shell components (`TheTopbar`, `TheSidebar` + `SidebarNavItem`, `UserMenu`, `UiLanguageSwitcher`, `ThemeToggle`, `AppBreadcrumb`). Chrome state (desktop collapse + mobile drawer) lives in a `sidebarStore`; the breadcrumb trail is a pure `buildBreadcrumb(route, collections, t)` function. Shell *layout* is custom markup on the FE-R0 `theme.css` OKLch tokens; discrete *controls* are PrimeVue (`Select`, `Menu`, `Breadcrumb`, `Toast`). The current `PanelMenu`-based `CollectionNav` is removed; its role moves into `TheSidebar`.

**Tech Stack:** Vue 3.5 (`<script setup lang="ts">`), Pinia 3, vue-router 5, vue-i18n 11 (`legacy:false`), PrimeVue 4.5 + primeicons 7, Vitest 4 + @vue/test-utils 2, vue-tsc.

## Global Constraints

- **Pure frontend.** No backend / API / route-structure / persistence change → **no `dotnet` gate, no live Postgres gate**. (spec §0)
- **Design-reference rule:** the prototype is a *visual* reference only — **copy no code from it**; rebuild from the design. (spec header)
- **Element strategy:** custom layout from `theme.css` tokens; PrimeVue for discrete controls (`Select`/`Menu`/`Breadcrumb`/`Toast`); keep primeicons. (spec §0)
- **No dead links / YAGNI:** system nav = Dashboard + Media (Media gated by `file.read`) only; user menu = logout only; toasts = infrastructure only. (spec §1)
- **User identity:** `/auth/me` has only `{ id, isSuperAdmin, permissions }` — user menu shows a generic person-icon avatar + a role label from `isSuperAdmin`; **no real name**. (spec §1)
- **i18n:** all shell strings via `useI18n().t`; `zh-TW` + `en` keys stay symmetric (existing `locales` symmetry test enforces it). Default `zh-TW`, fallback `en`. (spec §7)
- **localStorage** access wrapped in try/catch; unknown persisted value falls back to a safe default. (spec §3, §9)
- **Motion** respects `prefers-reduced-motion` (already globally disabled in `theme.css`). (spec §3)
- **Package versions** are never hand-authored; nothing new is installed here (all deps already present). (CLAUDE.md §17.5)
- **Verification gate:** `pnpm test` all green + `pnpm build` (vue-tsc) clean + Playwright MCP shell smoke. (spec §11)

**Conventions to follow (from existing code):**
- Stores: Pinia options API, `defineStore('name', { state, getters, actions })`; try/catch around `localStorage`.
- Component tests: `mount(...)` with `global.plugins:[i18n]`; mock `vue-router` with `vi.mock`; mock PrimeVue sub-components to lightweight stubs (see `CollectionNav.test.ts` mocking `primevue/panelmenu`).
- Test lifecycle: `beforeEach(() => { setActivePinia(createPinia()); vi.clearAllMocks(); i18n.global.locale.value = 'zh-TW' })`.
- Run a single test file: `pnpm test <path>`. Run all: `pnpm test`.

---

## File Structure

```
frontend/src/
  layouts/AppShell.vue                 (REBUILD) grid coordinator + <Toast/> + scrim + responsive wiring
  layouts/AppShell.test.ts             (REWRITE) shell composition + drawer-close-on-nav + logout via UserMenu
  components/shell/
    TheTopbar.vue                      (NEW) hamburger · brand · lang · theme · user
    TheTopbar.test.ts                  (NEW)
    TheSidebar.vue                     (NEW) system items + collection nav + footer
    TheSidebar.test.ts                 (NEW)
    SidebarNavItem.vue                 (NEW) one nav entry / collapsible group
    SidebarNavItem.test.ts             (NEW)
    UserMenu.vue                       (NEW) avatar + role header + logout dropdown
    UserMenu.test.ts                   (NEW)
    UiLanguageSwitcher.vue             (NEW) PrimeVue Select -> uiLocaleStore
    UiLanguageSwitcher.test.ts         (NEW)
    ThemeToggle.vue                    (NEW) icon button -> themeStore
    ThemeToggle.test.ts                (NEW)
    AppBreadcrumb.vue                  (NEW) PrimeVue Breadcrumb <- buildBreadcrumb
    AppBreadcrumb.test.ts              (NEW)
  stores/sidebarStore.ts               (NEW) collapsed + drawerOpen + persistence
  stores/sidebarStore.test.ts          (NEW)
  lib/buildBreadcrumb.ts               (NEW) pure: route + schema + t -> Crumb[]
  lib/buildBreadcrumb.test.ts          (NEW)
  locales/zh-TW.ts, locales/en.ts      (MODIFY) new shell keys
  main.ts                              (MODIFY) app.use(ToastService)
  components/CollectionNav.vue         (DELETE) absorbed by TheSidebar
  components/CollectionNav.test.ts     (DELETE) coverage moves to TheSidebar
```

Reused unchanged: `lib/buildNav.ts` (`buildNav(collections, isSuperAdmin, permissions) => NavGroup[]`, `UNGROUPED='General'`), `stores/themeStore.ts` (`toggle()`, `isDark`), `stores/uiLocaleStore.ts` (`set(locale)`), `stores/authStore.ts` (`logout()`, `user`), `stores/schemaStore.ts` (`collections`, `load()`, `loadError`).

---

## Task 1: Shell i18n keys

**Files:**
- Modify: `frontend/src/locales/zh-TW.ts`
- Modify: `frontend/src/locales/en.ts`
- Test: `frontend/src/locales/locales.test.ts` (existing symmetry test — no edit; it will cover the new keys)

**Interfaces:**
- Produces: i18n keys `nav.dashboard`, `nav.media`, `shell.collapse`, `shell.expand`, `shell.openMenu`, `shell.closeMenu`, `shell.version`, `shell.brandHome`, `user.superAdmin`, `user.member`, `user.account`, `breadcrumb.newItem`, `breadcrumb.editItem`. Existing `common.logout`, `theme.toggle`, `lang.*` reused.

- [ ] **Step 1: Run the existing symmetry test to confirm green baseline**

Run: `pnpm test src/locales/locales.test.ts`
Expected: PASS (baseline before edits).

- [ ] **Step 2: Add the new keys to `zh-TW.ts`**

Append these namespaces to the default export object in `frontend/src/locales/zh-TW.ts` (keep existing `common`/`theme`/`lang`):

```ts
  nav: {
    dashboard: '儀表板',
    media: '媒體庫',
  },
  shell: {
    collapse: '收合側欄',
    expand: '展開側欄',
    openMenu: '開啟導覽選單',
    closeMenu: '關閉導覽選單',
    brandHome: '回到儀表板',
    version: '重新設計原型',
  },
  user: {
    account: '帳戶',
    superAdmin: '超級管理員',
    member: '一般使用者',
  },
  breadcrumb: {
    newItem: '新增項目',
    editItem: '編輯項目',
  },
```

- [ ] **Step 3: Add the symmetric keys to `en.ts`**

Append the matching namespaces to `frontend/src/locales/en.ts` (same key structure):

```ts
  nav: {
    dashboard: 'Dashboard',
    media: 'Media Library',
  },
  shell: {
    collapse: 'Collapse sidebar',
    expand: 'Expand sidebar',
    openMenu: 'Open navigation menu',
    closeMenu: 'Close navigation menu',
    brandHome: 'Back to dashboard',
    version: 'Redesign prototype',
  },
  user: {
    account: 'Account',
    superAdmin: 'Super Admin',
    member: 'Member',
  },
  breadcrumb: {
    newItem: 'New item',
    editItem: 'Edit item',
  },
```

- [ ] **Step 4: Run the symmetry test to verify keys match**

Run: `pnpm test src/locales/locales.test.ts`
Expected: PASS (zh-TW and en key sets still symmetric).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/locales/zh-TW.ts frontend/src/locales/en.ts
git commit -m "feat(frontend): shell i18n keys (nav/shell/user/breadcrumb) (FE-R1)"
```

---

## Task 2: sidebarStore

**Files:**
- Create: `frontend/src/stores/sidebarStore.ts`
- Test: `frontend/src/stores/sidebarStore.test.ts`

**Interfaces:**
- Produces: `useSidebarStore()` with state `{ collapsed: boolean, drawerOpen: boolean }`; actions `toggleCollapse()`, `openDrawer()`, `closeDrawer()`, `toggleDrawer()`. `collapsed` initialises from `localStorage('struo.sidebar')` (`'collapsed'` → true, else false). `toggleCollapse()` persists (`'collapsed'`/`'expanded'`). Drawer state is not persisted.

- [ ] **Step 1: Write the failing test**

Create `frontend/src/stores/sidebarStore.test.ts`:

```ts
import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useSidebarStore } from './sidebarStore'

describe('sidebarStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
  })

  it('defaults to expanded when nothing is persisted', () => {
    const s = useSidebarStore()
    expect(s.collapsed).toBe(false)
    expect(s.drawerOpen).toBe(false)
  })

  it('initialises collapsed from a persisted "collapsed" value', () => {
    localStorage.setItem('struo.sidebar', 'collapsed')
    const s = useSidebarStore()
    expect(s.collapsed).toBe(true)
  })

  it('an unknown persisted value falls back to expanded', () => {
    localStorage.setItem('struo.sidebar', 'nonsense')
    const s = useSidebarStore()
    expect(s.collapsed).toBe(false)
  })

  it('toggleCollapse flips and persists', () => {
    const s = useSidebarStore()
    s.toggleCollapse()
    expect(s.collapsed).toBe(true)
    expect(localStorage.getItem('struo.sidebar')).toBe('collapsed')
    s.toggleCollapse()
    expect(s.collapsed).toBe(false)
    expect(localStorage.getItem('struo.sidebar')).toBe('expanded')
  })

  it('drawer open/close/toggle work and are not persisted', () => {
    const s = useSidebarStore()
    s.openDrawer()
    expect(s.drawerOpen).toBe(true)
    s.closeDrawer()
    expect(s.drawerOpen).toBe(false)
    s.toggleDrawer()
    expect(s.drawerOpen).toBe(true)
    expect(localStorage.getItem('struo.sidebar')).toBeNull()
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm test src/stores/sidebarStore.test.ts`
Expected: FAIL — cannot resolve `./sidebarStore`.

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/stores/sidebarStore.ts`:

```ts
import { defineStore } from 'pinia'

function readInitialCollapsed(): boolean {
  try {
    return localStorage.getItem('struo.sidebar') === 'collapsed'
  } catch {
    return false
  }
}

export const useSidebarStore = defineStore('sidebar', {
  state: () => ({ collapsed: readInitialCollapsed(), drawerOpen: false }),
  actions: {
    toggleCollapse(): void {
      this.collapsed = !this.collapsed
      try {
        localStorage.setItem('struo.sidebar', this.collapsed ? 'collapsed' : 'expanded')
      } catch {
        /* localStorage unavailable — state still applied in-memory */
      }
    },
    openDrawer(): void {
      this.drawerOpen = true
    },
    closeDrawer(): void {
      this.drawerOpen = false
    },
    toggleDrawer(): void {
      this.drawerOpen = !this.drawerOpen
    },
  },
})
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm test src/stores/sidebarStore.test.ts`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/stores/sidebarStore.ts frontend/src/stores/sidebarStore.test.ts
git commit -m "feat(frontend): sidebarStore (collapse + mobile drawer + persistence) (FE-R1)"
```

---

## Task 3: buildBreadcrumb

**Files:**
- Create: `frontend/src/lib/buildBreadcrumb.ts`
- Test: `frontend/src/lib/buildBreadcrumb.test.ts`

**Interfaces:**
- Consumes: `CollectionMeta` from `../types/schema`.
- Produces:
  ```ts
  export type Crumb = { label: string; to?: { name: string; params?: Record<string, string> } }
  export function buildBreadcrumb(
    route: { name?: string | null; params?: Record<string, string> },
    collections: CollectionMeta[],
    t: (key: string) => string,
  ): Crumb[]
  ```
  Always starts with a Dashboard crumb (`to: { name: 'dashboard' }`, label `t('nav.dashboard')`). A group crumb (when the collection has a non-empty `group`) is a non-link label. An unknown collection falls back to the raw `:name`. The current-page crumb has no `to`.

- [ ] **Step 1: Write the failing test**

Create `frontend/src/lib/buildBreadcrumb.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { buildBreadcrumb } from './buildBreadcrumb'
import type { CollectionMeta } from '../types/schema'

const t = (k: string) => k // identity: assert against keys
const collections: CollectionMeta[] = [
  { name: 'article', label: 'Article', group: 'Content', fields: [], relations: [] },
  { name: 'page', label: 'Page', group: null, fields: [], relations: [] },
]

describe('buildBreadcrumb', () => {
  it('dashboard is a single current crumb', () => {
    const c = buildBreadcrumb({ name: 'dashboard', params: {} }, collections, t)
    expect(c).toEqual([{ label: 'nav.dashboard' }])
  })

  it('media is dashboard > media', () => {
    const c = buildBreadcrumb({ name: 'media', params: {} }, collections, t)
    expect(c).toEqual([{ label: 'nav.dashboard', to: { name: 'dashboard' } }, { label: 'nav.media' }])
  })

  it('collection-list includes the group then the collection label', () => {
    const c = buildBreadcrumb({ name: 'collection-list', params: { name: 'article' } }, collections, t)
    expect(c).toEqual([
      { label: 'nav.dashboard', to: { name: 'dashboard' } },
      { label: 'Content' },
      { label: 'Article' },
    ])
  })

  it('ungrouped collection omits the group crumb', () => {
    const c = buildBreadcrumb({ name: 'collection-list', params: { name: 'page' } }, collections, t)
    expect(c).toEqual([
      { label: 'nav.dashboard', to: { name: 'dashboard' } },
      { label: 'Page' },
    ])
  })

  it('collection-create appends a generic new-item leaf and links the collection', () => {
    const c = buildBreadcrumb({ name: 'collection-create', params: { name: 'article' } }, collections, t)
    expect(c).toEqual([
      { label: 'nav.dashboard', to: { name: 'dashboard' } },
      { label: 'Content' },
      { label: 'Article', to: { name: 'collection-list', params: { name: 'article' } } },
      { label: 'breadcrumb.newItem' },
    ])
  })

  it('collection-item appends a generic edit-item leaf', () => {
    const c = buildBreadcrumb({ name: 'collection-item', params: { name: 'article', id: 'x' } }, collections, t)
    expect(c[c.length - 1]).toEqual({ label: 'breadcrumb.editItem' })
    expect(c[2]).toEqual({ label: 'Article', to: { name: 'collection-list', params: { name: 'article' } } })
  })

  it('unknown collection falls back to the raw name', () => {
    const c = buildBreadcrumb({ name: 'collection-list', params: { name: 'ghost' } }, collections, t)
    expect(c).toEqual([{ label: 'nav.dashboard', to: { name: 'dashboard' } }, { label: 'ghost' }])
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm test src/lib/buildBreadcrumb.test.ts`
Expected: FAIL — cannot resolve `./buildBreadcrumb`.

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/lib/buildBreadcrumb.ts`:

```ts
import type { CollectionMeta } from '../types/schema'

export type Crumb = { label: string; to?: { name: string; params?: Record<string, string> } }

const HOME: Crumb = { label: 'nav.dashboard', to: { name: 'dashboard' } }

export function buildBreadcrumb(
  route: { name?: string | null; params?: Record<string, string> },
  collections: CollectionMeta[],
  t: (key: string) => string,
): Crumb[] {
  const name = route.name ?? ''
  const params = route.params ?? {}

  if (name === 'dashboard') return [{ label: t('nav.dashboard') }]
  if (name === 'media') return [{ ...HOME, label: t('nav.dashboard') }, { label: t('nav.media') }]

  const collectionName = params.name ?? ''
  const meta = collections.find((c) => c.name === collectionName)
  const crumbs: Crumb[] = [{ ...HOME, label: t('nav.dashboard') }]

  if (meta?.group && meta.group.trim() !== '') crumbs.push({ label: meta.group })

  const collectionLabel = meta?.label ?? collectionName
  const isLeaf = name === 'collection-create' || name === 'collection-item'
  crumbs.push(
    isLeaf
      ? { label: collectionLabel, to: { name: 'collection-list', params: { name: collectionName } } }
      : { label: collectionLabel },
  )

  if (name === 'collection-create') crumbs.push({ label: t('breadcrumb.newItem') })
  if (name === 'collection-item') crumbs.push({ label: t('breadcrumb.editItem') })

  return crumbs
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm test src/lib/buildBreadcrumb.test.ts`
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/lib/buildBreadcrumb.ts frontend/src/lib/buildBreadcrumb.test.ts
git commit -m "feat(frontend): buildBreadcrumb pure resolver (route + schema -> crumbs) (FE-R1)"
```

---

## Task 4: ThemeToggle

**Files:**
- Create: `frontend/src/components/shell/ThemeToggle.vue`
- Test: `frontend/src/components/shell/ThemeToggle.test.ts`

**Interfaces:**
- Consumes: `useThemeStore()` (`isDark`, `toggle()`), `useI18n().t`.
- Produces: `<ThemeToggle/>` — a `button.theme-toggle` that calls `themeStore.toggle()` on click; shows `pi-moon` in light mode and `pi-sun` in dark mode; `aria-label` = `t('theme.toggle')`.

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/shell/ThemeToggle.test.ts`:

```ts
import { describe, it, expect, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ThemeToggle from './ThemeToggle.vue'
import { useThemeStore } from '../../stores/themeStore'
import { i18n } from '../../i18n'

describe('ThemeToggle', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
    document.documentElement.classList.remove('app-dark')
    i18n.global.locale.value = 'zh-TW'
  })

  it('shows the moon icon in light mode and toggles to dark on click', async () => {
    const store = useThemeStore()
    store.set('light')
    const wrapper = mount(ThemeToggle, { global: { plugins: [i18n] } })
    expect(wrapper.find('.pi-moon').exists()).toBe(true)
    await wrapper.find('button.theme-toggle').trigger('click')
    expect(store.isDark).toBe(true)
    expect(wrapper.find('.pi-sun').exists()).toBe(true)
  })

  it('exposes an i18n aria-label', () => {
    const wrapper = mount(ThemeToggle, { global: { plugins: [i18n] } })
    expect(wrapper.find('button.theme-toggle').attributes('aria-label')).toBe('切換主題')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm test src/components/shell/ThemeToggle.test.ts`
Expected: FAIL — cannot resolve `./ThemeToggle.vue`.

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/components/shell/ThemeToggle.vue`:

```vue
<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { useThemeStore } from '../../stores/themeStore'

const theme = useThemeStore()
const { t } = useI18n()
</script>

<template>
  <button
    type="button"
    class="icon-btn theme-toggle"
    :aria-label="t('theme.toggle')"
    @click="theme.toggle()"
  >
    <i class="pi" :class="theme.isDark ? 'pi-sun' : 'pi-moon'" aria-hidden="true" />
  </button>
</template>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm test src/components/shell/ThemeToggle.test.ts`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/shell/ThemeToggle.vue frontend/src/components/shell/ThemeToggle.test.ts
git commit -m "feat(frontend): ThemeToggle topbar control (FE-R1)"
```

---

## Task 5: UiLanguageSwitcher

**Files:**
- Create: `frontend/src/components/shell/UiLanguageSwitcher.vue`
- Test: `frontend/src/components/shell/UiLanguageSwitcher.test.ts`

**Interfaces:**
- Consumes: `useUiLocaleStore()` (`locale`, `set(locale)`), `useI18n().t`, PrimeVue `Select` (`primevue/select`).
- Produces: `<UiLanguageSwitcher/>` — a PrimeVue `Select` bound to the UI locale; changing it calls `uiLocaleStore.set(...)`. Options: `[{ value:'zh-TW', label: t('lang.zh-TW') }, { value:'en', label: t('lang.en') }]`.

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/shell/UiLanguageSwitcher.test.ts`. The PrimeVue `Select` is stubbed to a native `<select>` so we can drive `change`:

```ts
import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import UiLanguageSwitcher from './UiLanguageSwitcher.vue'
import { useUiLocaleStore } from '../../stores/uiLocaleStore'
import { i18n } from '../../i18n'

vi.mock('primevue/select', () => ({
  default: {
    name: 'Select',
    props: ['modelValue', 'options', 'optionLabel', 'optionValue'],
    emits: ['update:modelValue'],
    template:
      '<select class="lang-select" :value="modelValue" @change="$emit(\'update:modelValue\', ($event.target as HTMLSelectElement).value)">' +
      '<option v-for="o in options" :key="o.value" :value="o.value">{{ o.label }}</option></select>',
  },
}))

describe('UiLanguageSwitcher', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
    i18n.global.locale.value = 'zh-TW'
  })

  it('renders both locale options and reflects the current locale', () => {
    const wrapper = mount(UiLanguageSwitcher, { global: { plugins: [i18n] } })
    const options = wrapper.findAll('option').map((o) => o.text())
    expect(options).toEqual(['繁體中文', 'English'])
    expect((wrapper.find('select.lang-select').element as HTMLSelectElement).value).toBe('zh-TW')
  })

  it('changing the select calls uiLocaleStore.set', async () => {
    const store = useUiLocaleStore()
    const spy = vi.spyOn(store, 'set')
    const wrapper = mount(UiLanguageSwitcher, { global: { plugins: [i18n] } })
    await wrapper.find('select.lang-select').setValue('en')
    expect(spy).toHaveBeenCalledWith('en')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm test src/components/shell/UiLanguageSwitcher.test.ts`
Expected: FAIL — cannot resolve `./UiLanguageSwitcher.vue`.

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/components/shell/UiLanguageSwitcher.vue`:

```vue
<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import Select from 'primevue/select'
import { useUiLocaleStore, } from '../../stores/uiLocaleStore'
import type { UiLocale } from '../../theme/resolveInitialUiLocale'

const uiLocale = useUiLocaleStore()
const { t } = useI18n()

const options = computed(() => [
  { value: 'zh-TW', label: t('lang.zh-TW') },
  { value: 'en', label: t('lang.en') },
])

function onChange(value: UiLocale) {
  uiLocale.set(value)
}
</script>

<template>
  <Select
    :model-value="uiLocale.locale"
    :options="options"
    option-label="label"
    option-value="value"
    :aria-label="t('lang.label')"
    class="lang-switcher"
    @update:model-value="onChange"
  />
</template>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm test src/components/shell/UiLanguageSwitcher.test.ts`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/shell/UiLanguageSwitcher.vue frontend/src/components/shell/UiLanguageSwitcher.test.ts
git commit -m "feat(frontend): UiLanguageSwitcher topbar control (FE-R1)"
```

---

## Task 6: UserMenu

**Files:**
- Create: `frontend/src/components/shell/UserMenu.vue`
- Test: `frontend/src/components/shell/UserMenu.test.ts`

**Interfaces:**
- Consumes: `useAuthStore()` (`user`, `logout()`), `useI18n().t`, `useRouter()`, PrimeVue `Menu` (`primevue/menu`, popup mode).
- Produces: `<UserMenu/>` — a `button.user-btn` (avatar `pi-user` + role label from `isSuperAdmin`) that opens a popup `Menu`; the menu has one item that calls `auth.logout()` then `router.push({ name: 'login' })`. Exposes `menuModel` (array with a `logout` item carrying a `command`) via `defineExpose` for testing.

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/shell/UserMenu.test.ts`. PrimeVue `Menu` is stubbed to expose its `model`:

```ts
import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import UserMenu from './UserMenu.vue'
import { useAuthStore } from '../../stores/authStore'
import { i18n } from '../../i18n'

const push = vi.fn()
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))
vi.mock('primevue/menu', () => ({
  default: {
    name: 'Menu',
    props: ['model', 'popup'],
    methods: { toggle() {} },
    template: '<div class="pv-menu" />',
  },
}))

describe('UserMenu', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    i18n.global.locale.value = 'zh-TW'
  })

  it('shows the super-admin role label', () => {
    const auth = useAuthStore()
    auth.user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    const wrapper = mount(UserMenu, { global: { plugins: [i18n] } })
    expect(wrapper.find('.user-role').text()).toBe('超級管理員')
  })

  it('shows the member role label for a non-super-admin', () => {
    const auth = useAuthStore()
    auth.user = { id: 'u2', isSuperAdmin: false, permissions: {} }
    const wrapper = mount(UserMenu, { global: { plugins: [i18n] } })
    expect(wrapper.find('.user-role').text()).toBe('一般使用者')
  })

  it('the logout menu item logs out and routes to login', async () => {
    const auth = useAuthStore()
    auth.user = { id: 'u1', isSuperAdmin: true, permissions: {} }
    const logoutSpy = vi.spyOn(auth, 'logout').mockResolvedValue()
    const wrapper = mount(UserMenu, { global: { plugins: [i18n] } })
    const model = (wrapper.vm as unknown as { menuModel: { label: string; command: () => void }[] }).menuModel
    const logout = model.find((m) => m.label === '登出')!
    expect(logout).toBeTruthy()
    await logout.command()
    await new Promise((r) => setTimeout(r, 0))
    expect(logoutSpy).toHaveBeenCalledOnce()
    expect(push).toHaveBeenCalledWith({ name: 'login' })
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm test src/components/shell/UserMenu.test.ts`
Expected: FAIL — cannot resolve `./UserMenu.vue`.

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/components/shell/UserMenu.vue`:

```vue
<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import Menu from 'primevue/menu'
import { useAuthStore } from '../../stores/authStore'

const auth = useAuthStore()
const router = useRouter()
const { t } = useI18n()

const roleLabel = computed(() => (auth.user?.isSuperAdmin ? t('user.superAdmin') : t('user.member')))

async function onLogout() {
  await auth.logout()
  router.push({ name: 'login' })
}

const menuModel = computed(() => [
  { label: t('common.logout'), icon: 'pi pi-sign-out', command: onLogout },
])

const menu = ref<InstanceType<typeof Menu> | null>(null)
function toggle(event: Event) {
  menu.value?.toggle(event)
}

defineExpose({ menuModel })
</script>

<template>
  <div class="user-wrap">
    <button
      type="button"
      class="user-btn"
      :aria-label="t('user.account')"
      aria-haspopup="true"
      @click="toggle"
    >
      <span class="avatar"><i class="pi pi-user" aria-hidden="true" /></span>
      <span class="user-role">{{ roleLabel }}</span>
    </button>
    <Menu ref="menu" :model="menuModel" :popup="true" />
  </div>
</template>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm test src/components/shell/UserMenu.test.ts`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/shell/UserMenu.vue frontend/src/components/shell/UserMenu.test.ts
git commit -m "feat(frontend): UserMenu topbar control (avatar + role + logout) (FE-R1)"
```

---

## Task 7: SidebarNavItem

**Files:**
- Create: `frontend/src/components/shell/SidebarNavItem.vue`
- Test: `frontend/src/components/shell/SidebarNavItem.test.ts`

**Interfaces:**
- Consumes: nothing (leaf presentational component).
- Produces: `<SidebarNavItem/>` with props `{ label: string, icon?: string | null, active?: boolean }` and a default slot unused. Emits `activate` on click. Renders `button.nav-item`; applies `.active` when `active`; renders the icon (`icon || 'pi pi-file'`) and label. This is the flat/leaf item. Group rendering (expand/collapse) lives in `TheSidebar` (Task 8) using local state — SidebarNavItem stays a pure leaf to keep it trivially testable.

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/shell/SidebarNavItem.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import SidebarNavItem from './SidebarNavItem.vue'

describe('SidebarNavItem', () => {
  it('renders label + fallback icon and emits activate on click', async () => {
    const wrapper = mount(SidebarNavItem, { props: { label: 'Article' } })
    expect(wrapper.text()).toContain('Article')
    expect(wrapper.find('.pi-file').exists()).toBe(true)
    await wrapper.find('button.nav-item').trigger('click')
    expect(wrapper.emitted('activate')).toHaveLength(1)
  })

  it('uses the provided icon and marks active', () => {
    const wrapper = mount(SidebarNavItem, {
      props: { label: 'Dashboard', icon: 'pi pi-th-large', active: true },
    })
    expect(wrapper.find('.pi-th-large').exists()).toBe(true)
    expect(wrapper.find('button.nav-item').classes()).toContain('active')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm test src/components/shell/SidebarNavItem.test.ts`
Expected: FAIL — cannot resolve `./SidebarNavItem.vue`.

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/components/shell/SidebarNavItem.vue`:

```vue
<script setup lang="ts">
const props = withDefaults(
  defineProps<{ label: string; icon?: string | null; active?: boolean }>(),
  { icon: null, active: false },
)
defineEmits<{ activate: [] }>()
</script>

<template>
  <button
    type="button"
    class="nav-item"
    :class="{ active: props.active }"
    @click="$emit('activate')"
  >
    <i class="nav-icon" :class="props.icon || 'pi pi-file'" aria-hidden="true" />
    <span class="nav-label">{{ props.label }}</span>
  </button>
</template>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm test src/components/shell/SidebarNavItem.test.ts`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/shell/SidebarNavItem.vue frontend/src/components/shell/SidebarNavItem.test.ts
git commit -m "feat(frontend): SidebarNavItem leaf component (FE-R1)"
```

---

## Task 8: TheSidebar

**Files:**
- Create: `frontend/src/components/shell/TheSidebar.vue`
- Test: `frontend/src/components/shell/TheSidebar.test.ts`

**Interfaces:**
- Consumes: `useAuthStore()` (`user`), `useSchemaStore()` (`collections`, `load()`, `loadError`), `useSidebarStore()` (`collapsed`, `toggleCollapse()`, `closeDrawer()`), `useRouter()`, `useRoute()`, `useI18n().t`, `buildNav`, `SidebarNavItem`.
- Produces: `<TheSidebar/>` — renders system items (Dashboard always; Media when `isSuperAdmin || permissions.file?.read`) then `buildNav` output (ungrouped 'General' flat; other groups collapsible, default open). Clicking an item navigates and calls `closeDrawer()`. Marks the active item from the route. Shows a footer collapse button. On `schema.loadError`, shows an alert + Retry (`schema.load()`).

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/shell/TheSidebar.test.ts`:

```ts
import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import TheSidebar from './TheSidebar.vue'
import { useAuthStore } from '../../stores/authStore'
import { useSchemaStore } from '../../stores/schemaStore'
import { useSidebarStore } from '../../stores/sidebarStore'
import { i18n } from '../../i18n'

const push = vi.fn()
let currentRoute: { name: string; params: Record<string, string> }
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
  useRoute: () => currentRoute,
}))

function seed(isSuperAdmin: boolean, perms: Record<string, { read: boolean; write: boolean; delete: boolean }>) {
  const auth = useAuthStore()
  auth.user = { id: 'u1', isSuperAdmin, permissions: perms }
  const schema = useSchemaStore()
  schema.collections = [
    { name: 'article', label: 'Article', group: 'Content', fields: [], relations: [] },
    { name: 'page', label: 'Page', group: null, fields: [], relations: [] },
  ]
}

const mountSidebar = () => mount(TheSidebar, { global: { plugins: [i18n] } })

describe('TheSidebar', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    localStorage.clear()
    i18n.global.locale.value = 'zh-TW'
    currentRoute = { name: 'dashboard', params: {} }
  })

  it('shows Dashboard + Media for a user with file:read, and flat + grouped collections', () => {
    seed(false, {
      article: { read: true, write: false, delete: false },
      page: { read: true, write: false, delete: false },
      file: { read: true, write: false, delete: false },
    })
    const wrapper = mountSidebar()
    const labels = wrapper.findAll('.nav-label').map((n) => n.text())
    expect(labels).toContain('儀表板')
    expect(labels).toContain('媒體庫')
    expect(labels).toContain('Article') // grouped under Content
    expect(labels).toContain('Page') // ungrouped -> flat
    expect(labels).toContain('Content') // group header
  })

  it('hides Media without file:read', () => {
    seed(false, { article: { read: true, write: false, delete: false } })
    const wrapper = mountSidebar()
    const labels = wrapper.findAll('.nav-label').map((n) => n.text())
    expect(labels).not.toContain('媒體庫')
  })

  it('navigates and closes the drawer when a collection is clicked', async () => {
    seed(true, {})
    const sidebar = useSidebarStore()
    sidebar.openDrawer()
    const wrapper = mountSidebar()
    const article = wrapper.findAll('button.nav-item').find((b) => b.text().includes('Article'))!
    await article.trigger('click')
    expect(push).toHaveBeenCalledWith({ name: 'collection-list', params: { name: 'article' } })
    expect(sidebar.drawerOpen).toBe(false)
  })

  it('marks the active collection from the route', () => {
    currentRoute = { name: 'collection-list', params: { name: 'article' } }
    seed(true, {})
    const wrapper = mountSidebar()
    const article = wrapper.findAll('button.nav-item').find((b) => b.text().includes('Article'))!
    expect(article.classes()).toContain('active')
  })

  it('shows a retry affordance on schema load error', async () => {
    seed(true, {})
    const schema = useSchemaStore()
    schema.loadError = 'boom'
    const loadSpy = vi.spyOn(schema, 'load').mockResolvedValue()
    const wrapper = mountSidebar()
    expect(wrapper.find('[role="alert"]').exists()).toBe(true)
    await wrapper.find('[role="alert"] button').trigger('click')
    expect(loadSpy).toHaveBeenCalledOnce()
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm test src/components/shell/TheSidebar.test.ts`
Expected: FAIL — cannot resolve `./TheSidebar.vue`.

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/components/shell/TheSidebar.vue`:

```vue
<script setup lang="ts">
import { computed, reactive } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '../../stores/authStore'
import { useSchemaStore } from '../../stores/schemaStore'
import { useSidebarStore } from '../../stores/sidebarStore'
import { buildNav } from '../../lib/buildNav'
import SidebarNavItem from './SidebarNavItem.vue'

const UNGROUPED = 'General'

const auth = useAuthStore()
const schema = useSchemaStore()
const sidebar = useSidebarStore()
const router = useRouter()
const route = useRoute()
const { t } = useI18n()

const canReadMedia = computed(
  () => auth.user?.isSuperAdmin === true || auth.user?.permissions?.file?.read === true,
)

const groups = computed(() =>
  buildNav(schema.collections, auth.user?.isSuperAdmin ?? false, auth.user?.permissions ?? {}),
)

// Expanded state per real group (default open). Keyed by group name.
const open = reactive<Record<string, boolean>>({})
function isOpen(group: string): boolean {
  return open[group] ?? true
}
function toggleGroup(group: string): void {
  open[group] = !isOpen(group)
}

function activeCollection(): string | null {
  const r = route.name as string | undefined
  if (r === 'collection-list' || r === 'collection-create' || r === 'collection-item') {
    return (route.params as Record<string, string>).name ?? null
  }
  return null
}

function go(to: { name: string; params?: Record<string, string> }): void {
  router.push(to)
  sidebar.closeDrawer()
}
</script>

<template>
  <aside class="sidebar" :class="{ collapsed: sidebar.collapsed }" aria-label="主導覽">
    <div v-if="schema.loadError" class="nav-error" role="alert">
      <span>{{ schema.loadError }}</span>
      <button type="button" @click="schema.load()">{{ t('common.confirm') }}</button>
    </div>
    <template v-else>
      <!-- System (pinned) -->
      <SidebarNavItem
        :label="t('nav.dashboard')"
        icon="pi pi-th-large"
        :active="route.name === 'dashboard'"
        @activate="go({ name: 'dashboard' })"
      />
      <SidebarNavItem
        v-if="canReadMedia"
        :label="t('nav.media')"
        icon="pi pi-images"
        :active="route.name === 'media'"
        @activate="go({ name: 'media' })"
      />
      <hr class="nav-sep" aria-hidden="true" />

      <!-- Collections -->
      <template v-for="g in groups" :key="g.group">
        <template v-if="g.group === UNGROUPED">
          <SidebarNavItem
            v-for="it in g.items"
            :key="it.name"
            :label="it.label"
            :icon="it.icon"
            :active="activeCollection() === it.name"
            @activate="go({ name: 'collection-list', params: { name: it.name } })"
          />
        </template>
        <div v-else class="nav-group" :class="{ open: isOpen(g.group) }">
          <button
            type="button"
            class="nav-item nav-parent"
            :aria-expanded="isOpen(g.group)"
            @click="toggleGroup(g.group)"
          >
            <span class="nav-label">{{ g.group }}</span>
            <i class="pi pi-angle-down nav-chev" aria-hidden="true" />
          </button>
          <div v-show="isOpen(g.group)" class="nav-sub">
            <SidebarNavItem
              v-for="it in g.items"
              :key="it.name"
              :label="it.label"
              :icon="it.icon"
              :active="activeCollection() === it.name"
              @activate="go({ name: 'collection-list', params: { name: it.name } })"
            />
          </div>
        </div>
      </template>

      <div class="side-foot">
        <button
          type="button"
          class="nav-item collapse-btn"
          :aria-label="sidebar.collapsed ? t('shell.expand') : t('shell.collapse')"
          @click="sidebar.toggleCollapse()"
        >
          <i class="pi pi-angle-left nav-chev" aria-hidden="true" />
          <span class="nav-label">{{ t('shell.collapse') }}</span>
        </button>
        <p class="caption side-ver">v0.9.0 · {{ t('shell.version') }}</p>
      </div>
    </template>
  </aside>
</template>
```

Note: the retry button reuses `common.confirm` as its label placeholder to avoid a new key; the existing behaviour (message + a retry button inside `role="alert"`) is what the test asserts.

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm test src/components/shell/TheSidebar.test.ts`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/shell/TheSidebar.vue frontend/src/components/shell/TheSidebar.test.ts
git commit -m "feat(frontend): TheSidebar (system items + collection nav + collapse) (FE-R1)"
```

---

## Task 9: AppBreadcrumb

**Files:**
- Create: `frontend/src/components/shell/AppBreadcrumb.vue`
- Test: `frontend/src/components/shell/AppBreadcrumb.test.ts`

**Interfaces:**
- Consumes: `useRoute()`, `useRouter()`, `useSchemaStore()` (`collections`), `useI18n().t`, `buildBreadcrumb`, PrimeVue `Breadcrumb` (`primevue/breadcrumb`).
- Produces: `<AppBreadcrumb/>` — maps `buildBreadcrumb(route, collections, t)` to a PrimeVue `Breadcrumb` `model` (`{ label, command }`, command pushes `crumb.to`); last crumb has no command.

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/shell/AppBreadcrumb.test.ts`. PrimeVue `Breadcrumb` is stubbed to render its `model`:

```ts
import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import AppBreadcrumb from './AppBreadcrumb.vue'
import { useSchemaStore } from '../../stores/schemaStore'
import { i18n } from '../../i18n'

const push = vi.fn()
let currentRoute: { name: string; params: Record<string, string> }
vi.mock('vue-router', () => ({ useRouter: () => ({ push }), useRoute: () => currentRoute }))
vi.mock('primevue/breadcrumb', () => ({
  default: {
    name: 'Breadcrumb',
    props: ['model'],
    template:
      '<ul class="pv-bc"><li v-for="(m,i) in model" :key="i" class="crumb" @click="m.command && m.command()">{{ m.label }}</li></ul>',
  },
}))

describe('AppBreadcrumb', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    i18n.global.locale.value = 'zh-TW'
    const schema = useSchemaStore()
    schema.collections = [{ name: 'article', label: 'Article', group: 'Content', fields: [], relations: [] }]
    currentRoute = { name: 'collection-item', params: { name: 'article', id: 'x' } }
  })

  it('renders the crumb trail and navigates on a non-leaf crumb', async () => {
    const wrapper = mount(AppBreadcrumb, { global: { plugins: [i18n] } })
    const labels = wrapper.findAll('.crumb').map((c) => c.text())
    expect(labels).toEqual(['儀表板', 'Content', 'Article', '編輯項目'])
    // Click the Dashboard crumb -> pushes home.
    await wrapper.findAll('.crumb')[0].trigger('click')
    expect(push).toHaveBeenCalledWith({ name: 'dashboard' })
    // The leaf has no command -> clicking does not push again.
    push.mockClear()
    await wrapper.findAll('.crumb')[3].trigger('click')
    expect(push).not.toHaveBeenCalled()
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm test src/components/shell/AppBreadcrumb.test.ts`
Expected: FAIL — cannot resolve `./AppBreadcrumb.vue`.

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/components/shell/AppBreadcrumb.vue`:

```vue
<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import Breadcrumb from 'primevue/breadcrumb'
import { useSchemaStore } from '../../stores/schemaStore'
import { buildBreadcrumb } from '../../lib/buildBreadcrumb'

const route = useRoute()
const router = useRouter()
const schema = useSchemaStore()
const { t } = useI18n()

const model = computed(() =>
  buildBreadcrumb(
    { name: route.name as string, params: route.params as Record<string, string> },
    schema.collections,
    t,
  ).map((crumb) => ({
    label: crumb.label,
    command: crumb.to ? () => router.push(crumb.to!) : undefined,
  })),
)
</script>

<template>
  <Breadcrumb :model="model" class="app-breadcrumb" />
</template>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm test src/components/shell/AppBreadcrumb.test.ts`
Expected: PASS (1 test).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/shell/AppBreadcrumb.vue frontend/src/components/shell/AppBreadcrumb.test.ts
git commit -m "feat(frontend): AppBreadcrumb (route + schema driven) (FE-R1)"
```

---

## Task 10: TheTopbar

**Files:**
- Create: `frontend/src/components/shell/TheTopbar.vue`
- Test: `frontend/src/components/shell/TheTopbar.test.ts`

**Interfaces:**
- Consumes: `useSidebarStore()` (`toggleDrawer()`), `useRouter()`, `useI18n().t`, and the components `UiLanguageSwitcher`, `ThemeToggle`, `UserMenu`.
- Produces: `<TheTopbar/>` — hamburger `button.drawer-toggle` → `sidebar.toggleDrawer()`; brand `button.brand-btn` → `router.push({ name: 'dashboard' })`; then the three controls.

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/shell/TheTopbar.test.ts`. The three child controls are stubbed (their own tests cover them):

```ts
import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import TheTopbar from './TheTopbar.vue'
import { useSidebarStore } from '../../stores/sidebarStore'
import { i18n } from '../../i18n'

const push = vi.fn()
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))

const stubs = {
  UiLanguageSwitcher: { template: '<div class="stub-lang" />' },
  ThemeToggle: { template: '<div class="stub-theme" />' },
  UserMenu: { template: '<div class="stub-user" />' },
}

describe('TheTopbar', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    localStorage.clear()
    i18n.global.locale.value = 'zh-TW'
  })

  it('the hamburger toggles the drawer', async () => {
    const sidebar = useSidebarStore()
    const wrapper = mount(TheTopbar, { global: { plugins: [i18n], stubs } })
    await wrapper.find('button.drawer-toggle').trigger('click')
    expect(sidebar.drawerOpen).toBe(true)
  })

  it('the brand button routes to the dashboard', async () => {
    const wrapper = mount(TheTopbar, { global: { plugins: [i18n], stubs } })
    await wrapper.find('button.brand-btn').trigger('click')
    expect(push).toHaveBeenCalledWith({ name: 'dashboard' })
  })

  it('mounts the three topbar controls', () => {
    const wrapper = mount(TheTopbar, { global: { plugins: [i18n], stubs } })
    expect(wrapper.find('.stub-lang').exists()).toBe(true)
    expect(wrapper.find('.stub-theme').exists()).toBe(true)
    expect(wrapper.find('.stub-user').exists()).toBe(true)
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm test src/components/shell/TheTopbar.test.ts`
Expected: FAIL — cannot resolve `./TheTopbar.vue`.

- [ ] **Step 3: Write minimal implementation**

Create `frontend/src/components/shell/TheTopbar.vue`:

```vue
<script setup lang="ts">
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useSidebarStore } from '../../stores/sidebarStore'
import UiLanguageSwitcher from './UiLanguageSwitcher.vue'
import ThemeToggle from './ThemeToggle.vue'
import UserMenu from './UserMenu.vue'

const sidebar = useSidebarStore()
const router = useRouter()
const { t } = useI18n()
</script>

<template>
  <header class="topbar">
    <button
      type="button"
      class="icon-btn only-mobile drawer-toggle"
      :aria-label="t('shell.openMenu')"
      @click="sidebar.toggleDrawer()"
    >
      <i class="pi pi-bars" aria-hidden="true" />
    </button>
    <button
      type="button"
      class="brand brand-btn"
      :aria-label="t('shell.brandHome')"
      @click="router.push({ name: 'dashboard' })"
    >
      <span class="mark">S</span><b>StruoCMS</b>
    </button>
    <div class="spacer" />
    <UiLanguageSwitcher />
    <ThemeToggle />
    <UserMenu />
  </header>
</template>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm test src/components/shell/TheTopbar.test.ts`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/shell/TheTopbar.vue frontend/src/components/shell/TheTopbar.test.ts
git commit -m "feat(frontend): TheTopbar (hamburger + brand + controls) (FE-R1)"
```

---

## Task 11: AppShell rebuild + Toast wiring + remove CollectionNav

**Files:**
- Modify (rebuild): `frontend/src/layouts/AppShell.vue`
- Modify (rewrite): `frontend/src/layouts/AppShell.test.ts`
- Modify: `frontend/src/main.ts` (add `app.use(ToastService)`)
- Delete: `frontend/src/components/CollectionNav.vue`, `frontend/src/components/CollectionNav.test.ts`

**Interfaces:**
- Consumes: `TheTopbar`, `TheSidebar`, `AppBreadcrumb`, `useSidebarStore()` (`drawerOpen`, `closeDrawer()`, `collapsed`), `useSchemaStore()` (`load()`), `useRoute()`, PrimeVue `Toast` (`primevue/toast`).
- Produces: the shell grid: `<TheTopbar/>`, `<TheSidebar/>` (+ scrim when `drawerOpen`), `<AppBreadcrumb/>` above `<router-view :key="route.path"/>`, and one `<Toast position="top-right"/>`. Closes the drawer on route change. Loads schema on mount (preserved from the old shell).

- [ ] **Step 1: Rewrite the failing AppShell test**

Replace `frontend/src/layouts/AppShell.test.ts` with:

```ts
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { ref } from 'vue'
import AppShell from './AppShell.vue'
import { useSidebarStore } from '../stores/sidebarStore'
import { useSchemaStore } from '../stores/schemaStore'
import { i18n } from '../i18n'

const routeRef = ref<{ path: string; name: string; params: Record<string, string> }>({
  path: '/',
  name: 'dashboard',
  params: {},
})
vi.mock('vue-router', () => ({
  useRoute: () => routeRef.value,
  useRouter: () => ({ push: vi.fn() }),
  RouterView: { template: '<div class="rv" />' },
}))

const stubs = {
  TheTopbar: { template: '<div class="stub-topbar" />' },
  TheSidebar: { template: '<div class="stub-sidebar" />' },
  AppBreadcrumb: { template: '<div class="stub-bc" />' },
  Toast: { template: '<div class="stub-toast" />' },
  RouterView: true,
}

const mountShell = () => mount(AppShell, { global: { plugins: [i18n], stubs } })

describe('AppShell', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    routeRef.value = { path: '/', name: 'dashboard', params: {} }
  })

  it('composes topbar, sidebar, breadcrumb, router-view and a toast host', () => {
    const schema = useSchemaStore()
    vi.spyOn(schema, 'load').mockResolvedValue()
    const wrapper = mountShell()
    expect(wrapper.find('.stub-topbar').exists()).toBe(true)
    expect(wrapper.find('.stub-sidebar').exists()).toBe(true)
    expect(wrapper.find('.stub-bc').exists()).toBe(true)
    expect(wrapper.find('.stub-toast').exists()).toBe(true)
  })

  it('loads the schema on mount', () => {
    const schema = useSchemaStore()
    const loadSpy = vi.spyOn(schema, 'load').mockResolvedValue()
    mountShell()
    expect(loadSpy).toHaveBeenCalledOnce()
  })

  it('shows the scrim only when the drawer is open and closes it on click', async () => {
    const schema = useSchemaStore()
    vi.spyOn(schema, 'load').mockResolvedValue()
    const sidebar = useSidebarStore()
    const wrapper = mountShell()
    expect(wrapper.find('.scrim').exists()).toBe(false)
    sidebar.openDrawer()
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.scrim').exists()).toBe(true)
    await wrapper.find('.scrim').trigger('click')
    expect(sidebar.drawerOpen).toBe(false)
  })

  it('closes the drawer when the route changes', async () => {
    const schema = useSchemaStore()
    vi.spyOn(schema, 'load').mockResolvedValue()
    const sidebar = useSidebarStore()
    const wrapper = mountShell()
    sidebar.openDrawer()
    await wrapper.vm.$nextTick()
    routeRef.value = { path: '/media', name: 'media', params: {} }
    await wrapper.vm.$nextTick()
    expect(sidebar.drawerOpen).toBe(false)
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm test src/layouts/AppShell.test.ts`
Expected: FAIL — the current `AppShell.vue` has `button.logout`/`CollectionNav`, not the new composition; scrim/drawer assertions fail.

- [ ] **Step 3: Rebuild AppShell.vue**

Replace `frontend/src/layouts/AppShell.vue` with:

```vue
<script setup lang="ts">
import { onMounted, watch } from 'vue'
import { useRoute } from 'vue-router'
import Toast from 'primevue/toast'
import { useSchemaStore } from '../stores/schemaStore'
import { useSidebarStore } from '../stores/sidebarStore'
import TheTopbar from '../components/shell/TheTopbar.vue'
import TheSidebar from '../components/shell/TheSidebar.vue'
import AppBreadcrumb from '../components/shell/AppBreadcrumb.vue'

const schema = useSchemaStore()
const sidebar = useSidebarStore()
const route = useRoute()

onMounted(() => {
  schema.load()
})

// Close the mobile drawer whenever the route changes.
watch(
  () => route.path,
  () => sidebar.closeDrawer(),
)
</script>

<template>
  <div class="shell" :class="{ collapsed: sidebar.collapsed, drawer: sidebar.drawerOpen }">
    <TheTopbar />
    <TheSidebar />
    <button
      v-if="sidebar.drawerOpen"
      type="button"
      class="scrim"
      aria-label="關閉導覽選單"
      @click="sidebar.closeDrawer()"
    />
    <main class="content">
      <div class="page">
        <AppBreadcrumb />
        <!-- Key on route.path so params-only navigations between records of the same route remount the
             view (init() re-runs, loads the target item); query changes (list page/sort) do not. -->
        <router-view :key="route.path" />
      </div>
    </main>
    <Toast position="top-right" />
  </div>
</template>
```

- [ ] **Step 4: Add ToastService to main.ts**

In `frontend/src/main.ts`, add the import and registration (alongside the existing `ConfirmationService`):

```ts
import ToastService from 'primevue/toastservice'
```
and after `app.use(ConfirmationService)`:
```ts
app.use(ToastService)
```

- [ ] **Step 5: Delete the obsolete CollectionNav**

```bash
git rm frontend/src/components/CollectionNav.vue frontend/src/components/CollectionNav.test.ts
```

- [ ] **Step 6: Run the AppShell test to verify it passes**

Run: `pnpm test src/layouts/AppShell.test.ts`
Expected: PASS (4 tests).

- [ ] **Step 7: Run the full suite + build**

Run: `pnpm test`
Expected: all green (the removed CollectionNav tests are gone; TheSidebar covers that behaviour).

Run: `pnpm build`
Expected: vue-tsc + vite clean (pre-existing >500 kB chunk advisory only).

- [ ] **Step 8: Commit**

```bash
git add frontend/src/layouts/AppShell.vue frontend/src/layouts/AppShell.test.ts frontend/src/main.ts
git commit -m "feat(frontend): rebuild AppShell (topbar + sidebar + breadcrumb + toast); remove CollectionNav (FE-R1)"
```

---

## Task 12: Shell styling (theme.css tokens)

**Files:**
- Modify: `frontend/src/assets/theme.css` (append a shell layout section)

**Interfaces:**
- Consumes: the FE-R0 CSS custom properties already defined in `theme.css` (`--surface`, `--border`, `--muted`, `--fg`, `--accent`, `--radius`, `--sidebar-w`, `--shadow-*`, `--speed`, etc.).
- Produces: layout CSS for `.shell`, `.topbar`, `.sidebar`, `.nav-item`, `.nav-group`, `.side-foot`, `.scrim`, `.content`, `.page`, `.app-breadcrumb`, and the `≤1023px` / `≤520px` responsive rules. No JS/behaviour change → not unit-tested; verified in the Playwright MCP smoke.

- [ ] **Step 1: Confirm the token names available**

Run: `pnpm test` (baseline still green from Task 11) and open `frontend/src/assets/theme.css` to confirm the FE-R0 token names (`--surface`, `--border`, `--muted`, `--fg`, `--accent`, `--bg`, `--radius`, `--radius-lg`, `--sidebar-w`, `--speed`, `--shadow-1/2/3`, `--font`, `--mono`). Use the actual names present; if a name differs, adapt the selectors below to match.

- [ ] **Step 2: Append the shell layout section to `theme.css`**

Append (adjusting any token names to match what FE-R0 actually defined). This is layout only — colours come from the tokens so light/dark flip automatically:

```css
/* ---- FE-R1 shell layout ---- */
.shell {
  display: grid;
  grid-template-columns: var(--sidebar-w, 260px) 1fr;
  grid-template-rows: 56px 1fr;
  grid-template-areas: 'top top' 'side main';
  height: 100dvh;
  transition: grid-template-columns var(--speed, .15s) ease-out;
}
.shell.collapsed { --sidebar-w: 76px; }
.topbar {
  grid-area: top; display: flex; align-items: center; gap: 8px; padding: 0 16px;
  background: var(--surface); border-bottom: 1px solid var(--border); z-index: 50;
}
.topbar .spacer { flex: 1; }
.topbar .brand-btn {
  display: flex; align-items: center; gap: 10px; border: none; background: transparent;
  padding: 4px 10px; border-radius: var(--radius, 8px); cursor: pointer; color: var(--fg);
}
.topbar .brand-btn:hover { background: var(--bg); }
.topbar .mark {
  width: 28px; height: 28px; border-radius: 8px; background: var(--accent);
  color: #fff; display: grid; place-items: center; font-weight: 700;
}
.user-wrap { position: relative; }
.user-btn {
  display: flex; align-items: center; gap: 10px; padding: 4px 10px 4px 4px;
  border: none; border-radius: 999px; background: transparent; cursor: pointer; color: var(--fg);
}
.user-btn:hover { background: var(--bg); }
.avatar {
  width: 30px; height: 30px; border-radius: 999px; background: var(--bg);
  border: 1px solid var(--border); display: grid; place-items: center; color: var(--muted);
}
.user-role { font-size: .8rem; color: var(--muted); }
.sidebar {
  grid-area: side; display: flex; flex-direction: column; gap: 4px; padding: 14px 12px;
  background: var(--surface); border-right: 1px solid var(--border); overflow-y: auto;
}
.nav-sep { border: none; border-top: 1px solid var(--border); margin: 10px 4px; width: auto; }
.nav-item {
  display: flex; align-items: center; gap: 11px; padding: 9px 12px; border-radius: var(--radius, 8px);
  border: none; background: transparent; color: var(--muted); font-size: .9rem; font-weight: 500;
  cursor: pointer; text-align: left; width: 100%; transition: background var(--speed, .15s), color var(--speed, .15s);
}
.nav-item:hover { background: var(--bg); color: var(--fg); }
.nav-item.active { background: var(--bg); color: var(--fg); font-weight: 600; }
.nav-parent .nav-chev { margin-left: auto; width: 14px; transition: transform var(--speed, .15s); }
.nav-group.open .nav-parent .nav-chev { transform: rotate(180deg); }
.nav-sub { display: grid; gap: 2px; padding-left: 18px; }
.side-foot { margin-top: auto; display: grid; gap: 8px; padding-top: 12px; border-top: 1px solid var(--border); }
.shell.collapsed .nav-label, .shell.collapsed .side-ver, .shell.collapsed .nav-chev { display: none; }
.shell.collapsed .nav-item { justify-content: center; padding: 9px; }
.shell.collapsed .nav-sub { padding-left: 0; }
.scrim { position: fixed; inset: 56px 0 0 0; background: rgb(0 0 0 / .5); z-index: 35; border: none; padding: 0; }
.content { grid-area: main; overflow-y: auto; background: var(--surface); }
.page { padding: 24px 28px 48px; }
.app-breadcrumb { margin-bottom: 14px; background: transparent; border: none; padding: 0; }
.only-mobile { display: none; }
@media (max-width: 1023px) {
  .shell { grid-template-columns: 1fr; grid-template-areas: 'top' 'main'; }
  .sidebar {
    position: fixed; top: 56px; bottom: 0; left: 0; width: 280px;
    transform: translateX(-105%); transition: transform var(--speed, .15s) ease-out;
    z-index: 40; box-shadow: var(--shadow-2);
  }
  .shell.drawer .sidebar { transform: none; }
  .only-mobile { display: inline-flex; }
  .user-role { display: none; }
}
@media (max-width: 520px) {
  .page { padding: 16px 14px 40px; }
  .topbar { padding: 0 10px; }
  .lang-switcher { display: none; }
}
```

- [ ] **Step 3: Run the full suite (styling must not break tests)**

Run: `pnpm test`
Expected: all green (CSS-only change).

- [ ] **Step 4: Build**

Run: `pnpm build`
Expected: clean.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/assets/theme.css
git commit -m "feat(frontend): shell layout CSS on FE-R0 tokens (grid + sidebar + drawer RWD) (FE-R1)"
```

---

## Task 13: Final verification gate (whole slice)

**Files:** none (verification only).

- [ ] **Step 1: Full unit suite**

Run: `pnpm test`
Expected: all green. Confirm the count moved sensibly from 357 (new shell tests added; the 3 CollectionNav tests removed, their coverage now in `TheSidebar`).

- [ ] **Step 2: Type + production build**

Run: `pnpm build`
Expected: vue-tsc + vite clean (pre-existing >500 kB chunk advisory only).

- [ ] **Step 3: Playwright MCP browser smoke (manual, per FE-R0 gate)**

Start Vite: `pnpm dev --host 127.0.0.1` (backend not required for the pre-login checks). With the Playwright MCP browser, verify:
1. Backend down → the login page renders (shell not shown).
2. (Backend up, logged in) topbar controls render; the **theme toggle** flips `.app-dark` on `<html>` and tokens change; the **language switcher** flips shell strings + `<html lang>`.
3. The **sidebar collapse** button collapses to icons-only and the state survives a reload (`localStorage('struo.sidebar')`).
4. Resize to ≤1023px: the hamburger appears, opens the **drawer** with a scrim; clicking the scrim / navigating closes it.
5. **Breadcrumbs** reflect the route (dashboard / media / a collection list / a collection item).
6. A **shell toast** appears on a triggered failure (e.g. force a schema-load error).

Record evidence (screenshots / observations) in the SDD progress ledger.

- [ ] **Step 4: No commit** (verification only). If any check fails, fix under systematic-debugging and re-run this task.

---

## Self-Review (completed during authoring)

- **Spec coverage:** §2 decomposition → Tasks 2–11; §3 state → Task 2; §4 sidebar → Tasks 7–8; §5 topbar → Tasks 4–6, 10; §6 breadcrumbs → Tasks 3, 9; §6 Toast → Task 11; §7 i18n → Task 1; §9 error handling → Tasks 2 (localStorage), 8 (schema retry), 6/11 (logout); §10 testing → every task's TDD steps + Task 13; §11 gate → Task 13. Shell CSS (spec §0 custom-layout) → Task 12.
- **Placeholder scan:** none — every step carries real code/commands. The `TheSidebar` retry button reuses `common.confirm` (explicitly noted) to avoid an unused key.
- **Type consistency:** `Crumb`/`buildBreadcrumb` signature identical in Tasks 3 & 9; `useSidebarStore` action names (`toggleCollapse`/`openDrawer`/`closeDrawer`/`toggleDrawer`) consistent across Tasks 2, 8, 10, 11; `SidebarNavItem` props (`label`/`icon`/`active`) + `activate` event consistent across Tasks 7 & 8; `buildNav` `NavGroup`/`UNGROUPED='General'` used as defined.
```
