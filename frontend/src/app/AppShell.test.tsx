import { http, HttpResponse } from 'msw'
import { Route, Routes } from 'react-router'
import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it } from 'vitest'
import { server } from '../test/server'
import { renderWithProviders } from '../test/render'
import { resetStores, signIn, url } from '../test/fixtures'
import { AppShell } from './AppShell'

const tree = (
  <Routes>
    <Route element={<AppShell />}>
      <Route path="/pos" element={<p>pos page</p>} />
      <Route path="/orders/:id/receipt" element={<p>receipt page</p>} />
    </Route>
  </Routes>
)

const MANAGE = ['Menu banner', 'Catalog', 'Inventory', 'Staff', 'Locations', 'Settings']

function renderShell(route = '/pos') {
  server.use(http.get(url('/api/v1/branding'), () => HttpResponse.json({})))
  return renderWithProviders(tree, { route })
}

/** Link names inside the main navigation (one DOM at every size; CSS decides how it is laid out). */
const navLinks = () =>
  within(screen.getByRole('navigation', { name: 'Main' }))
    .getAllByRole('link')
    .map((a) => a.textContent)

describe('AppShell navigation', () => {
  beforeEach(resetStores)

  it.each([
    ['Cashier', {}, ['POS', 'Orders', 'Time clock']],
    ['Manager', { role: 'Manager' }, ['POS', 'Orders', 'Time clock', 'Menu banner', 'Catalog', 'Inventory']],
    [
      'Admin',
      { role: 'Admin' },
      ['POS', 'Orders', 'Time clock', 'Menu banner', 'Catalog', 'Inventory', 'Staff', 'Settings'],
    ],
    ['Owner', { role: 'Owner', isOwner: true }, ['POS', 'Orders', 'Time clock', ...MANAGE]],
  ])('%s sees only the links for their role', (_name, user, expected) => {
    signIn(user)
    renderShell()
    expect(navLinks()).toEqual(expected)
  })

  it('keeps links reachable by role and name, and the page in the main region', () => {
    signIn({ role: 'Manager' })
    renderShell()
    expect(screen.getByRole('link', { name: 'Orders' })).toHaveAttribute('href', '/orders')
    expect(screen.getByRole('link', { name: 'POS' })).toHaveClass('active')
    expect(within(screen.getByRole('main')).getByText('pos page')).toBeInTheDocument()
  })

  it('titles the phone app bar "Receipt" on an order receipt, not "Orders"', () => {
    signIn()
    renderShell('/orders/abc/receipt')
    expect(document.querySelector('.appbar-title')).toHaveTextContent('Receipt')
  })

  it('always shows a Log out button', () => {
    signIn()
    renderShell()
    expect(screen.getByRole('button', { name: 'Log out' })).toBeInTheDocument()
  })

  it('offers the More sheet only to roles with manage links, and renders its links only while open', async () => {
    const user = userEvent.setup()
    signIn()
    const { unmount } = renderShell()
    expect(screen.queryByRole('button', { name: 'More' })).not.toBeInTheDocument()
    unmount()

    signIn({ role: 'Admin' })
    renderShell()
    expect(screen.queryByRole('dialog', { name: 'More' })).not.toBeInTheDocument()
    expect(screen.getAllByRole('link', { name: 'Catalog' })).toHaveLength(1)

    await user.click(screen.getByRole('button', { name: 'More' }))
    const sheet = screen.getByRole('dialog', { name: 'More' })
    expect(within(sheet).getByRole('link', { name: 'Catalog' })).toBeInTheDocument()

    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog', { name: 'More' })).not.toBeInTheDocument()
  })

  it('keeps the location switcher always visible and puts account details in the Account popover', async () => {
    const user = userEvent.setup()
    signIn({ displayName: 'Casey Cashier' })
    renderShell()
    // Location is directly on screen (not inside a popover) ...
    expect(screen.getByText(/Main Branch/)).toBeInTheDocument()
    expect(screen.queryByText('Casey Cashier')).not.toBeInTheDocument()

    // ... while name, role and Full screen live in the popover.
    await user.click(screen.getByRole('button', { name: 'Account' }))
    const pop = screen.getByRole('dialog', { name: 'Account' })
    expect(within(pop).getByText('Casey Cashier')).toBeInTheDocument()
    expect(within(pop).queryByText(/Main Branch/)).not.toBeInTheDocument()
  })

  it('shows the Active location combobox for multi-location staff without opening anything', () => {
    signIn({
      role: 'Manager',
      locations: [
        { locationId: 'loc-main', locationName: 'Main Branch', role: 'Manager' },
        { locationId: 'loc-up', locationName: 'Uptown Branch', role: 'Manager' },
      ],
    })
    renderShell()
    expect(screen.getByRole('combobox', { name: 'Active location' })).toBeInTheDocument()
  })
})
