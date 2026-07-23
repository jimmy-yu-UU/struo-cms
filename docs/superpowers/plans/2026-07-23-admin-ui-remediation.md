# Admin UI Remediation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the 7 verified admin-UI bugs and close the highest-impact visual gaps vs the design prototype, per `docs/superpowers/specs/2026-07-23-admin-ui-remediation-design.md`.

**Architecture:** Two batches on one work branch. Batch 1 (Tasks 1–7) = behavioral fixes, Batch 2 (Tasks 8–14) = visual remediation. Almost everything is frontend (Vue 3 + PrimeVue + `frontend/src/assets/theme.css`); Task 9 adds `email`/`name` to the backend `/api/auth/me` response (only backend change). Each batch ends with a live-verification gate.

**Tech Stack:** Vue 3 `<script setup>` + TypeScript, PrimeVue 4 (Aura preset via `@primeuix/themes`), vitest + @vue/test-utils, Playwright e2e, .NET 10 + SqlSugar (Task 9 only).

## Global Constraints

- Design tokens source of truth: `docs/struo-cms-frontend-design/brand-spec.md` (light accent = sky-600, dark accent = sky-400; accent appears ≤2× per screen).
- The prototype `docs/struo-cms-frontend-design/struocms-admin-prototype.html` is a VISUAL reference only — never copy its code verbatim.
- Frontend gate per batch: `pnpm test -- --run` green AND `pnpm build` (vue-tsc) clean, run in `frontend/`.
- Backend (Task 9): `dotnet test` green from repo root.
- Live verification: reuse the backend already running on **http://localhost:5221** (plain `dotnet run` in `src/Struo.Api`, launch profile). NEVER start :5080, NEVER edit the vite proxy. Vite: `pnpm dev --host`. Dev admin `admin@admin.com` / `admin#90196080`. For the 17-spec e2e run the backend needs env `RateLimiting__Login__Enabled=false` and Playwright needs `--workers=1`.
- Existing e2e specs are a contract: when a selector breaks because of a planned UI change, update the selector to role/aria-based lookup — never weaken an assertion.
- No new packages. No changes to `samples/` entities. Commit after every task (conventional commits).

---

## Batch 1 — behavioral fixes

### Task 1: Branch + theme.css foundation fixes (body reset, utility ordering, scrim, overflow)

**Files:**
- Modify: `frontend/src/assets/theme.css`

CSS is not unit-testable in jsdom; this task's proof is the Batch-1 live gate (Task 7). Keep edits exactly as below.

- [ ] **Step 1: Create the work branch**

```bash
git checkout -b admin-ui-remediation main
```

- [ ] **Step 2: Add the body reset**

In `frontend/src/assets/theme.css`, directly after `html { font-family: var(--font); }` add:

```css
body { margin: 0; } /* missing reset caused a permanent 16px overflow + page-level scrollbar */
```

- [ ] **Step 3: Add the overlay token and use it for the scrim**

In the `:root` block add (after `--shadow-3`):

```css
  --overlay: rgb(0 0 0 / .5);
```

In the `.app-dark` block add (after `--shadow-3`):

```css
  --overlay: rgb(0 0 0 / .65);
```

Change the `.scrim` rule's background to use it:

```css
.scrim { position: fixed; inset: 56px 0 0 0; background: var(--overlay); z-index: 35; border: none; padding: 0; }
```

- [ ] **Step 4: Contain horizontal overflow at the content layer**

Change the `.content` rule to:

```css
.content { grid-area: main; overflow-y: auto; overflow-x: clip; background: var(--surface); }
```

- [ ] **Step 5: Move visibility utilities to the end of the file**

Delete these two existing rules where they currently sit:
- `.only-mobile { display: none; }` (standalone rule, currently line ~101)
- `.only-mobile { display: inline-flex; }` (inside the `@media (max-width: 1023px)` block, currently line ~110)

Append at the very end of `theme.css`:

```css
/* ---- visibility utilities — keep LAST in this file: they must win source-order ties
   against same-specificity display rules like .icon-btn / .nav-item (the desktop
   hamburger bug was exactly this ordering trap). ---- */
.only-mobile { display: none; }
@media (max-width: 1023px) {
  .only-mobile { display: inline-flex; }
  .only-desktop { display: none; }
}
```

- [ ] **Step 6: Build check**

Run: `cd frontend && pnpm build`
Expected: clean vue-tsc + vite build.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/assets/theme.css
git commit -m "fix(admin-ui): body reset, overlay token, overflow clip, visibility-utility ordering"
```

### Task 2: sidebarStore.expand()

**Files:**
- Modify: `frontend/src/stores/sidebarStore.ts`
- Test: `frontend/src/stores/sidebarStore.test.ts`

**Interfaces:**
- Produces: `useSidebarStore().expand(): void` — sets `collapsed = false` and persists `'expanded'` to `localStorage['struo.sidebar']`; no-op when already expanded. Task 3 calls it.

- [ ] **Step 1: Write the failing tests** (append to the existing `describe` in `sidebarStore.test.ts`)

```ts
it('expand() un-collapses and persists', () => {
  const s = useSidebarStore()
  s.toggleCollapse() // -> collapsed
  s.expand()
  expect(s.collapsed).toBe(false)
  expect(localStorage.getItem('struo.sidebar')).toBe('expanded')
})

