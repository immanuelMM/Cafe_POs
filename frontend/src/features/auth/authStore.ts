import { create } from 'zustand'
import type { AuthResponse, User } from '../../lib/types'

/** Access token lives in memory only; the refresh cookie is HttpOnly. */
interface AuthState {
  accessToken: string | null
  user: User | null
  /** true once the initial refresh-on-load attempt finished */
  ready: boolean
  setSession: (r: AuthResponse) => void
  clear: () => void
  setReady: () => void
}

export const useAuthStore = create<AuthState>((set) => ({
  accessToken: null,
  user: null,
  ready: false,
  setSession: (r) => set({ accessToken: r.accessToken, user: r.user }),
  clear: () => set({ accessToken: null, user: null }),
  setReady: () => set({ ready: true }),
}))

export const isManager = (u: User | null) => !!u && (u.isOwner || u.role === 'Manager' || u.role === 'Admin')
export const isAdmin = (u: User | null) => !!u && (u.isOwner || u.role === 'Admin')
export const isOwner = (u: User | null) => !!u?.isOwner
