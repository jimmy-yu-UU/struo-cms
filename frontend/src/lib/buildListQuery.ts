export type FilterSpec = Record<string, { op: string; value: string }>

export function buildListQuery(
  page: number,
  rows: number,
  sort?: string,
  search?: string,
  filter?: FilterSpec,
  locale?: string,
  deleted?: 'exclude' | 'only' | 'with',
  deep?: string[],
): Record<string, string> {
  const params: Record<string, string> = {
    limit: String(rows),
    offset: String(page * rows),
  }
  if (sort) params.sort = sort
  if (search && search.trim() !== '') params.search = search
  if (filter) {
    for (const [field, { op, value }] of Object.entries(filter)) {
      params[`filter[${field}][${op}]`] = value
    }
  }
  if (locale) params.locale = locale
  if (deleted && deleted !== 'exclude') params.deleted = deleted
  if (deep && deep.length) params.deep = deep.join(',')
  return params
}