it('expand() is a no-op when already expanded', () => {
  const s = useSidebarStore()
  localStorage.setItem('struo.sidebar', 'sentinel')
  s.expand()
  expect(s.collapsed).toBe(false)
  expect(localStorage.getItem('struo.sidebar')).toBe('sentinel') // untouched
})
```

(Match the file's existing setup — it already uses `setActivePinia(createPinia())` + `localStorage.clear()` in `beforeEach`.)

- [ ] **Step 2: Run to verify failure**

Run: `cd frontend && pnpm test -- --run src/stores/sidebarStore.test.ts`
Expected: FAIL — `expand is not a function`.

- [ ] **Step 3: Implement** — add to the `actions` block of `sidebarStore.ts`:

```ts
expand(): void {
  if (!this.collapsed) return
  this.collapsed = false
  try {
    localStorage.setItem('struo.sidebar', 'expanded')
  } catch {
    /* localStorage unavailable — state still applied in-memory */
  }
},
```

- [ ] **Step 4: Run to verify pass**

Run: `cd frontend && pnpm test -- --run src/stores/sidebarStore.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/stores/sidebarStore.ts frontend/src/stores/sidebarStore.test.ts
git commit -m "feat(admin-ui): sidebarStore.expand() for collapsed-group auto-expand"
```

### Task 3: Sidebar collapse fix (visible expand button, group icons, collapsed behavior)

**Files:**
- Modify: `frontend/src/components/shell/TheSidebar.vue`
- Modify: `frontend/src/assets/theme.css`
- Test: `frontend/src/components/shell/TheSidebar.test.ts`

**Interfaces:**
- Consumes: `useSidebarStore().expand()` from Task 2.
- Produces: collapse button markup `<i class="pi pi-angle-left collapse-chev">` + class `only-desktop` on `.collapse-btn`; group headers gain `<i class="nav-icon pi pi-folder">`. The Batch gates + e2e rely on `aside.sidebar` still being the nav landmark (unchanged).

- [ ] **Step 1: Write the failing tests** (append inside the existing `describe('TheSidebar')`)

```ts
it('collapse button keeps a visible chevron class that is never nav-chev', () => {
  seed(true, {})
  const wrapper = mountSidebar()
  const btn = wrapper.find('button.collapse-btn')
  expect(btn.exists()).toBe(true)
  expect(btn.classes()).toContain('only-desktop')
  expect(btn.find('i.collapse-chev').exists()).toBe(true)
  expect(btn.find('i.nav-chev').exists()).toBe(false)
})

it('group headers render a leading icon so they stay visible when collapsed', () => {
  seed(true, {})
  const wrapper = mountSidebar()
  const parent = wrapper.find('button.nav-parent')
  expect(parent.find('i.nav-icon').exists()).toBe(true)
})

it('clicking a group while collapsed expands the sidebar and opens the group', async () => {
  seed(true, {})
  const sidebar = useSidebarStore()
  sidebar.toggleCollapse() // -> collapsed
  const wrapper = mountSidebar()
  const parent = wrapper.find('button.nav-parent')
  await parent.trigger('click')
  expect(sidebar.collapsed).toBe(false)
  expect(parent.attributes('aria-expanded')).toBe('true')
})

it('clicking a group while collapsed does not close an already-open group', async () => {
  seed(true, {})
  const sidebar = useSidebarStore()
  sidebar.toggleCollapse()
  const wrapper = mountSidebar()
  // group defaults open; the collapsed-click must keep it open, not toggle it shut
  await wrapper.find('button.nav-parent').trigger('click')
  expect(wrapper.find('.nav-group').classes()).toContain('open')
})
```

- [ ] **Step 2: Run to verify failure**

Run: `cd frontend && pnpm test -- --run src/components/shell/TheSidebar.test.ts`
Expected: the four new tests FAIL (missing `collapse-chev`, `only-desktop`, `nav-icon`, and collapsed-click behavior).

- [ ] **Step 3: Implement in `TheSidebar.vue`**

Script — replace `toggleGroup` with:

```ts
function toggleGroup(group: string): void {
  // Collapsed rail: a group click means "let me navigate" — expand the sidebar and
  // make sure the group is open (prototype behavior), never toggle it shut blindly.
  if (sidebar.collapsed) {
    sidebar.expand()
    open[group] = true
    return
  }
  open[group] = !isOpen(group)
}
```

Template — group header button gains a leading icon (schema groups define no icon; `pi-folder` is the stable default):

```vue
<button
  type="button"
  class="nav-item nav-parent"
  :aria-expanded="isOpen(g.group)"
  @click="toggleGroup(g.group)"
>
  <i class="nav-icon pi pi-folder" aria-hidden="true" />
  <span class="nav-label">{{ g.group }}</span>
  <i class="pi pi-angle-down nav-chev" aria-hidden="true" />
</button>
```

Template — collapse button becomes:

```vue
<button
  type="button"
  class="nav-item collapse-btn only-desktop"
  :aria-label="sidebar.collapsed ? t('shell.expand') : t('shell.collapse')"
  @click="sidebar.toggleCollapse()"
>
  <i class="pi pi-angle-left collapse-chev" aria-hidden="true" />
  <span class="nav-label">{{ t('shell.collapse') }}</span>
