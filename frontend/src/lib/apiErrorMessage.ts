import { ApiError } from '../api/apiClient'

/** An i18n key plus its interpolation params, so callers keep control of `t`. */
export type LocalizedMessage = { key: string; params?: Record<string, unknown> }

/**
 * The one error message shared by the login page and the change-password dialog. Everything else in
 * those two surfaces has a disjoint code set, so their maps stay local to the component that uses
 * them rather than being merged into one table for the sake of symmetry.
 */
export function tooManyRequestsMessage(err: ApiError): LocalizedMessage {
  return err.retryAfterSeconds !== undefined
    ? { key: 'errors.tooManyRequestsWithWait', params: { seconds: err.retryAfterSeconds } }
    : { key: 'errors.tooManyRequests' }
}

// The error codes the server answers with for the two refusals below. The UI words them from the
// code (or the 413 status), never from the server's English prose.
const LAST_SUPER_ADMIN = 'LAST_SUPER_ADMIN'
const PAYLOAD_TOO_LARGE = 'PAYLOAD_TOO_LARGE'
const HTTP_PAYLOAD_TOO_LARGE = 413

/** A byte count as bytes, KB, MB or GB (1024-based) with one decimal above bytes. */
export function formatByteSize(bytes: number): string {
  const units = ['B', 'KB', 'MB', 'GB']
  let value = bytes
  let unit = 0
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024
    unit += 1
  }
  return unit === 0 ? `${value} B` : `${value.toFixed(1)} ${units[unit]}`
}

/** The localized refusal for a write that would leave no active super-admin; undefined for any other error. */
export function lastSuperAdminMessage(err: unknown): LocalizedMessage | undefined {
  return err instanceof ApiError && err.code === LAST_SUPER_ADMIN
    ? { key: 'errors.lastSuperAdmin' }
    : undefined
}

/**
 * The text for a failed write: the localized last-super-admin refusal, else the error's own message,
 * else the localized `fallbackKey` for anything that is not an Error.
 */
export function writeFailureMessage(
  err: unknown,
  t: (key: string, params?: Record<string, unknown>) => string,
  fallbackKey: string,
): string {
  const known = lastSuperAdminMessage(err)
  if (known) return t(known.key, known.params)
  return err instanceof Error ? err.message : t(fallbackKey)
}

/**
 * The localized refusal for an upload over the size limit; undefined for any other error. A 413 counts
 * without a code or details, since a reverse proxy answers it with a body that is not the error envelope.
 */
export function uploadTooLargeMessage(err: unknown): LocalizedMessage | undefined {
  if (!(err instanceof ApiError)) return undefined
  if (err.code !== PAYLOAD_TOO_LARGE && err.status !== HTTP_PAYLOAD_TOO_LARGE) return undefined
  const maxBytes = err.details?.find((d) => typeof d.maxBytes === 'number' && d.maxBytes > 0)?.maxBytes
  return maxBytes === undefined
    ? { key: 'media.uploadTooLarge' }
    : { key: 'media.uploadTooLargeWithSize', params: { size: formatByteSize(maxBytes) } }
}
