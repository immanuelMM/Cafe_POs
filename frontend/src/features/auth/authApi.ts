import { api, refreshSession } from '../../lib/api'
import type { AuthResponse } from '../../lib/types'
import { useAuthStore } from './authStore'

export async function login(email: string, password: string, locationId?: string | null) {
  const r = await api<AuthResponse>('/auth/login', {
    method: 'POST',
    body: { email, password, locationId: locationId ?? null },
    anonymous: true,
  })
  useAuthStore.getState().setSession(r)
  return r
}

export async function logout() {
  try {
    await api<void>('/auth/logout', { method: 'POST', anonymous: true })
  } finally {
    useAuthStore.getState().clear()
  }
}

/** Called once on app load: restore the session from the refresh cookie. */
export async function bootstrapSession() {
  await refreshSession()
  useAuthStore.getState().setReady()
}
