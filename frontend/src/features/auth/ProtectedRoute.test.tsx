import { Route, Routes } from 'react-router'
import { screen } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { renderWithProviders } from '../../test/render'
import { useAuthStore } from './authStore'
import { ProtectedRoute } from './ProtectedRoute'
import type { User } from '../../api/types'

const tree = (
  <Routes>
    <Route path="/login" element={<p>login page</p>} />
    <Route element={<ProtectedRoute roles={['Admin']} />}>
      <Route path="/admin" element={<p>admin page</p>} />
    </Route>
  </Routes>
)
const user = (role: string, isOwner = false) => ({ id: 'u', role, isOwner }) as unknown as User

describe('ProtectedRoute', () => {
  beforeEach(() => {
    useAuthStore.setState({ user: null, accessToken: null, ready: true })
  })

  it('redirects anonymous users to /login', () => {
    renderWithProviders(tree, { route: '/admin' })
    expect(screen.getByText('login page')).toBeInTheDocument()
  })

  it('blocks a role that is not allowed', () => {
    useAuthStore.setState({ user: user('Cashier') })
    renderWithProviders(tree, { route: '/admin' })
    expect(screen.getByRole('heading', { name: 'Not allowed' })).toBeInTheDocument()
  })

  it('lets an allowed role through', () => {
    useAuthStore.setState({ user: user('Admin') })
    renderWithProviders(tree, { route: '/admin' })
    expect(screen.getByText('admin page')).toBeInTheDocument()
  })
})
