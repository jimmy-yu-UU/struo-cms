import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import AppBreadcrumb from './AppBreadcrumb.vue'
import { useSchemaStore } from '../../stores/schemaStore'
import { i18n } from '../../i18n'

const push = vi.fn()
let currentRoute: { name: string; params: Record<string, string> }
vi.mock('vue-router', () => ({ useRouter: () => ({ push }), useRoute: () => currentRoute }))

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
    const items = wrapper.findAll('[data-slot="breadcrumb-item"]')
    expect(items.map((i) => i.text())).toEqual(['儀表板', 'Content', 'Article', '編輯項目'])

    // Click the Dashboard crumb (a real <a>) -> pushes home.
    const dashboardLink = items[0].find('a')
    expect(dashboardLink.exists()).toBe(true)
    await dashboardLink.trigger('click')
    expect(push).toHaveBeenCalledWith({ name: 'dashboard' })

    // The leaf has no link (BreadcrumbPage, not BreadcrumbLink) -> nothing to click, and
    // clicking whatever it renders as does not push again.
    push.mockClear()
    expect(items[3].find('a').exists()).toBe(false)
    await items[3].trigger('click')
    expect(push).not.toHaveBeenCalled()
  })

  it('prevents the anchor default action before pushing, so a guard-suspended navigation is not cancelled by hash nav', async () => {
    const wrapper = mount(AppBreadcrumb, { global: { plugins: [i18n] } })
    const link = wrapper.findAll('[data-slot="breadcrumb-item"]')[0].find('a')
    expect(link.exists()).toBe(true)

    const event = new MouseEvent('click', { bubbles: true, cancelable: true })
    const preventDefaultSpy = vi.spyOn(event, 'preventDefault')
    link.element.dispatchEvent(event)

    expect(preventDefaultSpy).toHaveBeenCalledOnce()
    expect(push).toHaveBeenCalledWith({ name: 'dashboard' })
  })
})
