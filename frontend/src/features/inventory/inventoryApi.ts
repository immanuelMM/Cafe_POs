import { useQuery } from '@tanstack/react-query'
import { api } from '../../lib/api'
import type { Ingredient, Paged, StockMovement } from '../../lib/types'

export const useIngredients = () =>
  useQuery({ queryKey: ['ingredients'], queryFn: () => api<Ingredient[]>('/ingredients') })

export const useMovements = (id: string | undefined, page: number) =>
  useQuery({
    queryKey: ['movements', id, page],
    queryFn: () => api<Paged<StockMovement>>(`/ingredients/${id}/movements`, { query: { page, pageSize: 15 } }),
    enabled: !!id,
    placeholderData: (prev) => prev,
  })

export type StockStatus = 'out' | 'low' | 'warn' | 'ok'

/** Colour coding: red at/below threshold (server isLowStock), amber within 25% above it, otherwise green. */
export function stockStatus(i: Ingredient): StockStatus {
  if (i.quantityOnHand <= 0) return 'out'
  if (i.isLowStock) return 'low'
  if (i.quantityOnHand <= i.lowStockThreshold * 1.25) return 'warn'
  return 'ok'
}

export const STATUS_LABEL: Record<StockStatus, string> = { out: 'Out of stock', low: 'Low stock', warn: 'Running low', ok: 'In stock' }

export const calls = {
  saveIngredient: (v: { id?: string; body: { name: string; unit: string; lowStockThreshold: number; active?: boolean; initialQuantity?: number } }) =>
    v.id
      ? api<Ingredient>(`/ingredients/${v.id}`, { method: 'PUT', body: v.body })
      : api<Ingredient>('/ingredients', { method: 'POST', body: v.body }),
  restock: (body: { ingredientId: string; quantity: number; unitCost?: number; supplier?: string; note?: string }) =>
    api<StockMovement>('/restocks', { method: 'POST', body }),
  adjust: (v: { id: string; quantityDelta: number; reason: 'Adjustment' | 'Waste'; note: string }) =>
    api<StockMovement>(`/ingredients/${v.id}/adjustments`, {
      method: 'POST',
      body: { quantityDelta: v.quantityDelta, reason: v.reason, note: v.note },
    }),
}
