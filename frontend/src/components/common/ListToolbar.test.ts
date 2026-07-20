import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import ListToolbar from './ListToolbar.vue'

function mountLT(props: Record<string, unknown> = {}, slots: Record<string, string> = {}) {
  return mount(ListToolbar, {
    props: { searchValue: '', searchPlaceholder: 'Search…', ...props },
    slots,
    global: { plugins: [PrimeVue] },
  })
}

describe('ListToolbar', () => {
  it('emits `search` with the raw value on input', async () => {
    const w = mountLT()
    const input = w.get('input')
    await input.setValue('hello')
    expect(w.emitted('search')?.at(-1)).toEqual(['hello'])
  })
  it('renders the search icon', () => {
    expect(mountLT().find('.pi-search').exists()).toBe(true)
  })
  it('applies the placeholder to the input', () => {
    expect(mountLT({ searchPlaceholder: 'Find…' }).get('input').attributes('placeholder')).toBe('Find…')
  })
  it('renders the filters slot', () => {
    const w = mountLT({}, { filters: '<div class="marker">F</div>' })
    expect(w.find('.marker').exists()).toBe(true)
  })
})
