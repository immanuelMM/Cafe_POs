import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'
import type { AuthResponse, LocationDto, StaffMember } from '../../lib/types'
import { useAuthStore } from '../auth/authStore'
import { useCartStore } from '../pos/cartStore'

/** All locations visible to the caller (Owner: every location incl. inactive; others: their assigned active ones). */
export const useLocations = (enabled = true) =>
  useQuery({ queryKey: ['locations'], queryFn: () => api<LocationDto[]>('/locations'), enabled })

/** The active location. Not available in an Owner cross-location session (400), so callers must check `user.location`. */
export const useCurrentLocation = () => {
  const activeId = useAuthStore((s) => s.user?.location?.id)
  return useQuery({
    queryKey: ['location', activeId],
    queryFn: () => api<LocationDto>('/locations/current'),
    enabled: !!activeId,
  })
}

export const useLocationStaff = (id: string | undefined) =>
  useQuery({
    queryKey: ['location-staff', id],
    queryFn: () => api<StaffMember[]>(`/locations/${id}/staff`),
    enabled: !!id,
  })

/** Switch the active location (null = Owner "All locations"): replaces the token and resets all cached server data. */
export function useSwitchLocation() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (locationId: string | null) =>
      api<AuthResponse>('/auth/switch-location', { method: 'POST', body: { locationId } }),
    onSuccess: (r) => {
      useAuthStore.getState().setSession(r)
      // Prices, stock and orders differ per location: drop the in-progress cart and refetch everything.
      useCartStore.getState().clear()
      void qc.resetQueries()
    },
  })
}
