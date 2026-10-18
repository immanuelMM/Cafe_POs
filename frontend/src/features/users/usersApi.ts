import { useQuery } from '@tanstack/react-query'
import { api } from '../../lib/api'
import type { AssignableRole, Paged, UserDto } from '../../lib/types'

export const ROLES: AssignableRole[] = ['Cashier', 'Manager', 'Admin']

export const useUsers = (params: { search: string; locationId: string; page: number }) =>
  useQuery({
    queryKey: ['users', params],
    queryFn: () => api<Paged<UserDto>>('/users', { query: { ...params, pageSize: 15 } }),
    placeholderData: (prev) => prev,
  })

export const useUser = (id: string) => useQuery({ queryKey: ['user', id], queryFn: () => api<UserDto>(`/users/${id}`) })

export interface NewUserInput {
  email: string
  displayName: string
  password: string
  isOwner?: boolean
  assignments: { locationId: string; role: AssignableRole }[]
}

export const userCalls = {
  create: (body: NewUserInput) => api<UserDto>('/users', { method: 'POST', body }),
  update: (v: { id: string; body: { displayName: string; active?: boolean; isOwner?: boolean } }) =>
    api<UserDto>(`/users/${v.id}`, { method: 'PUT', body: v.body }),
  deactivate: (id: string) => api<void>(`/users/${id}`, { method: 'DELETE' }),
  assign: (v: { id: string; locationId: string; role: AssignableRole }) =>
    api<UserDto>(`/users/${v.id}/locations`, { method: 'POST', body: { locationId: v.locationId, role: v.role } }),
  unassign: (v: { id: string; locationId: string }) => api<void>(`/users/${v.id}/locations/${v.locationId}`, { method: 'DELETE' }),
}
