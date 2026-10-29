import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it } from 'vitest'
import { server } from '../test/server'
import { useAuthStore } from '../features/auth/authStore'
import { assetUrl } from '../features/branding/useBranding'
import { anonClient, API_ORIGIN, client, resolveApiBase, unwrap } from './apiClient'
import { ApiError, NetworkError } from './errors'
import type { AuthResponse } from '../api/types'

const session = (token: string) =>
  ({
    accessToken: token,
    expiresAt: '2099-01-01T00:00:00Z',
    user: { id: 'u1' },
  }) as unknown as AuthResponse

const REFRESH = `${API_ORIGIN}/api/v1/auth/refresh`
const CATEGORIES = `${API_ORIGIN}/api/v1/categories`
const ID = '00000000-0000-0000-0000-000000000001'

describe('typed api client', () => {
  beforeEach(async () => {
    useAuthStore.getState().clear()
    // refreshSession() releases its single-flight slot on a macrotask; let the previous test's slot clear.
    await new Promise((r) => setTimeout(r, 0))
  })

  it('sends the in-memory bearer token', async () => {
    useAuthStore.getState().setSession(session('tok-1'))
    let auth: string | null = null
    server.use(
      http.get(CATEGORIES, ({ request }) => {
        auth = request.headers.get('authorization')
        return HttpResponse.json([])
      }),
    )
    await client.GET('/api/v1/categories')
    expect(auth).toBe('Bearer tok-1')
  })

  it('does not send the bearer token or refresh on 401 for anonymous calls', async () => {
    useAuthStore.getState().setSession(session('tok-1'))
    let refreshCalls = 0
    let auth: string | null = 'unset'
    server.use(
      http.post(REFRESH, () => {
        refreshCalls++
        return HttpResponse.json(session('fresh'))
      }),
      http.post(`${API_ORIGIN}/api/v1/auth/login`, ({ request }) => {
        auth = request.headers.get('authorization')
        return HttpResponse.json({ title: 'Unauthorized', detail: 'Bad credentials' }, { status: 401 })
      }),
    )
    await expect(
      anonClient.POST('/api/v1/auth/login', { body: { email: 'a@b.c', password: 'x', locationId: null } }),
    ).rejects.toMatchObject({ status: 401, message: 'Bad credentials' })
    expect(auth).toBeNull()
    expect(refreshCalls).toBe(0)
  })

  it('refreshes once on concurrent 401s (single-flight) and retries', async () => {
    useAuthStore.getState().setSession(session('stale'))
    let refreshCalls = 0
    server.use(
      http.post(REFRESH, () => {
        refreshCalls++
        return HttpResponse.json(session('fresh'))
      }),
      http.get(CATEGORIES, ({ request }) =>
        request.headers.get('authorization') === 'Bearer fresh'
          ? HttpResponse.json([{ id: 'c1' }])
          : new HttpResponse(null, { status: 401 }),
      ),
    )
    const results = await Promise.all([
      unwrap(client.GET('/api/v1/categories')),
      unwrap(client.GET('/api/v1/categories')),
      unwrap(client.GET('/api/v1/categories')),
    ])
    expect(results).toEqual([[{ id: 'c1' }], [{ id: 'c1' }], [{ id: 'c1' }]])
    expect(refreshCalls).toBe(1)
  })

  it('clears the session when the refresh fails', async () => {
    useAuthStore.getState().setSession(session('stale'))
    server.use(
      http.post(REFRESH, () => new HttpResponse(null, { status: 401 })),
      http.get(CATEGORIES, () => new HttpResponse(null, { status: 401 })),
    )
    await expect(client.GET('/api/v1/categories')).rejects.toMatchObject({ status: 401 })
    expect(useAuthStore.getState().accessToken).toBeNull()
  })

  it('resends a JSON body intact after the refresh', async () => {
    useAuthStore.getState().setSession(session('stale'))
    const bodies: unknown[] = []
    server.use(
      http.post(REFRESH, () => HttpResponse.json(session('fresh'))),
      http.post(CATEGORIES, async ({ request }) => {
        if (request.headers.get('authorization') !== 'Bearer fresh') return new HttpResponse(null, { status: 401 })
        bodies.push(await request.json())
        return HttpResponse.json({ id: 'c1' })
      }),
    )
    await client.POST('/api/v1/categories', { body: { name: 'Tea', sortOrder: 1, active: true } })
    expect(bodies).toEqual([{ name: 'Tea', sortOrder: 1, active: true }])
  })

  it('returns undefined data for 204 responses', async () => {
    useAuthStore.getState().setSession(session('tok'))
    server.use(http.delete(`${CATEGORIES}/:id`, () => new HttpResponse(null, { status: 204 })))
    const r = await client.DELETE('/api/v1/categories/{id}', { params: { path: { id: ID } } })
    expect(r.data).toBeUndefined()
  })

  it('rejects with ApiError carrying ProblemDetails fields', async () => {
    useAuthStore.getState().setSession(session('tok'))
    server.use(http.get(CATEGORIES, () => HttpResponse.json({ title: 'Nope', detail: 'Bad thing' }, { status: 400 })))
    await expect(client.GET('/api/v1/categories')).rejects.toMatchObject({ status: 400, message: 'Bad thing' })
    await expect(client.GET('/api/v1/categories')).rejects.toBeInstanceOf(ApiError)
  })

  it('rejects with NetworkError when the server is unreachable', async () => {
    server.use(http.get(CATEGORIES, () => HttpResponse.error()))
    await expect(client.GET('/api/v1/categories')).rejects.toBeInstanceOf(NetworkError)
  })

  const ok401 = () =>
    http.get(CATEGORIES, ({ request }) =>
      request.headers.get('authorization') === 'Bearer fresh'
        ? HttpResponse.json([])
        : new HttpResponse(null, { status: 401 }),
    )

  it.each([429, 500, 503])('keeps the session and surfaces the error when the refresh gets %i', async (status) => {
    useAuthStore.getState().setSession(session('stale'))
    server.use(
      http.post(REFRESH, () => new HttpResponse(null, { status, headers: { 'Retry-After': '2' } })),
      ok401(),
    )
    await expect(client.GET('/api/v1/categories')).rejects.toMatchObject({ status })
    expect(useAuthStore.getState().accessToken).toBe('stale')
  })

  it('keeps the session when the refresh cannot reach the server', async () => {
    useAuthStore.getState().setSession(session('stale'))
    server.use(
      http.post(REFRESH, () => HttpResponse.error()),
      ok401(),
    )
    await expect(client.GET('/api/v1/categories')).rejects.toBeInstanceOf(NetworkError)
    expect(useAuthStore.getState().accessToken).toBe('stale')
  })

  it('clears the session when the refresh is forbidden (403)', async () => {
    useAuthStore.getState().setSession(session('stale'))
    server.use(
      http.post(REFRESH, () => new HttpResponse(null, { status: 403 })),
      ok401(),
    )
    await expect(client.GET('/api/v1/categories')).rejects.toMatchObject({ status: 401 })
    expect(useAuthStore.getState().accessToken).toBeNull()
  })

  it('runs the refresh inside the cafepos-refresh Web Lock', async () => {
    useAuthStore.getState().setSession(session('stale'))
    const names: string[] = []
    let held = false
    Object.defineProperty(navigator, 'locks', {
      configurable: true,
      value: {
        request: async (name: string, cb: () => Promise<unknown>) => {
          names.push(name)
          held = true
          try {
            return await cb()
          } finally {
            held = false
          }
        },
      },
    })
    let heldDuringRefresh = false
    server.use(
      http.post(REFRESH, () => {
        heldDuringRefresh = held
        return HttpResponse.json(session('fresh'))
      }),
      ok401(),
    )
    try {
      await client.GET('/api/v1/categories')
    } finally {
      Reflect.deleteProperty(navigator, 'locks')
    }
    expect(names).toEqual(['cafepos-refresh'])
    expect(heldDuringRefresh).toBe(true)
    expect(useAuthStore.getState().accessToken).toBe('fresh')
  })

  it('falls back to a plain per-tab refresh when navigator.locks is unavailable', async () => {
    expect('locks' in navigator).toBe(false)
    useAuthStore.getState().setSession(session('stale'))
    server.use(
      http.post(REFRESH, () => HttpResponse.json(session('fresh'))),
      ok401(),
    )
    await client.GET('/api/v1/categories')
    expect(useAuthStore.getState().accessToken).toBe('fresh')
  })
})

