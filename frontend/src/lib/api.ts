import { useAuthStore } from '../features/auth/authStore'
import type { AuthResponse } from './types'

export const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? 'http://localhost:5080/api/v1'

/** RFC 9457 ProblemDetails (plus extension members such as ingredient/required/available). */
export class ApiError extends Error {
  status: number
  title: string
  detail?: string
  errors?: Record<string, string[]>
  extensions: Record<string, unknown>
  constructor(status: number, body: Record<string, unknown> | null, fallback: string) {
    const title = (body?.title as string | undefined) ?? fallback
    const detail = body?.detail as string | undefined
    super(detail ?? title)
    this.status = status
    this.title = title
    this.detail = detail
    this.errors = body?.errors as Record<string, string[]> | undefined
    this.extensions = body ?? {}
  }
  /** Human readable message, including field validation errors. */
  get display(): string {
    if (this.errors) {
      const msgs = Object.values(this.errors).flat()
      if (msgs.length) return msgs.join(' ')
    }
    if (this.status === 409 && this.extensions.ingredient) {
      const e = this.extensions
      return `Not enough ${String(e.ingredient)} (need ${String(e.required)}, only ${String(e.available)} left).`
    }
    return this.detail ?? this.title
  }
}

export class NetworkError extends Error {
  constructor() {
    super('Cannot reach the server. Check your connection and try again.')
  }
}

export const errorMessage = (e: unknown) =>
  e instanceof ApiError ? e.display : e instanceof Error ? e.message : 'Something went wrong.'

async function parseError(res: Response): Promise<ApiError> {
  let body: Record<string, unknown> | null = null
  try {
    body = (await res.json()) as Record<string, unknown>
  } catch {
    /* no body */
  }
  return new ApiError(res.status, body, res.statusText || `HTTP ${res.status}`)
}

let refreshing: Promise<boolean> | null = null

/** Single-flight refresh using the HttpOnly cookie. Resolves true when a new session was established. */
export function refreshSession(): Promise<boolean> {
  if (!refreshing) {
    refreshing = (async () => {
      try {
        const res = await fetch(`${API_BASE}/auth/refresh`, { method: 'POST', credentials: 'include' })
        if (!res.ok) {
          useAuthStore.getState().clear()
          return false
        }
        const data = (await res.json()) as AuthResponse
        useAuthStore.getState().setSession(data)
        return true
      } catch {
        return false
      } finally {
        setTimeout(() => {
          refreshing = null
        }, 0)
      }
    })()
  }
  return refreshing
}

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
  const url = `${API_BASE}${path}${qstr ? `?${qstr}` : ''}`

  const send = () => {
    const headers: Record<string, string> = { Accept: 'application/json', ...opts.headers }
    if (opts.body !== undefined) headers['Content-Type'] = 'application/json'
    const token = useAuthStore.getState().accessToken
    if (!opts.anonymous && token) headers.Authorization = `Bearer ${token}`
    return fetch(url, {
      method: opts.method ?? 'GET',
      headers,
      credentials: 'include',
      body: opts.body !== undefined ? JSON.stringify(opts.body) : undefined,
    })
  }

  let res: Response
  try {
    res = await send()
    if (res.status === 401 && !opts.anonymous && (await refreshSession())) res = await send()
  } catch {
    throw new NetworkError()
  }
  if (!res.ok) throw await parseError(res)
  return res
}

export async function api<T>(path: string, opts: RequestOptions = {}): Promise<T> {
  const res = await apiFetch(path, opts)
  if (res.status === 204) return undefined as T
  return (await res.json()) as T
}
