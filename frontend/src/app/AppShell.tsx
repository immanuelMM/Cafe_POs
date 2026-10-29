import { Suspense, useEffect, useRef, useState, type ReactNode } from 'react'
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router'
import { useQueryClient } from '@tanstack/react-query'
import { isAdmin, isManager, useAuthStore } from '../features/auth/authStore'
import { logout } from '../features/auth/authApi'
import { useBrandName } from '../features/branding/useBranding'
import { BrandLogo } from '../features/branding/BrandLogo'
import { LocationSwitcher } from './LocationSwitcher'
import { useFullscreen } from './useFullscreen'
import { Modal } from '../components/Modal'
import { Spinner } from '../components/ui'
import {
  IconBook,
  IconCart,
  IconClock,
  IconImage,
  IconLogOut,
  IconMapPin,
  IconMaximize,
  IconMore,
  IconPackage,
  IconReceipt,
  IconSettings,
  IconUser,
  IconUsers,
} from '../components/icons'

interface NavItem {
  to: string
  label: string
  icon: ReactNode
}

const PRIMARY: NavItem[] = [
  { to: '/pos', label: 'POS', icon: <IconCart /> },
  { to: '/orders', label: 'Orders', icon: <IconReceipt /> },
  { to: '/timeclock', label: 'Time clock', icon: <IconClock /> },
]

const TITLES: [string, string][] = [
  ['/pos', 'POS'],
  ['/orders', 'Orders'],
  ['/receipt', 'Receipt'],
  ['/timeclock', 'Time clock'],
  ['/menu-banner', 'Menu banner'],
  ['/admin/catalog', 'Catalog'],
  ['/admin/inventory', 'Inventory'],
  ['/admin/users', 'Staff'],
  ['/admin/locations', 'Locations'],
  ['/admin/settings', 'Settings'],
  ['/admin/design-studio', 'Design studio'],
]

function RailLink({ item }: { item: NavItem }) {
  return (
    <NavLink to={item.to} className="rail-link">
      {item.icon}
      <span className="rail-label">{item.label}</span>
    </NavLink>
  )
}

/** Account popover: who is signed in, the location switcher and the full-screen toggle. */
function AccountPopover({ onClose }: { onClose: () => void }) {
  const user = useAuthStore((s) => s.user)
  const fs = useFullscreen()
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const onDown = (e: MouseEvent) => {
      const t = e.target as Element | null
      if (ref.current && !ref.current.contains(t) && !t?.closest('[data-account-trigger]')) onClose()
    }
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    document.addEventListener('mousedown', onDown)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onDown)
      document.removeEventListener('keydown', onKey)
    }
  }, [onClose])

  return (
    <div className="account-pop" role="dialog" aria-label="Account" ref={ref}>
      <p className="account-who">
        <strong>{user?.displayName}</strong>
        <small>{user?.role}</small>
      </p>
      <LocationSwitcher />
      {fs.available && (
        <button type="button" className="btn btn-sm btn-block" onClick={fs.toggle}>
          <IconMaximize size={20} />
          {fs.on ? 'Exit full screen' : 'Full screen'}
        </button>
      )}
    </div>
  )
}

/**
 * One DOM for every size. CSS turns `.rail` into a left rail (tablets) or a bottom tab bar plus top app bar (phones).
 * The manage links stay in the rail on tablets; on phones they are hidden there and offered in the More sheet,
 * which only exists in the DOM while it is open.
 */
export function AppShell() {
  const user = useAuthStore((s) => s.user)
  const navigate = useNavigate()
  const { pathname } = useLocation()
  const qc = useQueryClient()
  const manager = isManager(user)
  const admin = isAdmin(user)
  const brandName = useBrandName()
  const [moreOpen, setMoreOpen] = useState(false)
  const [accountOpen, setAccountOpen] = useState(false)

  const manage: NavItem[] = [
    ...(manager
      ? [
          { to: '/menu-banner', label: 'Menu banner', icon: <IconImage /> },
          { to: '/admin/catalog', label: 'Catalog', icon: <IconBook /> },
          { to: '/admin/inventory', label: 'Inventory', icon: <IconPackage /> },
        ]
      : []),
    ...(admin ? [{ to: '/admin/users', label: 'Staff', icon: <IconUsers /> }] : []),
    ...(user?.isOwner ? [{ to: '/admin/locations', label: 'Locations', icon: <IconMapPin /> }] : []),
    ...(admin ? [{ to: '/admin/settings', label: 'Settings', icon: <IconSettings /> }] : []),
  ]
  const title = TITLES.find(([p]) => pathname.startsWith(p))?.[1] ?? brandName

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
      <aside className="rail">
        <div className="rail-brand">
          <BrandLogo />
        </div>
        <nav className="rail-nav" aria-label="Main">
          {PRIMARY.map((i) => (
            <RailLink key={i.to} item={i} />
          ))}
          {manage.length > 0 && (
            <div className="rail-manage">
              {manage.map((i) => (
                <RailLink key={i.to} item={i} />
              ))}
            </div>
          )}
          {manage.length > 0 && (
            <button
              type="button"
              className="rail-link rail-more"
              aria-haspopup="dialog"
              onClick={() => setMoreOpen(true)}
            >
              <IconMore />
              <span className="rail-label">More</span>
            </button>
          )}
        </nav>
        <div className="rail-foot">
          <button
            type="button"
            className="rail-link"
            data-account-trigger
            aria-haspopup="dialog"
            aria-expanded={accountOpen}
            onClick={() => setAccountOpen((o) => !o)}
          >
            <IconUser />
            <span className="rail-label">Account</span>
          </button>
          <button type="button" className="rail-link" onClick={onLogout}>
            <IconLogOut />
            <span className="rail-label">Log out</span>
          </button>
        </div>
      </aside>
      <header className="appbar">
        <div className="brand">
          <BrandLogo />
          <span className="brand-name">{brandName}</span>
        </div>
        <span className="appbar-title">{title}</span>
      </header>
      <main className="main">
        <Suspense fallback={<Spinner label="Loading…" />}>
          <Outlet />
        </Suspense>
      </main>
      {accountOpen && <AccountPopover onClose={() => setAccountOpen(false)} />}
      {moreOpen && (
        <Modal title="More" onClose={() => setMoreOpen(false)}>
          <nav className="more-links" aria-label="More">
            {manage.map((i) => (
              <NavLink key={i.to} to={i.to} className="more-link" onClick={() => setMoreOpen(false)}>
                {i.icon}
                {i.label}
              </NavLink>
            ))}
          </nav>
        </Modal>
      )}
    </div>
  )
}
