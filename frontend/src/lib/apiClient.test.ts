import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it } from 'vitest'
import { server } from '../test/server'
import { useAuthStore } from '../features/auth/authStore'
import { anonClient, API_ORIGIN, client, unwrap } from './apiClient'
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
})
