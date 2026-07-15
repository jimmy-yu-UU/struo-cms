# StruoCMS Admin — Frontend Redesign Implementation Prompt

> **Audience:** an AI coding agent working directly in this repository (`frontend/`).
> **Goal:** transform the current functional-but-unstyled admin UI into a **clean, intuitive, beautiful, low-learning-cost, fully responsive** interface, built **PrimeVue-first** on the **PrimeVue Aura** design system.
> **Authoritative constraints:** obey `CLAUDE.md` and the repo coding rules. This document is prescriptive — where it states a token, component, or file, use exactly that unless a stated constraint makes it impossible, in which case stop and flag it.

---

## 0. Role & mission

You are a senior frontend engineer redesigning the **StruoCMS admin SPA** (`frontend/`, Vue 3.5 + TypeScript + PrimeVue 4.5). The **backend, API contract, routing, stores, and business logic are correct and must not change behaviorally.** Your job is a **presentation-layer redesign**: replace ad-hoc markup and leftover starter CSS with a coherent, token-driven, PrimeVue-based design system and re-skin every screen.

**Prime directives**

1. **PrimeVue-first.** Every interactive control and structural surface must be a PrimeVue component unless PrimeVue genuinely has no equivalent (then use a minimal styled element that consumes the same design tokens). No raw `<button>`, `<input>`, `<select>` in shipped screens.
2. **Design-system-driven.** All color, spacing, radius, typography flow from **semantic design tokens** (PrimeVue Aura preset via `definePreset`). Zero hardcoded hex/px for themeable properties.
3. **Low learning cost.** Consistency over cleverness. The same action looks and behaves identically everywhere. A user who learns one screen can operate all of them.
4. **RWD.** Every screen is usable from 360px phone to wide desktop. No horizontal body scroll.
5. **Behavior-preserving.** Do not change API calls, payload shapes, route names, store contracts, validation logic, or permission gating. Re-skin only.
6. **Accessible.** Keyboard-operable, visible focus, labeled controls, `aria-live` for async feedback.

---

## 1. Current state (the gap you are closing)

- `frontend/src/style.css` is **leftover Vite starter template CSS** (hero image, `#next-steps`, `#docs`, `.ticks`, `#app { width: 1126px; text-align: center }`). It actively fights an admin layout. **Delete it entirely** and replace with a token-based global sheet (see §3.6).
- `frontend/src/layouts/AppShell.vue` is a bare `<header>/<nav>/<main>` with a raw `<button>` logout and a `<span>` brand. **Rebuild** (see §4).
- `frontend/src/views/DashboardView.vue` is a placeholder ("arrives in Phase 7b"). **Build a real dashboard** (see §6.3).
- `frontend/src/views/LoginView.vue` is **entirely raw HTML** (`<input>`, `<button>`). **Rebuild with PrimeVue** (see §6.1).
- Media components (`components/media/MediaGrid.vue`, `MediaUploadDropzone.vue`, `FileThumbnail.vue`) and **every field-row wrapper in `ItemForm.vue`** are raw HTML + scoped CSS with hardcoded colors (e.g. `#d33`, `--accent`). **Re-skin against tokens** (see §6.5–6.6, §7).
- PrimeVue usage is uneven: list/nav/form-shell already use `DataTable`, `PanelMenu`, `Tabs`, `SelectButton`, `ConfirmDialog`, `Button`, `InputText`, `Select`, `MultiSelect`, `TreeSelect`, `InputNumber`, `DatePicker`, `Checkbox`, `RadioButton`, `Textarea`, `OrderList`, `Dialog`. Standardize and extend this (see §9).

**What is already good and must be kept:** the schema-driven rendering architecture, the field-type registry (`lib/fieldTypes/registry.ts`), the stores (`authStore`, `schemaStore`, `languageStore`), the API layer (`api/*`), routing (`router/*`), and all `lib/*` pure helpers. Re-skin their *templates*; do not rewrite their *logic*.

---

## 2. Non-negotiable constraints

