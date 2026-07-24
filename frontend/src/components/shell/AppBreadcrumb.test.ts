import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import AppBreadcrumb from './AppBreadcrumb.vue'
import { useSchemaStore } from '../../stores/schemaStore'
import { i18n } from '../../i18n'

const push = vi.fn()
let currentRoute: { name: string; params: Record<string, string> }
vi.mock('vue-router', () => ({ useRouter: () => ({ push }), useRoute: () => currentRoute }))
vi.mock('primevue/breadcrumb', () => ({
  default: {
    name: 'Breadcrumb',
    props: ['model'],
    // PrimeVue always invokes command with { originalEvent, item } — mirror that contract here
    // rather than calling command() bare, since the real anchor's native click event is what
    // the fix under test (originalEvent.preventDefault()) actually operates on.
    template:
      '<ul class="pv-bc"><li v-for="(m,i) in model" :key="i" class="crumb" @click="m.command && m.command({ originalEvent: $event, item: m })">{{ m.label }}</li></ul>',
  },
}))

describe('AppBreadcrumb', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    i18n.global.locale.value = 'zh-TW'
    const schema = useSchemaStore()
    schema.collections = [{ name: 'article', label: 'Article', group: 'Content', fields: [], relations: [] }]
    currentRoute = { name: 'collection-item', params: { name: 'article', id: 'x' } }
  })

  it('renders the crumb trail and navigates on a non-leaf crumb', async () => {
    const wrapper = mount(AppBreadcrumb, { global: { plugins: [i18n] } })
    const labels = wrapper.findAll('.crumb').map((c) => c.text())
    expect(labels).toEqual(['儀表板', 'Content', 'Article', '編輯項目'])
    // Click the Dashboard crumb -> pushes home.
    await wrapper.findAll('.crumb')[0].trigger('click')
    expect(push).toHaveBeenCalledWith({ name: 'dashboard' })
    // The leaf has no command -> clicking does not push again.
    push.mockClear()
    await wrapper.findAll('.crumb')[3].trigger('click')
    expect(push).not.toHaveBeenCalled()
  })

  it('prevents the anchor default action before pushing, so a guard-suspended navigation is not cancelled by hash nav', () => {
    const wrapper = mount(AppBreadcrumb, { global: { plugins: [i18n] } })
    const model = (
      wrapper.vm as unknown as {
        model: { label: string; command?: (event: { originalEvent?: { preventDefault: () => void } }) => void }[]
      }
    ).model
    const dashboardCrumb = model.find((m) => m.label === '儀表板')!
    expect(dashboardCrumb.command).toBeTypeOf('function')

    const preventDefault = vi.fn()
    dashboardCrumb.command!({ originalEvent: { preventDefault } })

    expect(preventDefault).toHaveBeenCalledOnce()
    expect(push).toHaveBeenCalledWith({ name: 'dashboard' })
  })
})
