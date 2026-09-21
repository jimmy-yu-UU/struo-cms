import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
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

  it('loads the schema on mount', () => {
    const schema = useSchemaStore()
    const loadSpy = vi.spyOn(schema, 'load').mockResolvedValue()
    mountShell()
    expect(loadSpy).toHaveBeenCalledOnce()
  })
})

// Smoke test with the real subtree (no TheTopbar/TheSidebar stubs). This is the mount that
// would catch a missing SidebarProvider context — the stubbed tests above never touch
// useSidebar()/SidebarTrigger at all.
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
    // Both inject useSidebar() — if AppShell stopped supplying SidebarProvider this mount
    // would have thrown during setup instead of getting here.
    expect(wrapper.text()).toContain('routed content')
  })
})

// ItemFormView and CollectionListView both rely on this remount exclusively (see AppShell.vue's
// :key="route.path" comment): neither watches route.params, so if the key is ever removed, Vue
// reuses the existing router-view instance across a params-only navigation instead of
// remounting it, and this test is what catches that.
describe('AppShell (routed-view remount contract)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    routeState.path = '/collections/posts/1'
    routeState.name = 'collection-item'
    routeState.params = { name: 'posts', id: '1' }
  })

  // ItemFormView depends on this remount EXCLUSIVELY (see the describe-block comment above): if
  // a future "optimisation" removes AppShell's `:key="route.path"`, navigating record A -> B
  // would keep showing A's data with no error, because nothing else would ever reload it.
  it('remounts the routed view (a fresh instance) when route.path changes, not merely re-renders it', async () => {
    const schema = useSchemaStore()
    vi.spyOn(schema, 'load').mockResolvedValue()

    let mountCount = 0
    const RoutedStub = {
      name: 'RoutedStub',
      setup() { mountCount += 1 },
      template: '<div class="routed-stub" />',
    }

    mount(AppShell, {
      global: { plugins: [i18n], stubs: { ...stubs, RouterView: RoutedStub } },
    })
    // Let the real (unstubbed) SidebarProvider's own breakpoint detection and the
    // onMounted schema.load() settle before taking the baseline -- neither is what this
    // test is about, and asserting against a moving baseline would be flaky.
    await flushPromises()
    const baseline = mountCount

    // A params-only navigation to a different record of the same route (collection-item) —
    // route.path changes even though the route's `name` (the router record) does not.
    routeState.path = '/collections/posts/2'
    routeState.params = { name: 'posts', id: '2' }
    await flushPromises()

    expect(mountCount).toBe(baseline + 1)
  })
})
