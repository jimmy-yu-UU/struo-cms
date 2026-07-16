# FE-R1 — App shell (frontend redesign, slice 1)

> **Status:** design (brainstormed, approved 2026-07-16).
> **Slice of:** the **frontend admin redesign** — a full visual re-skin of the Vue admin SPA to the
> approved design prototype (`docs/struo-cms-frontend-design/struocms-admin-prototype.html` +
> `brand-spec.md`). Builds directly on **FE-R0** (design-token layer + custom Aura preset + theme engine
> + admin-UI i18n engine). **This spec is slice 1 (FE-R1): the application shell.**
> **Design-reference rule (user):** the prototype is a *visual* reference only — **no code is copied from
> it**; the shell is rebuilt from the design, not lifted.

## 0. Summary

FE-R1 replaces the current bare `AppShell` (a `StruoCMS` title, a logout button, a `PanelMenu`-based
`CollectionNav`, and a `router-view`) with the full application shell from the prototype:

1. **Topbar** (56px) — mobile drawer toggle · brand button (→ dashboard) · **UI-language switcher** ·
   **theme toggle** · user menu (avatar + role + logout). The language switcher and theme toggle finally
   expose the engines FE-R0 built.
2. **Sidebar** (260px, collapsible to 76px on desktop; a fixed drawer + scrim on mobile) — pinned system
   items (dashboard, media) → separator → the API-generated collection nav (flat for ungrouped
   collections, collapsible groups for grouped ones), rebuilt as custom layout (the `PanelMenu` is
   removed) → footer with a collapse button + version string.
3. **Breadcrumbs** — a route + schema derived trail (generic leaf labels).
4. **Toasts** — PrimeVue `Toast` infrastructure mounted once, used for shell-level feedback.

**Pure frontend.** No backend, API, route-structure, or persistence change → **no `dotnet` gate, no live
Postgres gate**. The app stays functional throughout; all existing tests stay green.

**Element strategy (FE-R0 decision, confirmed):** shell *layout* (topbar / sidebar / breadcrumb bar) is
custom markup styled from the `theme.css` OKLch tokens; discrete *controls* are PrimeVue components
(`Select` for the language switcher, `Menu` for the user dropdown, `Breadcrumb`, `Toast`). The current
`PanelMenu` collection nav is replaced by a custom sidebar nav that matches the prototype's flat-items +
collapsible-groups look.

## 1. Scope decisions (approved 2026-07-16)

