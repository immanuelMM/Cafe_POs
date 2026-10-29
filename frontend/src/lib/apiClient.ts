import createClient from 'openapi-fetch'
import type { paths } from '../api/schema'
import { useAuthStore } from '../features/auth/authStore'
import { ApiError, NetworkError } from './errors'
import type { AuthResponse } from './types'

export const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? 'http://localhost:5080/api/v1'

/** API origin (API_BASE without the trailing /api/v1). The generated paths already include `/api/v1`. */
export const API_ORIGIN = API_BASE.replace(/\/api\/v1\/?$/, '')

async function parseError(res: Response): Promise<ApiError> {
  let body: Record<string, unknown> | null = null
  try {
    body = (await res.json()) as Record<string, unknown>
  } catch {
    /* no body */
  }
  return new ApiError(res.status, body, res.statusText || `HTTP ${res.status}`)
}

/**
 * `fetch` for openapi-fetch: attaches the in-memory bearer token; on a 401 runs the single-flight refresh and
 * resends the request once; rejects with `ApiError` (non-2xx) or `NetworkError` (unreachable), so callers never
 * see an `{ error }` result.
 *
 * The request is cloned BEFORE it is sent because a Request body can be consumed only once; the clone (JSON or
 * multipart) is what gets resent after the refresh.
 */
function createApiFetch(anonymous: boolean) {
  return async (request: Request): Promise<Response> => {
    const retry = anonymous ? null : request.clone()
    const send = (req: Request) => {
      const token = useAuthStore.getState().accessToken
      if (!anonymous && token) req.headers.set('Authorization', `Bearer ${token}`)
      return globalThis.fetch(req)
    }
    let res: Response
    try {
      res = await send(request)
      if (res.status === 401 && retry && (await refreshSession())) res = await send(retry)
    } catch {
      throw new NetworkError()
    }
    if (!res.ok) throw await parseError(res)
    return res
  }
}

/** Shared with the temporary `api()` shim in `./api`. */
export const authedFetch = createApiFetch(false)
export const anonFetch = createApiFetch(true)

const clientOptions = { baseUrl: API_ORIGIN, credentials: 'include', headers: { Accept: 'application/json' } } as const

/** Authenticated client (bearer + refresh-on-401). */
export const client = createClient<paths>({ ...clientOptions, fetch: authedFetch })
/** For endpoints that must not carry a token or trigger a refresh (login, logout, refresh, public branding). */
export const anonClient = createClient<paths>({ ...clientOptions, fetch: anonFetch })

/**
 * The client rejects on every non-2xx, so a settled call always has `data` (undefined only for 204 responses).
 * Typed loosely on purpose: `T` is inferred from the call's response type.
 */
export async function unwrap<T>(call: Promise<{ data?: T }>): Promise<T> {
  return (await call).data as T
}

/** multipart/form-data body for a binary `file` field (openapi-typescript types binary as string). */
export const fileBody = (file: Blob, filename: string) => ({
  body: { file: file as unknown as string },
  bodySerializer: () => {
    const fd = new FormData()
    fd.append('file', file, filename)
    return fd
  },
})

let refreshing: Promise<boolean> | null = null

/** Single-flight refresh using the HttpOnly cookie. Resolves true when a new session was established. */
export function refreshSession(): Promise<boolean> {
  if (!refreshing) {
    refreshing = (async () => {
      try {
        const data = await unwrap(anonClient.POST('/api/v1/auth/refresh'))
        useAuthStore.getState().setSession(data as unknown as AuthResponse) // typed from the schema in F3
        return true
      } catch (e) {
        if (e instanceof ApiError) useAuthStore.getState().clear()
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