| Constraint | Detail |
|---|---|
| Framework | Vue 3.5 `<script setup lang="ts">`, Composition API, TypeScript strict. |
| UI library | **PrimeVue 4.5** + `@primeuix/themes` (Aura) + `primeicons` 7. No new UI libraries. No PrimeFlex, no Tailwind unless already present (it is not — do **not** add it; use tokens + scoped CSS / small utility classes). |
| Verification | `pnpm build` (`vue-tsc -b && vite build`) **must pass** — this is the type gate, stricter than `pnpm test`. `pnpm test` (vitest) must stay green. Existing component tests assert structure/behavior; if a re-skin changes DOM, update the test to match the new markup **only when the behavior is unchanged** — never weaken a behavioral assertion to make a test pass. |
| API envelope | Responses are `{ success: true, data, meta? }` or `{ success: false, error: { code, message, details? } }`. `apiClient` already unwraps and throws `ApiError { status, code?, details? }`. Consume errors via `code`/`message`, never by string-matching. |
| Immutability | Follow repo rule: never mutate props/store state in place; emit events / use store actions. |
| i18n | UI chrome may stay English for now, but **content is multilingual**: per-field `translatable` values live under `item.translations[localeCode][fieldName]`. Preserve the per-locale editing model. |
| RBAC | Render only what the user may do. Use `authStore.canRead/canWrite/canDelete(collection)` and `isSuperAdmin`. Hide (preferred) or disable forbidden actions; never render an action that will 403. |
| No secrets, no console.log | Per repo security/style rules. |

---

## 3. Design foundation

### 3.1 Theme & tokens

Create `frontend/src/theme/preset.ts` that extends Aura via `definePreset`, and wire it in `main.ts`.

```ts
// frontend/src/theme/preset.ts
import { definePreset } from '@primeuix/themes'
import Aura from '@primeuix/themes/aura'

export const StruoPreset = definePreset(Aura, {
  semantic: {
    primary: {
      // sky/indigo family — neutral, professional, high legibility.
      50:'{sky.50}',100:'{sky.100}',200:'{sky.200}',300:'{sky.300}',400:'{sky.400}',
      500:'{sky.500}',600:'{sky.600}',700:'{sky.700}',800:'{sky.800}',900:'{sky.900}',950:'{sky.950}',
    },
    colorScheme: {
      light: { surface: { /* slate ramp */ 0:'#ffffff',50:'{slate.50}',100:'{slate.100}',200:'{slate.200}',300:'{slate.300}',400:'{slate.400}',500:'{slate.500}',600:'{slate.600}',700:'{slate.700}',800:'{slate.800}',900:'{slate.900}',950:'{slate.950}' } },
      dark:  { surface: { 0:'#ffffff',50:'{slate.50}',100:'{slate.100}',200:'{slate.200}',300:'{slate.300}',400:'{slate.400}',500:'{slate.500}',600:'{slate.600}',700:'{slate.700}',800:'{slate.800}',900:'{slate.900}',950:'{slate.950}' } },
    },
  },
})
```

```ts
// main.ts (replace the current Aura registration)
import { StruoPreset } from './theme/preset'
import ToastService from 'primevue/toastservice' // NEW — see §5

app.use(PrimeVue, {
  theme: {
    preset: StruoPreset,
    options: {
      darkModeSelector: '.app-dark', // class-based, user-toggleable (NOT system-forced)
      cssLayer: false,
    },
  },
})
app.use(ToastService)      // required for the toast convention
app.use(ConfirmationService) // already present
```

> **Primary color is a decision point.** Default = **sky/indigo**. If the product owner supplies a brand hex, replace the `primary` ramp with a generated 50–950 scale of that hex. State the chosen value at the top of `preset.ts`.

### 3.2 Dark mode

- Persist the user's choice in `localStorage` (`struo.theme` = `light|dark`); default to `system` on first load (read `matchMedia('(prefers-color-scheme: dark)')`), then let an explicit toggle override.
- Toggle by adding/removing `.app-dark` on `document.documentElement`.
- Provide a small composable `frontend/src/composables/useTheme.ts` (`isDark`, `toggle()`, `set(mode)`), used by the topbar theme toggle (§4.3).

### 3.3 Color usage rules

- **One accent** (primary). Everything else is surface/neutral.
- Color communicates **state, not decoration**: `success` (create/save/restore ok), `warn` (destructive-but-recoverable, e.g. soft delete), `danger` (permanent, e.g. purge), `info`, `contrast`. Use PrimeVue `severity` props (`Button`, `Tag`, `Message`, `Badge`) — never hand-picked hex.
- Never hardcode a hex in a component. Replace existing hardcoded colors (`#d33`, `--accent`, `--code-bg`, etc.) with tokens (`var(--p-red-500)`, `var(--p-primary-color)`, `var(--p-surface-*)`, `var(--p-content-border-color)`, `var(--p-text-color)`, `var(--p-text-muted-color)`).

### 3.4 Spacing, rhythm, radius

