# FE-R5 Item Form Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Re-skin the item create/edit form (`ItemFormView.vue` + `ItemForm.vue`) to the FE-R0 design language — page-head action bar, per-locale completeness dots, "translatable" field badge — preserving every existing behaviour.

**Architecture:** Single-column re-skin (NOT the prototype's two-column grid; StruoCMS is schema-driven). Script logic is carried over verbatim; only presentation + string-extraction change. Save/Delete move to a page-head action bar built on the existing `PageHeader` common component (extended with an optional `#lead` back-button slot). Completeness dots are computed client-side by a new pure helper. A new `itemForm` i18n namespace holds the extracted strings.

**Tech Stack:** Vue 3 `<script setup>` + TypeScript, PrimeVue (Aura preset), vue-i18n (`legacy:false`), Vitest + @vue/test-utils, pnpm.

## Global Constraints

- **No backend / API / route / persistence change.** Frontend only. No `dotnet` gate, no hard live-PG gate.
- **No new dependencies** — reuse primeicons + existing PrimeVue components; no `pnpm add`.
- **Preserve all behaviour in spec §2** — load/init, submit+validate, 409 VERSION_CONFLICT recovery, server-error field mapping, delete confirm, dirty guards (leave/update/beforeunload), `splitFields`, error→default-locale-tab jump, all field types. `defineExpose` surfaces kept.
- **i18n:** zh-TW is default + en fallback; `locales.test.ts` enforces symmetric key sets between `zh-TW.ts` and `en.ts`.
- **Honest data:** no publish-status, no preview, no locale-coverage backend aggregate. Dots are purely client-side.
- **Immutability / small files / component-scoped styles** per repo conventions. Outbound presentation only; no global CSS bleed.
- **Gate:** both `pnpm --dir frontend build` (vue-tsc typecheck) AND `pnpm --dir frontend test` (vitest) must be green. vitest strips types, so build is a separate required gate.
- **Process:** subagent-driven, impl = Sonnet / review = Opus per task + Opus whole-branch review; `--no-ff` merge to `main`.

---

## File Structure

- `frontend/src/locales/zh-TW.ts` — add `itemForm` namespace (modify).
- `frontend/src/locales/en.ts` — add `itemForm` namespace (modify).
- `frontend/src/locales/locales.test.ts` — add one presence assertion (modify).
- `frontend/src/lib/localeCompleteness.ts` — new pure helper `hasLocaleContent`.
- `frontend/src/lib/localeCompleteness.test.ts` — new unit test.
- `frontend/src/components/common/PageHeader.vue` — add optional `#lead` slot (modify).
- `frontend/src/components/common/PageHeader.test.ts` — add `#lead` cases (modify).
- `frontend/src/components/ItemForm.vue` — re-skin: badge + dots + i18n; remove internal actions row (modify).
- `frontend/src/components/ItemForm.test.ts` — update for removed actions + new badge/dots (modify).
- `frontend/src/views/ItemFormView.vue` — rebuild template on `PageHeader` + i18n; Save/Delete in header (modify).
- `frontend/src/views/ItemFormView.test.ts` — install i18n plugin; add header/i18n cases (modify).

All `pnpm` commands assume repo root `D:\dotnet\struo-cms`; use `pnpm --dir frontend <cmd>` (or `cd frontend` first).

---

### Task 1: `itemForm` i18n namespace

**Files:**
- Modify: `frontend/src/locales/zh-TW.ts` (append `itemForm` block)
- Modify: `frontend/src/locales/en.ts` (append `itemForm` block)
- Test: `frontend/src/locales/locales.test.ts`

**Interfaces:**
- Produces: i18n keys `itemForm.{loading, collectionNotFound, itemNotFound, noCreatePermission, new, edit, delete, save, back, relations, translatableBadge, conflictText, reloadLatest}`. `new` and `edit` take a `{label}` param. Consumed by Tasks 4 and 5.

- [ ] **Step 1: Write the failing test** — add to `locales.test.ts` inside `describe('locale packs', …)`:

```typescript
  it('carries the itemForm namespace in both packs', () => {
    expect(zhTW.itemForm.save).toBe('儲存')
    expect(en.itemForm.save).toBe('Save')
  })
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm --dir frontend test -- locales.test.ts`
Expected: FAIL — `zhTW.itemForm` is undefined (TypeError) / assertion fails.

- [ ] **Step 3: Add the namespace to `zh-TW.ts`** — insert a new `itemForm` block as the last key of the default-export object (after `collectionList`), keeping the trailing `}`:

```typescript
  itemForm: {
    loading: '載入中…',
    collectionNotFound: '找不到集合',
    itemNotFound: '找不到項目',
    noCreatePermission: '您沒有在此集合建立項目的權限',
    new: '新增 {label}',
    edit: '編輯 {label}',
    delete: '刪除',
    save: '儲存',
    back: '返回列表',
    relations: '關聯',
    translatableBadge: '可翻譯',
    conflictText: '此項目已被他人變更。檢視您的編輯後再次儲存以覆寫,或重新載入最新版本。',
    reloadLatest: '重新載入最新版本',
  },
```

- [ ] **Step 4: Add the mirrored namespace to `en.ts`** — same key order:

```typescript
  itemForm: {
    loading: 'Loading…',
    collectionNotFound: 'Collection not found',
    itemNotFound: 'Item not found',
    noCreatePermission: "You don't have permission to create items here",
    new: 'New {label}',
    edit: 'Edit {label}',
    delete: 'Delete',
    save: 'Save',
    back: 'Back to list',
    relations: 'Relations',
    translatableBadge: 'Translatable',
    conflictText: 'This item was changed by someone else. Review your edits and save again to overwrite, or reload the latest version.',
    reloadLatest: 'Reload latest',
  },
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `pnpm --dir frontend test -- locales.test.ts`
Expected: PASS — both the new presence test and the existing symmetric-key-set parity test are green.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/locales/zh-TW.ts frontend/src/locales/en.ts frontend/src/locales/locales.test.ts
git commit -m "feat(frontend): itemForm i18n namespace zh-TW + en (FE-R5)"
```

---

### Task 2: `localeCompleteness` pure helper

**Files:**
- Create: `frontend/src/lib/localeCompleteness.ts`
- Test: `frontend/src/lib/localeCompleteness.test.ts`

**Interfaces:**
- Consumes: `FieldMeta` from `../types/schema`.
- Produces: `hasLocaleContent(fields: FieldMeta[], values: Record<string, unknown>): boolean` — `true` iff at least one field in `fields` has a non-empty value in `values`. "Non-empty" = not `undefined`/`null`; not an empty or whitespace-only string; not an empty array. Numbers (incl. `0`) and booleans (incl. `false`) count as content. Consumed by Task 4.

- [ ] **Step 1: Write the failing test** — `frontend/src/lib/localeCompleteness.test.ts`:

```typescript
import { describe, it, expect } from 'vitest'
import { hasLocaleContent } from './localeCompleteness'
import type { FieldMeta } from '../types/schema'

function f(name: string): FieldMeta {
  return { name, label: name, interface: 'text', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: true, sort: 0, isSystem: false }
}
const fields = [f('title'), f('body')]

describe('hasLocaleContent', () => {
  it('is false when all fields are missing or empty', () => {
    expect(hasLocaleContent(fields, {})).toBe(false)
    expect(hasLocaleContent(fields, { title: '', body: undefined })).toBe(false)
  })
  it('is false for whitespace-only strings and empty arrays', () => {
    expect(hasLocaleContent(fields, { title: '   ', body: [] })).toBe(false)
  })
  it('is true when any field has a non-empty string', () => {
    expect(hasLocaleContent(fields, { title: 'Hello', body: '' })).toBe(true)
  })
  it('counts numbers (incl. 0), booleans (incl. false), and non-empty arrays as content', () => {
    expect(hasLocaleContent([f('n')], { n: 0 })).toBe(true)
    expect(hasLocaleContent([f('b')], { b: false })).toBe(true)
    expect(hasLocaleContent([f('tags')], { tags: ['x'] })).toBe(true)
  })
  it('is false when there are no translatable fields', () => {
    expect(hasLocaleContent([], { anything: 'x' })).toBe(false)
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm --dir frontend test -- localeCompleteness.test.ts`
Expected: FAIL — cannot resolve `./localeCompleteness`.

- [ ] **Step 3: Write minimal implementation** — `frontend/src/lib/localeCompleteness.ts`:

```typescript
import type { FieldMeta } from '../types/schema'

/**
 * FE-R5 translation-completeness dot semantics (option a): a locale "has content"
 * when at least one of its translatable fields holds a non-empty value. Pure —
 * computed from the in-memory form model, no backend aggregate. NOT a validity or
 * required-field check.
 */
function isNonEmpty(value: unknown): boolean {
  if (value === undefined || value === null) return false
  if (typeof value === 'string') return value.trim().length > 0
  if (Array.isArray(value)) return value.length > 0
  return true // numbers (incl. 0), booleans (incl. false), objects count as content
}

export function hasLocaleContent(fields: FieldMeta[], values: Record<string, unknown>): boolean {
  return fields.some((f) => isNonEmpty(values[f.name]))
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm --dir frontend test -- localeCompleteness.test.ts`
Expected: PASS (all 5 cases).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/lib/localeCompleteness.ts frontend/src/lib/localeCompleteness.test.ts
git commit -m "feat(frontend): localeCompleteness helper for translation dots (FE-R5)"
```

---

### Task 3: `PageHeader` optional `#lead` slot

**Files:**
- Modify: `frontend/src/components/common/PageHeader.vue`
- Test: `frontend/src/components/common/PageHeader.test.ts`

**Interfaces:**
- Consumes: nothing new.
- Produces: `PageHeader` renders an optional `#lead` slot inside a `.head-lead` wrapper *before* `.titles`. When the `lead` slot is not provided, no `.head-lead` element is rendered. Props (`title`, `caption?`) and the `#actions` slot are unchanged. Consumed by Task 5.

- [ ] **Step 1: Write the failing tests** — append inside `describe('PageHeader', …)` in `PageHeader.test.ts`:

```typescript
  it('renders the lead slot inside .head-lead when provided', () => {
    const w = mount(PageHeader, { props: { title: 'Edit' }, slots: { lead: '<button>Back</button>' } })
    expect(w.find('.head-lead').exists()).toBe(true)
    expect(w.get('.head-lead').text()).toBe('Back')
  })
  it('omits the .head-lead wrapper when no lead slot is given', () => {
    const w = mount(PageHeader, { props: { title: 'Edit' } })
    expect(w.find('.head-lead').exists()).toBe(false)
  })
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm --dir frontend test -- PageHeader.test.ts`
Expected: FAIL — `.head-lead` does not exist.

- [ ] **Step 3: Add the slot** — update `PageHeader.vue` template + style. Use `$slots.lead` to conditionally render the wrapper:

```vue
<template>
  <header class="page-head">
    <div v-if="$slots.lead" class="head-lead">
      <slot name="lead" />
    </div>
    <div class="titles">
      <h1>{{ title }}</h1>
      <p v-if="caption" class="caption">{{ caption }}</p>
    </div>
    <div class="head-actions">
      <slot name="actions" />
    </div>
  </header>
</template>
```

Add to `<style scoped>` (keep existing rules; make the titles group flex-grow so actions stay right-aligned when a lead is present):

```css
.head-lead { display: flex; align-items: center; }
.titles { flex: 1 1 auto; }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm --dir frontend test -- PageHeader.test.ts`
Expected: PASS — new `#lead` cases plus the 4 existing cases (title/caption/omit-caption/actions) all green.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/common/PageHeader.vue frontend/src/components/common/PageHeader.test.ts
git commit -m "feat(frontend): PageHeader optional #lead slot (FE-R5)"
```

---

### Task 4: Re-skin `ItemForm.vue` (badge + dots, remove actions row)

**Files:**
- Modify: `frontend/src/components/ItemForm.vue`
- Test: `frontend/src/components/ItemForm.test.ts`

**Interfaces:**
- Consumes: `hasLocaleContent` (Task 2); `itemForm.relations` + `itemForm.translatableBadge` i18n keys (Task 1).
- Produces: `ItemForm` emits only `submit` (the `cancel` emit is removed). It no longer renders Save/Cancel buttons. It renders per-locale dots and a translatable badge. `defineExpose({ activeLocale })` unchanged. Consumed by Task 5 (which supplies Save/Cancel in the page-head).

- [ ] **Step 1: Update the test file for i18n + new behaviour.** Replace the top of `ItemForm.test.ts` imports and stubs, and rewrite the two actions-related cases. New import + i18n helper (add after existing imports):

```typescript
import { createI18n } from 'vue-i18n'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { itemForm: { relations: 'Relations', translatableBadge: 'Translatable' } } },
})
function mountForm(props: Record<string, unknown>, extraStubs: Record<string, unknown> = {}) {
  return mount(ItemForm, { props, global: { plugins: [i18n], stubs: { ...stubs, ...extraStubs } } })
}
```

Remove `Button` from the `stubs` object (ItemForm no longer renders Buttons). Then replace every `mount(ItemForm, { props: {...}, global: { stubs } })` call with `mountForm({...})`, and every `mount(ItemForm, { props: {...}, global: { stubs: { ...stubs, RelationInput: true } } })` with `mountForm({...}, { RelationInput: true })`.

- [ ] **Step 2: Rewrite the two actions cases + add badge/dots cases.** Replace the `emits submit on form submit and cancel on Cancel click` and `hides Save when disabled (read-only)` tests with:

```typescript
  it('emits submit on form submit (Enter/submit still works with no internal buttons)', async () => {
    const w = mountForm({ meta, model, locales, errors: {} })
    await w.find('form').trigger('submit')
    expect(w.emitted('submit')).toBeTruthy()
  })
  it('no longer renders internal Save/Cancel buttons (actions moved to the page-head)', () => {
    const w = mountForm({ meta, model, locales, errors: {} })
    expect(w.find('button[data-label="Save"]').exists()).toBe(false)
    expect(w.find('button[data-label="Cancel"]').exists()).toBe(false)
  })
  it('renders a translatable badge for translatable fields', () => {
    const w = mountForm({ meta, model, locales, errors: {} })
    expect(w.find('.tr-badge').exists()).toBe(true)
  })
  it('shows a filled dot for locales with content and a hollow dot for empty locales', () => {
    const withContent: FormModel = { shared: { status: 'draft' },
      translations: { en: { title: 'Hi' }, 'zh-TW': { title: '' } }, relations: {} }
    const w = mountForm({ meta, model: withContent, locales, errors: {} })
    const dots = w.findAll('.dot')
    expect(dots).toHaveLength(2)          // one per locale
    expect(dots[0].classes()).not.toContain('off') // en has content
    expect(dots[1].classes()).toContain('off')     // zh-TW empty
  })
  it('renders no dots when there is only one locale', () => {
    const w = mountForm({ meta, model, locales: [{ code: 'en', name: 'English', isDefault: true }], errors: {} })
    expect(w.findAll('.dot')).toHaveLength(0)
  })
