# Phase 7g.6 — Frontend field-type registry + lazy i18n tabs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the six scattered per-field-type code sites in the Vue admin SPA with a single field-type **registry** keyed by `FieldInterface`, fold the empty-`Guid?` coercion into it (F2), and make the i18n locale tabs lazy (F3) — a pure refactor that adds no new field types.

**Architecture:** A `lib/fieldTypes/` module exports a `Record<FieldInterface, FieldTypeDef>` registry (component + `defaultValue`/`parse`/`serialize`/`listColumn`) and `getFieldType(iface)` (unknown → read-only). Each field type renders through a small uniform-contract `*Field.vue` wrapper. `FieldInput`, `parseItemToForm`, `buildItemPayload`, `selectListColumns`, and `formatCell` all delegate to the registry. `ItemForm` mounts only the active locale's translatable fields.

**Tech Stack:** Vue 3.5 (`<script setup lang="ts">`), PrimeVue 4, Vitest 4 + `@vue/test-utils` 2 (jsdom), `vue-tsc` for typecheck. Package manager: **pnpm**, run from `frontend/`.

## Global Constraints

- **Zero behaviour change for F1/F2** — the existing tests (`parseItemToForm.test.ts`, `buildItemPayload.test.ts`, `validateItem.test.ts`, `selectListColumns.test.ts`, `formatCell.test.ts`, `FieldInput.test.ts`) MUST stay green as characterization. Only the **F3** lazy-tabs change (Task 11) deliberately alters `ItemForm.test.ts`.
- **No new field types.** `json`, `keyValue`, `repeater`, `files`, `multiSelect`, `checkboxGroup`, `tags`, `hidden`, `uuid` map to `ReadonlyField` — identical to today.
- **No backend change, no DDL, no live gate.** The relations system (`relationInputKind.ts`, `RelationInput.vue`) is NOT touched.
- **Uniform component contract:** every `*Field.vue` is `defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()` + `defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()`.
- **Faithful defaults:** `file`/`image` default value = `null`; every other interface = `''`. `serialize` is identity except `file`/`image` map `''` → `null`.
- **`FieldInterface` union** (33, camelCase, mirrors backend `Struo.Domain.Metadata.Enums.FieldInterface`): `text, textarea, richText, markdown, code, slug, email, url, password, color, phone, number, slider, rating, boolean, checkbox, date, time, dateTime, select, multiSelect, radio, checkboxGroup, tags, json, keyValue, repeater, file, image, files, hidden, divider, uuid`.
- **`listColumn` eligibility must equal today's `SCALAR_INTERFACES`:** non-`null` for `text, textarea, slug, email, url, phone, color, number, slider, rating, boolean, checkbox, date, time, dateTime, select, radio`. Everything else (incl. `password`, `markdown`, `code`, `richText`, `file`, `image`) = `null`.
- Run all commands from `frontend/`. Test one file: `pnpm exec vitest run <path>`. Whole suite: `pnpm test`. Typecheck + build: `pnpm build`.
- Commit messages: conventional commits, no attribution trailer (project convention).

---

## File Structure

**Create:**
- `frontend/src/lib/fieldTypes/types.ts` — `FieldInterface` union, `FieldTypeDef` interface, shared `isEmpty` helper.
- `frontend/src/lib/fieldTypes/registry.ts` — `registry` map + `getFieldType(iface)`.
- `frontend/src/lib/fieldTypes/registry.test.ts` — exhaustiveness, fallback, parse/serialize/default, listColumn parity.
- `frontend/src/components/fields/TextField.vue`, `TextareaField.vue`, `NumberField.vue`, `BooleanField.vue`, `DateField.vue`, `SelectField.vue`, `RadioField.vue`, `DividerField.vue`, `ReadonlyField.vue`, `RichTextField.vue`, `FileField.vue`.
- `frontend/src/components/fields/fieldComponents.test.ts` — one test file covering the new components.

**Modify:**
- `frontend/src/components/fields/FieldInput.vue` — `<component :is>` dispatcher.
- `frontend/src/lib/parseItemToForm.ts` — delegate to `def.parse`.
- `frontend/src/lib/buildItemPayload.ts` — delegate to `def.serialize`; drop the file/image string check.
- `frontend/src/lib/selectListColumns.ts` — eligibility via registry.
- `frontend/src/lib/formatCell.ts` — format via registry.
- `frontend/src/components/ItemForm.vue` — lazy locale panels + error-jump.
- `frontend/src/components/ItemForm.test.ts` — updated for lazy behaviour.

**Delete:**
- `frontend/src/lib/fieldInputKind.ts` and `frontend/src/lib/fieldInputKind.test.ts` (subsumed by the registry).

---

## Task 1: `fieldTypes/types.ts` — contract + union

**Files:**
- Create: `frontend/src/lib/fieldTypes/types.ts`
- Test: `frontend/src/lib/fieldTypes/types.test.ts`

**Interfaces:**
- Produces: `type FieldInterface` (33-member string union); `interface FieldTypeDef { component: Component; defaultValue(field: FieldMeta): unknown; parse(raw: unknown, field: FieldMeta): unknown; serialize(value: unknown, field: FieldMeta): unknown; listColumn: { format(value: unknown, field: FieldMeta): string } | null; validate?(value: unknown, field: FieldMeta): string | null }`; `function isEmpty(v: unknown): boolean`; `const ALL_FIELD_INTERFACES: readonly FieldInterface[]`.

- [ ] **Step 1: Write the failing test**

```ts
// frontend/src/lib/fieldTypes/types.test.ts
import { describe, it, expect } from 'vitest'
import { isEmpty, ALL_FIELD_INTERFACES } from './types'

describe('fieldTypes/types', () => {
  it('isEmpty treats null, undefined and empty string as empty', () => {
    expect(isEmpty(null)).toBe(true)
    expect(isEmpty(undefined)).toBe(true)
    expect(isEmpty('')).toBe(true)
    expect(isEmpty(0)).toBe(false)
    expect(isEmpty('x')).toBe(false)
    expect(isEmpty(false)).toBe(false)
  })

  it('lists all 33 backend field interfaces', () => {
    expect(ALL_FIELD_INTERFACES).toHaveLength(33)
    expect(new Set(ALL_FIELD_INTERFACES).size).toBe(33)
    expect(ALL_FIELD_INTERFACES).toContain('richText')
    expect(ALL_FIELD_INTERFACES).toContain('dateTime')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm exec vitest run src/lib/fieldTypes/types.test.ts`
Expected: FAIL — cannot resolve `./types`.

- [ ] **Step 3: Write minimal implementation**