describe('resolveApiBase', () => {
  it('accepts a base ending in /api/v1 and derives the origin', () => {
    expect(resolveApiBase('http://localhost:5080/api/v1/')).toEqual({
      base: 'http://localhost:5080/api/v1',
      origin: 'http://localhost:5080',
    })
  })
  it('keeps a gateway prefix in the origin used for asset URLs', () => {
    expect(resolveApiBase('https://host/pos-api/api/v1').origin).toBe('https://host/pos-api')
  })
  it('defaults when unset', () => {
    expect(resolveApiBase(undefined).base).toBe('http://localhost:5080/api/v1')
  })
  it('throws a clear error when the format is wrong', () => {
    expect(() => resolveApiBase('https://host/pos-api')).toThrow(/VITE_API_BASE_URL must end/)
    expect(() => resolveApiBase('https://host/pos-api')).toThrow(/absolute URL/)
  })
  it('resolves a root-relative /api/v1 against the page origin', () => {
    expect(resolveApiBase('/api/v1', 'https://pos.example.com')).toEqual({
      base: 'https://pos.example.com/api/v1',
      origin: 'https://pos.example.com',
    })
    expect(resolveApiBase('/api/v1/', 'https://pos.example.com').base).toBe('https://pos.example.com/api/v1')
  })
  it('resolves a root-relative prefixed path and keeps the prefix in the origin', () => {
    expect(resolveApiBase('/prefix/api/v1', 'https://pos.example.com')).toEqual({
      base: 'https://pos.example.com/prefix/api/v1',
      origin: 'https://pos.example.com/prefix',
    })
  })
  it('defaults the page origin to window.location.origin', () => {
    expect(resolveApiBase('/api/v1').origin).toBe(window.location.origin)
  })
  it.each([
    '//host/api/v1',
    'api/v1',
    'prefix/api/v1',
    '/api/v2',
    '/api/v1/extra',
    '/',
    'ftp://host/api/v1',
    '/a//api/v1',
  ])('rejects %s', (bad) => {
    expect(() => resolveApiBase(bad, 'https://pos.example.com')).toThrow(/VITE_API_BASE_URL must/)
  })
})

describe('assetUrl in relative mode', () => {
  it('builds a same-origin URL from a root-relative base', () => {
    const { origin } = resolveApiBase('/api/v1')
    expect(new URL(`${origin}/uploads/logo.png`).origin).toBe(window.location.origin)
  })
  it('uses the configured origin for asset paths', () => {
    expect(assetUrl('/uploads/logo.png')).toBe(`${API_ORIGIN}/uploads/logo.png`)
    expect(assetUrl(null)).toBeNull()
  })
})
