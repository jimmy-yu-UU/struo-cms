export type ApiError = { message: string }

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

  getRaw<T>(path: string): Promise<T> {
    return this.request<T>('GET', path, undefined, { unwrap: false })
  }

  private async request<T>(
    method: string,
    path: string,
    body?: unknown,
    opts?: { unwrap?: boolean },
  ): Promise<T> {
    const res = await fetch(`${this.baseUrl}${path}`, {
      method,
      credentials: 'include',
      headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
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
