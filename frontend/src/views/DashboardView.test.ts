import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { ref } from 'vue'
import { createI18n } from 'vue-i18n'
import PrimeVue from 'primevue/config'
import zhTW from '../locales/zh-TW'
import en from '../locales/en'

const push = vi.fn()
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))

const state = {
  stats: ref({ items: 15, collections: 2, media: 3, users: null as number | null }),
  recent: ref([{ id: 'a1', collection: 'article', collectionLabel: 'Article', title: 'Hello', updatedAt: '2026-07-14T00:00:00Z' }]),
  quickActions: ref([{ kind: 'uploadMedia' as const }]),
  greetingKey: ref('morning' as const),
  loading: ref(false),
  error: ref(''),
  load: vi.fn().mockResolvedValue(undefined),
}
vi.mock('../composables/useDashboardData', () => ({ useDashboardData: () => state }))

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en, 'zh-TW': zhTW } })

async function mountView() {
  const DashboardView = (await import('./DashboardView.vue')).default
  const w = mount(DashboardView, { global: { plugins: [i18n, PrimeVue] } })
  await Promise.resolve()
  return w
}

describe('DashboardView', () => {
  it('renders stat cards for readable stats and hides null ones (users)', async () => {
    const w = await mountView()
    expect(w.text()).toContain('15') // items
    expect(w.text()).toContain('Content items')
    expect(w.text()).toContain('3') // media
    expect(w.text()).not.toContain('Users') // users stat is null -> hidden
  })
  it('navigates to the item when a recent row is selected', async () => {
    const w = await mountView()
    await w.get('[data-test="recent-row"]').trigger('click')
    expect(push).toHaveBeenCalledWith({ name: 'collection-item', params: { name: 'article', id: 'a1' } })
  })
  it('navigates to media for the upload quick action', async () => {
    const w = await mountView()
    await w.get('[data-test="quick-action"]').trigger('click')
    expect(push).toHaveBeenCalledWith({ name: 'media' })
  })
})
