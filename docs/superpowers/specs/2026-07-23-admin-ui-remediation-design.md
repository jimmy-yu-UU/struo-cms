# Admin UI Remediation — Design Spec (2026-07-23)

## Background

Post-redesign (FE-R0..R7) live audit of the admin SPA against the design prototype
(`docs/struo-cms-frontend-design/struocms-admin-prototype.html` + `brand-spec.md`) found
confirmed behavioral bugs and visual-fidelity gaps. Evidence: full Playwright walkthrough
(desktop 1440×900 / mobile 390×844 × light/dark) on live PG stack, screenshots archived in
session scratchpad `ui-audit/`.

Scope decision (user-approved): **fix all behavioral bugs + high-impact visual remediation**,
in two batches. Full per-page prototype parity (2-column form, login remember-me, profile
pages) is explicitly out of scope.

## Root causes (verified)

| # | Symptom | Root cause |
|---|---------|-----------|
| 1 | Sidebar cannot re-expand after collapse | `theme.css:94` hides `.nav-chev` when collapsed; collapse button's only visible children are `.nav-label`+`.nav-chev` → invisible 51×18px empty button. Prototype uses a separate `.chev` class that stays visible and rotates (`proto:169-171`), plus `only-desktop` on the button (`proto:428`). Group headers (`nav-parent`) have no leading icon in impl (prototype has one, `proto:421`) so they also become invisible when collapsed. |
| 2 | Breadcrumb sometimes incomplete | `buildBreadcrumb.ts` has no `settings` case → falls into collection branch → trailing crumb with empty label ("儀表板 › "). Collection pages briefly lack group/label until schema loads (reactive, acceptable). |
| 3 | Hamburger menu visible on desktop | `.only-mobile{display:none}` (`theme.css:101`) is overridden by later same-specificity `.icon-btn{display:inline-flex}` (`theme.css:120`). |
| 4 | Double scrollbars / 16px page overflow on every page | `theme.css` lacks `body{margin:0}` reset (prototype has it, `proto:39`). |
| 5 | Mobile collection list broken | DataTable has no overflow container → page-level horizontal scroll; PageHeader/ListToolbar don't wrap → "+ 新增" and Active/Trash toggle clipped. |
| 6 | List titles mostly "—" | `CollectionListView.cellValue` reads translations for `langStore.defaultCode` only; no fallback to other locales. |
| 7 | Buttons lighter blue than spec | `theme/preset.ts` remaps the sky palette but keeps Aura's default `primary.color = {primary.500}`; brand-spec light accent = sky-600, dark = sky-400. |

## Batch 1 — behavioral fixes

- **B1-1 Sidebar collapse** (`theme.css`, `TheSidebar.vue`)
  - Collapse button chevron gets its own class (e.g. `collapse-chev`) excluded from the
    collapsed hide rule; rotates 180° when collapsed; stays visible. Button gains
    `only-desktop` (new utility) so it no longer appears inside the mobile drawer.
  - Group headers (`nav-parent`) gain a leading icon (default `pi pi-folder`; schema groups
    define no icon). Collapsed mode: hide `.nav-sub` (match prototype); clicking a group
    while collapsed expands the sidebar and opens that group (prototype behavior).
  - `aria-expanded` / `aria-label` semantics preserved.
- **B1-2 Breadcrumb** (`lib/buildBreadcrumb.ts` + unit tests)
  - Add `settings` case → `[儀表板(link), t('nav.settings')]`.
  - Guard: never emit a crumb with an empty label (unknown/non-collection routes degrade to
    `[儀表板]`).
- **B1-3 only-mobile utility** (`theme.css`): reorder so `.only-mobile{display:none}` comes
  after `.icon-btn`, with a comment pinning the ordering contract; mobile media query keeps
  `display:inline-flex`.
- **B1-4 Body reset** (`theme.css`): add `body { margin: 0; }`.
- **B1-5 Mobile list survivability** (`CollectionListView.vue`, `theme.css`)
  - Wrap DataTable in an `overflow-x:auto` container — the page itself never scrolls
    horizontally.
  - PageHeader / ListToolbar wrap correctly at narrow widths; New button and Active/Trash
    SelectButton stay fully visible.
  - Full card-ification of rows is deferred (documented, not in this remediation).
- **B1-6 Cross-locale title fallback** (`lib/translatedCell.ts`, used by
  `CollectionListView` + dashboard `RecentUpdatesTable`)
  - Value resolution: default locale → first locale with a non-empty value → "—".
- **B1-7 Drawer scrim in dark mode** (`theme.css`): scrim darkened via token so the open
  drawer visibly dims content in both themes.
