import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { client, unwrap } from '../../lib/apiClient'
import { useAuthStore } from '../auth/authStore'

/** Staff at the active location (400 without one, so callers must check `user.location`). */
export const useTimeClockStaff = () => {
  const locationId = useAuthStore((s) => s.user?.location?.id)
  return useQuery({
    queryKey: ['timeclock-staff', locationId],
    queryFn: () => unwrap(client.GET('/api/v1/timeclock/staff')),
    enabled: !!locationId,
  })
}

export function usePunch() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (v: { userId: string; pin: string }) => unwrap(client.POST('/api/v1/timeclock/punch', { body: v })),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['timeclock-staff'] })
      void qc.invalidateQueries({ queryKey: ['timeclock-entries'] })
    },
  })
}

export const useSetMyPin = () =>
  useMutation({ mutationFn: (pin: string) => unwrap(client.PUT('/api/v1/timeclock/my-pin', { body: { pin } })) })

export const useTimeEntries = (p: { from: string; to: string; userId: string }) => {
  const locationId = useAuthStore((s) => s.user?.location?.id)
  return useQuery({
    queryKey: ['timeclock-entries', locationId, p],
    queryFn: () =>
      unwrap(
        client.GET('/api/v1/timeclock/entries', { params: { query: { from: p.from, to: p.to, userId: p.userId } } }),
      ),
    enabled: !!locationId,
  })
}
