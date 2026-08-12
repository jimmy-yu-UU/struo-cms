import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import ListToolbar from './ListToolbar.vue'

function mountLT(props: Record<string, unknown> = {}, slots: Record<string, string> = {}) {
  return mount(ListToolbar, {
    props: { searchValue: '', searchPlaceholder: 'Search…', ...props },
    slots,
  })
}

describe('ListToolbar', () => {
  it('emits `search` with the raw value on input', async () => {
    const w = mountLT()
    await w.get('input').setValue('hello')
    expect(w.emitted('search')?.at(-1)).toEqual(['hello'])
  })

  it('renders the vendored Input, not a hand-rolled one', () => {
    expect(mountLT().find('[data-slot="input"]').exists()).toBe(true)
  })

  it('renders the search icon', () => {
    expect(mountLT().find('.lucide-search').exists()).toBe(true)
  })

  it('applies the placeholder and the accessible name to the input', () => {
    const input = mountLT({ searchPlaceholder: 'Find…' }).get('input')
    expect(input.attributes('placeholder')).toBe('Find…')
    expect(input.attributes('aria-label')).toBe('Find…')
  })

  // The inbound direction: searchValue is fully parent-driven, and this component keeps no local
  // copy. A post-mount prop change is the only assertion that proves the binding is still live —
  // ui/input runs useVModel with passive: true, so an unbound control would keep its own state and
  // stay on the stale value forever.
  it('reflects searchValue, including a change after mount', async () => {
    const w = mountLT({ searchValue: 'first' })
    expect((w.get('input').element as HTMLInputElement).value).toBe('first')
    await w.setProps({ searchValue: 'second' })
    expect((w.get('input').element as HTMLInputElement).value).toBe('second')
  })

  it('renders the filters slot', () => {
    expect(mountLT({}, { filters: '<div class="marker">F</div>' }).find('.marker').exists()).toBe(true)
  })
})
