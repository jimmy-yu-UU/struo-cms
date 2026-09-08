import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import JunctionLinksEditor from './JunctionLinksEditor.vue'
import RelationPicker from './RelationPicker.vue'
import { itemsApi } from '../../api/itemsApi'
import { useSchemaStore } from '../../stores/schemaStore'
import { useLanguageStore } from '../../stores/languageStore'
import { useAuthStore } from '../../stores/authStore'
import en from '../../locales/en'
import type { CollectionMeta, FieldMeta, RelationMeta } from '../../types/schema'
import type { RelationLink } from '../../types/itemForm'

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })

function field(name: string, over: Partial<FieldMeta> = {}): FieldMeta {
  return { name, label: name, interface: 'text', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over }
}
const tagMeta: CollectionMeta = { name: 'tag', label: 'Tag', defaultDisplayField: 'name', relations: [], fields: [field('name')] }
const junctionMeta: CollectionMeta = { name: 'articleTag', label: 'Article tag', relations: [], fields: [
  field('articleId', { interface: 'uuid' }), field('tagId', { interface: 'uuid' }),
  field('note', { sort: 1 }), field('weight', { interface: 'number', sort: 2 }), field('secret', { hidden: true }), field('sort', { interface: 'number' }),
] }
function rel(over: Partial<RelationMeta> = {}): RelationMeta {
  return { name: 'tags', label: 'Tags', kind: 'manyToMany', targetCollection: 'tag', interface: 'tagSelect',
    foreignKey: null, displayTemplate: '{Name}', editable: true, selfReferencing: false,
    junctionCollection: 'articleTag', junctionPayloadFields: ['note', 'weight', 'secret'], sortField: 'sort', ...over }
}
const links: RelationLink[] = [
  { id: 't1', junction: { note: 'first', weight: 1 } },
  { id: 't2', junction: { note: 'second', weight: 2 } },
]

// Combobox is stubbed (reka portals need a live DOM the other picker tests set up separately);
// the editor's own add/remove path is exercised by emitting from the RelationPicker child directly.
const stubs = { Combobox: true }

function setup(perms: { read?: boolean; write?: boolean; superAdmin?: boolean } = {}) {
  const schema = useSchemaStore()
  schema.get = vi.fn((n: string) => (n === 'tag' ? tagMeta : n === 'articleTag' ? junctionMeta : undefined)) as never
  const lang = useLanguageStore()
  lang.languages = [{ code: 'en', name: 'English', isDefault: true }]
  const auth = useAuthStore()
  auth.user = { id: 'u', isSuperAdmin: perms.superAdmin ?? false,
    permissions: { articleTag: { read: perms.read ?? true, write: perms.write ?? true, delete: false }, tag: { read: true, write: true, delete: false } } }
}

function mountEditor(props: Partial<{ relation: RelationMeta; modelValue: unknown; disabled: boolean }> = {}) {
  return mount(JunctionLinksEditor, {
    props: { relation: rel(), modelValue: links, ...props },
    global: { plugins: [i18n], stubs },
  })
}

