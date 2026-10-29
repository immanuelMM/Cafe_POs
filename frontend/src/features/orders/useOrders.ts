import { useQuery } from '@tanstack/react-query'
import type { components, paths } from '../../api/schema'
import { client, unwrap } from '../../lib/apiClient'

type S = components['schemas']

export const useOrders = (query: NonNullable<paths['/api/v1/orders']['get']['parameters']['query']>) =>
  useQuery({
    queryKey: ['orders', query],
    queryFn: () => unwrap(client.GET('/api/v1/orders', { params: { query } })),
    placeholderData: (prev) => prev,
  })

export const orderCalls = {
  /** Idempotent: 201 when created, 200 with the existing order when retried with the same id. */
  create: (body: S['CreateOrderRequest']) => unwrap(client.POST('/api/v1/orders', { body })),
  complete: (v: { orderId: string; idempotencyKey: string; body: S['CompleteOrderRequest'] }) =>
    unwrap(
      client.POST('/api/v1/orders/{id}/complete', {
        params: { path: { id: v.orderId }, header: { 'Idempotency-Key': v.idempotencyKey } },
        body: v.body,
      }),
    ),
  cancel: (id: string) => unwrap(client.POST('/api/v1/orders/{id}/cancel', { params: { path: { id } } })),
}
