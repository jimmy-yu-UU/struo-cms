import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { h } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en'
import { SidebarProvider } from '@/components/ui/sidebar'
import TheSidebar from './TheSidebar.vue'
import { useAuthStore } from '@/stores/authStore'
import { useSchemaStore } from '@/stores/schemaStore'
import { useAppConfigStore } from '@/stores/appConfigStore'

const push = vi.fn()
// Mutable so 'marks the active collection from the route' can point it at a collection
// route — the rest of the suite leaves it at the default dashboard route.
let currentRoute: { name: string; params: Record<string, string> } = { name: 'dashboard', params: {} }
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
  useRoute: () => currentRoute,
}))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

function mountSidebar(providerProps: Record<string, unknown> = {}) {
  return mount(SidebarProvider, {
    props: providerProps,
    slots: { default: () => h(TheSidebar) },
    global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true },
  })
}

function setUser(isSuperAdmin: boolean): void {
  useAuthStore().user = { id: '1', email: 'a@b.c', isSuperAdmin, permissions: {} } as never
}

describe('TheSidebar', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    push.mockClear()
    currentRoute = { name: 'dashboard', params: {} }
    setUser(true)
    const schema = useSchemaStore()
    schema.collections = [
      { name: 'article', label: 'Articles', group: 'Content', icon: 'article', hidden: false, fields: [] },
      { name: 'page', label: 'Pages', group: '', icon: null, hidden: false, fields: [] },
    ] as never
    schema.loadError = ''
  })

  it('renders the pinned system entries', () => {
    const text = mountSidebar().text()
    expect(text).toContain(en.nav.dashboard)
    expect(text).toContain(en.nav.media)
    expect(text).toContain(en.nav.settings)
  })

  // Relocated from TheTopbar's old suite: the brand button lived in the topbar before Task 8
  // moved it into SidebarHeader, but its own two behavioural assertions never moved with it.
  it('the brand button routes to the dashboard', async () => {
    const w = mountSidebar()
    const brandButton = w.find(`[aria-label="${en.shell.brandHome}"]`)
    expect(brandButton.exists()).toBe(true)
    await brandButton.trigger('click')
    expect(push).toHaveBeenCalledWith({ name: 'dashboard' })
  })

  it('renders the configured brand name', () => {
    useAppConfigStore().brandName = 'Acme Docs'
    const w = mountSidebar()
    expect(w.find(`[aria-label="${en.shell.brandHome}"]`).text()).toContain('Acme Docs')
  })

  // A text-only assertion here passes identically on a flat list or on broken nesting.
  // Assert the actual shadcn structure instead: ungrouped collections are top-level
  // menu-buttons, grouped ones are menu-sub-buttons, and never the other way round.
  it('renders ungrouped collections as top-level buttons and grouped ones as sub-buttons', () => {
    const w = mountSidebar()
    const topLevel = w.findAll('[data-sidebar="menu-button"]').map((el) => el.text())
    const subLevel = w.findAll('[data-sidebar="menu-sub-button"]').map((el) => el.text())
    expect(topLevel.some((t) => t.includes('Pages'))).toBe(true)
    expect(subLevel.some((t) => t.includes('Pages'))).toBe(false)
    expect(subLevel.some((t) => t.includes('Articles'))).toBe(true)
    expect(topLevel.some((t) => t.includes('Articles'))).toBe(false)
  })

  it('navigates to an ungrouped collection when its item is activated', async () => {
    const w = mountSidebar()
    const button = w.findAll('button').find((b) => b.text().includes('Pages'))
    expect(button).toBeDefined()
    await button!.trigger('click')
    expect(push).toHaveBeenCalledWith({ name: 'collection-list', params: { name: 'page' } })
  })

  // The old suite's navigation test clicked a grouped item; the rewrite's replacement
  // only clicked the ungrouped one, so SidebarMenuSubButton — the component that
  // actually changed in this task — lost its only click coverage.
  it('navigates to a grouped collection when its sub-item is activated', async () => {
    const w = mountSidebar()
    const button = w.findAll('[data-sidebar="menu-sub-button"]').find((b) => b.text().includes('Articles'))
    expect(button).toBeDefined()
    await button!.trigger('click')
    expect(push).toHaveBeenCalledWith({ name: 'collection-list', params: { name: 'article' } })
  })

  it('hides Media without file:read', () => {
    setUser(false)
    expect(mountSidebar().text()).not.toContain(en.nav.media)
  })

  it('shows the settings item only for super-admins', () => {
    expect(mountSidebar().text()).toContain(en.nav.settings)

    setUser(false)
    expect(mountSidebar().text()).not.toContain(en.nav.settings)
  })

  it('marks the active collection from the route', () => {
    currentRoute = { name: 'collection-list', params: { name: 'article' } }
    const w = mountSidebar()
    const button = w.findAll('[data-sidebar="menu-sub-button"]').find((b) => b.text().includes('Articles'))
    expect(button?.attributes('data-active')).toBe('true')
  })

  it('shows a retry affordance when the schema failed to load', async () => {
    const schema = useSchemaStore()
    schema.loadError = 'boom'
    const loadSpy = vi.spyOn(schema, 'load').mockResolvedValue()
    const w = mountSidebar()
    expect(w.text()).toContain('boom')
    expect(w.text()).toContain(en.common.retry)
    await w.find('[role="alert"] button').trigger('click')
    expect(loadSpy).toHaveBeenCalledOnce()
  })

  // Guards the v-model rewrite below (needed for the collapsed-rail cases): a normal
  // desktop toggle must still open/close the group like Collapsible's own default-open
  // did before.
  it('toggles a group closed and back open when its header is clicked while expanded', async () => {
    const w = mountSidebar()
    const header = w.findAll('button').find((b) => b.text().includes('Content'))!
    expect(w.text()).toContain('Articles')
    await header.trigger('click')
    expect(w.text()).not.toContain('Articles')
    await header.trigger('click')
    expect(w.text()).toContain('Articles')
  })

  // SidebarMenuSub is CSS-hidden whenever the rail is icon-collapsed, regardless of the
  // group's own open state, so a header click there must expand the whole sidebar or
  // grouped collections become permanently unreachable.
  it('clicking a group header while icon-collapsed expands the sidebar', async () => {
    const w = mountSidebar({ defaultOpen: false })
    const sidebarEl = w.get('[data-slot="sidebar"]')
    expect(sidebarEl.attributes('data-state')).toBe('collapsed')
    const header = w.findAll('button').find((b) => b.text().includes('Content'))!
    await header.trigger('click')
    expect(sidebarEl.attributes('data-state')).toBe('expanded')
  })

  it('clicking a group header while icon-collapsed keeps an already-open group open', async () => {
    const w = mountSidebar({ defaultOpen: false })
    const header = w.findAll('button').find((b) => b.text().includes('Content'))!
    await header.trigger('click')
    expect(w.text()).toContain('Articles')
  })

  // Regression guard for the sidebar's horizontal-scrollbar fix: ui/separator/Separator.vue's
  // `data-[orientation=horizontal]:w-full` beats ui/sidebar/SidebarSeparator.vue's own `w-auto`
  // override on CSS specificity (twMerge doesn't dedupe classes carrying different modifiers,
  // so both reach the DOM and the attribute-selector variant wins regardless of source order).
  // TheSidebar.vue compensates by re-supplying the same modifier
  // (`data-[orientation=horizontal]:w-auto`) on its one <SidebarSeparator /> — this only asserts
  // the emitted class list, not layout (jsdom does no CSS layout), but it does fail if that
  // compensating class is ever removed or edited to a mismatched modifier: with the fix reverted,
  // `data-[orientation=horizontal]:w-full` is present and this assertion catches it.
  it('never lets the vendored separator keep its w-full variant', () => {
    const w = mountSidebar()
    const separator = w.get('[data-slot="sidebar-separator"]')
    const hasWFull = separator.classes().some((c) => c === 'w-full' || c.endsWith(':w-full'))
    expect(hasWFull).toBe(false)
  })
})
