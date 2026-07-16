import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import TheSidebar from './TheSidebar.vue'
import { useAuthStore } from '../../stores/authStore'
import { useSchemaStore } from '../../stores/schemaStore'
import { useSidebarStore } from '../../stores/sidebarStore'
import { i18n } from '../../i18n'

const push = vi.fn()
let currentRoute: { name: string; params: Record<string, string> }
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
  useRoute: () => currentRoute,
}))

function seed(isSuperAdmin: boolean, perms: Record<string, { read: boolean; write: boolean; delete: boolean }>) {
  const auth = useAuthStore()
  auth.user = { id: 'u1', isSuperAdmin, permissions: perms }
  const schema = useSchemaStore()
  schema.collections = [
    { name: 'article', label: 'Article', group: 'Content', fields: [], relations: [] },
    { name: 'page', label: 'Page', group: null, fields: [], relations: [] },
  ]
}

const mountSidebar = () => mount(TheSidebar, { global: { plugins: [i18n] } })

describe('TheSidebar', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    localStorage.clear()
    i18n.global.locale.value = 'zh-TW'
    currentRoute = { name: 'dashboard', params: {} }
  })

  it('shows Dashboard + Media for a user with file:read, and flat + grouped collections', () => {
    seed(false, {
      article: { read: true, write: false, delete: false },
      page: { read: true, write: false, delete: false },
      file: { read: true, write: false, delete: false },
    })
    const wrapper = mountSidebar()
    const labels = wrapper.findAll('.nav-label').map((n) => n.text())
    expect(labels).toContain('儀表板')
    expect(labels).toContain('媒體庫')
    expect(labels).toContain('Article') // grouped under Content
    expect(labels).toContain('Page') // ungrouped -> flat
    expect(labels).toContain('Content') // group header
  })

  it('hides Media without file:read', () => {
    seed(false, { article: { read: true, write: false, delete: false } })
    const wrapper = mountSidebar()
    const labels = wrapper.findAll('.nav-label').map((n) => n.text())
    expect(labels).not.toContain('媒體庫')
  })

  it('navigates and closes the drawer when a collection is clicked', async () => {
    seed(true, {})
    const sidebar = useSidebarStore()
    sidebar.openDrawer()
    const wrapper = mountSidebar()
    const article = wrapper.findAll('button.nav-item').find((b) => b.text().includes('Article'))!
    await article.trigger('click')
    expect(push).toHaveBeenCalledWith({ name: 'collection-list', params: { name: 'article' } })
    expect(sidebar.drawerOpen).toBe(false)
  })

  it('marks the active collection from the route', () => {
    currentRoute = { name: 'collection-list', params: { name: 'article' } }
    seed(true, {})
    const wrapper = mountSidebar()
    const article = wrapper.findAll('button.nav-item').find((b) => b.text().includes('Article'))!
    expect(article.classes()).toContain('active')
  })

  it('shows a retry affordance on schema load error', async () => {
    seed(true, {})
    const schema = useSchemaStore()
    schema.loadError = 'boom'
    const loadSpy = vi.spyOn(schema, 'load').mockResolvedValue()
    const wrapper = mountSidebar()
    expect(wrapper.find('[role="alert"]').exists()).toBe(true)
    await wrapper.find('[role="alert"] button').trigger('click')
    expect(loadSpy).toHaveBeenCalledOnce()
  })
})
