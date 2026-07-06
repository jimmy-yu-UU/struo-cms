export type ApiError = { message: string }

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
      let message = `Request failed (${res.status})`
      try {
        const payload = await res.json()
        message = (payload?.error as ApiError)?.message ?? message
      } catch { /* non-JSON error body: keep default message */ }
      throw new Error(message)
    }

    if (res.status === 204) return undefined as T
    const text = await res.text()
    if (!text) return undefined as T
    const payload = JSON.parse(text)
    if (opts?.unwrap === false) return payload as T
    return (payload?.data ?? payload) as T
  }
}

export const apiClient = new ApiClient(import.meta.env.VITE_API_BASE_URL || '/api')
