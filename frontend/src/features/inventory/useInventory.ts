import { useQuery } from '@tanstack/react-query'
import type { Ingredient } from '../../api/types'
import type { components } from '../../api/schema'
import { client, unwrap } from '../../lib/apiClient'

/** Stock levels for `locationId`, or the active location when omitted. An Owner with no active location must pass one. */
export const useIngredients = (locationId?: string, enabled = true) =>
  useQuery({
    queryKey: ['ingredients', { locationId: locationId ?? null }],
    queryFn: () => unwrap(client.GET('/api/v1/ingredients', { params: { query: { locationId } } })),
    enabled,
  })

export const useMovements = (id: string | undefined, page: number, locationId?: string) =>
  useQuery({
    queryKey: ['movements', id, page, { locationId: locationId ?? null }],
    queryFn: () =>
      unwrap(
        client.GET('/api/v1/ingredients/{id}/movements', {
          params: { path: { id: id! }, query: { locationId, page, pageSize: 15 } },
        }),
      ),
    enabled: !!id,
    placeholderData: (prev) => prev,
  })

export type StockStatus = 'out' | 'low' | 'warn' | 'ok'

/** Colour coding: red at/below threshold (server isLowStock), amber within 25% above it, otherwise green. */
export function stockStatus(i: Ingredient): StockStatus {
  if (i.quantityOnHand === null) return 'ok'
  if (i.quantityOnHand <= 0) return 'out'
  if (i.isLowStock) return 'low'
  if (i.quantityOnHand <= i.lowStockThreshold * 1.25) return 'warn'
  return 'ok'
}

export const STATUS_LABEL: Record<StockStatus, string> = {
  out: 'Out of stock',
  low: 'Low stock',
  warn: 'Running low',
  ok: 'In stock',
}

type S = components['schemas']

export const calls = {
  saveIngredient: (v: {
    id?: string
    body: { name: string; unit: string; lowStockThreshold: number; active?: boolean; initialQuantity?: number }
  }) =>
    v.id
      ? unwrap(
          client.PUT('/api/v1/ingredients/{id}', {
            params: { path: { id: v.id } },
            body: v.body satisfies S['UpdateIngredientRequest'],
          }),
        )
      : unwrap(client.POST('/api/v1/ingredients', { body: v.body satisfies S['CreateIngredientRequest'] })),
  restock: (body: { ingredientId: string; quantity: number; unitCost?: number; supplier?: string; note?: string }) =>
    unwrap(client.POST('/api/v1/restocks', { body })),
  adjust: (v: { id: string; quantityDelta: number; reason: 'Adjustment' | 'Waste'; note: string }) =>
    unwrap(
      client.POST('/api/v1/ingredients/{id}/adjustments', {
        params: { path: { id: v.id } },
        body: { quantityDelta: v.quantityDelta, reason: v.reason, note: v.note },
      }),
    ),
}
