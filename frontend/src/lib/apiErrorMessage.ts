import type { ApiError } from '../api/apiClient'

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
