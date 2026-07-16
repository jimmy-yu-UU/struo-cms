import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { ref } from 'vue'
import AppShell from './AppShell.vue'
import { useSidebarStore } from '../stores/sidebarStore'
import { useSchemaStore } from '../stores/schemaStore'
import { i18n } from '../i18n'

const routeRef = ref<{ path: string; name: string; params: Record<string, string> }>({
  path: '/',
  name: 'dashboard',
  params: {},
})
vi.mock('vue-router', () => ({
  useRoute: () => routeRef.value,
  useRouter: () => ({ push: vi.fn() }),
  RouterView: { template: '<div class="rv" />' },
}))

const stubs = {
  TheTopbar: { template: '<div class="stub-topbar" />' },
  TheSidebar: { template: '<div class="stub-sidebar" />' },
  AppBreadcrumb: { template: '<div class="stub-bc" />' },
  Toast: { template: '<div class="stub-toast" />' },
  RouterView: true,
}

const mountShell = () => mount(AppShell, { global: { plugins: [i18n], stubs } })

describe('AppShell', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    routeRef.value = { path: '/', name: 'dashboard', params: {} }
  })

  it('composes topbar, sidebar, breadcrumb, router-view and a toast host', () => {
    const schema = useSchemaStore()
    vi.spyOn(schema, 'load').mockResolvedValue()
    const wrapper = mountShell()
    expect(wrapper.find('.stub-topbar').exists()).toBe(true)
    expect(wrapper.find('.stub-sidebar').exists()).toBe(true)
    expect(wrapper.find('.stub-bc').exists()).toBe(true)
    expect(wrapper.find('.stub-toast').exists()).toBe(true)
  })

  it('loads the schema on mount', () => {
    const schema = useSchemaStore()
    const loadSpy = vi.spyOn(schema, 'load').mockResolvedValue()
    mountShell()
    expect(loadSpy).toHaveBeenCalledOnce()
  })

  it('shows the scrim only when the drawer is open and closes it on click', async () => {
    const schema = useSchemaStore()
    vi.spyOn(schema, 'load').mockResolvedValue()
    const sidebar = useSidebarStore()
    const wrapper = mountShell()
    expect(wrapper.find('.scrim').exists()).toBe(false)
    sidebar.openDrawer()
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.scrim').exists()).toBe(true)
    await wrapper.find('.scrim').trigger('click')
    expect(sidebar.drawerOpen).toBe(false)
  })

  it('closes the drawer when the route changes', async () => {
    const schema = useSchemaStore()
    vi.spyOn(schema, 'load').mockResolvedValue()
    const sidebar = useSidebarStore()
    const wrapper = mountShell()
    sidebar.openDrawer()
    await wrapper.vm.$nextTick()
    routeRef.value = { path: '/media', name: 'media', params: {} }
    await wrapper.vm.$nextTick()
    expect(sidebar.drawerOpen).toBe(false)
  })
})
