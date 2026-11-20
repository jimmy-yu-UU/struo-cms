import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import ItemFormView from './ItemFormView.vue'
import { itemsApi } from '../api/itemsApi'
import { useAuthStore } from '../stores/authStore'
import { useSchemaStore } from '../stores/schemaStore'
import { useLanguageStore } from '../stores/languageStore'
import type { ConfirmRequest } from '@/composables/useConfirm'
import { catalogs } from '../locales'

// A JSON payload field on a many-to-many link: the real ItemForm, RelationInput, JunctionLinksEditor,
// FieldInput and JsonField are mounted.
vi.mock('../api/rbacApi', () => ({ rbacApi: { getRolePermissions: vi.fn(), putRolePermissions: vi.fn(), getEffectivePermissions: vi.fn() } }))
const push = vi.fn()
let routeParams: Record<string, string> = {}
vi.mock('vue-router', () => ({
  useRoute: () => ({ params: routeParams, name: 'collection-item' }),
  useRouter: () => ({ push }),
  onBeforeRouteLeave: () => {},
  onBeforeRouteUpdate: () => {},
}))
const confirmRequire = vi.fn<(req: ConfirmRequest) => Promise<boolean>>(() => Promise.resolve(true))
vi.mock('@/composables/useConfirm', () => ({ useConfirm: () => ({ require: confirmRequire }) }))
vi.mock('@/composables/useToast', () => ({ useToast: () => ({ add: vi.fn() }) }))

function f(name: string, iface: string, over: Record<string, unknown> = {}) {
  return { name, label: name, interface: iface, required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 1, isSystem: false, ...over }
}
const articleMeta = { name: 'article', label: 'Article', fields: [f('status', 'text')], relations: [
  { name: 'tags', label: 'Tags', kind: 'manyToMany', targetCollection: 'tag', interface: 'tagSelect',
    foreignKey: null, displayTemplate: '{Name}', editable: true, selfReferencing: false,
    junctionCollection: 'articleTag', junctionPayloadFields: ['note', 'config'], sortField: null },
] }
const tagMeta = { name: 'tag', label: 'Tag', defaultDisplayField: 'name', relations: [], fields: [f('name', 'text')] }
const junctionMeta = { name: 'articleTag', label: 'Article tag', relations: [], fields: [
  f('articleId', 'uuid'), f('tagId', 'uuid'), f('note', 'text', { sort: 1 }), f('config', 'json', { sort: 2 }),
] }
const item = {
  id: '5', version: 3, status: 'draft',
  tags: [
    { id: 't1', name: 'One', _junction: { note: 'a', config: { x: 1 } } },
    { id: 't2', name: 'Two', _junction: { note: 'b', config: { y: 2 } } },
  ],
  translations: {},
}

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: catalogs as never })
type Wrapper = VueWrapper<InstanceType<typeof ItemFormView>>

async function mountEdit(junction: typeof junctionMeta = junctionMeta): Promise<Wrapper> {
  routeParams = { name: 'article', id: '5' }
  useAuthStore().user = { id: '1', isSuperAdmin: true, permissions: {} }
  const schema = useSchemaStore()
  schema.load = vi.fn().mockResolvedValue(undefined)
  schema.get = vi.fn((n: string) =>
    (n === 'article' ? articleMeta : n === 'tag' ? tagMeta : n === 'articleTag' ? junction : undefined)) as never
  const lang = useLanguageStore()
  lang.load = vi.fn().mockResolvedValue(undefined)
  lang.languages = [{ code: 'en', name: 'English', isDefault: true }]
  const w = mount(ItemFormView, {
    attachTo: document.body,
    global: { plugins: [i18n], stubs: { RevisionHistoryDrawer: true, Combobox: true } },
  }) as Wrapper
  await flushPromises()
  return w
}

async function typeInto(w: Wrapper, index: number, text: string): Promise<void> {
  await w.findAll('textarea')[index].setValue(text)
}
async function clickSave(w: Wrapper): Promise<void> {
  await w.findAll('button').find((b) => b.text() === 'Save')!.trigger('click')
  await flushPromises()
  await new Promise((r) => setTimeout(r, 0))
}
const alertTexts = (w: Wrapper): string[] => w.findAll('[role="alert"]').map((a) => a.text()).filter((x) => x !== '')