```ts
// frontend/src/lib/fieldTypes/types.ts
import type { Component } from 'vue'
import type { FieldMeta } from '../../types/schema'

// Mirrors backend Struo.Domain.Metadata.Enums.FieldInterface (camelCase).
// MUST be kept in sync with that enum — adding a backend interface requires adding it here.
export type FieldInterface =
  | 'text' | 'textarea' | 'richText' | 'markdown' | 'code'
  | 'slug' | 'email' | 'url' | 'password' | 'color' | 'phone'
  | 'number' | 'slider' | 'rating'
  | 'boolean' | 'checkbox'
  | 'date' | 'time' | 'dateTime'
  | 'select' | 'multiSelect' | 'radio' | 'checkboxGroup' | 'tags'
  | 'json' | 'keyValue' | 'repeater'
  | 'file' | 'image' | 'files'
  | 'hidden' | 'divider' | 'uuid'

export const ALL_FIELD_INTERFACES: readonly FieldInterface[] = [
  'text', 'textarea', 'richText', 'markdown', 'code',
  'slug', 'email', 'url', 'password', 'color', 'phone',
  'number', 'slider', 'rating',
  'boolean', 'checkbox',
  'date', 'time', 'dateTime',
  'select', 'multiSelect', 'radio', 'checkboxGroup', 'tags',
  'json', 'keyValue', 'repeater',
  'file', 'image', 'files',
  'hidden', 'divider', 'uuid',
]

export interface FieldTypeDef {
  component: Component
  defaultValue(field: FieldMeta): unknown
  parse(raw: unknown, field: FieldMeta): unknown
  serialize(value: unknown, field: FieldMeta): unknown
  listColumn: { format(value: unknown, field: FieldMeta): string } | null
  validate?(value: unknown, field: FieldMeta): string | null
}

export function isEmpty(v: unknown): boolean {
  return v === undefined || v === null || v === ''
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm exec vitest run src/lib/fieldTypes/types.test.ts`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/lib/fieldTypes/types.ts frontend/src/lib/fieldTypes/types.test.ts
git commit -m "refactor(frontend): field-type registry contract + FieldInterface union (7g.6)"
```

---

## Task 2: Simple input components (Text/Textarea/Number/Boolean/Date)

**Files:**
- Create: `frontend/src/components/fields/TextField.vue`, `TextareaField.vue`, `NumberField.vue`, `BooleanField.vue`, `DateField.vue`
- Test: `frontend/src/components/fields/fieldComponents.test.ts`

**Interfaces:**
- Produces: five components, each `props { field: FieldMeta; modelValue: unknown; disabled?: boolean }`, emit `update:modelValue`. `TextField`/`TextareaField` bind `field.maxLength`; `DateField` derives `time-only` (interface `time`) / `show-time` (interface `dateTime`).

- [ ] **Step 1: Write the failing test**

```ts
// frontend/src/components/fields/fieldComponents.test.ts
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import TextField from './TextField.vue'
import TextareaField from './TextareaField.vue'
import NumberField from './NumberField.vue'
import BooleanField from './BooleanField.vue'
import DateField from './DateField.vue'
import type { FieldMeta } from '../../types/schema'

function field(over: Partial<FieldMeta> & { interface: string }): FieldMeta {
  return { name: 'f', label: 'F', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over } as FieldMeta
}
const opts = { global: { plugins: [PrimeVue] } }