```

> Note: the `Tab` stub renders its default slot, so dots/labels inside each `Tab` are queryable. The existing `renders a tab per locale…`, `jumps the active tab…`, `shows a server error banner`, and `renders a RelationInput per relation` cases stay (with `mountForm`).

- [ ] **Step 3: Run tests to verify the new/changed ones fail**

Run: `pnpm --dir frontend test -- ItemForm.test.ts`
Expected: FAIL — `.tr-badge` / `.dot` not found; Save/Cancel still present (old template).

- [ ] **Step 4: Rewrite `ItemForm.vue`.** Full new file:

```vue
<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import Tabs from 'primevue/tabs'
import TabList from 'primevue/tablist'
import Tab from 'primevue/tab'
import TabPanels from 'primevue/tabpanels'
import TabPanel from 'primevue/tabpanel'
import FieldInput from './fields/FieldInput.vue'
import RelationInput from './fields/RelationInput.vue'
import { splitFields } from '../lib/splitFields'
import { hasLocaleContent } from '../lib/localeCompleteness'
import type { CollectionMeta, LanguageInfo } from '../types/schema'
import type { FormModel } from '../types/itemForm'

const props = defineProps<{
  meta: CollectionMeta
  model: FormModel
  locales: LanguageInfo[]
  errors: Record<string, string>
  serverError?: string
  disabled?: boolean
  submitting?: boolean
  itemId?: string
}>()
const emit = defineEmits<{ (e: 'submit'): void }>()
const { t } = useI18n()

