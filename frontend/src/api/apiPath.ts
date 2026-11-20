import { ApiError } from './apiError'
import { i18n } from '../i18n'

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
    if (isUnaddressable(raw)) throw new ApiError(404, i18n.global.t('common.requestFailed', { status: 404 }), 'NOT_FOUND')
    return encodeURIComponent(raw)
  })
  return `/${encoded.join('/')}`
}

// The path followed by an already encoded query string; no `?` when the query is empty. For a caller
// whose query must keep an encoding of its own; every other caller uses withQuery.
export function withRawQuery(path: string, query: string): string {
  return query === '' ? path : `${path}?${query}`
}

// The path followed by its query string; no `?` when there are no parameters.
export function withQuery(path: string, params?: URLSearchParams): string {
  return withRawQuery(path, params?.toString() ?? '')
}
