import { useQuery } from '@tanstack/react-query'
import type { components } from '../../api/schema'
import type { AssignableRole } from '../../api/types'
import { client, unwrap } from '../../lib/apiClient'

export const ROLES: AssignableRole[] = ['Cashier', 'Manager', 'Admin']

export const useUsers = (params: { search: string; locationId: string; page: number }) =>
  useQuery({
    queryKey: ['users', params],
    queryFn: () => unwrap(client.GET('/api/v1/users', { params: { query: { ...params, pageSize: 15 } } })),
    placeholderData: (prev) => prev,
  })

export const useUser = (id: string) =>
  useQuery({
    queryKey: ['user', id],
    queryFn: () => unwrap(client.GET('/api/v1/users/{id}', { params: { path: { id } } })),
  })

export type NewUserInput = components['schemas']['CreateUserRequest']

export const userCalls = {
  create: (body: NewUserInput) => unwrap(client.POST('/api/v1/users', { body })),
  update: (v: { id: string; body: { displayName: string; active?: boolean; isOwner?: boolean } }) =>
    unwrap(client.PUT('/api/v1/users/{id}', { params: { path: { id: v.id } }, body: v.body })),
  deactivate: (id: string) => unwrap(client.DELETE('/api/v1/users/{id}', { params: { path: { id } } })),
  assign: (v: { id: string; locationId: string; role: AssignableRole }) =>
    unwrap(
      client.POST('/api/v1/users/{id}/locations', {
        params: { path: { id: v.id } },
        body: { locationId: v.locationId, role: v.role },
      }),
    ),
  addByEmail: (v: { email: string; locationId: string; role: AssignableRole }) =>
    unwrap(
      client.POST('/api/v1/locations/{id}/staff', {
        params: { path: { id: v.locationId } },
        body: { email: v.email, role: v.role },
      }),
    ),
  setPin: (v: { id: string; pin: string }) =>
    unwrap(client.PUT('/api/v1/users/{id}/pin', { params: { path: { id: v.id } }, body: { pin: v.pin } })),
  clearPin: (id: string) => unwrap(client.DELETE('/api/v1/users/{id}/pin', { params: { path: { id } } })),
  unassign: (v: { id: string; locationId: string }) =>
    unwrap(
      client.DELETE('/api/v1/users/{id}/locations/{locationId}', {
        params: { path: { id: v.id, locationId: v.locationId } },
      }),
    ),
}