</button>
```

- [ ] **Step 4: Implement in `theme.css`**

After the `.nav-sub` rule add:

```css
.collapse-chev { width: 14px; text-align: center; flex: none; transition: transform var(--speed, .15s); }
.shell.collapsed .collapse-chev { transform: rotate(180deg); }
```

Replace `.shell.collapsed .nav-sub { padding-left: 0; }` with (prototype hides children on the collapsed rail; the group icon is the entry point):

```css
.shell.collapsed .nav-sub { display: none; }
```

(The existing `.shell.collapsed .nav-label, .shell.collapsed .side-ver, .shell.collapsed .nav-chev { display: none; }` rule stays — it must NOT gain `.collapse-chev`.)

- [ ] **Step 5: Run to verify pass**

Run: `cd frontend && pnpm test -- --run src/components/shell/TheSidebar.test.ts`
Expected: all PASS (pre-existing tests too — the group-header find in the first test uses `.nav-label`, unaffected by the added icon).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/shell/TheSidebar.vue frontend/src/assets/theme.css frontend/src/components/shell/TheSidebar.test.ts
git commit -m "fix(admin-ui): collapsed sidebar keeps visible expand affordance + group icons"
```

### Task 4: Breadcrumb — settings route + empty-label guard

**Files:**
- Modify: `frontend/src/lib/buildBreadcrumb.ts`
- Test: `frontend/src/lib/buildBreadcrumb.test.ts`

**Interfaces:**
- Produces: unchanged signature `buildBreadcrumb(route, collections, t): Crumb[]`; new guarantees — `settings` route handled; no crumb ever has an empty `label`.

