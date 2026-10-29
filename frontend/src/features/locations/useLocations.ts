import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { client, unwrap } from '../../lib/apiClient'
import { useAuthStore } from '../auth/authStore'
import { useCartStore } from '../pos/cartStore'

/** All locations visible to the caller (Owner: every location incl. inactive; others: their assigned active ones). */
export const useLocations = (enabled = true) =>
  useQuery({ queryKey: ['locations'], queryFn: () => unwrap(client.GET('/api/v1/locations')), enabled })

/** The active location. Not available in an Owner cross-location session (400), so callers must check `user.location`. */
export const useCurrentLocation = () => {
  const activeId = useAuthStore((s) => s.user?.location?.id)
  return useQuery({
    queryKey: ['location', activeId],
    queryFn: () => unwrap(client.GET('/api/v1/locations/current')),
    enabled: !!activeId,
  })
}

export const useLocationStaff = (id: string | undefined) =>
  useQuery({
    queryKey: ['location-staff', id],
    queryFn: () => unwrap(client.GET('/api/v1/locations/{id}/staff', { params: { path: { id: id! } } })),
    enabled: !!id,
  })

/** Switch the active location (null = Owner "All locations"): replaces the token and resets all cached server data. */
export function useSwitchLocation() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (locationId: string | null) =>
      unwrap(client.POST('/api/v1/auth/switch-location', { body: { locationId } })),
    onSuccess: (r) => {
      useAuthStore.getState().setSession(r)
      // Prices, stock and orders differ per location: drop the in-progress cart and refetch everything.
      useCartStore.getState().clear()
      void qc.resetQueries()
    },
  })
}