- 4px base scale (4/8/12/16/24/32/48). Prefer PrimeVue's spacing tokens where a component exposes them; for custom layout use these steps consistently.
- **Comfortable density** by default (generous padding, breathing room). Data tables may use PrimeVue `size="small"` where rows are dense. (If the owner asks for tighter global density, set PrimeVue `ripple:false` is unrelated — instead switch component `size` props to `small` app-wide.)
- Radius: `md` (via `--p-content-border-radius`). Consistent everywhere.
- Borders: minimal. Prefer surface elevation / subtle background steps over heavy 1px grids.

### 3.5 Typography

- System UI stack (`system-ui, 'Segoe UI', Roboto, sans-serif`). No web-font downloads.
- A small type scale: page title (`1.5rem/600`), section title (`1.125rem/600`), body (`0.95rem/400`), meta/caption (`0.8rem/500`, muted). Do **not** carry over the starter's `h1 { font-size: 56px }`.
- Line length capped in content areas (`max-width` on prose/forms) for readability.

### 3.6 Global stylesheet replacement

**Delete** all current content of `frontend/src/style.css`. Replace with a minimal reset + token-consuming base:

- `*,*::before,*::after { box-sizing: border-box }`, `body { margin:0 }`.
- Base font/color from tokens: `body { font-family: …; color: var(--p-text-color); background: var(--p-content-background) }` (or `--p-surface-50` for the app canvas).
- Remove `#app { width:1126px; text-align:center }` — the shell (§4) owns layout; `#app` should be `min-height:100dvh; display:flex`.
- Utility helpers allowed (few, semantic): `.text-muted`, `.stack-*` (vertical rhythm), `.page` (content max-width + padding). Keep this file small.

### 3.7 Motion

- Subtle, fast (120–180ms), ease-out. Use PrimeVue's built-in transitions (drawer, dialog, menu). No decorative animation. Respect `prefers-reduced-motion`.

---

## 4. Layout system

### 4.1 AppShell (`frontend/src/layouts/AppShell.vue`) — rebuild

A three-region shell:

```
┌──────────────────────────────────────────────┐
│ Topbar (fixed, 56px)                           │  ← breadcrumb · spacer · lang · theme · user
├───────────┬────────────────────────────────────┤
│ Sidebar   │ Content (scrolls independently)     │
│ (fixed,   │  ┌──────────────────────────────┐   │
│  collaps- │  │ Page header (title + actions)│   │
│  ible)    │  ├──────────────────────────────┤   │
│           │  │ <router-view/>               │   │
│           │  └──────────────────────────────┘   │
└───────────┴────────────────────────────────────┘
```

- Mount **once, globally**: `<Toast/>` and `<ConfirmDialog/>` live in the shell so every screen can use `useToast()`/`useConfirm()`.
- Sidebar width: ~260px expanded, ~72px collapsed (icon-only). State persisted in `localStorage` (`struo.sidebar`).
- Content region has its own scroll; topbar and sidebar stay fixed.

### 4.2 Sidebar / navigation

- Reuse the existing `CollectionNav.vue` + `lib/buildNav.ts` **logic** (RBAC filtering, `group` grouping, Media entry gating). Re-skin the presentation.
- Use PrimeVue navigation primitives. Options, in order of preference:
  - **`PanelMenu`** (already used) restyled to feel like a sidebar (flush, no card chrome, active-route highlight), **or**
  - a custom list of `router-link`s styled as nav items with an active state — acceptable if PanelMenu's accordion behavior is unwanted. Pick one and be consistent.
- Each item: leading `primeicon` + label. Collapsed sidebar shows icon only with a `Tooltip` on hover.
- Group headers: small, muted, uppercase caption; hidden when sidebar collapsed.
- **Active state** must reflect the current route (highlight the collection currently open).
- Brand/logo at the top of the sidebar (text "StruoCMS" is fine; leave room for a logo slot).
- Schema-load-error branch: replace the raw `<div>/<button>` with an inline PrimeVue `Message severity="error"` + a `Button` retry.

### 4.3 Topbar

- Left: **breadcrumb** (PrimeVue `Breadcrumb`) reflecting `Home / <Collection> / <New|Edit>`. Derive from route + schema label.
- Right cluster:
  - **Language switcher** — PrimeVue `Select` bound to `languageStore` (shows languages; marks default). Controls the content locale used by list/form (do not change UI chrome language).
  - **Theme toggle** — `Button` (icon `pi-moon`/`pi-sun`, `text rounded`) calling `useTheme().toggle()`.
  - **User menu** — `Avatar` (initials) + `Menu` (popup) with the user email (from `authStore`), a "Settings" link (§6.9), and **Log out** (calls `authStore.logout()` → route `login`). Replaces the current raw logout button.
