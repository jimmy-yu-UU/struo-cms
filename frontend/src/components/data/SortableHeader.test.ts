import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en'
import SortableHeader from './SortableHeader.vue'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

type SortableHeaderProps = InstanceType<typeof SortableHeader>['$props']

function mountHeader(props: Record<string, unknown>) {
  return mount(SortableHeader, { props: props as unknown as SortableHeaderProps, global: { plugins: [i18n] } })
}

describe('SortableHeader', () => {
  it('renders a plain label when the column is not sortable', () => {
    const w = mountHeader({ label: 'Title', columnId: 'title', sortable: false, sort: [] })
    expect(w.text()).toContain('Title')
    expect(w.find('button').exists()).toBe(false)
  })

  it('exposes aria-sort=none when sortable but unsorted', () => {
    const w = mountHeader({ label: 'Title', columnId: 'title', sortable: true, sort: [] })
    expect(w.attributes('aria-sort')).toBe('none')
  })

  it('reflects the active direction through aria-sort', () => {
    expect(mountHeader({ label: 'T', columnId: 'title', sortable: true, sort: [{ id: 'title', desc: false }] })
      .attributes('aria-sort')).toBe('ascending')
    expect(mountHeader({ label: 'T', columnId: 'title', sortable: true, sort: [{ id: 'title', desc: true }] })
      .attributes('aria-sort')).toBe('descending')
  })

  it('ignores a sort that belongs to another column', () => {
    const w = mountHeader({ label: 'T', columnId: 'title', sortable: true, sort: [{ id: 'status', desc: true }] })
    expect(w.attributes('aria-sort')).toBe('none')
  })

  // Three-state cycle: none -> asc -> desc -> none. Without the final step there is no way
  // back to the server's natural ordering once a user has sorted.
  it('cycles none -> asc -> desc -> none', async () => {
    const none = mountHeader({ label: 'T', columnId: 'title', sortable: true, sort: [] })
    await none.get('button').trigger('click')
    expect(none.emitted('update:sort')![0][0]).toEqual([{ id: 'title', desc: false }])

    const asc = mountHeader({ label: 'T', columnId: 'title', sortable: true, sort: [{ id: 'title', desc: false }] })
    await asc.get('button').trigger('click')
    expect(asc.emitted('update:sort')![0][0]).toEqual([{ id: 'title', desc: true }])

    const desc = mountHeader({ label: 'T', columnId: 'title', sortable: true, sort: [{ id: 'title', desc: true }] })
    await desc.get('button').trigger('click')
    expect(desc.emitted('update:sort')![0][0]).toEqual([])
  })
})
