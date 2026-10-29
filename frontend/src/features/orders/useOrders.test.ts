import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { server } from '../../test/server'
import { API_ORIGIN } from '../../lib/apiClient'
import { orderCalls } from './useOrders'

describe('orderCalls', () => {
  it('completes an order with the Idempotency-Key header and the cash payment body', async () => {
    let seen: { key: string | null; body: unknown } | null = null
    server.use(
      http.post(`${API_ORIGIN}/api/v1/orders/:id/complete`, async ({ request }) => {
        seen = { key: request.headers.get('idempotency-key'), body: await request.json() }
        return HttpResponse.json({ id: 'o1' })
      }),
    )
    await orderCalls.complete({
      orderId: '00000000-0000-0000-0000-000000000001',
      idempotencyKey: 'key-123',
      body: { paymentMethod: 'cash', amountTendered: 200 },
    })
    expect(seen).toEqual({ key: 'key-123', body: { paymentMethod: 'cash', amountTendered: 200 } })
  })
})