describe('JunctionLinksEditor', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.restoreAllMocks()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [{ id: 't1', name: 'One' }, { id: 't2', name: 'Two' }, { id: 't3', name: 'Three' }], total: 3 })
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: 'x', name: 'X' })
  })

  it('renders one row per link with the target label and the visible payload values', async () => {
    setup()
    const w = mountEditor()
    await flushPromises()
    const rows = w.findAll('.junction-link')
    expect(rows).toHaveLength(2)
    expect(rows[0].find('.junction-link__label').text()).toBe('One')
    expect(rows[0].findAll('.junction-link__field')).toHaveLength(2) // note + weight; secret is hidden
    expect((rows[0].find('input').element as HTMLInputElement).value).toBe('first')
  })

  it('editing a payload field emits a new array with only that link changed, leaving the prop untouched', async () => {
    setup()
    const w = mountEditor()
    await flushPromises()
    await w.findAll('.junction-link')[1].find('input').setValue('changed')
    const emitted = w.emitted('update:modelValue')!.at(-1)![0] as RelationLink[]
    expect(emitted[1]).toEqual({ id: 't2', junction: { note: 'changed', weight: 2 } })
    expect(emitted[0]).toEqual(links[0])
    expect(links[1].junction.note).toBe('second')
  })

  it('a new id from the picker appends a row seeded with registry defaults; a dropped id removes its row', async () => {
    setup()
    const w = mountEditor()
    await flushPromises()
    const picker = w.findComponent(RelationPicker)
    expect(picker.props('showChips')).toBe(false)
    expect(picker.props('modelValue')).toEqual(['t1', 't2'])
    picker.vm.$emit('update:modelValue', ['t1', 't2', 't3'])
    let out = w.emitted('update:modelValue')!.at(-1)![0] as RelationLink[]
    expect(out.map((l) => l.id)).toEqual(['t1', 't2', 't3'])
    expect(out[2].junction).toEqual({ note: '', weight: '' })
    picker.vm.$emit('update:modelValue', ['t2'])
    out = w.emitted('update:modelValue')!.at(-1)![0] as RelationLink[]
    expect(out).toEqual([links[1]])
  })

  it('the remove button drops that row', async () => {
    setup()
    const w = mountEditor()
    await flushPromises()
    await w.findAll('.junction-link__remove')[0].trigger('click')
    expect((w.emitted('update:modelValue')!.at(-1)![0] as RelationLink[]).map((l) => l.id)).toEqual(['t2'])
  })

  it('shows reorder arrows only when the relation has a sortField, and reordering emits the new order', async () => {
    setup()
    const w = mountEditor()
    await flushPromises()
    await w.findAll('[data-testid="move-down"]')[0].trigger('click')
    expect((w.emitted('update:modelValue')!.at(-1)![0] as RelationLink[]).map((l) => l.id)).toEqual(['t2', 't1'])
    const noSort = mountEditor({ relation: rel({ sortField: null }) })
    await flushPromises()
    expect(noSort.findAll('[data-testid="move-down"]')).toHaveLength(0)
  })

  it('without junction write grant the payload inputs are disabled but rows can still be removed', async () => {
    setup({ write: false })
    const w = mountEditor()
    await flushPromises()
    expect((w.find('.junction-link input').element as HTMLInputElement).disabled).toBe(true)
    expect((w.find('.junction-link__remove').element as HTMLButtonElement).disabled).toBe(false)
  })

  it('without junction read grant no payload fields render at all', async () => {
    setup({ read: false, write: false })
    const w = mountEditor()
    await flushPromises()
    expect(w.findAll('.junction-link')).toHaveLength(2)
    expect(w.findAll('.junction-link__field')).toHaveLength(0)
  })

  it('an adminOnly junction needs super-admin to edit payload', async () => {
    setup({ superAdmin: false })
    const schema = useSchemaStore()
    schema.get = vi.fn((n: string) => (n === 'tag' ? tagMeta : n === 'articleTag' ? { ...junctionMeta, adminOnly: true } : undefined)) as never
    const w = mountEditor()
    await flushPromises()
    expect((w.find('.junction-link input').element as HTMLInputElement).disabled).toBe(true)
  })

  it('disabled disables everything', async () => {
    setup()
    const w = mountEditor({ disabled: true })
    await flushPromises()
    expect((w.find('.junction-link input').element as HTMLInputElement).disabled).toBe(true)
    expect((w.find('.junction-link__remove').element as HTMLButtonElement).disabled).toBe(true)
    expect(w.findComponent(RelationPicker).props('disabled')).toBe(true)
  })

  it('shows the empty message when there are no links', async () => {
    setup()
    const w = mountEditor({ modelValue: [] })
    await flushPromises()
    expect(w.find('.junction-links__empty').text()).toBe(en.fields.noItems)
  })
})