- On mobile: collapse breadcrumb to just the current page title; move language switcher into the user menu or an overflow menu; keep theme toggle + hamburger visible.

### 4.4 Content area & page-header pattern

Every screen renders inside a consistent **page header** component you create — `frontend/src/components/layout/PageHeader.vue`:

- Props: `title`, optional `subtitle`, `#actions` slot (right-aligned primary/secondary buttons), optional `#breadcrumbEnd`.
- Used by every view so titles, spacing, and action placement are identical everywhere.
- Content max-width for form-centric pages (~880px); full-width for tables.

### 4.5 Responsive behavior & breakpoints

Single breakpoint system (document at top of the global sheet):

| Token | Width | Behavior |
|---|---|---|
| `sm` | < 640px | Sidebar → off-canvas `Drawer` (hamburger in topbar). Tables → PrimeVue responsive stacked layout or horizontal scroll container. Form tabs full-width. Actions collapse into overflow menus. |
| `md` | 640–1024px | Sidebar collapsible to icon rail; two-column form fields become one column. |
| `lg` | > 1024px | Full shell; multi-column field grid where sensible. |

- No horizontal body scroll at any width. Wide tables scroll inside their own `overflow-x:auto` container.
- Touch targets ≥ 40px on mobile.

---

## 5. Cross-cutting interaction conventions (apply on every screen)

- **Loading:** use **`Skeleton`** for content regions (table rows, form, media grid, dashboard cards) — not bare spinners. Buttons use their `loading` prop during async actions. First app load may show a centered `ProgressSpinner` only for the initial auth/schema resolve.
- **Empty states:** a centered block — muted icon + one-line explanation + primary action (e.g. "No records yet" + "New <Item>"). Create `frontend/src/components/layout/EmptyState.vue` and reuse it (list empty, trash empty, media empty, search-no-results).
- **Errors:**
  - Field-level: inline message under the control (PrimeVue `Message`/small error text), driven by validation + `ApiError.details`.
  - Operation-level: **`Toast`** (`severity:'error'`) with `error.message`. Never fail silently; never leak stack traces.
  - Page-level (load failed / 403 / 404): a centered `Message` or dedicated state block with a retry or back action.
- **Success feedback:** **`Toast`** (`severity:'success'`) on every create / update / delete / restore / purge / upload. Short, specific ("Article saved", "Moved to trash", "Restored").
- **Destructive confirmation:** **`ConfirmDialog`** (`useConfirm`) before every destructive action. Match severity to reversibility: soft-delete → `warn`; permanent purge/revert-overwrite → `danger`, with the item name in the message. (Reuse existing `lib/deleteAction.ts` copy.)
- **RBAC rendering:** gate every action with `authStore` checks. Hidden > disabled for actions the user can never perform; disabled (with tooltip) for context-dependent unavailability.
- **Forms:** consistent label placement (top-aligned labels), required marker (`*` in `--p-red-500`), help text (`helpText` → muted caption under the control, or a `pi-info-circle` with `Tooltip`), inline validation on submit, Save button shows `loading`. Keep the existing default-locale-focus-on-error behavior.
- **Keyboard & focus:** visible focus rings (PrimeVue default), logical tab order, `Esc` closes dialogs/drawers, `Enter` submits forms.

---

## 6. Screen specifications

### 6.1 Login (`views/LoginView.vue`) — rebuild from raw HTML

- Centered **`Card`** on the app canvas (surface-50 background), max-width ~380px, vertically centered, responsive padding.
- Brand mark + "StruoCMS Admin" title.
- **`InputText`** (email, `type=email`, autofocus) with `FloatLabel` or top label; **`Password`** component (`:feedback="false"`, `toggleMask`) for the password.
- Submit **`Button`** full-width with `loading` during submit.
- Error → inline `Message severity="error"` (from `ApiError.message`), `role="alert"`.
- Preserve logic: `auth.login(email,password)` → redirect `dashboard`.
- Fully responsive; looks intentional on mobile.

### 6.2 App shell / navigation

Per §4. Deliverables: rebuilt `AppShell.vue`, re-skinned `CollectionNav.vue`, new `PageHeader.vue`, new topbar sub-components (`components/layout/AppTopbar.vue`, `AppSidebar.vue` as needed — keep files focused, <400 lines each per repo rule).

### 6.3 Dashboard (`views/DashboardView.vue`) — build real content

Purpose: an at-a-glance landing after login. Keep it simple and genuinely useful (no fake charts).

