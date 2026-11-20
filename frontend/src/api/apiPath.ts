import { ApiError } from './apiError'

// A segment that URL normalisation would drop or resolve against its parent changes which
// endpoint is addressed, so it never reaches the network.
function isUnaddressable(segment: string): boolean {
  return segment === '' || segment === '.' || segment === '..'
}

// The path of an API endpoint, relative to the API base: every segment is one encoded path segment,
// so an id carrying `?`, `#`, `/` or `..` cannot add a query or climb to another endpoint.
export function apiPath(...segments: Array<string | number>): string {
  const encoded = segments.map((segment) => {
    const raw = String(segment)
    if (isUnaddressable(raw)) throw new ApiError(404, 'Not found', 'NOT_FOUND')
    return encodeURIComponent(raw)
  })
  return `/${encoded.join('/')}`
}

// The path followed by its query string; no `?` when there are no parameters.
export function withQuery(path: string, params?: URLSearchParams): string {
  const qs = params?.toString() ?? ''
  return qs === '' ? path : `${path}?${qs}`
}
