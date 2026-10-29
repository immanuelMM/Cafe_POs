import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'
import type { PunchResult, TimeClockStaff, TimeEntry } from '../../lib/types'
import { useAuthStore } from '../auth/authStore'

/** Staff at the active location (400 without one, so callers must check `user.location`). */
export const useTimeClockStaff = () => {
  const locationId = useAuthStore((s) => s.user?.location?.id)
  return useQuery({
    queryKey: ['timeclock-staff', locationId],
    queryFn: () => api<TimeClockStaff[]>('/timeclock/staff'),
    enabled: !!locationId,
  })
}

export function usePunch() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (v: { userId: string; pin: string }) =>
      api<PunchResult>('/timeclock/punch', { method: 'POST', body: v }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['timeclock-staff'] })
      void qc.invalidateQueries({ queryKey: ['timeclock-entries'] })
    },
  })
}

export const useSetMyPin = () =>
  useMutation({ mutationFn: (pin: string) => api<void>('/timeclock/my-pin', { method: 'PUT', body: { pin } }) })

export const useTimeEntries = (p: { from: string; to: string; userId: string }) => {
  const locationId = useAuthStore((s) => s.user?.location?.id)
  return useQuery({
    queryKey: ['timeclock-entries', locationId, p],
    queryFn: () => api<TimeEntry[]>('/timeclock/entries', { query: { from: p.from, to: p.to, userId: p.userId } }),
    enabled: !!locationId,
  })
}