- [ ] **Step 1: Write the failing tests** (append to the existing describe; mirror the file's existing `t` stub and collection fixtures)

```ts
it('settings route -> dashboard link + settings leaf', () => {
  const crumbs = buildBreadcrumb({ name: 'settings', params: {} }, [], t)
  expect(crumbs).toEqual([
    { label: t('nav.dashboard'), to: { name: 'dashboard' } },
    { label: t('nav.settings') },
  ])
})

it('unknown non-collection route degrades to dashboard only (no empty crumb)', () => {
  const crumbs = buildBreadcrumb({ name: 'not-found', params: {} }, [], t)
  expect(crumbs).toEqual([{ label: t('nav.dashboard'), to: { name: 'dashboard' } }])
  for (const c of crumbs) expect(c.label).not.toBe('')
})
```

- [ ] **Step 2: Run to verify failure**

Run: `cd frontend && pnpm test -- --run src/lib/buildBreadcrumb.test.ts`
Expected: FAIL — settings falls into the collection branch and produces a trailing `{ label: '' }`.

- [ ] **Step 3: Implement** — in `buildBreadcrumb.ts`, after the `media` line add:

```ts
if (name === 'settings')
  return [{ ...HOME, label: t('nav.dashboard') }, { label: t('nav.settings') }]
```

And guard the collection label push — replace the `const collectionLabel ...` / `crumbs.push(...)` block with:

```ts
const collectionLabel = meta?.label ?? collectionName
// Unknown route with no collection param: never emit an empty-label crumb.
if (collectionLabel === '') return crumbs

const isLeaf = name === 'collection-create' || name === 'collection-item'
crumbs.push(
  isLeaf
    ? { label: collectionLabel, to: { name: 'collection-list', params: { name: collectionName } } }
    : { label: collectionLabel },
)
```

- [ ] **Step 4: Run to verify pass**

Run: `cd frontend && pnpm test -- --run src/lib/buildBreadcrumb.test.ts`
Expected: PASS (all pre-existing cases too).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/lib/buildBreadcrumb.ts frontend/src/lib/buildBreadcrumb.test.ts
git commit -m "fix(admin-ui): breadcrumb handles settings route, never emits empty crumbs"
```

### Task 5: Cross-locale translated-value fallback (list titles no longer "—")

**Files:**
- Create: `frontend/src/lib/pickTranslated.ts`
- Create: `frontend/src/lib/pickTranslated.test.ts`
- Modify: `frontend/src/views/CollectionListView.vue` (cellValue, ~line 61)
- Modify: `frontend/src/lib/resolveItemTitle.ts` (readField translatable branch)

**Interfaces:**
- Produces: `pickTranslated(translations: Record<string, Record<string, unknown>> | undefined, defaultCode: string, field: string): unknown` — default locale first, else first locale with a non-empty value, else `undefined`. Task 11 (dashboard) relies on `resolveItemTitle` picking up the same fallback via `readField`.

- [ ] **Step 1: Write the failing test** — `frontend/src/lib/pickTranslated.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { pickTranslated } from './pickTranslated'

describe('pickTranslated', () => {
  const tr = { 'zh-TW': { title: '' }, en: { title: 'Hello' } }

  it('prefers the default locale when it has a value', () => {
    expect(pickTranslated({ 'zh-TW': { title: '嗨' }, en: { title: 'Hello' } }, 'zh-TW', 'title')).toBe('嗨')
  })

  it('falls back to the first locale with a non-empty value', () => {
    expect(pickTranslated(tr, 'zh-TW', 'title')).toBe('Hello')
  })

  it('returns undefined when no locale has a value', () => {
    expect(pickTranslated({ en: { title: '' } }, 'zh-TW', 'title')).toBeUndefined()
    expect(pickTranslated(undefined, 'zh-TW', 'title')).toBeUndefined()
  })
})
```

- [ ] **Step 2: Run to verify failure**

Run: `cd frontend && pnpm test -- --run src/lib/pickTranslated.test.ts`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement** — `frontend/src/lib/pickTranslated.ts`:

```ts
export type TranslationMap = Record<string, Record<string, unknown>> | undefined

/** Default locale first; else the first locale carrying a non-empty value. Keeps list
 *  columns and dashboard titles readable when content exists only in another locale. */
export function pickTranslated(translations: TranslationMap, defaultCode: string, field: string): unknown {
  const primary = translations?.[defaultCode]?.[field]
  if (primary != null && primary !== '') return primary
  for (const code of Object.keys(translations ?? {})) {
    const v = translations?.[code]?.[field]
    if (v != null && v !== '') return v
  }
  return undefined
}
```

- [ ] **Step 4: Wire the consumers**

`CollectionListView.vue` — replace `cellValue` with:

```ts
function cellValue(row: Record<string, unknown>, field: FieldMeta): unknown {
  if (field.translatable) {
    return pickTranslated(row.translations as TranslationMap, langStore.defaultCode, field.name)
  }
  return row[field.name]
}
```

and add to the imports: `import { pickTranslated, type TranslationMap } from '../lib/pickTranslated'`.

`resolveItemTitle.ts` — replace the translatable branch of `readField`:

```ts
if (field?.translatable) {
  return pickTranslated(row.translations, locale, key)
}
```

with import `import { pickTranslated } from './pickTranslated'`.

- [ ] **Step 5: Run the full frontend suite** (readField/resolveItemTitle/CollectionListView have existing tests that must stay green — fallback only ADDS values where previously undefined)

Run: `cd frontend && pnpm test -- --run`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/lib/pickTranslated.ts frontend/src/lib/pickTranslated.test.ts frontend/src/views/CollectionListView.vue frontend/src/lib/resolveItemTitle.ts
git commit -m "fix(admin-ui): cross-locale fallback for translated list/dashboard values"
```

### Task 6: Mobile list survivability (scroll container + no clipped controls)

**Files:**
- Modify: `frontend/src/views/CollectionListView.vue`

**Interfaces:**
- Produces: DataTable wrapped in `div.table-scroll`; Batch gate asserts no page-level horizontal scroll at 390px.

- [ ] **Step 1: Wrap the table**

In the template, wrap the entire `<DataTable ...>...</DataTable>` block:

```vue
<div class="table-scroll">
  <DataTable ... > ... </DataTable>
</div>
```

Add a scoped style block at the end of the file (the view currently has none):

```vue
<style scoped>
/* Wide tables scroll inside their own container; the page itself never scrolls sideways. */
.table-scroll { overflow-x: auto; }
</style>
```

- [ ] **Step 2: Run tests + build**

Run: `cd frontend && pnpm test -- --run src/views/CollectionListView.test.ts && pnpm build`
Expected: PASS / clean. (Existing view tests find elements by role/text, not structure — the wrapper div is transparent to them; if any fails on structure, fix the selector, not the assertion.)

- [ ] **Step 3: Commit**

```bash
git add frontend/src/views/CollectionListView.vue
git commit -m "fix(admin-ui): collection table scrolls in its own container on narrow viewports"
```

### Task 7: Batch 1 gate — full suite + live verification + branding restore

**Files:** none (verification only; evidence in the task report)

- [ ] **Step 1: Frontend + backend suites**

Run: `cd frontend && pnpm test -- --run && pnpm build`
Expected: all green, clean build.

- [ ] **Step 2: Live walkthrough (backend already on :5221; start `pnpm dev --host` if not running)**

Login as `admin@admin.com` / `admin#90196080`, then verify with Playwright (MCP browser or a scratch script), capturing screenshots as evidence:

1. Desktop 1440×900: `document.documentElement.scrollHeight === document.documentElement.clientHeight` on dashboard, list, form, media, settings (body-margin fix).
2. `/settings` breadcrumb reads `儀表板 › 設定` — no dangling separator.
3. Desktop: hamburger button `.drawer-toggle` has `display: none` (computed).
4. Collapse the sidebar: the collapse button stays visible (chevron flipped); group headers show folder icons; clicking a group expands the sidebar with that group open; the collapse cycle works both ways.
5. Mobile 390×844 on the Article list: no page-level horizontal scrollbar (`document.documentElement.scrollWidth <= 390`); "+ 新增" and 使用中/回收桶 fully visible; table scrolls inside `.table-scroll`; drawer opens with a visible scrim in dark mode and contains NO 收合側欄 button.
6. Article list titles show values (fallback) instead of "—" where any locale has content.

- [ ] **Step 3: Restore the branding name (B1-8, data fix)**

With the authenticated browser session (cookie + `X-Struo-CSRF: 1` header required for writes):

```js
await fetch('/api/settings/branding', {
  method: 'PUT',
  headers: { 'Content-Type': 'application/json', 'X-Struo-CSRF': '1' },
  body: JSON.stringify({ name: 'StruoCMS', logoFileId: null }),
})
```

(First GET `/api/settings/branding` and keep the existing `logoFileId` if one is set.) Reload and confirm the topbar/tab title reads "StruoCMS".

- [ ] **Step 4: Live e2e regression**

Backend must be running with `RateLimiting__Login__Enabled=false`. Then:

```bash
cd frontend && E2E_EMAIL=admin@admin.com E2E_PASSWORD='admin#90196080' pnpm exec playwright test --workers=1
```

Expected: 17/17 green. If a spec fails on a sidebar selector (Task 3 added icons), update the selector to role/aria-label lookup; never weaken assertions.

- [ ] **Step 5: Commit any e2e selector updates + report**

```bash
git add frontend/e2e
git commit -m "test(admin-ui): align e2e selectors with sidebar markup changes"
```

Report the evidence (screenshots + scroll metrics + e2e summary) before starting Batch 2.

---

## Batch 2 — visual remediation

### Task 8: PrimeVue primary color = sky-600 light / sky-400 dark

**Files:**
- Modify: `frontend/src/theme/preset.ts`
- Test: `frontend/src/theme/preset.test.ts`

**Interfaces:**
- Produces: `struoPresetConfig.semantic.colorScheme.light.primary.color === '{primary.600}'`, `dark.primary.color === '{primary.400}'`.

- [ ] **Step 1: Write the failing tests** (append to `preset.test.ts`)

```ts
it('light primary is sky-600 per brand-spec (Aura default 500 is too light)', () => {
  expect(struoPresetConfig.semantic.colorScheme.light.primary.color).toBe('{primary.600}')
  expect(struoPresetConfig.semantic.colorScheme.light.primary.hoverColor).toBe('{primary.700}')
})

it('dark primary is sky-400 per brand-spec', () => {
  expect(struoPresetConfig.semantic.colorScheme.dark.primary.color).toBe('{primary.400}')
  expect(struoPresetConfig.semantic.colorScheme.dark.primary.hoverColor).toBe('{primary.300}')
})
```

- [ ] **Step 2: Run to verify failure**

Run: `cd frontend && pnpm test -- --run src/theme/preset.test.ts`
Expected: FAIL — `primary` undefined on colorScheme entries.

- [ ] **Step 3: Implement** — in `preset.ts`, inside `colorScheme.light` add a `primary` sibling of `surface`, same for `dark`:

```ts
light: {
  primary: { color: '{primary.600}', hoverColor: '{primary.700}', activeColor: '{primary.800}' },
  surface: { /* unchanged */ },
},
dark: {
  primary: { color: '{primary.400}', hoverColor: '{primary.300}', activeColor: '{primary.200}' },
  surface: { /* unchanged */ },
},
```

- [ ] **Step 4: Run to verify pass**

Run: `cd frontend && pnpm test -- --run src/theme/preset.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/theme/preset.ts frontend/src/theme/preset.test.ts
git commit -m "fix(admin-ui): align PrimeVue primary to brand-spec (sky-600 light / sky-400 dark)"
```

### Task 9: `/api/auth/me` returns email + name (backend) and authStore carries them

**Files:**
- Modify: `src/Struo.Api/Controllers/AuthController.cs` (Me action, lines 45–71)
- Modify: `frontend/src/stores/authStore.ts` (CurrentUser type)
- Test: `tests/Struo.Tests/Api/AuthMePermissionsTests.cs`

**Interfaces:**
- Produces: `/api/auth/me` data payload gains `email: string|null`, `name: string|null` (camelCase JSON). Frontend `CurrentUser` gains `email?: string | null; name?: string | null`. Task 12 (UserMenu/topbar) consumes them.

- [ ] **Step 1: Write the failing backend test** (append to `AuthMePermissionsTests`)

```csharp
[Fact]
public async Task Me_includes_email_and_name()
{
    using var factory = new ApiFactory();
    var client = await factory.CreateAuthenticatedClientAsync();
    var doc = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
    var data = doc.GetProperty("data");
    data.GetProperty("email").GetString().Should().NotBeNullOrEmpty();
    data.TryGetProperty("name", out _).Should().BeTrue(); // nullable but always present
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter "FullyQualifiedName~AuthMePermissionsTests"`
Expected: FAIL — `email` property missing.

- [ ] **Step 3: Implement** — make `Me` async and load the user row (same `ISqlSugarClient` pattern as `UsersController`):

```csharp
[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
[HttpGet("me")]
public async Task<IActionResult> Me(
    [FromServices] Struo.Application.Security.ICurrentPermissions permissions,
    [FromServices] Struo.Application.Metadata.SchemaService schema,
    [FromServices] SqlSugar.ISqlSugarClient db,
    CancellationToken ct)
{
    var eff = permissions.Current;
    var map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
    if (!eff.IsSuperAdmin)
    {
        foreach (var c in schema.GetAll())
        {
            var read = eff.CanRead(c.Name);
            var write = eff.CanWrite(c.Name);
            var del = eff.CanDelete(c.Name);
            if (read || write || del)
                map[c.Name] = new { read, write, @delete = del };
        }
    }

    var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
    string? email = null;
    string? name = null;
    if (Guid.TryParse(uid, out var userId))
    {
        var u = await db.Queryable<Struo.Infrastructure.Identity.User>()
            .Where(x => x.Id == userId).FirstAsync(ct);
        email = u?.Email;
        name = u?.Name;
    }

    return Ok(new
    {
        id = uid,
        email,
        name,
        isSuperAdmin = eff.IsSuperAdmin,
        permissions = map
    });
}
```

- [ ] **Step 4: Run backend tests**

Run: `dotnet test`
Expected: all green (existing Me tests unaffected — they only read added-to payload).

- [ ] **Step 5: Extend the frontend type** — in `authStore.ts`:

```ts
export type CurrentUser = {
  id: string
  email?: string | null
  name?: string | null
  isSuperAdmin: boolean
  permissions: Record<string, CollectionPermission>
}
```

Run: `cd frontend && pnpm build`
Expected: clean.

- [ ] **Step 6: Commit**

```bash
git add src/Struo.Api/Controllers/AuthController.cs tests/Struo.Tests/Api/AuthMePermissionsTests.cs frontend/src/stores/authStore.ts
git commit -m "feat(auth): /auth/me returns email + name for the admin shell"
```

### Task 10: Accent restraint — secondary styling for in-form pickers

**Files:**
- Modify: `frontend/src/components/fields/FilePicker.vue:91`
- Modify: `frontend/src/components/fields/FilesField.vue:142`
- Modify: `frontend/src/components/fields/RepeaterField.vue:85`

Page-level CTAs stay primary (list 新增, form 儲存, media 上傳檔案, settings 儲存變更, login 登入, dialog Done/Save). Only the three in-form pickers change.

- [ ] **Step 1: Apply the three edits**

`FilePicker.vue:91`:

```vue
<Button :label="t('fields.selectFile')" severity="secondary" outlined size="small" :disabled="disabled" @click="openDialog" />
```

`FilesField.vue:142`:

```vue
<Button class="files-add" :label="t('fields.selectFiles')" severity="secondary" outlined size="small" :disabled="disabled" @click="openDialog" />
```

`RepeaterField.vue:85`:

```vue
<Button class="repeater-add" icon="pi pi-plus" label="Add" severity="secondary" outlined size="small" :disabled="disabled" @click="add" />
```

- [ ] **Step 2: Run related tests + build**

Run: `cd frontend && pnpm test -- --run src/components/fields && pnpm build`
Expected: green (existing tests click by label/class, not severity).

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/fields/FilePicker.vue frontend/src/components/fields/FilesField.vue frontend/src/components/fields/RepeaterField.vue
git commit -m "style(admin-ui): in-form pickers use secondary buttons (accent ≤2 per screen)"
```

### Task 11: List + dashboard visuals (Tag pills, formatDateTime, icon actions, row-link)

**Files:**
- Create: `frontend/src/lib/formatDateTime.ts`
- Create: `frontend/src/lib/formatDateTime.test.ts`
- Modify: `frontend/src/views/CollectionListView.vue`
- Modify: `frontend/src/lib/fieldTypes/registry.ts:27` (DateTime listColumn format)
- Modify: `frontend/src/components/dashboard/RecentUpdatesTable.vue` (formatUpdated)
- Modify: `frontend/src/components/dashboard/QuickActions.vue` (icons)
- Modify: `frontend/src/components/media/MediaFileList.vue:12`
- Modify: `frontend/src/components/media/MediaDetailDialog.vue:60`
- Modify: `frontend/src/lib/formatRevisionTime.ts` + `frontend/src/lib/formatRevisionTime.test.ts`

**Interfaces:**
- Produces: `formatDateTime(iso: string | null | undefined): string` → `YYYY-MM-DD HH:mm` local time, `'—'` for null/empty/invalid.

- [ ] **Step 1: Write the failing test** — `frontend/src/lib/formatDateTime.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { formatDateTime } from './formatDateTime'

describe('formatDateTime', () => {
  it('formats as YYYY-MM-DD HH:mm in local time', () => {
    const d = new Date(2026, 6, 14, 18, 32) // local 2026-07-14 18:32
    expect(formatDateTime(d.toISOString())).toBe('2026-07-14 18:32')
  })
  it('returns em-dash for null/empty/invalid', () => {
    expect(formatDateTime(null)).toBe('—')
    expect(formatDateTime('')).toBe('—')
    expect(formatDateTime('not-a-date')).toBe('—')
  })
})
```

- [ ] **Step 2: Run to verify failure**

Run: `cd frontend && pnpm test -- --run src/lib/formatDateTime.test.ts`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement** — `frontend/src/lib/formatDateTime.ts`:

```ts
/** Compact tabular datetime for lists (brand-spec: mono, no locale-verbose strings). */
export function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return '—'
  const p = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}`
}
```

