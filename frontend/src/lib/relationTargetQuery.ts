import { buildListQuery, type FilterSpec } from './buildListQuery'

export function relationTargetQuery(opts: {
  page: number
  rows: number
  search?: string
  locale?: string
  filter?: FilterSpec
}): Record<string, string> {
  return buildListQuery(opts.page, opts.rows, {
    search: opts.search,
    filter: opts.filter,
    locale: opts.locale,
  })
}