const fields = computed(() => splitFields(props.meta))
const defaultCode = computed(() => props.locales.find((l) => l.isDefault)?.code ?? props.locales[0]?.code ?? '')
const activeLocale = ref(props.locales[0]?.code ?? '')
// Dots only carry information when there is more than one locale AND translatable fields exist.
const showDots = computed(() => fields.value.translatable.length > 0 && props.locales.length > 1)
function localeFilled(code: string): boolean {
  return hasLocaleContent(fields.value.translatable, props.model.translations[code] ?? {})
}

// Surface default-locale validation errors even if the user is on another locale's tab.
watch(() => props.errors, (e) => {
  if (Object.keys(e).length > 0) activeLocale.value = defaultCode.value
})

defineExpose({ activeLocale })
</script>

<template>
  <form class="item-form" @submit.prevent="emit('submit')">
    <p v-if="serverError" class="error" role="alert">{{ serverError }}</p>

    <div v-for="f in fields.shared" :key="f.name" class="field">
      <label :for="f.name">{{ f.label }}<span v-if="f.required" class="req">*</span></label>
      <FieldInput :field="f" v-model="model.shared[f.name]" :disabled="disabled" />
      <small v-if="f.helpText" class="help">{{ f.helpText }}</small>
      <small v-if="errors[f.name]" class="field-error" role="alert">{{ errors[f.name] }}</small>
    </div>

    <section v-if="meta.relations && meta.relations.length" class="relations">
      <h3>{{ t('itemForm.relations') }}</h3>
      <div v-for="rel in meta.relations" :key="rel.name" class="field">
        <label>{{ rel.label }}</label>
        <RelationInput
          :relation="rel"
          v-model="model.relations[rel.name]"
          :disabled="disabled"
          :parent-id="itemId"
          :exclude-id="rel.selfReferencing ? itemId : undefined"
        />
      </div>
    </section>

    <Tabs v-if="fields.translatable.length" v-model:value="activeLocale">
      <TabList>
        <Tab v-for="loc in locales" :key="loc.code" :value="loc.code">
          <span v-if="showDots" class="dot" :class="{ off: !localeFilled(loc.code) }" aria-hidden="true" />
          {{ loc.name }}<span v-if="loc.isDefault"> *</span>
        </Tab>
      </TabList>
      <TabPanels>
        <TabPanel v-for="loc in locales" :key="loc.code" :value="loc.code">
          <template v-if="loc.code === activeLocale">
            <div v-for="f in fields.translatable" :key="f.name" class="field">
              <div class="lbl-row">
                <label>{{ f.label }}<span v-if="f.required && loc.isDefault" class="req">*</span></label>
                <span class="tr-badge">{{ t('itemForm.translatableBadge') }}</span>
              </div>
              <FieldInput :field="f" v-model="model.translations[loc.code][f.name]" :disabled="disabled" />
              <small v-if="loc.isDefault && errors[f.name]" class="field-error" role="alert">{{ errors[f.name] }}</small>
            </div>
          </template>
        </TabPanel>
      </TabPanels>
    </Tabs>
  </form>