- [ ] **Step 4: Replace the scattered `toLocaleString()` call sites**

`fieldTypes/registry.ts` line 27 (DateTime listColumn) — keep the raw-string fallback for invalid values:

```ts
format: (v) => { const s = formatDateTime(String(v)); return s === '—' ? String(v) : s },
```

(add `import { formatDateTime } from '../formatDateTime'`)

`RecentUpdatesTable.vue` — replace the `formatUpdated` body:

```ts
import { formatDateTime } from '../../lib/formatDateTime'
function formatUpdated(iso: string | null): string {
  return formatDateTime(iso)
}
```

`MediaFileList.vue:12` — `return formatDateTime(f.createdAt ?? null)` (import as above).

`MediaDetailDialog.vue:60` — replace the computed body with:

```ts
typeof raw.value?.createdAt === 'string' ? formatDateTime(raw.value.createdAt as string) : '—',
```

`formatRevisionTime.ts` — delegate:

```ts
import { formatDateTime } from './formatDateTime'
/** Invalid input falls back to the raw string so revision rows never render "Invalid Date". */
export function formatRevisionTime(iso: string): string {
  const s = formatDateTime(iso)
  return s === '—' ? (iso || '—') : s
}
```

`formatRevisionTime.test.ts` — update the expectation at line 6:

