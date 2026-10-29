import { useEffect } from 'react'
import { bootstrapSession } from '../features/auth/authApi'
import { useApplyBranding } from '../features/branding/brandingApi'
import { AppRoutes } from './router'

export function App() {
  useApplyBranding()
  useEffect(() => {
    void bootstrapSession()
  }, [])

  return <AppRoutes />
}
