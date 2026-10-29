// @vitest-environment node
// Node environment: jsdom's FormData/Blob are not understood by Node's fetch (undici), which would corrupt the
// multipart body in the test only; browsers use their own consistent implementations.
import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it } from 'vitest'
import { server } from '../test/server'
import { useAuthStore } from '../features/auth/authStore'
import { API_ORIGIN, client, fileBody } from './apiClient'
import type { AuthResponse } from './types'

const session = (token: string) =>
  ({ accessToken: token, expiresAt: '2099-01-01T00:00:00Z', user: { id: 'u1' } }) as unknown as AuthResponse

describe('multipart uploads through the typed client', () => {
  beforeEach(() => useAuthStore.getState().clear())

  it('resends the file intact after a 401 + refresh', async () => {
    useAuthStore.getState().setSession(session('stale'))
    let refreshCalls = 0
    const seen: { name: string; text: string; type: string; auth: string | null }[] = []
    server.use(
      http.post(`${API_ORIGIN}/api/v1/auth/refresh`, () => {
        refreshCalls++
        return HttpResponse.json(session('fresh'))
      }),
      http.put(`${API_ORIGIN}/api/v1/items/:id/image`, async ({ request }) => {
        if (request.headers.get('authorization') !== 'Bearer fresh') return new HttpResponse(null, { status: 401 })
        const f = (await request.formData()).get('file') as File
        seen.push({
          name: f.name,
          text: await f.text(),
          type: request.headers.get('content-type') ?? '',
          auth: request.headers.get('authorization'),
        })
        return HttpResponse.json({ id: 'i1' })
      }),
    )
    const file = new Blob(['hello-bytes'], { type: 'image/jpeg' })
    await client.PUT('/api/v1/items/{id}/image', {
      params: { path: { id: '00000000-0000-0000-0000-000000000001' } },
      ...fileBody(file, 'photo.jpg'),
    })
    expect(refreshCalls).toBe(1)
    expect(seen).toHaveLength(1)
    expect(seen[0]).toMatchObject({ name: 'photo.jpg', text: 'hello-bytes', auth: 'Bearer fresh' })
    expect(seen[0].type).toMatch(/^multipart\/form-data; boundary=/)
  })
})
