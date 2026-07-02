export function buildListQuery(
  page: number,
  rows: number,
  sort?: string,
  search?: string,
): Record<string, string> {
  const params: Record<string, string> = {
    limit: String(rows),
    offset: String(page * rows),
  }
  if (sort) params.sort = sort
  if (search && search.trim() !== '') params.search = search
  return params
}
