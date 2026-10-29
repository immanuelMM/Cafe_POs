import createClient from 'openapi-fetch'
import type { paths } from '../api/schema'
import { useAuthStore } from '../features/auth/authStore'
import { ApiError, NetworkError } from './errors'

const DEFAULT_API_BASE = 'http://localhost:5080/api/v1'

/**
 * Validates `VITE_API_BASE_URL`. The generated paths already start with `/api/v1`, so the base must end with
 * `/api/v1`; everything before it (origin plus any gateway prefix) is what the client and asset URLs hang off.
 *
 * Two forms are accepted: an absolute http(s) URL, or a root-relative path (`/api/v1`, `/prefix/api/v1`) that is
 * resolved against `pageOrigin` (the page's own origin at runtime) so one build works on any host.
 */
export function resolveApiBase(
  raw: string | undefined,
  pageOrigin: string = globalThis.location?.origin ?? 'http://localhost',
): { base: string; origin: string } {
  const configured = (raw?.trim() || DEFAULT_API_BASE).replace(/\/+$/, '')
  const isAbsolute = /^https?:\/\/.+\/api\/v1$/.test(configured)
  const isRootRelative = /^(\/[^/\s]+)*\/api\/v1$/.test(configured) // rejects //host and a bare api/v1
  if (!isAbsolute && !isRootRelative) {
    throw new Error(
      `VITE_API_BASE_URL must end in /api/v1 and be either an absolute URL (e.g. ${DEFAULT_API_BASE} or https://host/prefix/api/v1) or a root-relative path (e.g. /api/v1 or /prefix/api/v1); got "${raw ?? ''}".`,
    )
  }
  const base = isAbsolute ? configured : `${pageOrigin.replace(/\/+$/, '')}${configured}`
  return { base, origin: base.slice(0, -'/api/v1'.length) }
}

const resolved = resolveApiBase(import.meta.env.VITE_API_BASE_URL as string | undefined)
export const API_BASE = resolved.base

/** API origin (API_BASE without the trailing /api/v1, keeping any gateway prefix). Used for asset URLs. */
export const API_ORIGIN = resolved.origin

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
 * Sources for multipart bodies, keyed by a per-call marker header, so a retry can be rebuilt from the original
 * Blob after a 401 instead of buffering a clone of every upload up front.
 */
const RETRY_HEADER = 'x-cafepos-retry-source'
const retrySources = new Map<string, { file: Blob; filename: string }>()

/**
 * `fetch` for openapi-fetch: attaches the in-memory bearer token; on a 401 runs the single-flight refresh and
 * resends the request once; rejects with `ApiError` (non-2xx) or `NetworkError` (unreachable), so callers never
 * see an `{ error }` result. If the refresh itself fails transiently (429/5xx/network) that error is surfaced and
 * the session is kept.
 *
 * A Request body can be consumed only once. Body-less requests are resent as-is, multipart uploads are rebuilt
 * lazily from their preserved source (see `fileBody`), and other (small JSON) bodies are cloned up front.
 */
function createApiFetch(anonymous: boolean) {
  return async (request: Request): Promise<Response> => {
    const sourceKey = request.headers.get(RETRY_HEADER)
    request.headers.delete(RETRY_HEADER)
    const source = sourceKey ? retrySources.get(sourceKey) : undefined
    const spare = !anonymous && request.body && !source ? request.clone() : null
    const send = (req: Request) => {
      const token = useAuthStore.getState().accessToken
      if (!anonymous && token) req.headers.set('Authorization', `Bearer ${token}`)
      return globalThis.fetch(req)
    }
    const buildRetry = (): Request | null => {
      if (!request.body) return request
      if (spare) return spare
      if (!source) return null
      const headers = new Headers(request.headers)
      headers.delete('content-type') // regenerated with the new multipart boundary
      const fd = new FormData()
      fd.append('file', source.file, source.filename)
      return new Request(request.url, { method: request.method, headers, body: fd, credentials: request.credentials })
    }
    let res: Response
    try {
      res = await send(request)
      if (res.status === 401 && !anonymous && (await refreshOrThrow())) {
        const retry = buildRetry()
        if (retry) res = await send(retry)
      }
    } catch (e) {
      // A transient refresh failure (429/5xx) is surfaced to the failed request as that ApiError.
      if (e instanceof ApiError) throw e
      throw new NetworkError()
    } finally {
      if (sourceKey) retrySources.delete(sourceKey)
    }
    if (!res.ok) throw await parseError(res)
    return res
  }
}

const authedFetch = createApiFetch(false)
const anonFetch = createApiFetch(true)

/** Query strings as the app has always built them: null, undefined and '' are omitted. */
const querySerializer = (query: object) => {
  const qs = new URLSearchParams()
  for (const [k, v] of Object.entries(query)) {
    if (v !== undefined && v !== null && v !== '') qs.set(k, String(v))
  }
  return qs.toString()
}

const clientOptions = {
  baseUrl: API_ORIGIN,
  credentials: 'include',
  headers: { Accept: 'application/json' },
  querySerializer,
} as const

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
export const fileBody = (file: Blob, filename: string) => {
  const key = crypto.randomUUID()
  retrySources.set(key, { file, filename })
  return {
    body: { file: file as unknown as string },
    headers: { [RETRY_HEADER]: key },
    bodySerializer: () => {
      const fd = new FormData()
      fd.append('file', file, filename)
      return fd
    },
  }
}

let refreshing: Promise<boolean> | null = null

const REFRESH_LOCK = 'cafepos-refresh'

/**
 * Refresh tokens rotate, so two tabs presenting the same cookie look like token theft to the backend. The Web
 * Lock serialises tabs: the waiting tab then sends the already-rotated cookie. Without `navigator.locks`
 * (jsdom, old browsers) this falls back to per-tab single-flight only.
 */
function refreshUnderLock<T>(fn: () => Promise<T>): Promise<T> {
  const locks = typeof navigator === 'undefined' ? undefined : navigator.locks
  return locks ? locks.request(REFRESH_LOCK, fn) : fn()
}

async function doRefresh(): Promise<boolean> {
  try {
    const data = await refreshUnderLock(() => unwrap(anonClient.POST('/api/v1/auth/refresh')))
    useAuthStore.getState().setSession(data)
    return true
  } catch (e) {
    // Only an explicit rejection of the cookie ends the session; 429/5xx/network keep it and are rethrown.
    if (e instanceof ApiError && (e.status === 401 || e.status === 403)) {
      useAuthStore.getState().clear()
      return false
    }
    throw e
  }
}

/** Single-flight refresh; resolves false when the session is gone, rejects on a transient failure. */
function refreshOrThrow(): Promise<boolean> {
  if (!refreshing) {
    refreshing = doRefresh().finally(() => {
      setTimeout(() => {
        refreshing = null
      }, 0)
    })
  }
  return refreshing
}

/** Single-flight refresh using the HttpOnly cookie. Resolves true when a new session was established. */
export async function refreshSession(): Promise<boolean> {
  try {
    return await refreshOrThrow()
  } catch {
    return false
  }
}
