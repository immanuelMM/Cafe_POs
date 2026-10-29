import { Route, Routes } from 'react-router'
import { screen } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { renderWithProviders } from '../../test/render'
import { resetStores, signIn } from '../../test/fixtures'
import { useAuthStore } from './authStore'
import { ProtectedRoute } from './ProtectedRoute'

const tree = (
  <Routes>
    <Route path="/login" element={<p>login page</p>} />
    <Route element={<ProtectedRoute roles={['Manager', 'Admin']} />}>
      <Route path="/manage" element={<p>manage page</p>} />
    </Route>
    <Route element={<ProtectedRoute ownerOnly />}>
      <Route path="/locations" element={<p>locations page</p>} />
    </Route>
  </Routes>
)

describe('ProtectedRoute role and owner guards', () => {
  beforeEach(resetStores)

  it('shows a spinner (not a redirect) until the session bootstrap finished', () => {
    useAuthStore.setState({ ready: false })
    renderWithProviders(tree, { route: '/manage' })
    expect(screen.getByText('Restoring session…')).toBeInTheDocument()
    expect(screen.queryByText('login page')).not.toBeInTheDocument()
  })

  it.each([
    ['Cashier', false],
    ['Manager', true],
    ['Admin', true],
  ])('%s opening a Manager/Admin page: allowed=%s', (role, allowed) => {
    signIn({ role })
    renderWithProviders(tree, { route: '/manage' })
    if (allowed) expect(screen.getByText('manage page')).toBeInTheDocument()
    else expect(screen.getByRole('heading', { name: 'Not allowed' })).toBeInTheDocument()
  })

  it('lets an Owner through role-gated pages regardless of role', () => {
    signIn({ role: 'Owner', isOwner: true })
    renderWithProviders(tree, { route: '/manage' })
    expect(screen.getByText('manage page')).toBeInTheDocument()
  })

  it('owner-only pages reject an Admin and accept an Owner', () => {
    signIn({ role: 'Admin' })
    const { unmount } = renderWithProviders(tree, { route: '/locations' })
    expect(screen.getByRole('heading', { name: 'Not allowed' })).toBeInTheDocument()
    unmount()
    signIn({ role: 'Owner', isOwner: true })
    renderWithProviders(tree, { route: '/locations' })
    expect(screen.getByText('locations page')).toBeInTheDocument()
  })
})
