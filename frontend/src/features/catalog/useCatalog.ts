import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { components } from '../../api/schema'
import { client, fileBody, unwrap } from '../../lib/apiClient'

type S = components['schemas']

export const useCategories = (includeInactive = false) =>
  useQuery({
    queryKey: ['categories', { includeInactive }],
    queryFn: () => unwrap(client.GET('/api/v1/categories', { params: { query: { includeInactive } } })),
  })

export const useItems = (params: {
  categoryId?: string
  search?: string
  page?: number
  pageSize?: number
  includeInactive?: boolean
}) =>
  useQuery({
    queryKey: ['items', params],
    queryFn: () => unwrap(client.GET('/api/v1/items', { params: { query: { pageSize: 100, ...params } } })),
  })

/** The endpoint takes no `includeInactive` parameter (the old client sent one that the server ignored); it stays in the cache key only. */
export const useModifierLists = (includeInactive = false) =>
  useQuery({
    queryKey: ['modifier-lists', { includeInactive }],
    queryFn: () => unwrap(client.GET('/api/v1/modifier-lists')),
  })

export const useRecipe = (variationId: string | undefined) =>
  useQuery({
    queryKey: ['recipe', variationId],
    queryFn: () => unwrap(client.GET('/api/v1/variations/{id}/recipe', { params: { path: { id: variationId! } } })),
    enabled: !!variationId,
  })

/** Generic mutation that invalidates catalog caches on success. */
export function useCatalogMutation<TVars, TRes = unknown>(fn: (v: TVars) => Promise<TRes>, extraKeys: string[] = []) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSuccess: () => {
      for (const k of ['categories', 'items', 'modifier-lists', ...extraKeys])
        void qc.invalidateQueries({ queryKey: [k] })
    },
  })
}

export type OverrideInput = S['UpsertLocationOverrideRequest']

export const useLocationOverrides = (variationId: string) =>
  useQuery({
    queryKey: ['location-overrides', variationId],
    queryFn: () =>
      unwrap(client.GET('/api/v1/variations/{variationId}/location-overrides', { params: { path: { variationId } } })),
  })

/** PUT with all three fields null removes the override (204, no body). */
export const saveOverride = (v: { variationId: string; locationId: string; body: OverrideInput }) =>
  unwrap(
    client.PUT('/api/v1/variations/{variationId}/location-overrides/{locationId}', {
      params: { path: { variationId: v.variationId, locationId: v.locationId } },
      body: v.body,
    }),
  )

export type CategoryInput = S['UpsertCategoryRequest']
export type VariationInput = S['UpsertVariationRequest']
export type ItemInput = S['UpsertItemRequest']
export type ModifierListInput = S['UpsertModifierListRequest']

export const catalogCalls = {
  saveCategory: (v: { id?: string; body: CategoryInput }) =>
    v.id
      ? unwrap(client.PUT('/api/v1/categories/{id}', { params: { path: { id: v.id } }, body: v.body }))
      : unwrap(client.POST('/api/v1/categories', { body: v.body })),
  deleteCategory: (id: string) => unwrap(client.DELETE('/api/v1/categories/{id}', { params: { path: { id } } })),
  saveItem: (v: { id?: string; body: ItemInput }) =>
    v.id
      ? unwrap(client.PUT('/api/v1/items/{id}', { params: { path: { id: v.id } }, body: v.body }))
      : unwrap(client.POST('/api/v1/items', { body: v.body })),
  uploadItemImage: (v: { id: string; file: Blob }) =>
    unwrap(
      client.PUT('/api/v1/items/{id}/image', { params: { path: { id: v.id } }, ...fileBody(v.file, 'photo.jpg') }),
    ),
  deleteItemImage: (id: string) => unwrap(client.DELETE('/api/v1/items/{id}/image', { params: { path: { id } } })),
  deleteItem: (id: string) => unwrap(client.DELETE('/api/v1/items/{id}', { params: { path: { id } } })),
  saveVariation: (v: { id?: string; itemId: string; body: VariationInput }) =>
    v.id
      ? unwrap(client.PUT('/api/v1/variations/{id}', { params: { path: { id: v.id } }, body: v.body }))
      : unwrap(client.POST('/api/v1/items/{id}/variations', { params: { path: { id: v.itemId } }, body: v.body })),
  deleteVariation: (id: string) => unwrap(client.DELETE('/api/v1/variations/{id}', { params: { path: { id } } })),
  saveModifierList: (v: { id?: string; body: ModifierListInput }) =>
    v.id
      ? unwrap(client.PUT('/api/v1/modifier-lists/{id}', { params: { path: { id: v.id } }, body: v.body }))
      : unwrap(client.POST('/api/v1/modifier-lists', { body: v.body })),
  saveRecipe: (v: { variationId: string; lines: { ingredientId: string; quantity: number }[] }) =>
    unwrap(
      client.PUT('/api/v1/variations/{id}/recipe', {
        params: { path: { id: v.variationId } },
        body: { lines: v.lines },
      }),
    ),
}