- Greeting + current user email.
- A **grid of collection cards** (PrimeVue `Card`): one per collection the user can read, showing label, icon, and a **record count** (`meta.total` from an `itemsApi.list(name,{page:0,rows:1})` call — do one lightweight count query per card, or a batched call if available). Card click → that collection's list.
- Quick actions: "New <X>" buttons for collections the user can write.
- If the user can read nothing: a friendly empty state.
- Fully responsive card grid (`auto-fill, minmax(220px,1fr)`), skeletons while counts load.

### 6.4 Collection list (`views/CollectionListView.vue`) — re-skin

Keep all server-driven `DataTable` logic (lazy paginate/sort, 300ms debounced search, locale, `deleted:'only'` for trash). Re-skin:

- Wrap in `PageHeader` (title = collection label; `#actions` = **New** button, shown only if `canWrite`).
- Toolbar row above the table: search **`InputText`** with a leading `pi-search` (`IconField`/`InputIcon`), and the **Active/Trash `SelectButton`** (shown only when `meta.softDelete && canDelete`). On mobile the toolbar wraps cleanly.
- `DataTable`: `stripedRows`, `size` comfortable, `rowHover`, `paginator`, `rows` (25), `rowsPerPageOptions`. Columns from `selectListColumns(meta)` via `formatCell`. Sensible column widths; long text truncates with ellipsis + `Tooltip`.
- Cell formatting: booleans → `Tag`/icon (Yes/No), dates → localized, relations/options → readable labels (already handled by `formatCell`; keep). Consider a leading identifier column emphasized.
- **Row actions** column (only if `canDelete`): use an overflow `Menu` (`pi-ellipsis-vertical`) OR inline icon buttons — active mode: Edit (row click already navigates; keep) + Delete (`severity:danger`, `text`); trash mode: Restore + Delete permanently (`danger`). Use `@click.stop`. Match existing confirm flows.
- Row click → edit (disabled in trash). Keyboard-accessible rows.
- Loading → skeleton rows; empty → `EmptyState`; error → inline `Message role="alert"`.

### 6.5 Item form (`views/ItemFormView.vue` + `components/ItemForm.vue`) — flagship re-skin

This is the most complex and highest-value screen. Keep all logic (schema+languages load, `blankItemForm`/`parseItemToForm`, `validateItem`, `buildItemPayload`, create/update/delete, deep relation load). Re-skin structure:

- **Container (`ItemFormView`)**: `PageHeader` (title `New <Label>` / `Edit <Label>`; `#actions` = Delete button, shown only if editing && `canDelete`, `severity:danger outlined`). Guard branches (loading / no-collection / not-found / no-permission) render as centered state blocks (`Skeleton` for loading; `Message`/`EmptyState` for the rest), not raw `<p>`.
- **Form body (`ItemForm`)** wrapped in a `Card` (or plain paneled surface), content max-width ~880px:
  - **Shared (non-translatable) fields** first, in a responsive field grid (1 col mobile, 2 col ≥ md where fields are short; full-width for rich/complex fields).
  - **Relations** section with a clear subheading (PrimeVue `Divider` with text, or a section caption).
  - **Translatable fields** grouped under **`Tabs`** (one `Tab` per locale, default marked with a small `Tag`/`Badge` or `*`). Keep lazy per-locale rendering and the "jump to default locale on validation error" behavior. Consider a per-tab "has errors" indicator (`Badge severity="danger"`) so users find invalid locales fast.
  - Every field row: top label + required `*` + optional help caption + the field component (§7) + inline error `Message`.
  - **Actions bar** (sticky at the bottom of the form on desktop; stacked on mobile): **Cancel** (`text`, back to list) + **Save** (`primary`, `loading` while submitting; hidden if no write permission). On dirty-state, consider a "You have unsaved changes" guard on navigation (optional, nice-to-have).
- Field-level errors from `validateItem` and from `ApiError.details` map to the right control.

### 6.6 Media library (`views/MediaLibraryView.vue` + `components/media/*`) — re-skin

- `PageHeader` (title "Media"; `#actions` = an Upload button that triggers the dropzone/file dialog).
- **Upload** (`MediaUploadDropzone.vue`): re-skin the drag/drop zone against tokens (dashed `--p-content-border-color`, primary tint on drag-over, no hardcoded `#d33`). Per-file progress uses `ProgressBar` + status `Tag` (uploading/done/error). Keep parallel upload logic.
- **Grid** (`MediaGrid.vue`): responsive tile grid (`auto-fill, minmax(160px,1fr)`), each tile a `Card`-like surface with `FileThumbnail` + filename (truncated + `Tooltip`) + size caption. Hover reveals overlay actions (Edit/Delete) as icon `Button`s — replace the current flat action list. Selection modes (used by relation file pickers) keep working; selected tile uses a primary ring/`Badge`, driven by tokens.
- `FileThumbnail.vue`: image tiles use `<img loading="lazy">`; non-image → a typed **file chip** (icon by content-type + name). Broken image → graceful chip fallback. Consistent aspect ratio.
- Delete → confirm + toast; keep `filesApi` calls.

