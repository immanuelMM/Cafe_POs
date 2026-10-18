import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError, api } from '../../lib/api'
import type { DesignKind, SavedDesign, StudioDoc } from './types'

/** Server limit for a saved design document, including embedded images. */
export const MAX_DESIGN_BYTES = 8 * 1024 * 1024
export const WARN_DESIGN_BYTES = 6 * 1024 * 1024

export const designKey = (kind: DesignKind) => ['design', kind] as const

/** The saved design for a kind; null when nothing has been saved yet (the API answers 404). */
export const useDesign = (kind: DesignKind) =>
  useQuery({
    queryKey: designKey(kind),
    queryFn: async () => {
      try {
        return await api<SavedDesign>(`/branding/designs/${kind}`)
      } catch (e) {
        if (e instanceof ApiError && e.status === 404) return null
        throw e
      }
    },
    staleTime: 0,
    gcTime: 0,
    refetchOnMount: 'always',
  })

export function useSaveDesign(kind: DesignKind) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (doc: StudioDoc) => api<SavedDesign>(`/branding/designs/${kind}`, { method: 'PUT', body: { document: doc } }),
    onSuccess: (saved) => qc.setQueryData(designKey(kind), saved),
  })
}

/** Serialized size in bytes (what the 8 MB server limit applies to). */
export const docBytes = (doc: StudioDoc) => new Blob([JSON.stringify({ document: doc })]).size

export const formatBytes = (n: number) => (n >= 1024 * 1024 ? `${(n / 1024 / 1024).toFixed(1)} MB` : `${Math.max(1, Math.round(n / 1024))} KB`)
