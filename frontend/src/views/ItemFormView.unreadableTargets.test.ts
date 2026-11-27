import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import ItemFormView from './ItemFormView.vue'
import { itemsApi } from '../api/itemsApi'
import { useAuthStore } from '../stores/authStore'
import { useSchemaStore } from '../stores/schemaStore'
import { useLanguageStore } from '../stores/languageStore'
import type { ConfirmRequest } from '@/composables/useConfirm'
import type { CollectionPermission } from '../types/schema'
import { catalogs } from '../locales'

// The real ItemForm and its field components are mounted: a relation or file field whose target the
// signed-in user cannot read is shown disabled, loads nothing, is never sent, and never reads as edited.
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
function rel(name: string, target: string, over: Record<string, unknown>) {
  return { name, label: name, kind: 'manyToOne', targetCollection: target, interface: 'dropdown', foreignKey: null,
    displayTemplate: '{Name}', editable: true, selfReferencing: false, ...over }
}
const articleMeta = { name: 'article', label: 'Article', fields: [
  f('status', 'text'),
  f('hero', 'image', { required: true }),
], relations: [
  rel('category', 'category', { foreignKey: 'CategoryId' }),
  rel('tags', 'tag', { kind: 'manyToMany', interface: 'tagSelect' }),
] }
const userMeta = { name: 'user', label: 'User', fields: [f('email', 'text')], relations: [
  rel('roles', 'role', { kind: 'manyToMany', interface: 'tagSelect' }),
] }
const labels: Record<string, { name: string; label: string; fields: never[]; relations: never[] }> = {
  category: { name: 'category', label: 'Categories', fields: [], relations: [] },
}

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: catalogs as never })
type Wrapper = VueWrapper<InstanceType<typeof ItemFormView>>

async function mountEdit(
  collection: 'article' | 'user',
  permissions: Record<string, CollectionPermission>,
): Promise<Wrapper> {
  routeParams = { name: collection, id: '5' }
  useAuthStore().user = { id: '1', isSuperAdmin: false, permissions }
  const schema = useSchemaStore()
  schema.load = vi.fn().mockResolvedValue(undefined)
  schema.get = vi.fn((n: string) => (n === 'article' ? articleMeta : n === 'user' ? userMeta : labels[n])) as never
  const lang = useLanguageStore()
  lang.load = vi.fn().mockResolvedValue(undefined)
  lang.languages = [{ code: 'en', name: 'English', isDefault: true }]
  const w = mount(ItemFormView, {
    attachTo: document.body,
    global: {
      plugins: [i18n],
      stubs: { RevisionHistoryDrawer: true, EffectivePermissionsPanel: true, AccessTokenPanel: true, PasswordPanel: true,
        ChangePasswordDialog: true, teleport: true },
      renderStubDefaultSlot: true,
    },
  }) as Wrapper
  await flushPromises()
  return w
}
async function clickSave(w: Wrapper): Promise<void> {
  await w.findAll('button').find((b) => b.text() === 'Save')!.trigger('click')
  await flushPromises()
}
const grant = (read: boolean, write = false): CollectionPermission => ({ read, write, delete: false })

describe('ItemFormView with relation and file targets the user cannot read', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    push.mockReset()
    confirmRequire.mockClear()
    vi.restoreAllMocks()
    vi.spyOn(itemsApi, 'list').mockResolvedValue({ data: [], total: 0 } as never)
    vi.spyOn(itemsApi, 'get').mockImplementation(((collection: string) => Promise.resolve(itemFor(collection))) as never)
  })
  function itemFor(collection: string): Record<string, unknown> {
      if (collection === 'article') return { id: '5', version: 2, status: 'draft', hero: null, translations: {} }
      if (collection === 'file') return { id: 'f1', fileName: 'a.png', contentType: 'image/png', translations: {} }
      return { id: '5', email: 'a@struo.test', translations: {} }
  }
  const articlePerms = { article: grant(true, true), tag: grant(true) }

  it('shows each unreadable field disabled with the reason and no editable picker', async () => {
    const w = await mountEdit('article', articlePerms)
    const category = w.find('[data-field-key="category"]')
    expect(category.find('input').attributes('disabled')).toBeDefined()
    expect(category.text()).toContain('You do not have permission to read Categories')
    const hero = w.find('[data-field-key="hero"]')
    expect(hero.find('input').attributes('disabled')).toBeDefined()
    expect(hero.text()).toContain('You do not have permission to read file')
    expect(w.find('[data-field-key="category"] [role="combobox"]').exists()).toBe(false)
    expect(w.find('[data-field-key="hero"] .file-picker').exists()).toBe(false)
  })

  it('requests nothing from a collection it cannot read and still loads a readable target', async () => {
    await mountEdit('article', articlePerms)
    const requested = [...vi.mocked(itemsApi.list).mock.calls, ...vi.mocked(itemsApi.get).mock.calls].map((c) => c[0])
    expect(requested).not.toContain('category')
    expect(requested).not.toContain('file')
    expect(requested).toContain('tag')
  })

  it('does not count the omitted fields as unsaved edits', async () => {
    const w = await mountEdit('article', articlePerms)
    expect(await w.vm.guardLeave()).toBe(true)
    expect(confirmRequire).not.toHaveBeenCalled()
  })

  it('saves without the unreadable keys and without a required error for the file field', async () => {
    const update = vi.spyOn(itemsApi, 'update').mockResolvedValue({ id: '5', version: 3 })
    const w = await mountEdit('article', articlePerms)
    await clickSave(w)
    expect(w.vm.errors).toEqual({})
    expect(update).toHaveBeenCalledTimes(1)
    const body = update.mock.calls[0][2] as Record<string, unknown>
    expect(body).not.toHaveProperty('categoryId')
    expect(body).not.toHaveProperty('hero')
    expect(body).toMatchObject({ status: 'draft', tags: [], version: 2 })
  })

  it('sends every key when the user can read every target', async () => {
    const update = vi.spyOn(itemsApi, 'update').mockResolvedValue({ id: '5', version: 3 })
    const w = await mountEdit('article', { ...articlePerms, category: grant(true), file: grant(true) })
    w.vm.model.shared.hero = 'f1'
    await clickSave(w)
    expect(update.mock.calls[0][2]).toMatchObject({ categoryId: null, hero: 'f1', tags: [] })
  })

  it('saves a user without a roles key when the caller cannot read the role collection', async () => {
    const update = vi.spyOn(itemsApi, 'update').mockResolvedValue({ id: '5', version: 3 })
    const w = await mountEdit('user', { user: grant(true, true) })
    expect(w.find('[data-field-key="roles"] input').attributes('disabled')).toBeDefined()
    w.vm.model.shared.email = 'b@struo.test'
    await clickSave(w)
    const body = update.mock.calls[0][2] as Record<string, unknown>
    expect(body).toMatchObject({ email: 'b@struo.test' })
    expect(body).not.toHaveProperty('roles')
  })

  it('sends the roles key when the caller can read the role collection', async () => {
    const update = vi.spyOn(itemsApi, 'update').mockResolvedValue({ id: '5', version: 3 })
    const w = await mountEdit('user', { user: grant(true, true), role: grant(true) })
    await clickSave(w)
    expect(update.mock.calls[0][2]).toHaveProperty('roles', [])
  })
})
