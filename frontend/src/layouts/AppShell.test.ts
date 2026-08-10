import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { reactive } from 'vue'
import AppShell from './AppShell.vue'
import { useSchemaStore } from '../stores/schemaStore'
import { i18n } from '../i18n'

const routeState = reactive<{ path: string; name: string; params: Record<string, string> }>({
  path: '/',
  name: 'dashboard',
  params: {},
})
vi.mock('vue-router', () => ({
  useRoute: () => routeState,
  useRouter: () => ({ push: vi.fn() }),
  RouterView: { template: '<div class="rv" />' },
}))

const stubs = {
  TheTopbar: { template: '<div class="stub-topbar" />' },
  TheSidebar: { template: '<div class="stub-sidebar" />' },
  AppBreadcrumb: { template: '<div class="stub-bc" />' },
  Toaster: { template: '<div class="stub-toaster" />' },
  ConfirmHost: { template: '<div class="stub-confirm" />' },
  RouterView: true,
}

const mountShell = () => mount(AppShell, { global: { plugins: [i18n], stubs } })

describe('AppShell', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    routeState.path = '/'
    routeState.name = 'dashboard'
    routeState.params = {}
  })

  it('composes topbar, sidebar and breadcrumb', () => {
    const schema = useSchemaStore()
    vi.spyOn(schema, 'load').mockResolvedValue()
    const wrapper = mountShell()
    expect(wrapper.find('.stub-topbar').exists()).toBe(true)
    expect(wrapper.find('.stub-sidebar').exists()).toBe(true)
    expect(wrapper.find('.stub-bc').exists()).toBe(true)
  })

  it('mounts the single global toaster and confirm host', () => {
    const schema = useSchemaStore()
    vi.spyOn(schema, 'load').mockResolvedValue()
    const w = mountShell()
    expect(w.find('.stub-toaster').exists()).toBe(true)
    expect(w.find('.stub-confirm').exists()).toBe(true)
  })

  it('no longer renders the hand-rolled shell grid or scrim', () => {
    const schema = useSchemaStore()
    vi.spyOn(schema, 'load').mockResolvedValue()
    const w = mountShell()
    expect(w.find('.shell').exists()).toBe(false)
    expect(w.find('.scrim').exists()).toBe(false)
  })

  it('loads the schema on mount', () => {
    const schema = useSchemaStore()
    const loadSpy = vi.spyOn(schema, 'load').mockResolvedValue()
    mountShell()
    expect(loadSpy).toHaveBeenCalledOnce()
  })
})

// Escape-closes-drawer, route-change-closes-drawer and scrim-click-closes-drawer used to be
// AppShell's own responsibility (sidebarStore + a hand-rolled watcher/keydown listener). They
// are now the vendored SidebarProvider/Sheet's job (Escape + overlay click, via reka-ui's
// Dialog primitives underneath Sheet) plus TheSidebar.go()'s setOpenMobile(false) for the
// navigation case (Task 8). None of that is re-tested here — see the stubbed tests above,
// which stub TheSidebar out entirely — because it belongs to Sheet's own test suite and
// TheSidebar's, not AppShell's. Coverage is verified manually; see task-10-report.md.

// Smoke test with the real subtree (no TheTopbar/TheSidebar stubs). This is the mount that
// would have caught the missing SidebarProvider context that made the app throw on load for
// three tasks — the stubbed tests above never touch useSidebar()/SidebarTrigger at all.
describe('AppShell (real subtree smoke test)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    routeState.path = '/'
    routeState.name = 'dashboard'
    routeState.params = {}
  })

  it('mounts the real topbar/sidebar without throwing and renders the routed view', async () => {
    const schema = useSchemaStore()
    vi.spyOn(schema, 'load').mockResolvedValue()

    const wrapper = mount(AppShell, {
      global: {
        plugins: [i18n],
        stubs: { RouterView: { template: '<div class="rv-real">routed content</div>' } },
      },
    })

    expect(wrapper.find('.rv-real').exists()).toBe(true)
    // Real TheSidebar renders the vendored Sidebar; real TheTopbar renders SidebarTrigger.
    // Both inject useSidebar() — if AppShell no longer supplied SidebarProvider this mount
    // would have thrown during setup instead of getting here.
    expect(wrapper.text()).toContain('routed content')
  })
})
