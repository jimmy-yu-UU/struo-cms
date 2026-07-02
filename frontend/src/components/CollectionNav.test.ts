import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import CollectionNav from './CollectionNav.vue'
import { useAuthStore } from '../stores/authStore'
import { useSchemaStore } from '../stores/schemaStore'

const push = vi.fn()
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))
vi.mock('primevue/panelmenu', () => ({ default: { name: 'PanelMenu', props: ['model'], template: '<div class="pm" />' } }))

describe('CollectionNav', () => {
  beforeEach(() => { setActivePinia(createPinia()); vi.clearAllMocks() })

  function seed(isSuperAdmin: boolean, permissions: Record<string, { read: boolean; write: boolean; delete: boolean }>) {
    const auth = useAuthStore()
    auth.user = { id: 'u1', isSuperAdmin, permissions }
    const schema = useSchemaStore()
    schema.collections = [
      { name: 'article', label: 'Article', group: 'Content', fields: [] },
      { name: 'user', label: 'User', group: 'System', fields: [] },
    ]
  }

  it('builds a grouped model of readable collections and navigates on command', () => {
    seed(false, { article: { read: true, write: false, delete: false } })
    const wrapper = mount(CollectionNav)
    const model = (wrapper.vm as unknown as { model: any[] }).model
    const names = model.flatMap((g) => g.items.map((i: any) => i.key))
    expect(names).toEqual(['article']) // 'user' filtered out

    model[0].items[0].command()
    expect(push).toHaveBeenCalledWith({ name: 'collection-list', params: { name: 'article' } })
  })

  it('super-admin model includes every collection', () => {
    seed(true, {})
    const wrapper = mount(CollectionNav)
    const names = (wrapper.vm as unknown as { model: any[] }).model.flatMap((g) => g.items.map((i: any) => i.key))
    expect(names.sort()).toEqual(['article', 'user'])
  })
})