### 6.7 Trash management

Trash is a **mode of the collection list** (existing `SelectButton`), not a separate route — keep that model. Ensure the trash mode is visually distinct (e.g. a subtle `Tag` "Trash" near the title, muted row styling) and its actions (Restore / Delete permanently) are clearly differentiated (Restore = `success/secondary`, Purge = `danger`, with a strong `danger` confirm naming the record). Empty trash → `EmptyState` ("Trash is empty").

### 6.8 Revision history & revert — new screen (has a backend dependency)

Backend supports revisions (opt-in `[CmsCollection(Revisions=true)]`; REST `x/{id}/revisions`, `.../revisions/{no}`, `.../revert`; GraphQL `xRevisions`/`revertX`). **The frontend `CollectionMeta` type does not yet carry a `revisions` flag**, and `/api/schema` does not emit one.

- **Dependency (flag it, do not silently fake it):** the schema endpoint must expose `revisions: boolean` on the collection descriptor. Add `revisions?: boolean` to `types/schema.ts`. If backend work is required to emit it, **stop and report this as a prerequisite** — do not hardcode which collections have revisions.
- **UX:** on the item form, when `meta.revisions` is true and editing, add a **"History"** action (topbar of the form, or a right-side `Drawer`). Opening it shows a `DataTable`/`Timeline` of revisions (revision no., timestamp, author if available). Each row: **View** (read-only snapshot preview in a `Dialog`) and **Revert** (`ConfirmDialog` `danger` — "This creates a new revision that restores revision #N"). Revert calls the existing REST/GraphQL revert endpoint and reloads the form.
- If no revisions exist yet: empty state inside the drawer.

### 6.9 Settings / profile — new light screen

A minimal settings page (route `settings`, linked from the user menu):

- **Appearance**: theme mode (`SelectButton`: System / Light / Dark) bound to `useTheme`.
- **Content language**: default working locale (bound to `languageStore`).
- **Account**: display the current user email and super-admin status (read-only for now; no password change unless backend supports it — if not, omit).
- Keep it a single `Card` with sections; do not over-build. YAGNI.

### 6.10 Error / 403 / 404 states

- Route-level not-found and no-permission: a clean centered state block (icon + message + "Back to dashboard"). Reuse `EmptyState` styling. The list/form already detect `NOT_FOUND`/no-access — route those into this shared presentation.

---

## 7. Field-type rendering matrix (all 34 `FieldInterface` values)

Field components live in `components/fields/*` and are resolved by `lib/fieldTypes/registry.ts` via `FieldInput.vue`. **Keep the registry and resolution mechanism.** Re-skin each component to (a) use the specified PrimeVue control, (b) fill width by default, (c) support `disabled`/`readOnly`, (d) surface validation state, (e) consume tokens only.

Legend: **Current** = PrimeVue control already imported; **Target** = what the redesigned component should use; **Upgrade** = a fidelity improvement to make.