```ts
expect(formatRevisionTime('2026-07-21T10:00:00Z')).toBe(formatDateTime('2026-07-21T10:00:00Z'))
```

(add `import { formatDateTime } from './formatDateTime'`)

- [ ] **Step 5: CollectionListView — Tag pills, icon actions, row-link**

Script additions:

```ts
import Tag from 'primevue/tag'

function isSelectField(colField: string): boolean {
  return String(fieldOf(colField)?.interface ?? '').toLowerCase() === 'select'
}
function tagSeverity(v: unknown): 'success' | 'warn' | 'secondary' {
  const s = String(v ?? '').toLowerCase()
  if (s === 'published') return 'success'
  if (s === 'archived') return 'warn'
  return 'secondary'
}
```

Data column body template — replace the current interpolation-only body:

```vue
<template #body="{ data }">
  <Tag
    v-if="isSelectField(col.field) && cellValue(data, fieldOf(col.field)!) != null && cellValue(data, fieldOf(col.field)!) !== ''"
    :value="formatCell(cellValue(data, fieldOf(col.field)!), fieldOf(col.field)!)"
    :severity="tagSeverity(cellValue(data, fieldOf(col.field)!))"
  />
  <span v-else :class="{ 'row-link': col.field === columns[0]?.field }">
    {{ formatCell(cellValue(data, fieldOf(col.field)!), fieldOf(col.field)!) }}
  </span>
</template>
```

