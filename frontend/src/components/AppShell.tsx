import { NavLink, Outlet, useNavigate } from 'react-router'
import { useQueryClient } from '@tanstack/react-query'
import { isAdmin, isManager, useAuthStore } from '../features/auth/authStore'
import { logout } from '../features/auth/authApi'
import { LocationSwitcher } from './LocationSwitcher'

export function AppShell() {
  const user = useAuthStore((s) => s.user)
  const navigate = useNavigate()
  const qc = useQueryClient()
  const manager = isManager(user)
  const admin = isAdmin(user)

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
          <span aria-hidden="true">☕</span> <span className="brand-name">Brew &amp; Bean</span>
        </div>
        <nav className="nav" aria-label="Main">
          <NavLink to="/pos">POS</NavLink>
          <NavLink to="/orders">Orders</NavLink>
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
        <Outlet />
      </main>
    </div>
  )
}
