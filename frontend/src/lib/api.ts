/**
 * Compatibility shim: the old untyped `api()` helper, now built on the shared fetch in `./apiClient` so there is a
 * single refresh/retry implementation. Features migrate to the typed `client` one slice at a time; removed in F7.
 */
import { API_BASE, anonFetch, authedFetch } from './apiClient'

export { API_BASE, refreshSession } from './apiClient'
export { ApiError, NetworkError, errorMessage } from './errors'

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE'
  body?: unknown
  query?: Record<string, string | number | boolean | null | undefined>
  headers?: Record<string, string>
  /** Skip bearer + refresh-on-401 (login). */
  anonymous?: boolean
}

export async function apiFetch(path: string, opts: RequestOptions = {}): Promise<Response> {
  const qs = new URLSearchParams()
  for (const [k, v] of Object.entries(opts.query ?? {})) {
    if (v !== undefined && v !== null && v !== '') qs.set(k, String(v))
  }
  const qstr = qs.toString()
  const headers: Record<string, string> = { Accept: 'application/json', ...opts.headers }
  const isForm = opts.body instanceof FormData
  if (opts.body !== undefined && !isForm) headers['Content-Type'] = 'application/json'
  const request = new Request(`${API_BASE}${path}${qstr ? `?${qstr}` : ''}`, {
    method: opts.method ?? 'GET',
    headers,
    credentials: 'include',
    body: opts.body === undefined ? undefined : isForm ? (opts.body as FormData) : JSON.stringify(opts.body),
  })
  return (opts.anonymous ? anonFetch : authedFetch)(request)
}

export async function api<T>(path: string, opts: RequestOptions = {}): Promise<T> {
  const res = await apiFetch(path, opts)
  if (res.status === 204) return undefined as T
  return (await res.json()) as T
}