Actions column — icon buttons; keep aria-labels equal to the old visible labels so role-based e2e lookups keep working:

```vue
<Column v-if="canDelete" header="" :style="{ width: '8rem' }">
  <template #body="{ data }">
    <template v-if="mode === 'active'">
      <Button icon="pi pi-trash" severity="danger" text rounded size="small"
              :title="t('collectionList.delete')" :aria-label="t('collectionList.delete')"
              @click.stop="onDelete(data)" />
    </template>
    <template v-else>
      <Button icon="pi pi-undo" text rounded size="small"
              :title="t('collectionList.restore')" :aria-label="t('collectionList.restore')"
              @click.stop="onRestore(data)" />
      <Button icon="pi pi-trash" severity="danger" text rounded size="small"
              :title="t('collectionList.purge')" :aria-label="t('collectionList.purge')"
              @click.stop="onPurge(data)" />
    </template>
  </template>
</Column>
```

Append to the scoped style block from Task 6:

```css
:deep(.row-link) { font-weight: 600; }
:deep(tr:hover .row-link) { color: var(--accent); text-decoration: underline; }
```

- [ ] **Step 6: QuickActions icons** — in `QuickActions.vue` add:

```ts
function iconFor(action: QuickAction): string {
  return action.kind === 'uploadMedia' ? 'pi pi-upload' : 'pi pi-plus'
}
```

and on the button: `:icon="iconFor(action)"`.

- [ ] **Step 7: Run full frontend suite + fix selector-level fallout only**

Run: `cd frontend && pnpm test -- --run && pnpm build`
Expected: green. CollectionListView tests asserting on delete-button labels must switch to `getByRole`/aria-label — same accessible name, so behavior assertions stay identical.

- [ ] **Step 8: Commit**

```bash
git add frontend/src/lib/formatDateTime.ts frontend/src/lib/formatDateTime.test.ts frontend/src/lib/fieldTypes/registry.ts frontend/src/components/dashboard/RecentUpdatesTable.vue frontend/src/components/dashboard/QuickActions.vue frontend/src/components/media/MediaFileList.vue frontend/src/components/media/MediaDetailDialog.vue frontend/src/lib/formatRevisionTime.ts frontend/src/lib/formatRevisionTime.test.ts frontend/src/views/CollectionListView.vue
git commit -m "style(admin-ui): tag pills, tabular datetimes, icon row-actions, quick-action icons"
```

### Task 12: User identity in shell (UserMenu header + topbar name) and neutral version copy

**Files:**
- Modify: `frontend/src/components/shell/UserMenu.vue`
- Modify: `frontend/src/assets/theme.css` (`.user-role` → `.user-name` styles + mobile hide)
- Modify: `frontend/src/locales/zh-TW.ts:49` and the matching `shell.version` key in `frontend/src/locales/en.ts`
- Test: `frontend/src/components/shell/UserMenu.test.ts`

**Interfaces:**
- Consumes: `auth.user.email` / `auth.user.name` from Task 9.

- [ ] **Step 1: Write the failing tests** (append to `UserMenu.test.ts`, following its existing pinia/i18n mount setup)

```ts
it('shows the user name (fallback email) next to the avatar', () => {
  const auth = useAuthStore()
  auth.user = { id: 'u1', email: 'a@b.c', name: '陳雅婷', isSuperAdmin: true, permissions: {} }
  const wrapper = mountMenu()
  expect(wrapper.find('.user-name b').text()).toBe('陳雅婷')
})

it('falls back to email when name is missing', () => {
  const auth = useAuthStore()
  auth.user = { id: 'u1', email: 'a@b.c', name: null, isSuperAdmin: false, permissions: {} }
  const wrapper = mountMenu()
  expect(wrapper.find('.user-name b').text()).toBe('a@b.c')
})
```