describe('ItemFormView JSON payload on a link', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    push.mockClear()
    confirmRequire.mockReset(); confirmRequire.mockResolvedValue(true)
    vi.spyOn(itemsApi, 'get').mockResolvedValue(structuredClone(item))
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [{ id: 't1', name: 'One' }, { id: 't2', name: 'Two' }], total: 2 })
  })
  afterEach(() => vi.restoreAllMocks())

  it('shows the payload text as loaded, and a plain load is clean', async () => {
    const w = await mountEdit()
    expect((w.findAll('textarea')[0].element as HTMLTextAreaElement).value).toContain('"x": 1')
    expect(await w.vm.guardLeave()).toBe(true)
    expect(confirmRequire).not.toHaveBeenCalled()
  })

  it('sends nothing, shows one error on that payload field and focuses it when its text does not parse', async () => {
    const update = vi.spyOn(itemsApi, 'update').mockResolvedValue({ id: '5', version: 4 })
    const w = await mountEdit()
    await typeInto(w, 1, '{"y":')
    await clickSave(w)
    expect(update).not.toHaveBeenCalled()
    expect(alertTexts(w)).toHaveLength(1)
    const area = w.findAll('textarea')[1]
    expect(area.attributes('aria-invalid')).toBe('true')
    expect(document.getElementById(area.attributes('aria-describedby')!)!.textContent).toBe(alertTexts(w)[0])
    expect(document.activeElement).toBe(area.element)
    expect(push).not.toHaveBeenCalled()
  })

  it('follows the text after a blocked save, and saves once it parses', async () => {
    const update = vi.spyOn(itemsApi, 'update').mockResolvedValue({ id: '5', version: 4 })
    const w = await mountEdit()
    await typeInto(w, 1, '{"y":')
    await clickSave(w)
    await typeInto(w, 1, '{"y":3}')
    expect(alertTexts(w)).toEqual([])
    await clickSave(w)
    expect(update).toHaveBeenCalledTimes(1)
  })

  it('asks before leaving when a first edit never parsed, and warns on unload', async () => {
    const w = await mountEdit()
    await typeInto(w, 0, '{a:1}')
    confirmRequire.mockResolvedValue(false)
    expect(await w.vm.guardLeave()).toBe(false)
    expect(confirmRequire).toHaveBeenCalledTimes(1)
    const ev = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(ev)
    expect(ev.defaultPrevented).toBe(true)
  })

  it('can be saved and left once the link holding the unparsable text is removed', async () => {
    const update = vi.spyOn(itemsApi, 'update').mockResolvedValue({ id: '5', version: 4 })
    const w = await mountEdit()
    await typeInto(w, 1, '{"y":')
    await w.findAll('.junction-link__remove')[1].trigger('click')
    await flushPromises()
    expect(alertTexts(w)).toEqual([])
    confirmRequire.mockResolvedValue(true)
    await clickSave(w)
    expect(update).toHaveBeenCalledTimes(1)
  })

  it('does not bring a relation error back when the payload text parses again', async () => {
    const required = { ...junctionMeta, fields: [...junctionMeta.fields.slice(0, 3), f('config', 'json', { sort: 2, required: true })] }
    vi.mocked(itemsApi.get).mockResolvedValue({
      ...structuredClone(item),
      tags: [{ id: 't1', name: 'One', _junction: { note: 'a', config: null } }],
    })
    const update = vi.spyOn(itemsApi, 'update').mockResolvedValue({ id: '5', version: 4 })
    const w = await mountEdit(required)
    await clickSave(w)
    expect(update).not.toHaveBeenCalled()
    expect(alertTexts(w)).toHaveLength(1)
    await typeInto(w, 0, '{')
    await typeInto(w, 0, '{"y":1}')
    expect(alertTexts(w)).toEqual([])
    await clickSave(w)
    expect(update).toHaveBeenCalledTimes(1)
  })

  it('focuses the payload textarea, not the relation\'s first control, when the relation also has a validation error', async () => {
    const required = { ...junctionMeta, fields: [...junctionMeta.fields.slice(0, 2), f('note', 'text', { sort: 1, required: true }), f('config', 'json', { sort: 2 })] }
    vi.mocked(itemsApi.get).mockResolvedValue({
      ...structuredClone(item),
      tags: [
        { id: 't1', name: 'One', _junction: { note: 'a', config: { x: 1 } } },
        { id: 't2', name: 'Two', _junction: { note: '', config: { y: 2 } } },
      ],
    })
    const w = await mountEdit(required)
    await typeInto(w, 0, '{"x":')
    await clickSave(w)
    expect(alertTexts(w).length).toBeGreaterThan(0)
    expect(document.activeElement).toBe(w.findAll('textarea')[0].element)
  })

  it('discards the draft when the item is loaded again', async () => {
    const w = await mountEdit()
    await typeInto(w, 0, '{')
    await w.vm.init()
    await flushPromises()
    expect(await w.vm.guardLeave()).toBe(true)
    expect(alertTexts(w)).toEqual([])
  })

  it('puts no draft attribute on the DOM of the payload fields that parse nothing', async () => {
    const w = await mountEdit()
    await typeInto(w, 0, '{')
    expect(w.findAll('*').filter((el) => 'draft' in el.attributes())).toHaveLength(0)
  })
})