| Interface(s) | Component | Current → Target PrimeVue control | Notes / Upgrade |
|---|---|---|---|
| `text`, `slug`, `email`, `url`, `color`, `phone` | TextField | `InputText` | Add `type`/`inputmode` per interface (email/url/tel). `color` → `ColorPicker` **+** `InputText` combo. `slug` → prefix icon + monospace. Respect `maxLength`. |
| `password` | TextField | `InputText` → **`Password`** | `toggleMask`, `:feedback="false"`. **Upgrade** from plain text input. |
| `textarea` | TextareaField | `Textarea` | `autoResize`, sensible rows, `maxLength` counter. |
| `markdown` | TextareaField | `Textarea` | Monospace; optional split preview later (YAGNI now — keep textarea). |
| `code` | TextareaField | `Textarea` | Monospace, `spellcheck=false`, `autoResize`. |
| `richText` | RichTextField | TipTap (custom) + `Dialog`/`InputText` (link) | Re-skin the toolbar as a token-based button group (`Button text` + `pi-*` icons), sticky toolbar, bordered editor surface using `--p-content-*`. Keep sanitizer + all marks (tables/align/color/sub-sup). |
| `number` | NumberField | `InputNumber` | `showButtons` optional; respect min/max if present. |
| `slider` | NumberField | `InputNumber` → **`Slider`** (+ value readout) | **Upgrade**: a slider is expected. Pair `Slider` with a small numeric display. |
| `rating` | NumberField | `InputNumber` → **`Rating`** | **Upgrade**: star rating. |
| `boolean` | BooleanField | `Checkbox` → **`ToggleSwitch`** | **Upgrade**: a switch reads better for on/off. (Keep `Checkbox` for `checkbox`.) |
| `checkbox` | BooleanField | `Checkbox` (binary) | Single checkbox with label. |
| `date` | DateField | `DatePicker` | `dateFormat` localized; icon trigger. |
| `time` | DateField | `DatePicker` → `DatePicker timeOnly` | **Upgrade**: time-only mode. |
| `dateTime` | DateField | `DatePicker showTime` | **Upgrade**: `showTime hourFormat`. |
| `select` | SelectField | `Select` | `filter` when options are many; `showClear` if not required. |
| `radio` | RadioField | `RadioButton` (group) | Vertical/inline group; label per option. |
| `divider` | DividerField | → **`Divider`** | Use PrimeVue `Divider` (optionally with label). Presentational only. |
| `file`, `image` | FileField | `FilePicker` (`Dialog`+media grid) | Re-skin picker dialog + selected preview (thumbnail for image, chip for file) + clear button. Scalar `Guid?`. |
| `multiSelect` | MultiSelectField | `MultiSelect` | `filter`, `display="chip"`, `showClear`. |
| `checkboxGroup` | CheckboxGroupField | `Checkbox` (multiple) | Token-based group layout; wraps responsively. |
| `tags` | TagsField | `InputText`+`Button` → **`AutoComplete multiple`** | **Upgrade**: free-text chips via `AutoComplete` (`multiple`, `typeahead`), or PrimeVue `Chips` if retained. Preserve `{value,label?}[]` serialization. |
| `json` | JsonField | `Textarea` | Monospace; validate JSON on blur, inline error; pretty-print helper button (optional). |
| `keyValue` | KeyValueField | `InputText`+`Button` | Re-skin as paired key/value rows with add/remove icon `Button`s; consistent spacing. |
| `repeater` | RepeaterField | `Button` (+ nested `FieldInput`) | Re-skin each row as a bordered/elevated sub-panel with drag handle (optional), collapse, and a `danger text` remove button; "Add item" primary `Button`. Recurse via `FieldInput` for sub-fields. |
| `files` | FilesField | `OrderList` + `Dialog` | Keep ordered gallery (`OrderList`) re-skinned; thumbnails as items; add-from-media dialog; reorder + remove. `string[]`. |
| `hidden`, `uuid` | ReadonlyField | (read-only display) | Muted, non-editable; `uuid`/`hidden` shown as caption or omitted from the visible form as appropriate. |

**Relations** (rendered by `RelationInput` → `RelationPicker`/`RelatedList`, driven by `RelationMeta.interface`):

| Relation interface | Kind | Control | Notes |
|---|---|---|---|
| `dropdown` | manyToOne | `Select` (filterable) | Single related item; `showClear`; label via `displayTemplate`/`resolveDisplayLabel`. |
| `tagSelect` | manyToMany | `MultiSelect` (chips, filter) | Multiple related items as chips. |
| `treeSelect` | (hierarchical) | `TreeSelect` | Uses `buildRelationTree`; supports self-referencing `excludeId`. |
| `relatedList` | oneToMany | `DataTable` (read/manage) | Reverse FK list; re-skin as a compact embedded table with add/detach where permitted. |

---

## 8. Component inventory (standardize on these PrimeVue components)

Structure: `Card`, `Divider`, `Tabs/TabList/Tab/TabPanels/TabPanel`, `Drawer`, `Dialog`, `Toolbar`, `Breadcrumb`, `Menu`, `PanelMenu`, `Avatar`, `Tag`, `Badge`, `Skeleton`, `ProgressBar`, `ProgressSpinner`, `Message`, `Toast`, `ConfirmDialog`, `Tooltip`, `IconField/InputIcon`.
Inputs: `InputText`, `Password`, `Textarea`, `InputNumber`, `Slider`, `Rating`, `Select`, `MultiSelect`, `TreeSelect`, `Checkbox`, `RadioButton`, `ToggleSwitch`, `SelectButton`, `DatePicker`, `AutoComplete`, `ColorPicker`, `OrderList`, `FileUpload` (optional for media), `Button`.
Data: `DataTable`, `Column`, `Paginator`, `Timeline` (revisions).

