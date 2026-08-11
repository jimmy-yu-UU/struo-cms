import { describe, it, expect } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import ItemForm from './ItemForm.vue'
import RelationInput from './fields/RelationInput.vue'
import type { CollectionMeta, FieldMeta, LanguageInfo } from '../types/schema'
import type { FormModel } from '../types/itemForm'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { itemForm: {
    relations: 'Relations', translatableBadge: 'Translatable',
    localeComplete: 'Has content', localeIncomplete: 'No content yet',
  } } },
})
type ItemFormProps = InstanceType<typeof ItemForm>['$props']
function mountForm(props: Record<string, unknown>, extraStubs: Record<string, unknown> = {}) {
  return mount(ItemForm, { props: props as unknown as ItemFormProps, global: { plugins: [i18n], stubs: { ...stubs, ...extraStubs } } })
}

function field(name: string, over: Partial<FieldMeta> = {}): FieldMeta {
  return { name, label: name, interface: 'text', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over }
}
const meta: CollectionMeta = { name: 'article', label: 'Article', fields: [
  field('status', { sort: 1 }),
  field('title', { translatable: true, sort: 2 }),
], relations: [] }
const locales: LanguageInfo[] = [
  { code: 'en', name: 'English', isDefault: true },
  { code: 'zh-TW', name: '繁中', isDefault: false },
]
const model: FormModel = { shared: { status: 'draft' }, translations: { en: { title: '' }, 'zh-TW': { title: '' } }, relations: {} }
// Tabs/TabsList/TabsTrigger/TabsContent are thin reka wrappers with no portal, so they mount as
// their real selves rather than stubs. That alone only buys "no PrimeVue global needed"; the tests
// below that exercise the v-model binding and the mount/unmount behaviour do so by actually
// triggering reka's activation event, not merely by mounting the real component.
const stubs = {
  FieldInput: { props: ['field', 'modelValue', 'disabled'], template: '<div class="field-input" :data-name="field.name" />' },
}

describe('ItemForm', () => {
  it('renders a tab per locale but mounts only the active locale panel body', () => {
    const w = mountForm({ meta, model, locales, errors: {} })
    expect(w.findAll('[role="tab"]')).toHaveLength(2)
    // 1 shared (status) + translatable title for the ACTIVE locale only (1) = 2 FieldInputs
    expect(w.findAll('.field-input')).toHaveLength(2)
  })
  it('jumps the active tab to the default locale when validation errors appear', async () => {
    const w = mountForm({ meta, model, locales, errors: {} })
    // move off the default locale, then surface an error
    ;(w.vm as unknown as { activeLocale: string }).activeLocale = 'zh-TW'
    await w.setProps({ errors: { title: 'Title is required.' } })
    expect((w.vm as unknown as { activeLocale: string }).activeLocale).toBe('en')
  })
  it('activates the clicked locale tab and mounts that locale\'s field body', async () => {
    const w = mountForm({ meta, model, locales, errors: {} })
    // reka's TabsTrigger activates on `mousedown` (activationMode defaults to 'automatic'), not
    // `click` — a click-based trigger would pass this test while exercising nothing.
    await w.findAll('[role="tab"]')[1].trigger('mousedown')
    expect((w.vm as unknown as { activeLocale: string }).activeLocale).toBe('zh-TW')
    // TabsContent's Presence settles `hidden`/data-state one microtask flush after the
    // v-model change (usePresence awaits nextTick before dispatching its state transition).
    await flushPromises()
    expect(w.findAll('.field-input')).toHaveLength(2) // shared status + zh-TW's translatable title
  })
  it('renders each locale tab as an explicit type="button", the only <button>s inside this <form> besides field controls', () => {
    const w = mountForm({ meta, model, locales, errors: {} })
    for (const tab of w.findAll('[role="tab"]')) {
      expect(tab.attributes('type')).toBe('button')
    }
  })
  it('shows a server error banner', () => {
    const w = mountForm({ meta, model, locales, errors: {}, serverError: 'boom' })
    expect(w.find('.error').text()).toContain('boom')
  })
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
  it('gives each completeness dot an accessible label instead of hiding it', () => {
    const withContent: FormModel = { shared: { status: 'draft' },
      translations: { en: { title: 'Hi' }, 'zh-TW': { title: '' } }, relations: {} }
    const w = mountForm({ meta, model: withContent, locales, errors: {} })
    const dots = w.findAll('.dot')
    expect(dots[0].attributes('aria-hidden')).toBeUndefined()
    expect(dots[0].attributes('aria-label')).toBe('Has content')
    expect(dots[1].attributes('aria-label')).toBe('No content yet')
  })
  it('renders no dots when there is only one locale', () => {
    const w = mountForm({ meta, model, locales: [{ code: 'en', name: 'English', isDefault: true }], errors: {} })
    expect(w.findAll('.dot')).toHaveLength(0)
  })
  it('renders a RelationInput per relation in the shared section', () => {
    const relMeta: CollectionMeta = {
      name: 'article', label: 'Article', defaultDisplayField: null,
      fields: [field('status', { sort: 1 })],
      relations: [{ name: 'category', label: 'Category', kind: 'manyToOne', targetCollection: 'category', interface: 'dropdown', foreignKey: 'CategoryId', displayTemplate: '{Name}', editable: true, selfReferencing: false }],
    }
    const relModel: FormModel = { shared: { status: 'draft' }, translations: {}, relations: { category: null } }
    const w = mountForm({ meta: relMeta, model: relModel, locales: [], errors: {} }, { RelationInput: true })
    expect(w.findComponent(RelationInput).exists()).toBe(true)
    // The relation lives in its own `.field` wrapper too (a THIRD call site, distinct from the
    // shared- and translatable-field wrappers covered below) — pinned here rather than folded into
    // an aggregate count, since this is the only test that mounts a relation at all.
    expect(w.find('.relations .field').exists()).toBe(true)
  })
  it('keeps the .field wrapper class on each of the three call sites the e2e suite locates fields by', () => {
    const w = mountForm({ meta, model, locales, errors: {} })
    // Six e2e specs find a field by label inside `.field` / `.field:visible`. This is a contract,
    // not a styling detail — dropping the class on any ONE wrapper breaks that wrapper's fields
    // silently at the Playwright layer. An aggregate `length > 0` cannot detect a single dropped
    // wrapper (the other two still match), so each call site is pinned by its actual content
    // instead of trusting the total.
    expect(w.findAll('.field')).toHaveLength(2) // 1 shared (status) + 1 active-locale translatable (title)
    expect(w.find('.field .field-input[data-name="status"]').exists()).toBe(true) // shared
    expect(w.find('.field .field-input[data-name="title"]').exists()).toBe(true)  // translatable
  })
  it('no longer renders PrimeVue tabs', () => {
    const w = mountForm({ meta, model, locales, errors: {} })
    expect(w.findComponent({ name: 'TabPanel' }).exists()).toBe(false)
    expect(w.findAll('[role="tab"]')).toHaveLength(2)
  })
})
