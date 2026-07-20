import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import RecentUpdatesTable from './RecentUpdatesTable.vue'
import type { RecentRow } from '../../lib/aggregateRecentUpdates'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { dashboard: { recent: { colTitle: 'Title', colCollection: 'Collection', colUpdated: 'Updated', empty: 'No recent content updates yet.' } } } },
})

const rows: RecentRow[] = [
  { id: '1', collection: 'article', collectionLabel: 'Article', title: 'Hello', updatedAt: '2026-07-14T10:00:00Z' },
]

function mountTable(props: { rows: RecentRow[] }) {
  return mount(RecentUpdatesTable, { props, global: { plugins: [i18n] } })
}

describe('RecentUpdatesTable', () => {
  it('renders a row with title and collection label', () => {
    const w = mountTable({ rows })
    expect(w.text()).toContain('Hello')
    expect(w.text()).toContain('Article')
  })
  it('emits select with the row when a row is clicked', async () => {
    const w = mountTable({ rows })
    await w.get('[data-test="recent-row"]').trigger('click')
    expect(w.emitted('select')?.[0]).toEqual([rows[0]])
  })
  it('shows the empty state when there are no rows', () => {
    const w = mountTable({ rows: [] })
    expect(w.text()).toContain('No recent content updates yet.')
  })
})
