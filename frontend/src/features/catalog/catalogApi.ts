import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'
import type { Category, Item, LocationOverride, ModifierList, Paged, Recipe, Variation } from '../../lib/types'

export const useCategories = (includeInactive = false) =>
  useQuery({
    queryKey: ['categories', { includeInactive }],
    queryFn: () => api<Category[]>('/categories', { query: { includeInactive } }),
  })

export const useItems = (params: { categoryId?: string; search?: string; page?: number; pageSize?: number; includeInactive?: boolean }) =>
  useQuery({
    queryKey: ['items', params],
    queryFn: () => api<Paged<Item>>('/items', { query: { pageSize: 100, ...params } }),
  })

export const useModifierLists = (includeInactive = false) =>
  useQuery({
    queryKey: ['modifier-lists', { includeInactive }],
    queryFn: () => api<ModifierList[]>('/modifier-lists', { query: { includeInactive } }),
  })

export const useRecipe = (variationId: string | undefined) =>
  useQuery({
    queryKey: ['recipe', variationId],
    queryFn: () => api<Recipe>(`/variations/${variationId}/recipe`),
    enabled: !!variationId,
  })

/** Generic mutation that invalidates catalog caches on success. */
export function useCatalogMutation<TVars, TRes = unknown>(fn: (v: TVars) => Promise<TRes>, extraKeys: string[] = []) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSuccess: () => {
      for (const k of ['categories', 'items', 'modifier-lists', ...extraKeys]) void qc.invalidateQueries({ queryKey: [k] })
    },
  })
}

export interface OverrideInput {
  priceOverride: number | null
  trackInventoryOverride: boolean | null
  lowStockThresholdOverride: number | null
}

export const useLocationOverrides = (variationId: string) =>
  useQuery({
    queryKey: ['location-overrides', variationId],
    queryFn: () => api<LocationOverride[]>(`/variations/${variationId}/location-overrides`),
  })

/** PUT with all three fields null removes the override (204, no body). */
export const saveOverride = (v: { variationId: string; locationId: string; body: OverrideInput }) =>
  api<LocationOverride | undefined>(`/variations/${v.variationId}/location-overrides/${v.locationId}`, { method: 'PUT', body: v.body })

export interface CategoryInput { name: string; sortOrder: number; active: boolean }
export interface VariationInput { name: string; sku: string; price: number; active?: boolean }
export interface ItemInput {
  categoryId: string
  name: string
  description: string | null
  sortOrder: number
  active: boolean
  modifierListIds: string[]
  variations?: VariationInput[]
}
export interface ModifierListInput {
  name: string
  minSelections: number
  maxSelections: number
  active: boolean
  modifiers: { id?: string; name: string; priceDelta: number; active?: boolean }[]
}

export const catalogCalls = {
  saveCategory: (v: { id?: string; body: CategoryInput }) =>
    v.id
      ? api<Category>(`/categories/${v.id}`, { method: 'PUT', body: v.body })
      : api<Category>('/categories', { method: 'POST', body: v.body }),
  deleteCategory: (id: string) => api<void>(`/categories/${id}`, { method: 'DELETE' }),
  saveItem: (v: { id?: string; body: ItemInput }) =>
    v.id ? api<Item>(`/items/${v.id}`, { method: 'PUT', body: v.body }) : api<Item>('/items', { method: 'POST', body: v.body }),
  deleteItem: (id: string) => api<void>(`/items/${id}`, { method: 'DELETE' }),
  saveVariation: (v: { id?: string; itemId: string; body: VariationInput }) =>
    v.id
      ? api<Variation>(`/variations/${v.id}`, { method: 'PUT', body: v.body })
      : api<Variation>(`/items/${v.itemId}/variations`, { method: 'POST', body: v.body }),
  deleteVariation: (id: string) => api<void>(`/variations/${id}`, { method: 'DELETE' }),
  saveModifierList: (v: { id?: string; body: ModifierListInput }) =>
    v.id
      ? api<ModifierList>(`/modifier-lists/${v.id}`, { method: 'PUT', body: v.body })
      : api<ModifierList>('/modifier-lists', { method: 'POST', body: v.body }),
  saveRecipe: (v: { variationId: string; lines: { ingredientId: string; quantity: number }[] }) =>
    api<Recipe>(`/variations/${v.variationId}/recipe`, { method: 'PUT', body: { lines: v.lines } }),
}
