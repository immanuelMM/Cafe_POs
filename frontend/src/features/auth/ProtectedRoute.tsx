import { Navigate, Outlet, useLocation } from 'react-router'
import { useAuthStore } from './authStore'
import { Spinner } from '../../components/ui'
import type { Role } from '../../lib/types'

/** Requires a session; optionally restricts to specific roles (Owners always pass) or to Owners only. */
export function ProtectedRoute({ roles, ownerOnly }: { roles?: Role[]; ownerOnly?: boolean }) {
  const { user, ready } = useAuthStore()
  const location = useLocation()

  if (!ready) return <Spinner label="Restoring session…" />
  if (!user) return <Navigate to="/login" replace state={{ from: location.pathname }} />
  if ((ownerOnly && !user.isOwner) || (roles && !user.isOwner && !roles.includes(user.role))) {
    return (
      <div className="center-card">
        <h1>Not allowed</h1>
        <p>Your role ({user.role}) cannot open this page.</p>
      </div>
    )
  }
  return <Outlet />
}