</template>

<style scoped>
.item-form { display: grid; gap: 18px; }
.error { color: var(--danger, #dc2626); margin: 0; }
.field { display: grid; gap: 6px; }
.field label { font-size: 0.9rem; font-weight: 500; color: var(--fg); }
.req { color: var(--danger, #dc2626); margin-left: 2px; }
.help { color: var(--muted); font-size: 0.8rem; }
.field-error { color: var(--danger, #dc2626); font-size: 0.8rem; }
.relations { display: grid; gap: 14px; }
.relations h3 { margin: 0; font-size: 1.125rem; color: var(--fg); }
.lbl-row { display: flex; align-items: center; gap: 8px; }
.tr-badge {
  font-size: 0.68rem; font-weight: 700; padding: 1px 7px; border-radius: 5px;
  background: var(--surface-2, color-mix(in srgb, var(--fg) 8%, transparent)); color: var(--muted);
}
.dot {
  display: inline-block; width: 8px; height: 8px; border-radius: 99px;
  background: var(--success, #16a34a); margin-right: 6px; vertical-align: middle;
}
.dot.off { background: transparent; border: 1.5px solid var(--border); }
</style>
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `pnpm --dir frontend test -- ItemForm.test.ts`
Expected: PASS — badge, dots (filled/hollow), single-locale no-dots, submit-still-emits, no internal buttons, plus retained cases.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/ItemForm.vue frontend/src/components/ItemForm.test.ts
git commit -m "feat(frontend): re-skin ItemForm with completeness dots + translatable badge (FE-R5)"
```

---

### Task 5: Rebuild `ItemFormView.vue` (page-head action bar + i18n)

**Files:**
- Modify: `frontend/src/views/ItemFormView.vue`
- Test: `frontend/src/views/ItemFormView.test.ts`

**Interfaces:**
- Consumes: `PageHeader` `#lead` slot (Task 3); `itemForm.*` i18n keys (Task 1); the re-skinned `ItemForm` with no `@cancel` emit (Task 4).
- Produces: no exposed-surface change — `defineExpose` stays `{ init, onSubmit, onDelete, onCancel, reloadLatest, model, errors, serverError, notFound, loading, conflict }`.

- [ ] **Step 1: Install i18n in the view test + add header cases.** In `ItemFormView.test.ts`, add the import and helper near the top (after existing imports):

```typescript
import { createI18n } from 'vue-i18n'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { itemForm: {
    loading: 'Loading…', collectionNotFound: 'Collection not found', itemNotFound: 'Item not found',
    noCreatePermission: "You don't have permission to create items here",
    new: 'New {label}', edit: 'Edit {label}', delete: 'Delete', save: 'Save', back: 'Back to list',
    relations: 'Relations', translatableBadge: 'Translatable',
    conflictText: 'This item was changed by someone else.', reloadLatest: 'Reload latest',
  } } },
})
function mountView() {
  return mount(ItemFormView, { global: { plugins: [i18n], stubs } })
}
```

Then replace **every** occurrence of `mount(ItemFormView, { global: { stubs } })` in this file with `mountView()`. (All existing cases keep their assertions — they drive behaviour through `w.vm.*`, which is unaffected by the template change.)

- [ ] **Step 2: Add two new view cases** (append inside the describe block):

```typescript
  it('renders the page-head title from i18n in edit mode', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'x', translations: {} })
    const w = mountView()
    await w.vm.init()
    expect(w.get('.page-head h1').text()).toBe('Edit Article')
  })
  it('shows the conflict banner text from i18n when a version conflict is armed', async () => {
    routeParams = { name: 'article', id: '5' }
    setupStores()
    const get = vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: '5', status: 'x', translations: {}, version: 1 })
    vi.spyOn(itemsApi, 'update').mockRejectedValueOnce(new ApiError(409, 'Conflict', 'VERSION_CONFLICT'))
    const w = mountView()
    await w.vm.init()
    ;(w.vm as any).model.shared.status = 'my-edit'
    get.mockResolvedValueOnce({ id: '5', status: 'x', translations: {}, version: 9 })
    await (w.vm as any).onSubmit()
    expect(w.get('.conflict-banner').text()).toContain('changed by someone else')
  })
```

> `stubs` keeps `Button: true` and `ItemForm: true`; `PageHeader` and `ConfirmDialog` render for real. `ConfirmDialog: true` remains stubbed. Since `Button` is stubbed, the header Save/Delete are stub elements — behaviour is still driven via `w.vm.onSubmit()/onDelete()`.

- [ ] **Step 3: Run tests to verify the new ones fail**

Run: `pnpm --dir frontend test -- ItemFormView.test.ts`
Expected: FAIL — mount throws without i18n until Step 1 helper is used AND the component uses `useI18n`; `.page-head` / `.conflict-banner` structure asserted by new cases not yet present. (After Step 1 wiring, the new cases still fail until Step 4 rewrites the component.)

- [ ] **Step 4: Rewrite `ItemFormView.vue`.** Keep the ENTIRE `<script setup>` verbatim EXCEPT: (a) add `import PageHeader from '../components/common/PageHeader.vue'`; (b) add `import { useI18n } from 'vue-i18n'` and `const { t } = useI18n()` (place the `const { t }` after the other `const` store declarations near `const confirm = useConfirm()`). Replace ONLY the `<template>` and the `<style scoped>` conflict-banner colours. New template:

```vue
<template>
  <section class="item-form-view">
    <ConfirmDialog />
    <p v-if="loading" class="notice">{{ t('itemForm.loading') }}</p>
    <p v-else-if="!meta" class="notice">{{ t('itemForm.collectionNotFound') }}</p>
    <p v-else-if="notFound" class="notice">{{ t('itemForm.itemNotFound') }}</p>
    <p v-else-if="isCreate && !canWrite" class="notice">{{ t('itemForm.noCreatePermission') }}</p>
    <template v-else>
      <PageHeader :title="isCreate ? t('itemForm.new', { label: meta.label }) : t('itemForm.edit', { label: meta.label })">
        <template #lead>
          <Button text severity="secondary" icon="pi pi-chevron-left"
                  :aria-label="t('itemForm.back')" @click="onCancel" />
        </template>
        <template #actions>
          <Button v-if="!isCreate && canDelete" :label="t('itemForm.delete')" severity="danger" @click="onDelete" />
          <Button v-if="canWrite" :label="t('itemForm.save')" :loading="submitting" @click="onSubmit" />
        </template>
      </PageHeader>

      <div v-if="conflict" class="conflict-banner" role="alert">
        <span class="conflict-text">{{ t('itemForm.conflictText') }}</span>
        <Button :label="t('itemForm.reloadLatest')" severity="secondary" size="small" @click="reloadLatest" />
      </div>

      <ItemForm
        :meta="meta"
        :item-id="id"
        :model="model"
        :locales="langStore.languages"
        :errors="errors"
        :server-error="serverError"
        :disabled="!canWrite"
        :submitting="submitting"
        @submit="onSubmit"
      />
    </template>
  </section>
</template>

<style scoped>
.item-form-view { display: grid; gap: 4px; }
.notice { color: var(--muted); }
.conflict-banner {
  display: flex;
  align-items: center;
  gap: 1rem;
  padding: 0.75rem 1rem;
  margin-bottom: 1rem;
  border: 1px solid var(--warn, #d97706);
  background: color-mix(in srgb, var(--warn, #d97706) 10%, var(--surface));
  border-radius: var(--radius, 8px);
  color: var(--fg);
}
.conflict-text { flex: 1; }
</style>
```

> The `@cancel` handler on `<ItemForm>` is removed (ItemForm no longer emits it; Cancel is the `#lead` back button). All script functions — `init`, `onSubmit`, `onDelete`, `onCancel`, `reloadLatest`, `recoverFromConflict`, `guardLeave`, `onBeforeUnload`, the route guards, `defineExpose` — remain byte-unchanged.

- [ ] **Step 5: Run tests to verify they pass**

Run: `pnpm --dir frontend test -- ItemFormView.test.ts`
Expected: PASS — all pre-existing behaviour cases (load, submit, 409 recovery, server-error mapping, delete, dirty guards, beforeunload) plus the 2 new header/i18n cases.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/views/ItemFormView.vue frontend/src/views/ItemFormView.test.ts
git commit -m "feat(frontend): rebuild ItemFormView on page-head action bar + i18n (FE-R5)"
```

---

### Task 6: Full suite gate + build + live smoke

**Files:** none (verification only).

**Interfaces:** none.

- [ ] **Step 1: Run the full unit suite**

Run: `pnpm --dir frontend test`
Expected: PASS — all suites green; total count risen by the FE-R5 additions (localeCompleteness suite + PageHeader `#lead` cases + ItemForm badge/dots cases + ItemFormView header cases + itemForm i18n presence case).

- [ ] **Step 2: Run the typecheck/build gate**

Run: `pnpm --dir frontend build`
Expected: PASS — vue-tsc reports no type errors; Vite build completes. (Required separate gate: vitest strips types.)

- [ ] **Step 3: Live smoke (recommended, not a hard gate).** Start backend + Vite and drive with the `plugin_playwright` MCP (logged-in):

```bash
# backend (background): ASPNETCORE_URLS=http://localhost:5080 dotnet run --no-launch-profile --project src/Struo.Api
# frontend (background): pnpm --dir frontend dev --host 127.0.0.1
```

Verify on a populated collection with translatable fields + relations (e.g. Article): page-head shows "Edit {label}" + `[Delete] [Save]` + back chevron; switching locale tabs shows the completeness dot fill when a translatable field has content and go hollow when empty; edit each field type incl. RichText and Save → returns to the collection list; open an item → Delete → confirm; make an edit then navigate away → unsaved-changes prompt; toggle dark/light and confirm token lockstep; browser console shows 0 errors.

- [ ] **Step 4: Whole-branch review + merge.** Opus whole-branch review (Ready-to-merge gate), then:

```bash
git checkout main
git merge --no-ff <fe-r5-branch> -m "Merge FE-R5: item form (frontend redesign slice 5)"
```

---

## Self-Review

**1. Spec coverage:**
- §1 rebuild ItemFormView (assembly + strings) → Task 5. ✓
- §1 re-skin ItemForm (badge, dots, remove actions, keep `<form>`) → Task 4. ✓
- §1 extend PageHeader `#lead` → Task 3. ✓
- §1 `localeCompleteness.ts` → Task 2. ✓
- §1 `itemForm` i18n ns → Task 1. ✓
- §2 preserved behaviour → carried verbatim in Task 5 Step 4; existing view/form tests retained (re-mounted through helpers). ✓
- §3.1 `#lead` before `.titles`, absent when unused → Task 3. ✓
- §3.3 dots semantics (any content) + shown only when translatable && >1 locale → Task 2 + Task 4 (`showDots`). ✓
- §4 i18n keys (incl. `{label}` params on new/edit) → Task 1 + used in Task 5. ✓
- §5 styling with OKLch/status tokens, conflict banner re-skin warn token → Task 4/5 style blocks. ✓
- §6 tests (unit/component/view) + build gate → Tasks 2–6. ✓
- §9 risks: Save relocation (both header click + `<form>` Enter tested) ✓; `#lead` backward-compat test ✓; `@cancel` removal single-consumer update in same slice ✓; no new deps ✓.

**2. Placeholder scan:** No TBD/TODO/"handle edge cases"/"similar to Task N". All code shown in full. ✓

**3. Type consistency:** `hasLocaleContent(fields: FieldMeta[], values: Record<string, unknown>): boolean` defined in Task 2, consumed with that exact signature in Task 4. `PageHeader` `#lead` slot named consistently across Tasks 3 & 5. `itemForm` key names identical across Tasks 1, 4, 5. `ItemForm` emit reduced to `submit` in Task 4; Task 5 template binds only `@submit`. `defineExpose` surfaces unchanged. ✓