| Decision | Choice |
|---|---|
| Sidebar system items | **Only what exists**: Dashboard + Media (Media gated by `file.read`). The `User` collection surfaces naturally in the collection nav below; no separate "Users & Roles" or "Settings" system screens (they don't exist). No dead links. |
| User menu items | **Logout only** (plus an avatar + role header). |
| Toasts | **Infrastructure only**: stand up `Toast` + `ToastService` (top-right); wire shell-level feedback (schema-load failure, logout). Per-screen toast-ification is deferred to each screen's slice. |
| Element strategy | **Custom layout + PrimeVue controls**; replace `PanelMenu`. |
| Breadcrumb leaf | **Generic label + future enrichment**: derived purely from route + schema; the item level shows a generic label ("新增項目" / "編輯項目"). The real record title is filled in when FE-R5 rebuilds the item form. |

**User-identity constraint (discovered):** `/auth/me` returns only `{ id, isSuperAdmin, permissions }` —
**no display name, email, or role name**. Because FE-R1 is a pure-frontend slice (no backend change), the
user menu shows a generic person-icon avatar and a role label derived from `isSuperAdmin`
(`user.superAdmin` / `user.member`). Surfacing a real display name / email requires extending `/auth/me`
and is explicitly **out of scope** (see §10).

## 2. Architecture — component decomposition (approach A)

Chosen over a monolithic `AppShell.vue` (approach B: fewer files but a fat, hard-to-test component,
violating the many-small-files rule) and a wholesale PrimeVue layout template (approach C: over-built,
conflicts with the custom-layout decision). Each unit has one clear purpose and is independently testable.

```
layouts/AppShell.vue            grid layout coordinator + <Toast/> + scrim + responsive state wiring
components/shell/
  TheTopbar.vue                 drawer toggle · brand button (→dashboard) · lang switcher · theme toggle · user menu
  TheSidebar.vue                system items + collection nav (custom, replaces PanelMenu) + footer collapse/version
  SidebarNavItem.vue            one nav entry (icon + label + active); a collapsible container for a group
  UserMenu.vue                  avatar + role header + dropdown (logout) — PrimeVue Menu (popup)
  UiLanguageSwitcher.vue        PrimeVue Select → uiLocaleStore.set()
  ThemeToggle.vue               icon button → themeStore.toggle(); moon/sun icon
  AppBreadcrumb.vue             PrimeVue Breadcrumb fed by buildBreadcrumb()
stores/sidebarStore.ts          collapsed (desktop) + drawerOpen (mobile) + localStorage persistence
lib/buildBreadcrumb.ts          pure fn: route + schema → breadcrumb node array
```

**Reused unchanged:** `buildNav.ts` (grouping logic). **Removed:** `CollectionNav.vue` (its role is
absorbed by `TheSidebar` + `SidebarNavItem`; the `PanelMenu` dependency for nav goes away).

## 3. State & responsiveness — `stores/sidebarStore.ts`

Pinia store, source-of-truth for shell chrome state:
- state `collapsed: boolean` (desktop sidebar 260px ⇄ 76px, icons only), `drawerOpen: boolean` (mobile).
- `collapsed` initialises from `localStorage('struo.sidebar')` (`'collapsed'` → true, else false).
- `toggleCollapse()` → flip `collapsed`, write `localStorage('struo.sidebar')`.
- `openDrawer()` / `closeDrawer()` / `toggleDrawer()` → set `drawerOpen`.
- localStorage access wrapped in try/catch (private-mode / SSR-less guards, per FE-R0 style); an unknown
  persisted value falls back to expanded.

**Responsive behaviour (breakpoints match the prototype):**
- `≤1023px`: sidebar becomes a fixed drawer (transform slide-in) with a scrim overlay; the topbar shows
  the hamburger button. Opening → `drawerOpen = true`; clicking the scrim, pressing Escape, or navigating
  (route change) → `closeDrawer()`.
- Desktop: the collapse button toggles `collapsed`; the drawer state is irrelevant.
- Motion respects `prefers-reduced-motion` (transitions already disabled globally via `theme.css`).

## 4. Sidebar — `TheSidebar.vue` + `SidebarNavItem.vue`

- **System section (pinned, flat):** Dashboard (always); Media (only when `canReadMedia` —
  `isSuperAdmin || permissions.file?.read`, the existing check). Then a separator.
- **Collection section:** `buildNav(schema.collections, isSuperAdmin, permissions)`:
  - the `General` (ungrouped) group renders its items **flat** (no group header);
  - any real group renders as a **collapsible group** (chevron rotates 150ms on expand — matches the
    prototype and the critique's hierarchy note). Groups default to expanded.
  - each item uses the collection's `icon` (fallback: a document icon).
- **Active state:** derived from the current `route` — dashboard, media, and `collection-list` /
  `collection-create` / `collection-item` (matched by `:name` param). One active item at a time.
- **Collapsed state (desktop):** icons only; labels, group sub-lists, and chevrons hidden (per the
  prototype's collapsed CSS behaviour).
- **Footer:** desktop-only collapse button + a version caption.
- **Schema-load error:** preserve the existing "error message + Retry" affordance (`role="alert"`).

## 5. Topbar — `TheTopbar.vue`

Left → right:
- Hamburger (`only-mobile`) → `sidebarStore.toggleDrawer()`.
- Brand button (S mark + `StruoCMS`) → `router.push({ name: 'dashboard' })`.
- Spacer.
- `UiLanguageSwitcher` (繁體中文 / English via `uiLocaleStore`; hidden on very narrow viewports per the
  prototype — a `≤520px` rule).
- `ThemeToggle` (moon when light / sun when dark; calls `themeStore.toggle()`).
- `UserMenu`: avatar = generic person icon; header = role label
  (`isSuperAdmin ? t('user.superAdmin') : t('user.member')`, **no real name** — see §1); dropdown = a
  single "登出" item (existing `auth.logout()` → `router.push({ name: 'login' })`).

## 6. Breadcrumbs — `lib/buildBreadcrumb.ts` + `AppBreadcrumb.vue`

`buildBreadcrumb(route, collections)` → an ordered array of `{ label, to? }` nodes (pure, unit-testable).
The trail always starts with Dashboard (label + link home):

| Route | Trail |
|---|---|
| `dashboard` (`/`) | 儀表板 |
| `media` (`/media`) | 儀表板 / 媒體庫 |
| `collection-list` (`/collections/:name`) | 儀表板 / (group, if any) / {collection label} |
| `collection-create` (`/collections/:name/new`) | 儀表板 / (group) / {collection label} / **新增項目** |
| `collection-item` (`/collections/:name/:id`) | 儀表板 / (group) / {collection label} / **編輯項目** |

- The group node (when present) is a non-link label (no group landing page exists).
- The collection label + group come from the schema (`collections.find(c => c.name === name)`); an unknown
  collection falls back to the raw `:name`.
- The real record title at the item level is a deliberate future enrichment (FE-R5) — R1 uses the generic
  `breadcrumb.newItem` / `breadcrumb.editItem` labels.

`AppBreadcrumb.vue` renders the nodes via PrimeVue `Breadcrumb` and sits at the top of the content area
(one instance in the shell, not per-view).

## 7. i18n — new keys

Extend `locales/zh-TW.ts` + `locales/en.ts` (symmetric keys — the existing `locales` symmetry test
covers them). New namespaces / keys:
- `nav.dashboard`, `nav.media`
- `shell.collapse`, `shell.expand`, `shell.openMenu`, `shell.closeMenu`, `shell.version`
- `user.superAdmin`, `user.member`
- `breadcrumb.newItem`, `breadcrumb.editItem`
- reuse existing `theme.*`, `lang.*`, `common.logout`.

## 8. Integration & wiring

- `main.ts`: add `app.use(ToastService)` (PrimeVue). Theme/i18n/preset wiring from FE-R0 unchanged.
- `router/index.ts`: unchanged (route structure is stable). The shell reads the current route reactively.
- `AppShell.vue`: rebuilt as the grid coordinator — renders `<TheTopbar/>`, `<TheSidebar/>`,
  `<AppBreadcrumb/>` above `<router-view :key="route.path"/>` (the existing keying comment/behaviour is
  preserved), the scrim, and one `<Toast position="top-right"/>`.

**Files added:** `components/shell/TheTopbar.vue`, `TheSidebar.vue`, `SidebarNavItem.vue`, `UserMenu.vue`,
`UiLanguageSwitcher.vue`, `ThemeToggle.vue`, `AppBreadcrumb.vue`; `stores/sidebarStore.ts`;
`lib/buildBreadcrumb.ts` (+ their `.test.ts`).
**Files changed:** `layouts/AppShell.vue` (full rebuild), `main.ts` (ToastService), `locales/zh-TW.ts` +
`en.ts` (new keys).
**Files removed:** `components/CollectionNav.vue` + `CollectionNav.test.ts` (absorbed by `TheSidebar`).
**Not touched:** backend, any API, route structure, all view components' internal logic, field/media
components.

## 9. Error handling

- `sidebarStore` localStorage read/write wrapped in try/catch; unknown persisted value → expanded default.
- Schema-load error keeps the existing message + Retry affordance (`role="alert"`).
- Media nav item hidden when unauthorised (existing permission check).
- Logout failure still clears the local session (existing `finally` in `auth.logout()`) and a shell toast
  reports it.

## 10. Testing strategy (TDD)

**Unit (vitest, new):**
- `sidebarStore`: collapse/drawer toggles; `localStorage('struo.sidebar')` persistence + restore; unknown
  value defends to expanded.
- `buildBreadcrumb`: each route × schema combination (group present / absent; new / edit leaf; unknown
  collection falls back to raw name); Dashboard-first invariant.
- `TheSidebar`: system-item permission gate (no `file.read` → no Media item); flat vs grouped rendering;
  active-item marking per route; schema-error Retry.
- `SidebarNavItem`: icon + label + active class; group expand/collapse toggles sub-list + chevron.
- `TheTopbar` / `ThemeToggle` / `UiLanguageSwitcher` / `UserMenu`: interactions drive the right store
  (theme toggle, locale set, logout, drawer toggle); i18n strings render and follow locale.
- `AppBreadcrumb`: renders the nodes from `buildBreadcrumb`.
- `AppShell`: responsive state coordination; mounts `Toast` + scrim; drawer closes on route change / scrim
  click / Escape.
- `locales`: the existing symmetric-key test covers the new keys.

**Regression:** the existing **357** frontend tests stay green (minus the removed `CollectionNav` tests,
whose coverage moves into `TheSidebar`); `pnpm build` (vue-tsc + vite) clean (pre-existing >500 kB chunk
advisory only).

**Manual visual smoke (Playwright MCP, per FE-R0 — not automated):** run Vite (`--host 127.0.0.1`); with
the backend down, confirm the login page renders. Then (backend up) log in and confirm: topbar controls,
theme toggle flips `.app-dark` + tokens, language switcher flips shell strings + `<html lang>`, sidebar
collapse persists across reload, mobile drawer opens/closes (resize ≤1023px), breadcrumbs reflect the
route, and a shell toast appears on a triggered failure.

## 11. Acceptance gate

- `pnpm test` all green; `pnpm build` clean.
- Backend untouched → **no `dotnet` gate, no live Postgres gate**.
- Manual Playwright MCP shell smoke passes (theme + i18n + collapse/drawer + breadcrumb + toast).

## 12. Out of scope (recorded so they aren't lost)

- Any screen's visual rebuild (login / dashboard / list / form / media → FE-R2–R6; revisions UI → FE-R7).
- The user menu's "個人資料" / "偏好設定" screens; a real display name / email in the user menu (needs a
  backend `/auth/me` field).
- The breadcrumb's real record title at the item level (→ FE-R5).
- Standalone "Users & Roles" / "Settings" system screens.
- A third UI language (日本語 in the prototype) — add a locale pack later; the engine supports it.
- Adopting the prototype's bespoke SVG icon sheet (keep primeicons).
