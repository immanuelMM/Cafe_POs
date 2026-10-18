import { Suspense, lazy, useEffect } from 'react'
import { Navigate, Route, Routes } from 'react-router'
import { AppShell } from './components/AppShell'
import { LoginPage } from './features/auth/LoginPage'
import { ProtectedRoute } from './features/auth/ProtectedRoute'
import { bootstrapSession } from './features/auth/authApi'
import { PosPage } from './features/pos/PosPage'
import { OrdersPage } from './features/orders/OrdersPage'
import { ReceiptPage } from './features/orders/ReceiptPage'
import { CatalogPage } from './features/catalog/CatalogPage'
import { InventoryPage } from './features/inventory/InventoryPage'
import { SettingsPage } from './features/settings/SettingsPage'
import { LocationsPage } from './features/locations/LocationsPage'
import { UsersPage } from './features/users/UsersPage'
import { useApplyBranding } from './features/branding/brandingApi'
import { MenuBannerPage } from './features/branding/MenuBannerPage'
import { Spinner } from './components/ui'
import { TimeClockPage } from './features/timeclock/TimeClockPage'
import { TimeRecordsPage } from './features/timeclock/TimeRecordsPage'

// The design studio pulls in konva; load it only when the route is opened.
const DesignStudioPage = lazy(() => import('./features/design-studio/DesignStudioPage'))

export function App() {
  useApplyBranding()
  useEffect(() => {
    void bootstrapSession()
  }, [])

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
