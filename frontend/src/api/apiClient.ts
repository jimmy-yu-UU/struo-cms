export type ValidationDetail = { field: string; message: string }

// The shape of the server's error envelope body.
export type ApiErrorBody = {
  code?: string
  message: string
  details?: ValidationDetail[]
}

// The Error thrown by apiClient on any non-2xx response.
export class ApiError extends Error {
  readonly status: number
  readonly code?: string
  readonly details?: ValidationDetail[]
  // Seconds to wait, lifted from the standard Retry-After response header when the rejecting rate
  // limiter supplies one (the window limiters this API ships with do; OnRejected also has a bare
  // fallback branch for limiters that do not, so this can be absent on a 429 too). Kept off the
  // error envelope on purpose: ErrorBody.details is a {field,message} list, so a scalar delay has
  // no honest place in it.
  readonly retryAfterSeconds?: number
  constructor(
    status: number,
    message: string,
    code?: string,
    details?: ValidationDetail[],
    retryAfterSeconds?: number,
  ) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
    this.details = details
    this.retryAfterSeconds = retryAfterSeconds
  }
}

// CSRF: the API rejects cookie-authenticated mutations that lack this header (OWASP custom-header
// method). The value is irrelevant — a cross-site page cannot set a custom header on a credentialed
// request without the API's CORS allowing its origin, so its mere presence proves same-app origin.
const CSRF_HEADER = 'X-Struo-CSRF'
const SAFE_METHODS = new Set(['GET', 'HEAD', 'OPTIONS', 'TRACE'])

export class ApiClient {
  private readonly baseUrl: string
  private onUnauthorized: (() => void) | null = null

  constructor(baseUrl: string) {
    this.baseUrl = baseUrl
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
    const res = await fetch(`${this.baseUrl}${path}`, {
      method,
      credentials: 'include',
      headers: Object.keys(headers).length > 0 ? headers : undefined,
      body: body === undefined ? undefined : isForm ? (body as FormData) : JSON.stringify(body),
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
      throw new ApiError(
        res.status,
        errBody?.message ?? `Request failed (${res.status})`,
        errBody?.code,
        errBody?.details,
        retryAfterSeconds,
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
