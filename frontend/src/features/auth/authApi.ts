import { anonClient, refreshSession, unwrap } from '../../lib/apiClient'
import { useAuthStore } from './authStore'

export async function login(email: string, password: string, locationId?: string | null) {
  const r = await unwrap(
    anonClient.POST('/api/v1/auth/login', { body: { email, password, locationId: locationId ?? null } }),
  )
  useAuthStore.getState().setSession(r)
  return r
}

export async function logout() {
  try {
    await anonClient.POST('/api/v1/auth/logout')
  } finally {
    useAuthStore.getState().clear()
  }
}

/** Called once on app load: restore the session from the refresh cookie. */
export async function bootstrapSession() {
  await refreshSession()
  useAuthStore.getState().setReady()
}