- **B1-8 Branding restore** (data, not code): during live verification, PUT
  `/api/settings/branding` name back to `StruoCMS` (test articles are kept — user decision).

## Batch 2 — visual remediation

- **B2-1 Primary color alignment** (`theme/preset.ts`): add semantic
  `colorScheme.light.primary = { color: '{primary.600}', hover 700, active 800 }` and
  `colorScheme.dark.primary = { color: '{primary.400}', hover 300 }`, matching
  `brand-spec.md` tokens and `assets/theme.css` `--accent`.
- **B2-2 Accent restraint** (form/settings/media views): FilePicker "選擇", Repeater
  "+ Add", upload pickers and similar secondary actions become `severity="secondary"`;
  primary reserved for the page-level CTA (儲存 / 新增 / 上傳檔案). Target: ≤2 accent
  uses per screen (brand-spec posture #1).
- **B2-3 Form layout** (`ItemFormView.vue` / `ItemForm.vue`)
  - Form column `max-width` ≈ 860px (no more full-bleed 1300px inputs).
  - Section order: translatable content (locale Tabs) → non-translatable fields →
    relations. Single-column layout stays (FE-R5 decision unchanged).
  - CheckboxGroup spacing fixed (Audiences visual collapse).
  - Relation dropdowns get a consistent width.
- **B2-4 List visuals** (`CollectionListView.vue`)
  - Status column → PrimeVue Tag pill (published=success, draft=neutral, others mapped).
  - New `lib/formatDateTime.ts` → `YYYY-MM-DD HH:mm`, tabular/mono class; replaces scattered
    `toLocaleString()` in list DateTime formatting, dashboard, media list/detail, revisions.
  - Row actions → icon button (trash icon + tooltip + existing confirm flow).
  - Title cell styled as row-link (bold, hover underline).
- **B2-5 Dashboard**: QuickActions get icons; RecentUpdatesTable uses formatDateTime +
  translatedCell.
- **B2-6 Copy & user menu**
  - Sidebar caption "v0.9.0 · 重新設計原型" → neutral copy (drop the prototype placeholder).
  - UserMenu: header with display name + email, separator, 登出 (data from `auth.user`).
  - Topbar shows user name (fallback email), role as caption.
  - Requires one small backend change (the only one in this remediation): `/api/auth/me`
    currently returns only `{id, isSuperAdmin, permissions}` — extend it with `email` +
    `name` (User entity already has both; same `ISqlSugarClient` pattern as UsersController).
- **B2-7 Media library**: remove duplicated filename on tiles (chip + caption both show it).
  Broken thumbnails are a MinIO presigned-URL env issue (https vs http) — out of frontend
  scope, tracked here for ops.

## Out of scope (YAGNI)

- 2-column form layout; login remember-me / forgot-password; profile/preferences menu items
  (no backend capability — honest-data principle).
- Localizing sample-schema group names (Content/System) and sample field labels — sample
  data is deleted on fork; downstream provides real labels.
- Deleting test articles (user chose to keep); MinIO env fix.
- Full mobile card-layout for DataTable rows.

## Testing & verification

- TDD for behavior: failing unit tests first for `buildBreadcrumb` (settings, empty-label
  guard), `translatedCell` fallback chain, `formatDateTime`; component tests for
  `TheSidebar` collapsed rendering (group icon visible, collapse button content visible,
  drawer excludes collapse button).
- CSS-level fixes (B1-3/4/7, B2-1) verified by live Playwright assertions (computed styles,
  `document.documentElement.scrollHeight === clientHeight`, no page-level horizontal
  scroll at 390px).
- Per-batch gate: vitest green + `pnpm build` (vue-tsc) + live walkthrough of all six pages
  (desktop/mobile × light/dark) + existing 17 live e2e specs green
  (backend on launch-profile port **:5221**, `RateLimiting__Login__Enabled=false`,
  `--workers=1`).
- Existing e2e selectors that touch the sidebar (`aside.sidebar`) must be re-checked after
  B1-1 (nav markup gains icons).

## Environment notes for implementers

- Backend: reuse the running instance on `http://localhost:5221` (plain `dotnet run`,
  launch profile). Do NOT start :5080 or edit the vite proxy.
- Vite: `pnpm dev --host`; dev admin `admin@admin.com` (password in gitignored
  appsettings.Development.json / see memory).
- The Playwright MCP browser carries an AdGuard extension that stalls `file://`-style and
  buffered static HTML loads — audit the live app, not the prototype file, via MCP; the
  prototype is read as source.
