import { useQuery } from '@tanstack/react-query'
import { client, unwrap } from '../../lib/apiClient'

export const useReceipt = (orderId: string | undefined) =>
  useQuery({
    queryKey: ['receipt', orderId],
    queryFn: () => unwrap(client.GET('/api/v1/orders/{id}/receipt', { params: { path: { id: orderId! } } })),
    enabled: !!orderId,
    staleTime: Infinity,
  })
