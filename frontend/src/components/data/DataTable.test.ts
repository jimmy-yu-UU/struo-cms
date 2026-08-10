import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en'
import DataTable, { type DataTableState, toSortParam } from './DataTable.vue'

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

  // The PrimeVue DataTable this replaced rendered a spinner overlay for the same `loading` prop;
  // this component only wired it to aria-busy, which is invisible without assistive tech. A
  // loading affordance must actually render something a sighted user can see.
  it('renders a visible loading indicator when loading is true', () => {
    expect(mountTable({ loading: true }).find('[role="status"]').exists()).toBe(true)
  })

  it('renders no loading indicator when loading is false or unset', () => {
    expect(mountTable({ loading: false }).find('[role="status"]').exists()).toBe(false)
    expect(mountTable().find('[role="status"]').exists()).toBe(false)
  })

  it('keeps rendering the (stale) rows underneath the loading indicator, not a blank table', () => {
    const w = mountTable({ loading: true })
    expect(w.text()).toContain('Alpha')
    expect(w.text()).toContain('Beta')
  })

  it('still marks the scroll wrapper aria-busy while loading', () => {
    expect(mountTable({ loading: true }).find('.overflow-x-auto').attributes('aria-busy')).toBe('true')
    expect(mountTable({ loading: false }).find('.overflow-x-auto').attributes('aria-busy')).toBe('false')
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

  // TanStack derives a column id from accessorKey when the column def omits `id` explicitly.
  // Headers must read that table-resolved id, not re-derive (or fail to derive) their own from
  // the raw column def — otherwise a header click emits a bogus/undefined sort field.
  it('derives the sort column id from the resolved column, not the column def, when id is omitted', async () => {
    const idLessColumns = [
      { accessorKey: 'title', header: 'Title', meta: { sortable: true } },
      { accessorKey: 'status', header: 'Status', meta: { sortable: false } },
    ]
    const w = mountTable({ columns: idLessColumns })
    await w.findAll('th')[0].get('button').trigger('click')
    expect(w.emitted('update:state')![0][0]).toEqual({
      sort: [{ id: 'title', desc: false }],   // the real accessor key, never "undefined"
      page: 0,
      pageSize: 25,
    })
  })
})

describe('toSortParam', () => {
  it('formats an ascending sort as the bare field name', () => {
    expect(toSortParam([{ id: 'title', desc: false }])).toBe('title')
  })

  it('formats a descending sort with a leading minus', () => {
    expect(toSortParam([{ id: 'title', desc: true }])).toBe('-title')
  })

  it('returns undefined for no sort', () => {
    expect(toSortParam([])).toBeUndefined()
  })
})