Register components locally per-view (tree-shakeable) as the codebase already does; do not switch to global registration.

---

## 9. File / module plan

**Create**
- `frontend/src/theme/preset.ts` — Aura-extended preset.
- `frontend/src/composables/useTheme.ts` — dark-mode toggle + persistence.
- `frontend/src/components/layout/PageHeader.vue`, `EmptyState.vue`, `AppTopbar.vue`, `AppSidebar.vue` (split from AppShell as needed; keep each < 400 lines).
- `frontend/src/views/SettingsView.vue` (+ route `settings`).
- Revision history UI (drawer/component) — gated on the schema `revisions` flag dependency (§6.8).

**Modify (re-skin templates + styles; preserve script logic)**
- `layouts/AppShell.vue`, `components/CollectionNav.vue`
- `views/LoginView.vue`, `DashboardView.vue`, `CollectionListView.vue`, `ItemFormView.vue`, `MediaLibraryView.vue`
- `components/ItemForm.vue`
- `components/media/MediaGrid.vue`, `MediaUploadDropzone.vue`, `FileThumbnail.vue`
- all `components/fields/*.vue` (per §7)
- `main.ts` (preset + `ToastService`)
- `types/schema.ts` (add `revisions?: boolean`)

**Delete / replace**
- `frontend/src/style.css` contents (replace with token base, §3.6).
- `components/HelloWorld.vue`, `assets/hero.png`, `assets/vue.svg`, `assets/vite.svg` if unreferenced after cleanup (verify with a search first).

---

## 10. Accessibility & RWD acceptance criteria

- [ ] Every interactive element is reachable and operable by keyboard; visible focus ring everywhere.
- [ ] All form controls have associated labels; required state announced; errors linked via `aria-describedby`.
- [ ] Async results announced (`Toast` is `aria-live`; page state blocks use `role="status"`/`role="alert"`).
- [ ] Color is never the sole carrier of meaning (pair with icon/text). Contrast ≥ WCAG AA in both themes.
- [ ] No horizontal body scroll at 360/768/1024/1440px. Sidebar becomes a drawer < 640px.
- [ ] Touch targets ≥ 40px on mobile.
- [ ] `prefers-reduced-motion` honored.
- [ ] Dark mode fully styled (no unthemed white/black patches); toggle persists across reloads.

---

## 11. Definition of done (self-check before declaring complete)

- [ ] `pnpm build` passes (vue-tsc + vite) — **the hard gate**.
- [ ] `pnpm test` (vitest) green; any test touched only for markup changes, never to hide a behavior regression.
- [ ] No raw `<button>/<input>/<select>` in shipped screens (grep to confirm); no hardcoded hex for themeable properties (grep `#[0-9a-fA-F]{3,6}` in `src`, justify any remaining).
- [ ] `style.css` starter cruft gone; `#app` no longer fixed-width/centered.
- [ ] Every screen in §6 re-skinned; every field in §7 re-skinned; RBAC gating intact.
- [ ] Toast + ConfirmDialog mounted once globally; every mutation toasts; every destructive action confirms.
- [ ] Light **and** dark verified; RWD verified at the four widths above.
- [ ] No behavioral change to API calls, payloads, routes, stores, or validation (diff review confirms template/style-only changes in logic files).
- [ ] Run the app and drive the core flows (login → dashboard → list → create/edit an item with several field types + a relation + i18n tab → upload media → soft-delete → restore) — capture that they work.

---

## 12. Out of scope / explicit non-goals

- No backend/API changes **except** the `/api/schema` `revisions` flag (which must be flagged as a prerequisite, not implemented blindly — §6.8).
- No new UI framework, no PrimeFlex/Tailwind.
- No content-model / field-type additions.
- No i18n of the admin chrome itself (English UI labels are acceptable this pass).
- No analytics/charting on the dashboard beyond simple counts.

---

## 13. How to work

1. Land the **foundation first** (§3–§5: preset, theme composable, global sheet, AppShell, PageHeader/EmptyState, Toast wiring). Verify build + a screen renders.
2. Then re-skin screen-by-screen in this order: Login → Shell/Nav → Dashboard → Collection list → **Item form + fields** (largest) → Media → Trash → Settings → Revisions (if flag available) → error states.
3. Keep files focused and small (repo rule: 200–400 lines typical, 800 max). Commit in logical slices.
4. After each screen, run `pnpm build` and the relevant tests. Fix the type gate immediately.
5. When you hit the `revisions` flag dependency, **stop and report** rather than guessing.
