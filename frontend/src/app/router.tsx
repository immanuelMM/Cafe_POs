import { Suspense, lazy } from 'react'
import { Navigate, Route, Routes } from 'react-router'
import { AppShell } from './AppShell'
import { LoginPage } from '../features/auth/LoginPage'
import { ProtectedRoute } from '../features/auth/ProtectedRoute'
import { PosPage } from '../features/pos/PosPage'
import { Spinner } from '../components/ui'

// POS and login stay eager for first paint; everything else is split per route.
const OrdersPage = lazy(() => import('../features/orders/OrdersPage').then((m) => ({ default: m.OrdersPage })))
const ReceiptPage = lazy(() => import('../features/orders/ReceiptPage').then((m) => ({ default: m.ReceiptPage })))
const TimeClockPage = lazy(() =>
  import('../features/timeclock/TimeClockPage').then((m) => ({ default: m.TimeClockPage })),
)
const TimeRecordsPage = lazy(() =>
  import('../features/timeclock/TimeRecordsPage').then((m) => ({ default: m.TimeRecordsPage })),
)
const MenuBannerPage = lazy(() =>
  import('../features/branding/MenuBannerPage').then((m) => ({ default: m.MenuBannerPage })),
)
const CatalogPage = lazy(() => import('../features/catalog/CatalogPage').then((m) => ({ default: m.CatalogPage })))
const InventoryPage = lazy(() =>
  import('../features/inventory/InventoryPage').then((m) => ({ default: m.InventoryPage })),
)
const SettingsPage = lazy(() => import('../features/settings/SettingsPage').then((m) => ({ default: m.SettingsPage })))
const UsersPage = lazy(() => import('../features/users/UsersPage').then((m) => ({ default: m.UsersPage })))
const LocationsPage = lazy(() =>
  import('../features/locations/LocationsPage').then((m) => ({ default: m.LocationsPage })),
)
// The design studio pulls in konva; load it only when the route is opened.
const DesignStudioPage = lazy(() => import('../features/design-studio/DesignStudioPage'))

export function AppRoutes() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<ProtectedRoute />}>
        <Route element={<AppShell />}>
          <Route path="/pos" element={<PosPage />} />
          <Route path="/orders" element={<OrdersPage />} />
          <Route path="/orders/:id/receipt" element={<ReceiptPage />} />
          <Route path="/timeclock" element={<TimeClockPage />} />
          <Route path="/timeclock/records" element={<TimeRecordsPage />} />
          <Route element={<ProtectedRoute roles={['Manager', 'Admin']} />}>
            <Route path="/menu-banner" element={<MenuBannerPage />} />
            <Route path="/admin/catalog" element={<CatalogPage />} />
            <Route path="/admin/inventory" element={<InventoryPage />} />
          </Route>
          <Route element={<ProtectedRoute roles={['Admin']} />}>
            <Route path="/admin/settings" element={<SettingsPage />} />
            <Route path="/admin/users" element={<UsersPage />} />
            <Route
              path="/admin/design-studio"
              element={
                <Suspense fallback={<Spinner label="Loading the design studio…" />}>
                  <DesignStudioPage />
                </Suspense>
              }
            />
          </Route>
          <Route element={<ProtectedRoute ownerOnly />}>
            <Route path="/admin/locations" element={<LocationsPage />} />
          </Route>
        </Route>
      </Route>
      <Route path="*" element={<Navigate to="/pos" replace />} />
    </Routes>
  )
}
