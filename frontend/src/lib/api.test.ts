import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it } from 'vitest'
import { server } from '../test/server'
import { useAuthStore } from '../features/auth/authStore'
import { ApiError, API_BASE, api } from './api'
import type { AuthResponse } from '../api/types'

const session = (token: string) =>
  ({ accessToken: token, expiresAt: '2099-01-01T00:00:00Z', user: { id: 'u1' } }) as unknown as AuthResponse

describe('api client', () => {
  beforeEach(() => useAuthStore.getState().clear())

  it('sends the in-memory bearer token', async () => {
    useAuthStore.getState().setSession(session('tok-1'))
    server.use(
      http.get(`${API_BASE}/ping`, ({ request }) => HttpResponse.json({ auth: request.headers.get('authorization') })),
    )
    expect(await api<{ auth: string }>('/ping')).toEqual({ auth: 'Bearer tok-1' })
  })

  it('refreshes once on concurrent 401s (single-flight) and retries', async () => {
    useAuthStore.getState().setSession(session('stale'))
    let refreshCalls = 0
    server.use(
      http.post(`${API_BASE}/auth/refresh`, () => {
        refreshCalls++
        return HttpResponse.json(session('fresh'))
      }),
      http.get(`${API_BASE}/data`, ({ request }) =>
        request.headers.get('authorization') === 'Bearer fresh'
          ? HttpResponse.json({ ok: true })
          : new HttpResponse(null, { status: 401 }),
      ),
    )
    const results = await Promise.all([api('/data'), api('/data'), api('/data')])
    expect(results).toEqual([{ ok: true }, { ok: true }, { ok: true }])
    expect(refreshCalls).toBe(1)
  })

  it('throws ApiError with ProblemDetails fields', async () => {
    server.use(
      http.get(`${API_BASE}/bad`, () => HttpResponse.json({ title: 'Nope', detail: 'Bad thing' }, { status: 400 })),
    )
    await expect(api('/bad')).rejects.toMatchObject({ status: 400, message: 'Bad thing' })
    await expect(api('/bad')).rejects.toBeInstanceOf(ApiError)
  })
})