(`mountMenu` = the file's existing mount helper; add one matching its pattern if absent.)

- [ ] **Step 2: Run to verify failure**

Run: `cd frontend && pnpm test -- --run src/components/shell/UserMenu.test.ts`
Expected: FAIL — `.user-name` not found.

- [ ] **Step 3: Implement `UserMenu.vue`**

Script addition:

```ts
const displayName = computed(() => auth.user?.name || auth.user?.email || roleLabel.value)
```

Template — replace the `user-role` span and add a popover header:

```vue
<button type="button" class="user-btn" :aria-label="t('user.account')" aria-haspopup="true" @click="toggle">
  <span class="avatar"><i class="pi pi-user" aria-hidden="true" /></span>
  <span class="user-name">
    <b>{{ displayName }}</b>
    <span>{{ roleLabel }}</span>
  </span>
</button>
<Menu ref="menu" :model="menuModel" :popup="true">
  <template #start>
    <div class="user-menu-head">
      <b>{{ displayName }}</b>
      <span v-if="auth.user?.email && auth.user?.name" class="user-menu-email">{{ auth.user.email }}</span>
    </div>
  </template>
</Menu>
```

Scoped style (new block in `UserMenu.vue`):

```vue
<style scoped>
.user-menu-head { display: grid; gap: 2px; padding: 10px 12px 8px; border-bottom: 1px solid var(--border); margin-bottom: 4px; }
.user-menu-head b { font-size: .9rem; }
.user-menu-email { font-size: .78rem; color: var(--muted); }
</style>
```

- [ ] **Step 4: theme.css — two-line identity styles** (prototype 147-149); replace the `.user-role` rule:

```css
.user-name { display: grid; text-align: left; line-height: 1.2; }
.user-name b { font-size: .83rem; }
.user-name span { font-size: .7rem; color: var(--muted); }
```

and inside `@media (max-width: 1023px)` replace `.user-role { display: none; }` with `.user-name { display: none; }`.

- [ ] **Step 5: Neutral version copy**

`zh-TW.ts:49`: `version: '管理後台',` — `en.ts` `shell.version`: `version: 'Admin console',`

- [ ] **Step 6: Run to verify pass**

Run: `cd frontend && pnpm test -- --run src/components/shell && pnpm build`
Expected: PASS (TheTopbar tests unaffected; locales.test.ts key-parity still holds — values changed, keys unchanged).

- [ ] **Step 7: Commit**

```bash
git add frontend/src/components/shell/UserMenu.vue frontend/src/components/shell/UserMenu.test.ts frontend/src/assets/theme.css frontend/src/locales/zh-TW.ts frontend/src/locales/en.ts
git commit -m "feat(admin-ui): user name/email in topbar + menu header; neutral version copy"
```

### Task 13: Form layout (content-first order, max-width, checkbox/relation polish) + media filename dedup

**Files:**
- Modify: `frontend/src/components/ItemForm.vue`
- Modify: `frontend/src/components/fields/CheckboxGroupField.vue`
- Modify: `frontend/src/components/media/FileThumbnail.vue`

- [ ] **Step 1: ItemForm — reorder sections and constrain width**

Template: move the entire `<Tabs v-if="fields.translatable.length" ...>...</Tabs>` block to the TOP of the form (immediately after the `serverError` paragraph), so the order becomes: translatable Tabs → shared fields → relations. The blocks themselves are unchanged, only their order.

Scoped style — change `.item-form` to:

```css
.item-form { display: grid; gap: 18px; max-width: 860px; }
```

and add width normalization for pickers (relation dropdowns currently render shrink-wrapped):

```css
.field :deep(.p-select),
.field :deep(.p-multiselect),
.field :deep(.p-treeselect) { width: 100%; max-width: 480px; }
```

- [ ] **Step 2: CheckboxGroupField — fix the crushed layout** (component has no style block today; add one)

```vue
<style scoped>
.checkbox-group { display: flex; flex-wrap: wrap; gap: 8px 20px; }
.checkbox-option { display: inline-flex; align-items: center; gap: 8px; cursor: pointer; }
</style>
```

- [ ] **Step 3: FileThumbnail — chip stops duplicating the filename** (MediaGrid's `.media-tile__name` caption already shows it; the chip keeps icon + content type)

Replace the chip block in the template:

```vue
<div v-else class="file-chip">
  <i class="pi pi-file file-chip__icon" aria-hidden="true" />
  <span class="file-chip__meta">{{ file.contentType }}</span>
</div>
```

Delete the now-unused `.file-chip__name` CSS rule and add:

```css
.file-chip__icon { font-size: 20px; color: var(--muted); }
```

Check consumers of the removed name: `grep -rn "file-chip__name" frontend/src frontend/e2e` — update any test/e2e selector to `.media-tile__name` (same visible name, one source). `FilePicker.vue`'s current-file row already prints `current.fileName` in its own span, so nothing is lost there.

- [ ] **Step 4: Run the full frontend suite**

Run: `cd frontend && pnpm test -- --run && pnpm build`
Expected: green. ItemForm tests that assert field rendering must still pass — they query by label, not section order; if one asserts order, update it to the new content-first order (that IS the intended behavior change).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/ItemForm.vue frontend/src/components/fields/CheckboxGroupField.vue frontend/src/components/media/FileThumbnail.vue
git commit -m "style(admin-ui): content-first form order + max-width; checkbox spacing; single filename on media tiles"
```

### Task 14: Batch 2 gate — full suites + live visual verification

**Files:** none (verification only)

- [ ] **Step 1: Suites**

Run: `cd frontend && pnpm test -- --run && pnpm build` and `dotnet test` (repo root).
Expected: all green.

- [ ] **Step 2: Live visual walkthrough (same env as Task 7)**

1. Light + dark: primary buttons (登入, 新增, 儲存) render sky-600 (`#0284c7`-family) in light, sky-400 in dark — assert via computed `background-color` on the login submit button.
2. Item form (Article edit): locale Tabs section appears FIRST; form column ≤860px wide; Audiences checkboxes evenly spaced; Category/Tags dropdowns full-width (≤480px); exactly the 儲存/刪除 area carries accent — 選擇/Add buttons render as outlined secondary.
3. Article list: Status column shows Tag pills (published=green, draft=neutral); dates read `YYYY-MM-DD HH:mm`; row actions are icon buttons with hover titles; first column styled as row-link on hover.
4. Dashboard: quick actions show icons; recent-updates dates tabular.
5. Topbar: user name (or email) + role two-liner; user menu shows name/email header above 登出. Sidebar footer no longer says 重新設計原型.
6. Media library: tiles show the filename once (caption only); non-image chips show icon + content type.

- [ ] **Step 3: Live e2e regression**

```bash
cd frontend && E2E_EMAIL=admin@admin.com E2E_PASSWORD='admin#90196080' pnpm exec playwright test --workers=1
```

Expected: 17/17 green (update selectors per the Task 11/13 aria-label contract if needed — never weaken assertions).

- [ ] **Step 4: Final report**

Present evidence (screenshots light/dark, suite counts, e2e summary), then use superpowers:finishing-a-development-branch to decide merge of `admin-ui-remediation` into `main`.
