import { describe, it, expect, vi, afterEach, beforeEach } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en'
import zhTW from '@/locales/zh-TW'
import ItemForm from './ItemForm.vue'
import { itemsApi } from '../api/itemsApi'
import { useSchemaStore } from '../stores/schemaStore'
import { useLanguageStore } from '../stores/languageStore'
import type { CollectionMeta, FieldMeta, LanguageInfo, RelationMeta } from '../types/schema'
import type { FormModel } from '../types/itemForm'

vi.mock('../api/itemsApi', () => ({
  itemsApi: { list: vi.fn().mockResolvedValue({ data: [], total: 0 }), get: vi.fn() },
}))

const i18n = createI18n({ legacy: false, locale: 'zh-TW', fallbackLocale: 'en', messages: { en, 'zh-TW': zhTW } })

function field(name: string, over: Partial<FieldMeta> = {}): FieldMeta {
  return { name, label: name, interface: 'text', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over }
}

const locales: LanguageInfo[] = [
  { code: 'en', name: 'English', isDefault: true },
  { code: 'fr', name: 'Francais', isDefault: false },
]
const OPTIONS = [{ value: 'a', label: 'A' }, { value: 'b', label: 'B' }]

type FormProps = InstanceType<typeof ItemForm>['$props']
let wrapper: VueWrapper | undefined

function mountForm(
  meta: CollectionMeta,
  model: FormModel,
  extra: Record<string, unknown> = {},
): VueWrapper {
  wrapper = mount(ItemForm, {
    props: { meta, model, locales, errors: {}, ...extra } as unknown as FormProps,
    attachTo: document.body,
    global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true },
  })
  return wrapper
}

async function settle(): Promise<void> {
  await flushPromises()
  await flushPromises()
}

// The rich text editor is an async component; wait until its editing surface exists.
async function editorReady(w: VueWrapper): Promise<void> {
  await vi.waitFor(() => expect(w.find('[contenteditable="true"]').exists()).toBe(true))
}

beforeEach(() => {
  setActivePinia(createPinia())
  const lang = useLanguageStore()
  lang.languages = locales
  const schema = useSchemaStore()
  schema.get = vi.fn().mockReturnValue(undefined) as never
  vi.mocked(itemsApi.list).mockResolvedValue({ data: [], total: 0 })
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined; document.body.innerHTML = '' })

const emptyModel = (shared: Record<string, unknown> = {}): FormModel =>
  ({ shared, translations: { en: {}, fr: {} }, relations: {} })

const MESSAGE = '此欄位有誤'
const SERVER_MESSAGE = '伺服器錯誤'
const FIELD_CASES: Array<[string, Partial<FieldMeta>]> = [
  ['text', {}], ['textarea', {}], ['number', {}], ['boolean', {}], ['date', {}], ['dateTime', {}], ['time', {}],
  ['select', { options: OPTIONS }], ['multiSelect', { options: OPTIONS }], ['radio', { options: OPTIONS }],
  ['checkboxGroup', { options: OPTIONS }], ['tags', {}], ['json', {}], ['keyValue', {}],
  ['repeater', { fields: [field('sub')] }], ['file', {}], ['image', {}], ['files', {}], ['richText', {}],
]

function labelTarget(w: VueWrapper, key: string): HTMLElement | null {
  const forId = w.get(`[data-field-key="${key}"] label[for]`).attributes('for') as string
  return document.getElementById(forId)
}

describe('ItemForm field accessibility wiring', () => {
  it.each(FIELD_CASES)('%s: the label target carries aria-invalid and aria-describedby', async (iface, over) => {
    const meta: CollectionMeta = { name: 'a', label: 'A', relations: [],
      fields: [field('f', { interface: iface, label: 'F', ...over })] }
    const w = mountForm(meta, emptyModel(), { errors: { f: MESSAGE } })
    await settle()
    if (iface === 'richText') await editorReady(w)
    const control = labelTarget(w, 'f')
    expect(control, `no element with the id the ${iface} label points at`).not.toBeNull()
    expect(control?.getAttribute('aria-invalid')).toBe('true')
    const describedBy = control?.getAttribute('aria-describedby') ?? ''
    const errorEl = describedBy ? document.getElementById(describedBy) : null
    expect(errorEl?.textContent).toContain(MESSAGE)
  })

  it.each(FIELD_CASES)('%s: no aria-invalid or aria-describedby without an error', async (iface, over) => {
    const meta: CollectionMeta = { name: 'a', label: 'A', relations: [],
      fields: [field('f', { interface: iface, label: 'F', ...over })] }
    const w = mountForm(meta, emptyModel())
    await settle()
    if (iface === 'richText') await editorReady(w)
    const control = labelTarget(w, 'f')
    expect(control?.hasAttribute('aria-describedby')).toBe(false)
    expect(control?.getAttribute('aria-invalid') ?? 'false').not.toBe('true')
  })

  it('wires a translatable control the same way', async () => {
    const meta: CollectionMeta = { name: 'a', label: 'A', relations: [],
      fields: [field('title', { translatable: true, label: 'Title' })] }
    const w = mountForm(meta, emptyModel(), { errors: { title: MESSAGE } })
    await settle()
    const control = labelTarget(w, 'title')
    expect(control?.getAttribute('aria-invalid')).toBe('true')
    expect(document.getElementById(control?.getAttribute('aria-describedby') as string)?.textContent).toContain(MESSAGE)
  })

  const relation = (iface: string): RelationMeta => ({
    name: 'rel', label: 'Rel', kind: 'manyToOne', targetCollection: 'other', interface: iface,
    foreignKey: 'OtherId', displayTemplate: null, editable: true, selfReferencing: false,
  })

  it.each(['dropdown', 'tagSelect', 'treeSelect'])('relation %s: the label target carries the aria wiring', async (iface) => {
    const meta: CollectionMeta = { name: 'a', label: 'A', fields: [], relations: [relation(iface)] }
    const model = { ...emptyModel(), relations: { rel: iface === 'dropdown' ? null : [] } }
    const w = mountForm(meta, model, { errors: { rel: MESSAGE } })
    await settle()
    const control = labelTarget(w, 'rel')
    expect(control?.getAttribute('aria-invalid')).toBe('true')
    expect(document.getElementById(control?.getAttribute('aria-describedby') as string)?.textContent).toContain(MESSAGE)
  })
})

