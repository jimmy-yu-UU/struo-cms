import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { h, reactive } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en'
import { SidebarProvider } from '@/components/ui/sidebar'
import TheSidebar from './TheSidebar.vue'
import { useAuthStore } from '@/stores/authStore'
import { useSchemaStore } from '@/stores/schemaStore'

// This file exists because jsdom's matchMedia stub (vitest.setup.ts) is hard `matches: false`,
// so the real SidebarProvider/Sidebar never observe isMobile as true — TheSidebar.test.ts's
// mobile-affecting branches (the collapsed-rail guard, and the behaviour here) are untestable
// through that real chain. Own vue-router mock, own reactive route: independent of
// TheSidebar.test.ts's non-reactive `currentRoute`, which only supports "set before mount".

const push = vi.fn()
const routeState = reactive<{ name: string; params: Record<string, string>; path: string }>({
  name: 'dashboard',
  params: {},
  path: '/',
})
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
  useRoute: () => routeState,
}))

const setOpenMobileSpy = vi.fn()

// TheSidebar imports useSidebar from the barrel ('@/components/ui/sidebar'); Sidebar.vue and
// SidebarProvider.vue both import it from './utils' directly — a different module id — so
// mocking only the barrel swaps out just TheSidebar's own composable call. The real desktop
// <Sidebar> (jsdom's matchMedia is hard-false, so it never renders as a Sheet here) keeps using
// the real context from the SidebarProvider we still wrap TheSidebar in below — required so
// <Sidebar> doesn't throw for lack of a provider. Proxy through everything except isMobile/
// setOpenMobile so this still exercises TheSidebar's real branch logic, not a fully-synthetic
// double.
vi.mock('@/components/ui/sidebar', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/components/ui/sidebar')>()
  return {
    ...actual,
    useSidebar: () => {
      const real = actual.useSidebar()
      return {
        ...real,
        isMobile: { value: true },
        setOpenMobile: (v: boolean) => {
          setOpenMobileSpy(v)
          real.setOpenMobile(v)
        },
      }
    },
  }
})

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

function mountSidebar() {
  return mount(SidebarProvider, {
    slots: { default: () => h(TheSidebar) },
    global: { plugins: [i18n] },
  })
}

describe('TheSidebar on mobile (isMobile forced true)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    push.mockClear()
    setOpenMobileSpy.mockClear()
    routeState.name = 'dashboard'
    routeState.params = {}
    routeState.path = '/'
    useAuthStore().user = { id: '1', email: 'a@b.c', isSuperAdmin: true, permissions: {} } as never
    const schema = useSchemaStore()
    schema.collections = []
    schema.loadError = ''
  })

  it('closes the drawer via setOpenMobile(false) when a nav item is activated', async () => {
    const w = mountSidebar()
    const button = w.findAll('button').find((b) => b.text().includes(en.nav.dashboard))!
    expect(button).toBeDefined()
    await button.trigger('click')
    expect(push).toHaveBeenCalledWith({ name: 'dashboard' })
    expect(setOpenMobileSpy).toHaveBeenCalledWith(false)
  })

  // The drawer-closes-on-navigation behaviour used to be "incidental" — only reachable via
  // TheSidebar.go()'s own router.push call. Browser back/forward (and the OS back gesture) also
  // change route.path but never go through go(), so nothing closed the drawer for those before
  // this watcher existed.
  it('closes the drawer when route.path changes without going through go() (e.g. browser back/forward)', async () => {
    const w = mountSidebar()
    setOpenMobileSpy.mockClear()
    routeState.path = '/media'
    routeState.name = 'media'
    await w.vm.$nextTick()
    expect(setOpenMobileSpy).toHaveBeenCalledWith(false)
  })

})
