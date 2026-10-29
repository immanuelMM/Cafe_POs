import { Suspense } from 'react'
import { NavLink, Outlet, useNavigate } from 'react-router'
import { useQueryClient } from '@tanstack/react-query'
import { isAdmin, isManager, useAuthStore } from '../features/auth/authStore'
import { logout } from '../features/auth/authApi'
import { useBrandName } from '../features/branding/useBranding'
import { BrandLogo } from '../features/branding/BrandLogo'
import { LocationSwitcher } from './LocationSwitcher'
import { Spinner } from '../components/ui'

export function AppShell() {
  const user = useAuthStore((s) => s.user)
  const navigate = useNavigate()
  const qc = useQueryClient()
  const manager = isManager(user)
  const admin = isAdmin(user)
  const brandName = useBrandName()

  const onLogout = async () => {
    try {
      await logout()
    } finally {
      qc.clear()
      navigate('/login', { replace: true })
    }
  }

  return (
    <div className="shell">
      <header className="topbar">
        <div className="brand">
          <BrandLogo /> <span className="brand-name">{brandName}</span>
        </div>
        <nav className="nav" aria-label="Main">
          <NavLink to="/pos">POS</NavLink>
          <NavLink to="/orders">Orders</NavLink>
          <NavLink to="/timeclock">Time clock</NavLink>
          {manager && <NavLink to="/menu-banner">Menu banner</NavLink>}
          {manager && <NavLink to="/admin/catalog">Catalog</NavLink>}
          {manager && <NavLink to="/admin/inventory">Inventory</NavLink>}
          {admin && <NavLink to="/admin/users">Staff</NavLink>}
          {user?.isOwner && <NavLink to="/admin/locations">Locations</NavLink>}
          {admin && <NavLink to="/admin/settings">Settings</NavLink>}
        </nav>
        <div className="userbox">
          <LocationSwitcher />
          <span className="user-meta">
            <strong>{user?.displayName}</strong>
            <small>{user?.role}</small>
          </span>
          <button type="button" className="btn btn-sm" onClick={onLogout}>
            Log out
          </button>
        </div>
      </header>
      <main className="main">
        <Suspense fallback={<Spinner label="Loading…" />}>
          <Outlet />
        </Suspense>
      </main>
    </div>
  )
}