describe('ItemForm focus on validation errors', () => {
  const meta: CollectionMeta = { name: 'a', label: 'A', relations: [], fields: [
    field('status', { label: 'Status', sort: 1 }),
    field('title', { label: 'Title', translatable: true, sort: 2 }),
    field('tags', { label: 'Tags', interface: 'tags', sort: 3 }),
    field('body', { label: 'Body', interface: 'richText', sort: 4 }),
  ] }

  it('focuses a shared field with an error', async () => {
    const w = mountForm(meta, emptyModel())
    await w.setProps({ errors: { status: MESSAGE } })
    await settle()
    expect(document.activeElement).toBe(labelTarget(w, 'status'))
  })

  it('focuses the default-locale translatable field before a shared one', async () => {
    const w = mountForm(meta, emptyModel())
    await w.setProps({ errors: { status: MESSAGE, title: MESSAGE } })
    await settle()
    expect(document.activeElement).toBe(labelTarget(w, 'title'))
  })

  it('switches to an inactive locale tab and focuses its field', async () => {
    const w = mountForm(meta, emptyModel())
    await w.setProps({ errors: { 'fr:title': MESSAGE } })
    await settle()
    expect((w.vm as unknown as { activeLocale: string }).activeLocale).toBe('fr')
    expect(document.activeElement).toBe(labelTarget(w, 'fr:title'))
  })

  it('focuses the first focusable control inside a composite field', async () => {
    const w = mountForm(meta, { ...emptyModel(), shared: { tags: [] } })
    await w.setProps({ errors: { tags: MESSAGE } })
    await settle()
    const wrapperEl = w.get('[data-field-key="tags"]').element
    expect(wrapperEl.contains(document.activeElement)).toBe(true)
    expect(document.activeElement?.tagName).toBe('BUTTON')
  })

  it('focuses the rich text editing surface, not a toolbar button', async () => {
    const w = mountForm(meta, emptyModel())
    await editorReady(w)
    await w.setProps({ errors: { body: MESSAGE } })
    await settle()
    expect(document.activeElement).toBe(labelTarget(w, 'body'))
    expect(document.activeElement?.getAttribute('contenteditable')).toBe('true')
  })

  it('focuses a relation field when it holds the only error', async () => {
    const relMeta: CollectionMeta = { name: 'a', label: 'A', fields: [], relations: [{
      name: 'rel', label: 'Rel', kind: 'manyToOne', targetCollection: 'other', interface: 'dropdown',
      foreignKey: 'OtherId', displayTemplate: null, editable: true, selfReferencing: false }] }
    const w = mountForm(relMeta, { ...emptyModel(), relations: { rel: null } })
    await w.setProps({ errors: { rel: MESSAGE } })
    await settle()
    expect(w.get('[data-field-key="rel"]').element.contains(document.activeElement)).toBe(true)
    expect(document.activeElement?.tagName).toBe('BUTTON')
  })

  it('moves focus to the banner only when the parent asks for it', async () => {
    const w = mountForm(meta, emptyModel())
    const outside = document.createElement('button')
    document.body.appendChild(outside)
    outside.focus()
    await w.setProps({ serverError: SERVER_MESSAGE })
    await settle()
    expect(document.activeElement).toBe(outside)
    await (w.vm as unknown as { focusBanner: () => Promise<void> }).focusBanner()
    const banner = w.get('[role="alert"]').element
    expect(banner.getAttribute('tabindex')).toBe('-1')
    expect(document.activeElement).toBe(banner)
  })

  it('skips an invalid field that has nothing focusable and focuses the next one', async () => {
    const roMeta: CollectionMeta = { name: 'a', label: 'A', relations: [], fields: [
      field('locked', { label: 'Locked', readOnly: true, sort: 1 }),
      field('status', { label: 'Status', sort: 2 }),
    ] }
    const w = mountForm(roMeta, emptyModel())
    await w.setProps({ errors: { locked: MESSAGE, status: MESSAGE } })
    await settle()
    expect(document.activeElement).toBe(labelTarget(w, 'status'))
  })

  it('falls back to the banner when no invalid field can take focus', async () => {
    const roMeta: CollectionMeta = { name: 'a', label: 'A', relations: [], fields: [
      field('locked', { label: 'Locked', readOnly: true }),
    ] }
    const w = mountForm(roMeta, emptyModel(), { serverError: SERVER_MESSAGE })
    await w.setProps({ errors: { locked: MESSAGE } })
    await settle()
    expect(document.activeElement).toBe(w.get('[role="alert"]').element)
  })

  it('prefers the failing field over the banner when both are present', async () => {
    const w = mountForm(meta, emptyModel())
    await w.setProps({ errors: { status: MESSAGE }, serverError: SERVER_MESSAGE })
    await settle()
    expect(document.activeElement).toBe(labelTarget(w, 'status'))
  })
})
