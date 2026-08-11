import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises, enableAutoUnmount } from '@vue/test-utils'
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
  Toast: { template: '<div class="stub-toast" />' },
  Toaster: { template: '<div class="stub-toaster" />' },
  ConfirmHost: { template: '<div class="stub-confirm" />' },
  RouterView: true,
}

const mountShell = () => mount(AppShell, { global: { plugins: [i18n], stubs } })

// Do not delete this as unrelated boilerplate. Every mount() below (across every describe
// block) shares one module-level reactive `routeState`, and until this call, wrappers were
// never unmounted between tests: every earlier test's AppShell instance stayed alive and
// reactive to that same shared object, so every later beforeEach's writes to routeState.path/
// name/params were observed by every still-live prior instance too, not just the current
// test's own. The running app only ever has one AppShell mounted at a time; this file did not
// enforce that invariant. It happened to go unnoticed because no test before the routed-view
// remount-contract test below wrote to routeState after mounting -- but that made it a latent
// leak, not a non-issue, and the next test that depends on route-mutation-after-mount being
// scoped to its own instance would be exposed to it. enableAutoUnmount restores the invariant.
enableAutoUnmount(afterEach)

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

  // Six production components (SettingsView, MediaLibraryView, ItemFormView, PermissionMatrix,
  // RevisionHistoryDrawer, MediaDetailDialog) still call primevue/usetoast — ToastService stays
  // registered in main.ts, so useToast().add() never throws, it just emits on ToastEventBus with
  // nothing subscribed if no <Toast> is mounted. That failure mode is silent (no console
  // warning) and invisible to those six components' own tests, which all mock
  // primevue/usetoast and assert the call happened regardless of whether a host exists. The
  // PrimeVue <Toast> host must stay mounted alongside <Toaster/> until the last
  // primevue/usetoast call site migrates (S9) — this test exists so removing it again breaks
  // the build red, not silently.
  it('mounts the PrimeVue Toast host alongside the shadcn Toaster', () => {
    const schema = useSchemaStore()
    vi.spyOn(schema, 'load').mockResolvedValue()
    const w = mountShell()
    expect(w.find('.stub-toast').exists()).toBe(true)
    expect(w.find('.stub-toaster').exists()).toBe(true)
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
// AppShell's own responsibility (sidebarStore + a hand-rolled watcher/keydown listener).
// Escape and overlay-click are now genuinely inherited from reka-ui's Dialog primitives
// underneath Sheet (SheetContent.vue adds no override) — no app code, so nothing to unit-test
// at any level. Route-change-closes-drawer is NOT inherited from anything; it's TheSidebar's
// own `go()` (Task 8) plus a `watch(() => route.path, ...)` in TheSidebar (added in the Task 10
// review round, to also cover browser back/forward — see TheSidebar.mobileNav.test.ts).
// Neither belongs here: the stubbed tests above stub TheSidebar out entirely, so testing
// TheSidebar's own internal behaviour has to live in TheSidebar's own test files.

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

// ItemFormView has no param watcher of its own — it has onMounted(init) only, and relies
// entirely on this remount to re-run init() when navigating between two records of
// items/:id (see ItemFormView.vue and AppShell.vue's own comment on the router-view key).
// CollectionListView used to carry a redundant watch(name) "just in case"; it never fired in
// the running app (route.params.name cannot change without route.path changing, since name is
// part of the route's own path pattern) and has been deleted — this test is what actually
// guards the collection-switch behaviour now. If AppShell's `:key="route.path"` is ever
// removed, Vue reuses the existing router-view component instance across a params-only
// navigation instead of remounting it, and this test fails.
describe('AppShell (routed-view remount contract)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    routeState.path = '/collections/posts/1'
    routeState.name = 'collection-item'
    routeState.params = { name: 'posts', id: '1' }
  })

  // ItemFormView depends on this remount EXCLUSIVELY: it has onMounted(init) and no watcher on
  // route.params.id/name, so this is the only thing that re-runs init() when navigating between
  // two records of items/:id. If a future "optimisation" removes AppShell's `:key="route.path"`,
  // this test is the alarm -- this comment is why: navigating record A -> record B would keep
  // showing record A's data with no error, because nothing else would ever reload it.
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
