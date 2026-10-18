import { useQuery } from '@tanstack/react-query'
import { api } from '../../lib/api'
import type { Receipt } from '../../lib/types'

export const useReceipt = (orderId: string | undefined) =>
  useQuery({
    queryKey: ['receipt', orderId],
    queryFn: () => api<Receipt>(`/orders/${orderId}/receipt`),
    enabled: !!orderId,
    staleTime: Infinity,
  })
