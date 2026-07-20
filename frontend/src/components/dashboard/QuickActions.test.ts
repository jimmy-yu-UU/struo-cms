import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import PrimeVue from 'primevue/config'
import QuickActions from './QuickActions.vue'
import type { QuickAction } from '../../lib/quickActions'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { dashboard: { quick: { uploadMedia: 'Upload media', newItem: 'New {label}', empty: 'No quick actions available.' } } } },
})

function mountQA(actions: QuickAction[]) {
  return mount(QuickActions, { props: { actions }, global: { plugins: [i18n, PrimeVue] } })
}

describe('QuickActions', () => {
  it('renders a button per action with resolved labels', () => {
    const w = mountQA([{ kind: 'uploadMedia' }, { kind: 'newItem', collection: 'article', label: 'Article' }])
    expect(w.text()).toContain('Upload media')
    expect(w.text()).toContain('New Article')
  })
  it('emits run with the action when a button is clicked', async () => {
    const action: QuickAction = { kind: 'uploadMedia' }
    const w = mountQA([action])
    await w.get('[data-test="quick-action"]').trigger('click')
    expect(w.emitted('run')?.[0]).toEqual([action])
  })
  it('shows the empty state when there are no actions', () => {
    const w = mountQA([])
    expect(w.text()).toContain('No quick actions available.')
  })
})
