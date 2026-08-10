import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en'
import DataTable, { type DataTableState } from './DataTable.vue'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

type Row = { id: string; title: string; status: string }

const rows: Row[] = [
  { id: '1', title: 'Alpha', status: 'published' },
  { id: '2', title: 'Beta', status: 'draft' },
]

const columns = [
  { id: 'title', accessorKey: 'title', header: 'Title', meta: { sortable: true } },
  { id: 'status', accessorKey: 'status', header: 'Status', meta: { sortable: false } },
]

const state: DataTableState = { sort: [], page: 0, pageSize: 25 }

// DataTable is a generic SFC; instantiate it with a concrete row type via a TS instantiation
// expression so `mount` sees non-generic props instead of failing to satisfy
// `abstract new (...) => any` against the generic component signature.
const RowDataTable = DataTable<Row>

function mountTable(overrides: Record<string, unknown> = {}) {
  return mount(RowDataTable, {
    props: { columns, rows, total: 2, state, ...overrides },
    global: { plugins: [i18n] },
  })
}

describe('DataTable', () => {
  it('renders one row per datum plus the header row', () => {
    expect(mountTable().findAll('tr')).toHaveLength(3)
  })

  it('renders cell values', () => {
    const text = mountTable().text()
    expect(text).toContain('Alpha')
    expect(text).toContain('Beta')
  })

  it('marks a sortable column with aria-sort and leaves others alone', () => {
    const heads = mountTable().findAll('th')
    expect(heads[0].attributes('aria-sort')).toBe('none')
    expect(heads[1].attributes('aria-sort')).toBeUndefined()
  })

  it('emits a full state with sort applied and page reset when a header is clicked', async () => {
    const w = mountTable({ state: { sort: [], page: 3, pageSize: 25 } })
    await w.findAll('th')[0].get('button').trigger('click')
    expect(w.emitted('update:state')![0][0]).toEqual({
      sort: [{ id: 'title', desc: false }],
      page: 0,          // a new sort must return to the first page
      pageSize: 25,
    })
  })

  it('shows the empty message when there are no rows', () => {
    expect(mountTable({ rows: [], total: 0, emptyMessage: 'Nothing here' }).text()).toContain('Nothing here')
  })

  // The single most dangerous defect in this component: with manual* unset, TanStack would
  // re-sort the already-paginated page client-side, so the UI would move but show wrong data.
  it('never reorders rows client-side', async () => {
    const w = mountTable({ state: { sort: [{ id: 'title', desc: true }], page: 0, pageSize: 25 } })
    const cells = w.findAll('tbody tr td:first-child').map((c) => c.text())
    expect(cells).toEqual(['Alpha', 'Beta'])   // server order preserved, NOT ['Beta', 'Alpha']
  })

  // Pins manualPagination specifically. `rows` is already the server's page 4 (2 rows only);
  // if the table re-paginated client-side using page=3/pageSize=25 against that 2-row array,
  // the offset (75) would run past the array and render NOTHING — a silent empty table, not
  // just a reordering. This mirrors the "emits..." test's page:3 but asserts the render, not
  // the emission.
  it('never re-paginates client-side against an already-server-paginated page', () => {
    const w = mountTable({ total: 50, state: { sort: [], page: 3, pageSize: 25 } })
    const cells = w.findAll('tbody tr td:first-child').map((c) => c.text())
    expect(cells).toEqual(['Alpha', 'Beta'])   // both server-supplied rows still render
  })
})
