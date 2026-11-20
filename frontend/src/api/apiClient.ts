import { i18n } from '../i18n'
import { ApiError, type ApiErrorBody } from './apiError'

export { ApiError } from './apiError'
export type { ValidationDetail, ApiErrorBody } from './apiError'

// A server fault (5xx) is the only error a user cannot act on, so it is the only one that shows the
// trace id.
function showsTraceId(status: number, traceId: string | undefined): traceId is string {
  return status >= 500 && !!traceId
}

// The localized trace line for a toast whose summary is a fixed string; undefined for any other error.
export function traceIdDetail(e: unknown): string | undefined {
  return e instanceof ApiError && showsTraceId(e.status, e.traceId)
    ? i18n.global.t('errors.traceIdLine', { id: e.traceId })
    : undefined
}

// CSRF: the API rejects cookie-authenticated mutations that lack this header (OWASP custom-header
// method). The value is irrelevant — a cross-site page cannot set a custom header on a credentialed
// request without the API's CORS allowing its origin, so its mere presence proves same-app origin.
const CSRF_HEADER = 'X-Struo-CSRF'
const SAFE_METHODS = new Set(['GET', 'HEAD', 'OPTIONS', 'TRACE'])

function serializeBody(body: unknown, isForm: boolean): FormData | string | undefined {
  if (body === undefined) return undefined
  return isForm ? (body as FormData) : JSON.stringify(body)
}

export class ApiClient {
  private readonly baseUrl: string
  private onUnauthorized: (() => void) | null = null

  constructor(baseUrl: string) {
    this.baseUrl = baseUrl
  }

  // The URL of an endpoint path under this client's base: the only place the configured API base and a path are joined.
  urlFor(path: string): string {
    return `${this.baseUrl}${path}`
  }

  setUnauthorizedHandler(fn: () => void): void {
    this.onUnauthorized = fn
  }

  get<T>(path: string): Promise<T> {
    return this.request<T>('GET', path)
  }

  post<T>(path: string, body?: unknown): Promise<T> {
    return this.request<T>('POST', path, body)
  }

  postForm<T>(path: string, form: FormData): Promise<T> {
    return this.request<T>('POST', path, form)
  }

  getRaw<T>(path: string): Promise<T> {
    return this.request<T>('GET', path, undefined, { unwrap: false })
  }

  put<T>(path: string, body?: unknown): Promise<T> {
    return this.request<T>('PUT', path, body)
  }

  delete<T>(path: string): Promise<T> {
    return this.request<T>('DELETE', path)
  }

  private async request<T>(
    method: string,
    path: string,
    body?: unknown,
    opts?: { unwrap?: boolean },
  ): Promise<T> {
    const isForm = body instanceof FormData
    const headers: Record<string, string> = {}
    if (body !== undefined && !isForm) headers['Content-Type'] = 'application/json'
    if (!SAFE_METHODS.has(method)) headers[CSRF_HEADER] = '1'
    const res = await fetch(this.urlFor(path), {
      method,
      credentials: 'include',
      headers: Object.keys(headers).length > 0 ? headers : undefined,
      body: serializeBody(body, isForm),
    })

    if (res.status === 401) this.onUnauthorized?.()

    if (!res.ok) {
      let errBody: ApiErrorBody | undefined
      try {
        errBody = (await res.json())?.error as ApiErrorBody | undefined
      } catch { /* non-JSON error body: leave errBody undefined */ }
      // Retry-After is either delta-seconds or an HTTP-date; only the former is emitted here, so a
      // non-numeric value is dropped rather than guessed at.
      const retryAfterRaw = res.headers.get('Retry-After')
      const retryAfterSeconds =
        retryAfterRaw !== null && /^\d+$/.test(retryAfterRaw.trim())
          ? Number(retryAfterRaw.trim())
          : undefined
      const message = errBody?.message ?? i18n.global.t('common.requestFailed', { status: res.status })
      throw new ApiError(
        res.status,
        showsTraceId(res.status, errBody?.traceId)
          ? message + i18n.global.t('errors.traceIdSuffix', { id: errBody.traceId })
          : message,
        errBody?.code,
        errBody?.details,
        retryAfterSeconds,
        errBody?.traceId,
      )
    }

    if (res.status === 204) return undefined as T
    const text = await res.text()
    if (!text) return undefined as T
    const payload = JSON.parse(text)
    if (opts?.unwrap === false) return payload as T
    if (payload && typeof payload === 'object' && 'success' in payload) {
      return (payload as { data?: unknown }).data as T
    }
    return (payload?.data ?? payload) as T
  }
}

export const apiBaseUrl = import.meta.env.VITE_API_BASE_URL || '/api'
export const apiClient = new ApiClient(apiBaseUrl)