describe('field components (simple inputs)', () => {
  it('TextField renders an input, binds maxlength, and emits on input', async () => {
    const w = mount(TextField, { props: { field: field({ interface: 'text', maxLength: 50 }), modelValue: '' }, ...opts })
    const input = w.get('input')
    expect(input.attributes('maxlength')).toBe('50')
    await input.setValue('hi')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual(['hi'])
  })

  it('TextField omits maxlength when field has none', () => {
    const w = mount(TextField, { props: { field: field({ interface: 'text', maxLength: null }), modelValue: '' }, ...opts })
    expect(w.get('input').attributes('maxlength')).toBeUndefined()
  })

  it('TextareaField renders a textarea and binds maxlength', () => {
    const w = mount(TextareaField, { props: { field: field({ interface: 'textarea', maxLength: 20 }), modelValue: '' }, ...opts })
    expect(w.get('textarea').attributes('maxlength')).toBe('20')
  })

  it('NumberField renders a numeric input', () => {
    const w = mount(NumberField, { props: { field: field({ interface: 'number' }), modelValue: 3 }, ...opts })
    expect(w.find('input').exists()).toBe(true)
  })

  it('BooleanField renders a checkbox and is disabled when asked', () => {
    const w = mount(BooleanField, { props: { field: field({ interface: 'boolean' }), modelValue: true, disabled: true }, ...opts })
    expect(w.find('input[type="checkbox"]').exists()).toBe(true)
  })

  it('DateField sets time-only for time and show-time for dateTime', () => {
    const t = mount(DateField, { props: { field: field({ interface: 'time' }), modelValue: null }, ...opts })
    expect(t.findComponent({ name: 'DatePicker' }).props('timeOnly')).toBe(true)
    const dt = mount(DateField, { props: { field: field({ interface: 'dateTime' }), modelValue: null }, ...opts })
    expect(dt.findComponent({ name: 'DatePicker' }).props('showTime')).toBe(true)
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm exec vitest run src/components/fields/fieldComponents.test.ts`
Expected: FAIL — cannot resolve `./TextField.vue`.

- [ ] **Step 3: Write minimal implementation**

```vue
<!-- frontend/src/components/fields/TextField.vue -->
<script setup lang="ts">
import InputText from 'primevue/inputtext'
import type { FieldMeta } from '../../types/schema'
defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()
</script>
<template>
  <InputText :model-value="(modelValue as string)" :disabled="disabled"
    :maxlength="field.maxLength ?? undefined"
    @update:model-value="$emit('update:modelValue', $event)" />
</template>
```

```vue
<!-- frontend/src/components/fields/TextareaField.vue -->
<script setup lang="ts">
import Textarea from 'primevue/textarea'
import type { FieldMeta } from '../../types/schema'
defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()
</script>
<template>
  <Textarea :model-value="(modelValue as string)" :disabled="disabled" :rows="6"
    :maxlength="field.maxLength ?? undefined"
    @update:model-value="$emit('update:modelValue', $event)" />
</template>
```

```vue
<!-- frontend/src/components/fields/NumberField.vue -->
<script setup lang="ts">
import InputNumber from 'primevue/inputnumber'
import type { FieldMeta } from '../../types/schema'
defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()
</script>
<template>
  <InputNumber :model-value="(modelValue as number)" :disabled="disabled"
    @update:model-value="$emit('update:modelValue', $event)" />
</template>
```

```vue
<!-- frontend/src/components/fields/BooleanField.vue -->
<script setup lang="ts">
import Checkbox from 'primevue/checkbox'
import type { FieldMeta } from '../../types/schema'
defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()
</script>
<template>
  <Checkbox :model-value="(modelValue as boolean)" :binary="true" :disabled="disabled"
    @update:model-value="$emit('update:modelValue', $event)" />
</template>
```

```vue
<!-- frontend/src/components/fields/DateField.vue -->
<script setup lang="ts">
import { computed } from 'vue'
import DatePicker from 'primevue/datepicker'
import type { FieldMeta } from '../../types/schema'
const props = defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()
const timeOnly = computed(() => props.field.interface === 'time')
const showTime = computed(() => props.field.interface === 'dateTime')
</script>
<template>
  <DatePicker :model-value="(modelValue as Date)" :time-only="timeOnly" :show-time="showTime"
    :disabled="disabled" @update:model-value="$emit('update:modelValue', $event)" />
</template>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm exec vitest run src/components/fields/fieldComponents.test.ts`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/fields/TextField.vue frontend/src/components/fields/TextareaField.vue frontend/src/components/fields/NumberField.vue frontend/src/components/fields/BooleanField.vue frontend/src/components/fields/DateField.vue frontend/src/components/fields/fieldComponents.test.ts
git commit -m "refactor(frontend): uniform-contract Text/Textarea/Number/Boolean/Date field components (7g.6)"
```

---

## Task 3: Choice + structural components (Select/Radio/Divider/Readonly)

**Files:**
- Create: `frontend/src/components/fields/SelectField.vue`, `RadioField.vue`, `DividerField.vue`, `ReadonlyField.vue`
- Test: `frontend/src/components/fields/fieldComponents.test.ts` (append)

**Interfaces:**
- Produces: `SelectField` (PrimeVue `Select`, `field.options`), `RadioField` (`RadioButton` per option, `.radio-group`/`.radio-option` classes preserved), `DividerField` (`<hr>`), `ReadonlyField` (`<span class="readonly-field">{{ modelValue ?? '—' }}</span>`).

- [ ] **Step 1: Write the failing test (append to fieldComponents.test.ts)**

```ts
// append to frontend/src/components/fields/fieldComponents.test.ts
import SelectField from './SelectField.vue'
import RadioField from './RadioField.vue'
import DividerField from './DividerField.vue'
import ReadonlyField from './ReadonlyField.vue'

describe('field components (choice + structural)', () => {
  it('SelectField exposes its options', () => {
    const w = mount(SelectField, {
      props: { field: field({ interface: 'select', options: [{ value: 'a', label: 'A' }] }), modelValue: 'a' },
      global: { plugins: [PrimeVue] },
    })
    expect(w.findComponent({ name: 'Select' }).props('options')).toEqual([{ value: 'a', label: 'A' }])
  })

  it('RadioField renders one option per choice', () => {
    const w = mount(RadioField, {
      props: { field: field({ interface: 'radio', options: [{ value: 'a', label: 'A' }, { value: 'b', label: 'B' }] }), modelValue: 'a' },
      global: { plugins: [PrimeVue] },
    })
    expect(w.findAll('.radio-option')).toHaveLength(2)
  })

  it('DividerField renders an hr', () => {
    const w = mount(DividerField, { props: { field: field({ interface: 'divider' }), modelValue: '' } })
    expect(w.find('hr').exists()).toBe(true)
  })

  it('ReadonlyField shows the value, em-dash when empty', () => {
    expect(mount(ReadonlyField, { props: { field: field({ interface: 'json' }), modelValue: '{}' } }).text()).toBe('{}')
    expect(mount(ReadonlyField, { props: { field: field({ interface: 'json' }), modelValue: null } }).find('.readonly-field').text()).toBe('—')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm exec vitest run src/components/fields/fieldComponents.test.ts`
Expected: FAIL — cannot resolve `./SelectField.vue`.

- [ ] **Step 3: Write minimal implementation**

```vue
<!-- frontend/src/components/fields/SelectField.vue -->
<script setup lang="ts">
import Select from 'primevue/select'
import type { FieldMeta } from '../../types/schema'
defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()
</script>
<template>
  <Select :model-value="modelValue" :options="field.options ?? []" option-label="label" option-value="value"
    :disabled="disabled" @update:model-value="$emit('update:modelValue', $event)" />
</template>
```

```vue
<!-- frontend/src/components/fields/RadioField.vue -->
<script setup lang="ts">
import RadioButton from 'primevue/radiobutton'
import type { FieldMeta } from '../../types/schema'
defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()
</script>
<template>
  <div class="radio-group">
    <label v-for="opt in field.options ?? []" :key="opt.value" class="radio-option">
      <RadioButton :model-value="modelValue" :value="opt.value" :disabled="disabled"
        @update:model-value="$emit('update:modelValue', $event)" />
      <span>{{ opt.label }}</span>
    </label>
  </div>
</template>
```

```vue
<!-- frontend/src/components/fields/DividerField.vue -->
<script setup lang="ts">
import type { FieldMeta } from '../../types/schema'
defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()
</script>
<template>
  <hr />
</template>
```

```vue
<!-- frontend/src/components/fields/ReadonlyField.vue -->
<script setup lang="ts">
import type { FieldMeta } from '../../types/schema'
defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()
</script>
<template>
  <span class="readonly-field">{{ modelValue ?? '—' }}</span>
</template>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm exec vitest run src/components/fields/fieldComponents.test.ts`
Expected: PASS (10 tests total).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/fields/SelectField.vue frontend/src/components/fields/RadioField.vue frontend/src/components/fields/DividerField.vue frontend/src/components/fields/ReadonlyField.vue frontend/src/components/fields/fieldComponents.test.ts
git commit -m "refactor(frontend): uniform-contract Select/Radio/Divider/Readonly field components (7g.6)"
```

---

## Task 4: Rich-text + file wrapper components

**Files:**
- Create: `frontend/src/components/fields/RichTextField.vue`, `FileField.vue`
- Test: `frontend/src/components/fields/fieldComponents.test.ts` (append)

**Interfaces:**
- Produces: `RichTextField` (wraps existing `RichTextInput`, coerces `modelValue` to `string`), `FileField` (wraps existing `FilePicker`, `image = field.interface === 'image'`). Both relay `update:modelValue`.

- [ ] **Step 1: Write the failing test (append)**

```ts
// append to frontend/src/components/fields/fieldComponents.test.ts
import { setActivePinia, createPinia } from 'pinia'
import { flushPromises } from '@vue/test-utils'
import { vi } from 'vitest'
import RichTextField from './RichTextField.vue'
import FileField from './FileField.vue'
import RichTextInput from './RichTextInput.vue'
import FilePicker from './FilePicker.vue'
import { itemsApi } from '../../api/itemsApi'
import { useLanguageStore } from '../../stores/languageStore'

describe('field components (rich-text + file wrappers)', () => {
  it('RichTextField relays the RichTextInput value', () => {
    const w = mount(RichTextField, {
      props: { field: field({ interface: 'richText' }), modelValue: '<p>hi</p>' },
      global: { stubs: { RichTextInput: { name: 'RichTextInput', props: ['modelValue'], template: '<div class="stub-rt" />' } } },
    })
    const rt = w.findComponent({ name: 'RichTextInput' })
    expect(rt.props('modelValue')).toBe('<p>hi</p>')
    rt.vm.$emit('update:modelValue', '<p>bye</p>')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual(['<p>bye</p>'])
  })

  it('RichTextField coerces a null model to empty string', () => {
    const w = mount(RichTextField, {
      props: { field: field({ interface: 'richText' }), modelValue: null },
      global: { stubs: { RichTextInput: { name: 'RichTextInput', props: ['modelValue'], template: '<div class="stub-rt" />' } } },
    })
    expect(w.findComponent({ name: 'RichTextInput' }).props('modelValue')).toBe('')
  })

  it('FileField sets image=true for the image interface and relays the id', async () => {
    setActivePinia(createPinia())
    useLanguageStore().languages = [{ code: 'en', name: 'English', isDefault: true }]
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1 })
    const w = mount(FileField, {
      props: { field: field({ name: 'heroImageId', interface: 'image' }), modelValue: null },
      global: { stubs: { Dialog: true, Button: true, MediaGrid: true } },
    })
    await flushPromises()
    const picker = w.findComponent(FilePicker)
    expect(picker.props('image')).toBe(true)
    picker.vm.$emit('update:modelValue', 'f1')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual(['f1'])
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm exec vitest run src/components/fields/fieldComponents.test.ts`
Expected: FAIL — cannot resolve `./RichTextField.vue`.

- [ ] **Step 3: Write minimal implementation**

```vue
<!-- frontend/src/components/fields/RichTextField.vue -->
<script setup lang="ts">
import RichTextInput from './RichTextInput.vue'
import type { FieldMeta } from '../../types/schema'
defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()
</script>
<template>
  <RichTextInput :model-value="((modelValue as string) ?? '')" :disabled="disabled"
    @update:model-value="(v: string) => $emit('update:modelValue', v)" />
</template>
```

```vue
<!-- frontend/src/components/fields/FileField.vue -->
<script setup lang="ts">
import { computed } from 'vue'
import FilePicker from './FilePicker.vue'
import type { FieldMeta } from '../../types/schema'
const props = defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()
const isImage = computed(() => props.field.interface === 'image')
</script>
<template>
  <FilePicker :model-value="(modelValue as string | null)" :image="isImage" :disabled="disabled"
    @update:model-value="(v: string | null) => $emit('update:modelValue', v)" />
</template>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm exec vitest run src/components/fields/fieldComponents.test.ts`
Expected: PASS (13 tests total).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/fields/RichTextField.vue frontend/src/components/fields/FileField.vue frontend/src/components/fields/fieldComponents.test.ts
git commit -m "refactor(frontend): RichText/File wrapper field components with uniform contract (7g.6)"
```

---

## Task 5: The registry + `getFieldType`

**Files:**
- Create: `frontend/src/lib/fieldTypes/registry.ts`, `frontend/src/lib/fieldTypes/registry.test.ts`

**Interfaces:**
- Consumes: `FieldTypeDef`, `FieldInterface`, `ALL_FIELD_INTERFACES` from `./types`; all 11 `*Field.vue` components.
- Produces: `const registry: Record<FieldInterface, FieldTypeDef>`; `function getFieldType(iface: string): FieldTypeDef` (unknown → `ReadonlyField` def).

- [ ] **Step 1: Write the failing test**

```ts
// frontend/src/lib/fieldTypes/registry.test.ts
import { describe, it, expect } from 'vitest'
import { getFieldType } from './registry'
import { ALL_FIELD_INTERFACES } from './types'
import type { FieldMeta } from '../../types/schema'
import { formatCell } from '../formatCell'

function field(over: Partial<FieldMeta> & { interface: string }): FieldMeta {
  return { name: 'f', label: 'F', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over } as FieldMeta
}

const LIST_ELIGIBLE = new Set([
  'text', 'textarea', 'slug', 'email', 'url', 'phone', 'color',
  'number', 'slider', 'rating', 'boolean', 'checkbox',
  'date', 'time', 'dateTime', 'select', 'radio',
])

describe('field-type registry', () => {
  it('resolves a def for every known interface', () => {
    for (const i of ALL_FIELD_INTERFACES) expect(getFieldType(i).component).toBeTruthy()
  })

  it('falls back to the read-only def for unknown interfaces', () => {
    expect(getFieldType('somethingNew').component).toBe(getFieldType('json').component)
    expect(getFieldType('somethingNew').listColumn).toBeNull()
  })

  it('lists exactly the legacy scalar interfaces as list columns', () => {
    for (const i of ALL_FIELD_INTERFACES) {
      const eligible = getFieldType(i).listColumn !== null
      expect(eligible, i).toBe(LIST_ELIGIBLE.has(i))
    }
  })

  it('defaults file/image to null and everything else to empty string', () => {
    const f = field({ interface: 'file' })
    expect(getFieldType('file').defaultValue(f)).toBeNull()
    expect(getFieldType('image').defaultValue(f)).toBeNull()
    expect(getFieldType('text').defaultValue(field({ interface: 'text' }))).toBe('')
    expect(getFieldType('richText').defaultValue(field({ interface: 'richText' }))).toBe('')
  })

  it('parse returns the raw value or the default when nullish', () => {
    expect(getFieldType('text').parse('hi', field({ interface: 'text' }))).toBe('hi')
    expect(getFieldType('text').parse(undefined, field({ interface: 'text' }))).toBe('')
    expect(getFieldType('file').parse(undefined, field({ interface: 'file' }))).toBeNull()
  })

  it('serialize coerces empty string to null only for file/image', () => {
    expect(getFieldType('file').serialize('', field({ interface: 'file' }))).toBeNull()
    expect(getFieldType('image').serialize('', field({ interface: 'image' }))).toBeNull()
    expect(getFieldType('file').serialize('id1', field({ interface: 'file' }))).toBe('id1')
    expect(getFieldType('text').serialize('', field({ interface: 'text' }))).toBe('')
  })

  it('list formatters match the legacy formatCell output', () => {
    const sel = field({ interface: 'select', options: [{ value: 'draft', label: 'Draft' }] })
    expect(getFieldType('select').listColumn!.format('draft', sel)).toBe(formatCell('draft', sel))
    const bool = field({ interface: 'boolean' })
    expect(getFieldType('boolean').listColumn!.format(true, bool)).toBe('Yes')
    const dt = field({ interface: 'dateTime' })
    expect(getFieldType('dateTime').listColumn!.format('2026-01-02T03:04:05Z', dt)).toContain('2026')
    expect(getFieldType('dateTime').listColumn!.format('not-a-date', dt)).toBe('not-a-date')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm exec vitest run src/lib/fieldTypes/registry.test.ts`
Expected: FAIL — cannot resolve `./registry`.

- [ ] **Step 3: Write minimal implementation**

```ts
// frontend/src/lib/fieldTypes/registry.ts
import type { Component } from 'vue'
import type { FieldMeta } from '../../types/schema'
import type { FieldInterface, FieldTypeDef } from './types'
import TextField from '../../components/fields/TextField.vue'
import TextareaField from '../../components/fields/TextareaField.vue'
import RichTextField from '../../components/fields/RichTextField.vue'
import NumberField from '../../components/fields/NumberField.vue'
import BooleanField from '../../components/fields/BooleanField.vue'
import DateField from '../../components/fields/DateField.vue'
import SelectField from '../../components/fields/SelectField.vue'
import RadioField from '../../components/fields/RadioField.vue'
import DividerField from '../../components/fields/DividerField.vue'
import FileField from '../../components/fields/FileField.vue'
import ReadonlyField from '../../components/fields/ReadonlyField.vue'

type ListColumn = FieldTypeDef['listColumn']
const asString: ListColumn = { format: (v) => String(v) }
const asYesNo: ListColumn = { format: (v) => (v ? 'Yes' : 'No') }
const asDate: ListColumn = {
  format: (v) => { const d = new Date(String(v)); return Number.isNaN(d.getTime()) ? String(v) : d.toLocaleString() },
}
const asOption: ListColumn = {
  format: (v, f) => { const o = f.options?.find((x) => x.value === String(v)); return o ? o.label : String(v) },
}

function def(opts: {
  component: Component
  empty?: unknown
  serialize?: (v: unknown, f: FieldMeta) => unknown
  listColumn?: ListColumn
}): FieldTypeDef {
  const empty = 'empty' in opts ? opts.empty : ''
  return {
    component: opts.component,
    defaultValue: () => empty,
    parse: (raw) => raw ?? empty,
    serialize: opts.serialize ?? ((v) => v),
    listColumn: opts.listColumn ?? null,
  }
}

const fileSerialize = (v: unknown): unknown => (v === '' ? null : v)
const readonlyDef = def({ component: ReadonlyField })

export const registry: Record<FieldInterface, FieldTypeDef> = {
  text: def({ component: TextField, listColumn: asString }),
  slug: def({ component: TextField, listColumn: asString }),
  email: def({ component: TextField, listColumn: asString }),
  url: def({ component: TextField, listColumn: asString }),
  color: def({ component: TextField, listColumn: asString }),
  phone: def({ component: TextField, listColumn: asString }),
  password: def({ component: TextField }),
  textarea: def({ component: TextareaField, listColumn: asString }),
  markdown: def({ component: TextareaField }),
  code: def({ component: TextareaField }),
  richText: def({ component: RichTextField }),
  number: def({ component: NumberField, listColumn: asString }),
  slider: def({ component: NumberField, listColumn: asString }),
  rating: def({ component: NumberField, listColumn: asString }),
  boolean: def({ component: BooleanField, listColumn: asYesNo }),
  checkbox: def({ component: BooleanField, listColumn: asYesNo }),
  date: def({ component: DateField, listColumn: asDate }),
  time: def({ component: DateField, listColumn: asDate }),
  dateTime: def({ component: DateField, listColumn: asDate }),
  select: def({ component: SelectField, listColumn: asOption }),
  radio: def({ component: RadioField, listColumn: asOption }),
  divider: def({ component: DividerField }),
  file: def({ component: FileField, empty: null, serialize: fileSerialize }),
  image: def({ component: FileField, empty: null, serialize: fileSerialize }),
  // Deferred to later 7g+ slices — render read-only for now (unchanged behaviour).
  multiSelect: readonlyDef,
  checkboxGroup: readonlyDef,
  tags: readonlyDef,
  json: readonlyDef,
  keyValue: readonlyDef,
  repeater: readonlyDef,
  files: readonlyDef,
  hidden: readonlyDef,
  uuid: readonlyDef,
}

export function getFieldType(iface: string): FieldTypeDef {
  return registry[iface as FieldInterface] ?? readonlyDef
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm exec vitest run src/lib/fieldTypes/registry.test.ts`
Expected: PASS (7 tests).

- [ ] **Step 5: Typecheck (exhaustiveness proof)**

Run: `pnpm build`
Expected: `vue-tsc -b` clean (a missing interface key would be a compile error here), then `vite build` succeeds.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/lib/fieldTypes/registry.ts frontend/src/lib/fieldTypes/registry.test.ts
git commit -m "refactor(frontend): field-type registry keyed by FieldInterface + getFieldType fallback (7g.6)"
```

---

## Task 6: Rewire `FieldInput.vue` to the registry

**Files:**
- Modify: `frontend/src/components/fields/FieldInput.vue`
- Test: `frontend/src/components/fields/FieldInput.test.ts` (existing — must stay green)

**Interfaces:**
- Consumes: `getFieldType` from `../../lib/fieldTypes/registry`.

- [ ] **Step 1: Confirm the existing test is the safety net**

Run: `pnpm exec vitest run src/components/fields/FieldInput.test.ts`
Expected: PASS currently (baseline). This test stubs PrimeVue components by name and asserts the correct control renders per interface; it must still pass after the rewrite because each `*Field.vue` renders the same underlying PrimeVue component.

- [ ] **Step 2: Replace the dispatcher implementation**

```vue
<!-- frontend/src/components/fields/FieldInput.vue -->
<script setup lang="ts">
import { computed } from 'vue'
import { getFieldType } from '../../lib/fieldTypes/registry'
import type { FieldMeta } from '../../types/schema'

const props = defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()

const def = computed(() => getFieldType(props.field.interface))
const isDisabled = computed(() => props.disabled === true || props.field.readOnly)
</script>

<template>
  <component :is="def.component" :field="field" :model-value="modelValue" :disabled="isDisabled"
    @update:model-value="(v: unknown) => emit('update:modelValue', v)" />
</template>
```

- [ ] **Step 3: Run the FieldInput test to verify it still passes**

Run: `pnpm exec vitest run src/components/fields/FieldInput.test.ts`
Expected: PASS (all existing cases — text/richText/select/json-readonly/maxlength/image).

- [ ] **Step 4: Commit**

```bash
git add frontend/src/components/fields/FieldInput.vue
git commit -m "refactor(frontend): FieldInput dispatches via field-type registry (7g.6)"
```

---

## Task 7: Rewire `parseItemToForm.ts`

**Files:**
- Modify: `frontend/src/lib/parseItemToForm.ts`
- Test: `frontend/src/lib/parseItemToForm.test.ts` (existing — must stay green)

**Interfaces:**
- Consumes: `getFieldType` from `./fieldTypes/registry`.

- [ ] **Step 1: Confirm baseline**

Run: `pnpm exec vitest run src/lib/parseItemToForm.test.ts`
Expected: PASS (baseline).

- [ ] **Step 2: Delegate per-field parse to the registry**

Replace the two default-value sites. New `parseItemToForm.ts`:

```ts
import type { CollectionMeta, LanguageInfo } from '../types/schema'
import type { FormModel } from '../types/itemForm'
import { splitFields } from './splitFields'
import { relationInputKind } from './relationInputKind'
import { getFieldType } from './fieldTypes/registry'

export function parseItemToForm(
  meta: CollectionMeta,
  item: Record<string, unknown>,
  locales: LanguageInfo[],
): FormModel {
  const { shared, translatable } = splitFields(meta)
  const sharedModel: Record<string, unknown> = {}
  for (const f of shared) sharedModel[f.name] = getFieldType(f.interface).parse(item[f.name], f)

  const itemTranslations = (item.translations ?? {}) as Record<string, Record<string, unknown>>
  const translations: Record<string, Record<string, unknown>> = {}
  for (const loc of locales) {
    const src = itemTranslations[loc.code] ?? {}
    const entry: Record<string, unknown> = {}
    for (const f of translatable) entry[f.name] = getFieldType(f.interface).parse(src[f.name], f)
    translations[loc.code] = entry
  }
  const relations: Record<string, unknown> = {}
  for (const rel of meta.relations ?? []) {
    const kind = relationInputKind(rel.interface)
    if (kind === 'dropdown' || kind === 'treeSelect') {
      const nested = item[rel.name] as { id?: unknown } | null | undefined
      relations[rel.name] = nested?.id ?? null
    } else if (kind === 'tagSelect') {
      const arr = (item[rel.name] as Array<{ id?: unknown }> | undefined) ?? []
      relations[rel.name] = arr.map((r) => r.id)
    }
  }
  const version = typeof item.version === 'number' ? item.version : undefined
  return { shared: sharedModel, translations, relations, version }
}

export function blankItemForm(meta: CollectionMeta, locales: LanguageInfo[]): FormModel {
  return parseItemToForm(meta, {}, locales)
}
```

- [ ] **Step 3: Run the test to verify it still passes**

Run: `pnpm exec vitest run src/lib/parseItemToForm.test.ts`
Expected: PASS. (`file`/`image` still default to `null`; other fields to `''` — identical to before.)

- [ ] **Step 4: Commit**

```bash
git add frontend/src/lib/parseItemToForm.ts
git commit -m "refactor(frontend): parseItemToForm delegates default/parse to the registry (7g.6)"
```

---

## Task 8: Rewire `buildItemPayload.ts` (folds in F2)

**Files:**
- Modify: `frontend/src/lib/buildItemPayload.ts`
- Test: `frontend/src/lib/buildItemPayload.test.ts` (existing — must stay green)

**Interfaces:**
- Consumes: `getFieldType` from `./fieldTypes/registry`; `isEmpty` from `./fieldTypes/types`.

- [ ] **Step 1: Confirm baseline**

Run: `pnpm exec vitest run src/lib/buildItemPayload.test.ts`
Expected: PASS (baseline).

- [ ] **Step 2: Delegate per-field serialize to the registry; remove the inline file/image check**

New `buildItemPayload.ts`:

```ts
import type { CollectionMeta, LanguageInfo } from '../types/schema'
import type { FormModel } from '../types/itemForm'
import { splitFields } from './splitFields'
import { relationInputKind } from './relationInputKind'
import { getFieldType } from './fieldTypes/registry'
import { isEmpty } from './fieldTypes/types'

function camel(s: string): string {
  return s.length ? s[0].toLowerCase() + s.slice(1) : s
}

export function buildItemPayload(
  meta: CollectionMeta,
  model: FormModel,
  locales: LanguageInfo[],
  mode: 'create' | 'update',
): Record<string, unknown> {
  const { shared, translatable } = splitFields(meta)
  const payload: Record<string, unknown> = {}

  for (const f of shared) {
    // The registry's serialize owns per-type coercion — e.g. file/image '' -> null so a
    // nullable Guid FK never serialises "" (Postgres 22P02 -> 500). See fieldTypes/registry.ts.
    const v = getFieldType(f.interface).serialize(model.shared[f.name], f)
    if (mode === 'update' || !isEmpty(v)) payload[f.name] = v
  }

  const defaultCode = locales.find((l) => l.isDefault)?.code
  const translations: Record<string, Record<string, unknown>> = {}
  for (const loc of locales) {
    const values = model.translations[loc.code] ?? {}
    const hasContent = translatable.some((f) => !isEmpty(values[f.name]))
    const isDefault = loc.code === defaultCode
    if (mode === 'create' && !isDefault && !hasContent) continue
    if (mode === 'update' && !hasContent) continue
    const entry: Record<string, unknown> = {}
    for (const f of translatable) entry[f.name] = getFieldType(f.interface).serialize(values[f.name], f)
    translations[loc.code] = entry
  }
  const relations = model.relations ?? {}
  for (const rel of meta.relations ?? []) {
    if (!(rel.name in relations)) continue
    const kind = relationInputKind(rel.interface)
    if (kind === 'dropdown' || kind === 'treeSelect') {
      if (rel.foreignKey) payload[camel(rel.foreignKey)] = relations[rel.name] ?? null
    } else if (kind === 'tagSelect') {
      payload[rel.name] = relations[rel.name] ?? []
    }
  }

  if (Object.keys(translations).length > 0) payload.translations = translations
  if (mode === 'update' && typeof model.version === 'number') payload.version = model.version
  return payload
}
```

- [ ] **Step 3: Run the test to verify it still passes**

Run: `pnpm exec vitest run src/lib/buildItemPayload.test.ts`
Expected: PASS. (file/image `''`→`null` still applied, now via `getFieldType(...).serialize`.)

- [ ] **Step 4: Commit**

```bash
git add frontend/src/lib/buildItemPayload.ts
git commit -m "refactor(frontend): buildItemPayload serialises via registry, empty-Guid coercion single-homed (7g.6, F2)"
```

---

## Task 9: Rewire `selectListColumns.ts` + `formatCell.ts`

**Files:**
- Modify: `frontend/src/lib/selectListColumns.ts`, `frontend/src/lib/formatCell.ts`
- Test: `frontend/src/lib/selectListColumns.test.ts`, `frontend/src/lib/formatCell.test.ts` (existing — must stay green)

**Interfaces:**
- Consumes: `getFieldType` from `./fieldTypes/registry`.

- [ ] **Step 1: Confirm baseline**

Run: `pnpm exec vitest run src/lib/selectListColumns.test.ts src/lib/formatCell.test.ts`
Expected: PASS (baseline).

- [ ] **Step 2: Registry-drive eligibility and formatting**

New `selectListColumns.ts` (replace the `SCALAR_INTERFACES` set with a registry check):

```ts
import type { CollectionMeta, FieldMeta } from '../types/schema'
import { getFieldType } from './fieldTypes/registry'

export type ColumnDef = { field: string; header: string; sortable: boolean }

const MAX_COLUMNS = 6

export function selectListColumns(meta: CollectionMeta): ColumnDef[] {
  const eligible = meta.fields.filter(
    (f) => !f.isSystem && !f.hidden && getFieldType(f.interface).listColumn !== null,
  )

  const ordered: FieldMeta[] = []
  const ddf = meta.defaultDisplayField
  if (ddf) {
    const hit = eligible.find((f) => f.name === ddf)
    if (hit) ordered.push(hit)
  }
  for (const f of eligible) {
    if (!ordered.includes(f)) ordered.push(f)
  }

  return ordered.slice(0, MAX_COLUMNS).map((f) => ({
    field: f.name,
    header: f.label,
    sortable: f.sortable,
  }))
}
```

New `formatCell.ts`:

```ts
import type { FieldMeta } from '../types/schema'
import { getFieldType } from './fieldTypes/registry'

export function formatCell(value: unknown, field: FieldMeta): string {
  if (value === null || value === undefined || value === '') return '—'
  const def = getFieldType(field.interface)
  return def.listColumn ? def.listColumn.format(value, field) : String(value)
}
```

- [ ] **Step 3: Run the tests to verify they still pass**

Run: `pnpm exec vitest run src/lib/selectListColumns.test.ts src/lib/formatCell.test.ts`
Expected: PASS (eligibility set unchanged; formatter output unchanged).

- [ ] **Step 4: Commit**

```bash
git add frontend/src/lib/selectListColumns.ts frontend/src/lib/formatCell.ts
git commit -m "refactor(frontend): list column eligibility + cell formatting via registry (7g.6)"
```

---

## Task 10: Retire `fieldInputKind`

**Files:**
- Delete: `frontend/src/lib/fieldInputKind.ts`, `frontend/src/lib/fieldInputKind.test.ts`

**Interfaces:**
- None produced. Removes the now-unused coarse-kind map.

- [ ] **Step 1: Verify nothing still imports it**

Run: `git grep -n "fieldInputKind" -- frontend/src`
Expected: matches only in `fieldInputKind.ts` and `fieldInputKind.test.ts` (no other importers — `FieldInput.vue` was rewired in Task 6).
If any other file still imports it, STOP and rewire that file to `getFieldType` first.

- [ ] **Step 2: Delete the files**

```bash
git rm frontend/src/lib/fieldInputKind.ts frontend/src/lib/fieldInputKind.test.ts
```

- [ ] **Step 3: Run the whole suite + typecheck to prove no dangling reference**

Run: `pnpm test`
Expected: PASS (no missing-module errors).
Run: `pnpm build`
Expected: clean.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor(frontend): retire fieldInputKind (subsumed by the registry) (7g.6)"
```

---

## Task 11: Lazy i18n tabs in `ItemForm.vue` (F3)

**Files:**
- Modify: `frontend/src/components/ItemForm.vue`
- Test: `frontend/src/components/ItemForm.test.ts` (deliberately updated for lazy behaviour)

**Interfaces:**
- Consumes: existing props (`meta`, `model`, `locales`, `errors`, ...). No new exports.

- [ ] **Step 1: Update the test for lazy panels (write the new expectations first)**

Replace the first test and add two new ones in `ItemForm.test.ts`. The `Tabs` stub must reflect the active value so only the active locale's panel renders; use a stub that models `v-model:value`:

```ts
// in ItemForm.test.ts — replace the `stubs` Tabs/TabPanel entries with active-aware stubs:
const stubs = {
  FieldInput: { props: ['field', 'modelValue', 'disabled'], template: '<div class="field-input" :data-name="field.name" />' },
  Button: { props: ['label'], template: '<button :data-label="label" @click="$emit(\'click\')">{{ label }}</button>' },
  Tabs: { props: ['value'], emits: ['update:value'], template: '<div class="tabs"><slot /></div>' },
  TabList: { template: '<div><slot /></div>' },
  Tab: { props: ['value'], template: '<button class="tab" @click="$emit(\'click\')"><slot /></button>' },
  TabPanels: { template: '<div><slot /></div>' },
  TabPanel: { props: ['value'], template: '<div class="tab-panel"><slot /></div>' },
}
```

```ts
// replace the existing 'renders shared fields once and a tab per locale' test:
it('renders a tab per locale but mounts only the active locale panel body', () => {
  const w = mount(ItemForm, { props: { meta, model, locales, errors: {} }, global: { stubs } })
  expect(w.findAll('.tab')).toHaveLength(2)
  // 1 shared (status) + translatable title for the ACTIVE locale only (1) = 2 FieldInputs
  expect(w.findAll('.field-input')).toHaveLength(2)
})

it('jumps the active tab to the default locale when validation errors appear', async () => {
  const w = mount(ItemForm, { props: { meta, model, locales, errors: {} }, global: { stubs } })
  // move off the default locale, then surface an error
  ;(w.vm as unknown as { activeLocale: string }).activeLocale = 'zh-TW'
  await w.setProps({ errors: { title: 'Title is required.' } })
  expect((w.vm as unknown as { activeLocale: string }).activeLocale).toBe('en')
})
```

> Note: `activeLocale` must be exposed for the test via `defineExpose({ activeLocale })` (added in Step 3).

- [ ] **Step 2: Run the test to verify it fails**

Run: `pnpm exec vitest run src/components/ItemForm.test.ts`
Expected: FAIL — currently 3 `.field-input` (all locales mounted) and no `activeLocale` jump.

- [ ] **Step 3: Implement lazy panels + error-jump**

New `ItemForm.vue` (only the `<script setup>` additions and the `Tabs`/`TabPanel` block change; keep the rest identical):

```vue
<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import Button from 'primevue/button'
import Tabs from 'primevue/tabs'
import TabList from 'primevue/tablist'
import Tab from 'primevue/tab'
import TabPanels from 'primevue/tabpanels'
import TabPanel from 'primevue/tabpanel'
import FieldInput from './fields/FieldInput.vue'
import RelationInput from './fields/RelationInput.vue'
import { splitFields } from '../lib/splitFields'
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
const emit = defineEmits<{ (e: 'submit'): void; (e: 'cancel'): void }>()

const fields = computed(() => splitFields(props.meta))
const defaultCode = computed(() => props.locales.find((l) => l.isDefault)?.code ?? props.locales[0]?.code ?? '')
const activeLocale = ref(props.locales[0]?.code ?? '')

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
      <h3>Relations</h3>
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
          {{ loc.name }}<span v-if="loc.isDefault"> *</span>
        </Tab>
      </TabList>
      <TabPanels>
        <TabPanel v-for="loc in locales" :key="loc.code" :value="loc.code">
          <template v-if="loc.code === activeLocale">
            <div v-for="f in fields.translatable" :key="f.name" class="field">
              <label>{{ f.label }}<span v-if="f.required && loc.isDefault" class="req">*</span></label>
              <FieldInput :field="f" v-model="model.translations[loc.code][f.name]" :disabled="disabled" />
              <small v-if="loc.isDefault && errors[f.name]" class="field-error" role="alert">{{ errors[f.name] }}</small>
            </div>
          </template>
        </TabPanel>
      </TabPanels>
    </Tabs>

    <div class="actions">
      <Button type="button" label="Cancel" severity="secondary" @click="emit('cancel')" />
      <Button v-if="!disabled" type="submit" label="Save" :loading="submitting" />
    </div>
  </form>
</template>
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `pnpm exec vitest run src/components/ItemForm.test.ts`
Expected: PASS (tab-per-locale + only-active-panel-mounted + error-jump; the unchanged server-error/submit/cancel/disabled/relation tests still pass).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/ItemForm.vue frontend/src/components/ItemForm.test.ts
git commit -m "feat(frontend): lazy-mount only the active locale's translatable fields + error-jump to default (7g.6, F3)"
```

---

## Task 12: Full green gate + docs

**Files:**
- Modify: `docs/ROADMAP.md`
- Reference: none

- [ ] **Step 1: Run the full frontend suite**

Run (from `frontend/`): `pnpm test`
Expected: all green — the 177 pre-existing minus the removed `fieldInputKind` cases (≈11), plus the new `types`/`registry`/`fieldComponents` cases and the updated `ItemForm` cases. Net count is higher; **zero failures** is the gate.

- [ ] **Step 2: Typecheck + production build**

Run: `pnpm build`
Expected: `vue-tsc -b` clean (registry exhaustiveness enforced), `vite build` succeeds (the pre-existing >500 kB chunk advisory is acceptable).

- [ ] **Step 3: Confirm no backend impact**

Run (from repo root): `git status --porcelain`
Expected: only files under `frontend/` and `docs/` changed — no `src/` (backend) changes. (No backend build/test or live gate is required for this slice.)

- [ ] **Step 4: Update the roadmap**

In `docs/ROADMAP.md`, add a `7g.6` row to the Phases table (after the `7g.5` row) and a status paragraph. Row:

```markdown
| 7g.6 | Frontend field-type registry (`lib/fieldTypes/*`, keyed by `FieldInterface`, TS-exhaustive, unknown→read-only) + empty-`Guid?` coercion single-homed (F2) + lazy i18n tabs (F3) — *pure refactor, no new field types, no backend* | ✅ done (frontend gates green; no live gate — no server/persistence change) | [spec](superpowers/specs/2026-07-06-phase7g6-field-type-registry-design.md) | [plan](superpowers/plans/2026-07-06-phase7g6-field-type-registry.md) |
```

Also change the "Next up" note so 7g+ now stands on the registry: adding a field type = one registry entry + one `*Field.vue` + tests.

- [ ] **Step 5: Commit**

```bash
git add docs/ROADMAP.md
git commit -m "docs: Phase 7g.6 done (frontend field-type registry + lazy tabs; pure refactor) (7g.6)"
```

---

## Self-Review notes (for the implementer)

- **Spec coverage:** F1 registry = Tasks 1–9; F2 empty-`Guid?` single-homed = Task 8 (via file/image def in Task 5); F3 lazy tabs = Task 11; `fieldInputKind` retirement = Task 10; exhaustiveness = Task 5 Step 5 typecheck + `registry.test.ts`; graceful unknown→readonly = Task 5. Non-goals honoured: no new field types (all deferred interfaces → `ReadonlyField`), no backend/DDL/live-gate (Task 12 Step 3 asserts this), relations untouched.
- **Characterization safety net:** Tasks 6–9 keep the existing `FieldInput`/`parseItemToForm`/`buildItemPayload`/`selectListColumns`/`formatCell` tests green — that is the proof of zero behaviour change for F1/F2. Only `ItemForm.test.ts` changes (Task 11), because F3 is a deliberate behaviour change.
- **Type consistency:** `getFieldType(iface: string): FieldTypeDef` and `registry: Record<FieldInterface, FieldTypeDef>` are used identically in Tasks 6–9; `isEmpty` imported from `./fieldTypes/types` in Task 8; `defaultValue`/`parse`/`serialize`/`listColumn` names match across Tasks 1, 5, 7, 8, 9.
- **If `FieldInput.test.ts` breaks in Task 6** because a stub resolves differently through the wrapper: the wrappers render the same PrimeVue component names, so the by-name stubs should still hit. If a specific assertion needs the intermediate component, update it to `findComponent(TheField)` — but prefer keeping the existing assertions to preserve the characterization guarantee.
