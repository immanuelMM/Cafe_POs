import { http, HttpResponse } from 'msw'
import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it } from 'vitest'
import type { components } from '../../api/schema'
import { server } from '../../test/server'
import { renderWithProviders } from '../../test/render'
import { MAIN, paged, resetStores, signIn, url } from '../../test/fixtures'
import { UsersPage } from './UsersPage'

type S = components['schemas']
const staff: S['UserDto'] = {
  id: 'u2',
  email: 'casey@cafe.local',
  displayName: 'Casey Cashier',
  active: true,
  isOwner: false,
  locations: [{ ...MAIN, role: 'Cashier' }],
}

describe('UsersPage', () => {
  beforeEach(() => {
    resetStores()
    signIn({ id: 'admin-1', role: 'Admin', displayName: 'Ada Admin' })
    server.use(http.get(url('/api/v1/users'), () => HttpResponse.json(paged([staff]))))
  })

  it('lists staff with their location roles and lets an Admin remove them from the location', async () => {
    renderWithProviders(<UsersPage />)
    expect(await screen.findByText('Casey Cashier')).toBeInTheDocument()
    expect(screen.getByText('Main Branch: Cashier')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Remove from location' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Deactivate account' })).not.toBeInTheDocument()
  })

  it('validates the new staff form (required fields, email format, 8 character password)', async () => {
    let posted = false
    server.use(
      http.post(url('/api/v1/users'), () => {
        posted = true
        return HttpResponse.json(staff, { status: 201 })
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(<UsersPage />)
    await screen.findByText('Casey Cashier')
    await user.click(screen.getByRole('button', { name: '+ New staff member' }))
    const dialog = await screen.findByRole('dialog', { name: 'New staff member' })
    await user.click(within(dialog).getByRole('button', { name: 'Create user' }))
    expect(await within(dialog).findByText('Name is required')).toBeInTheDocument()
    expect(within(dialog).getByText('Email is required')).toBeInTheDocument()
    expect(within(dialog).getByText('At least 8 characters')).toBeInTheDocument()

    await user.type(within(dialog).getByLabelText(/^Email/), 'nope')
    await user.type(within(dialog).getByLabelText(/^Temporary password/), 'short')
    await user.click(within(dialog).getByRole('button', { name: 'Create user' }))
    expect(await within(dialog).findByText('Enter a valid email')).toBeInTheDocument()
    expect(posted).toBe(false)
  })

  it("creates a staff member assigned to the Admin's own location with the chosen role", async () => {
    let body: unknown
    server.use(
      http.post(url('/api/v1/users'), async ({ request }) => {
        body = await request.json()
        return HttpResponse.json(staff, { status: 201 })
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(<UsersPage />)
    await screen.findByText('Casey Cashier')
    await user.click(screen.getByRole('button', { name: '+ New staff member' }))
    const dialog = await screen.findByRole('dialog', { name: 'New staff member' })
    await user.type(within(dialog).getByLabelText(/^Display name/), 'New Hire')
    await user.type(within(dialog).getByLabelText(/^Email/), 'hire@cafe.local')
    await user.type(within(dialog).getByLabelText(/^Temporary password/), 'LongEnough#1')
    await user.selectOptions(within(dialog).getByLabelText('Role'), 'Manager')
    await user.click(within(dialog).getByRole('button', { name: 'Create user' }))
    await screen.findByText('Casey Cashier')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(body).toEqual({
      email: 'hire@cafe.local',
      displayName: 'New Hire',
      password: 'LongEnough#1',
      assignments: [{ locationId: 'loc-main', role: 'Manager' }],
    })
  })

  it('shows a server-side validation error (duplicate email) inside the dialog', async () => {
    server.use(
      http.post(url('/api/v1/users'), () =>
        HttpResponse.json({ title: 'Conflict', detail: 'That email is already registered.' }, { status: 409 }),
      ),
    )
    const user = userEvent.setup()
    renderWithProviders(<UsersPage />)
    await screen.findByText('Casey Cashier')
    await user.click(screen.getByRole('button', { name: '+ New staff member' }))
    const dialog = await screen.findByRole('dialog', { name: 'New staff member' })
    await user.type(within(dialog).getByLabelText(/^Display name/), 'Dup')
    await user.type(within(dialog).getByLabelText(/^Email/), 'casey@cafe.local')
    await user.type(within(dialog).getByLabelText(/^Temporary password/), 'LongEnough#1')
    await user.click(within(dialog).getByRole('button', { name: 'Create user' }))
    expect(await within(dialog).findByText('That email is already registered.')).toBeInTheDocument()
  })

  it('does not offer the Owner checkbox to a non-owner', async () => {
    const user = userEvent.setup()
    renderWithProviders(<UsersPage />)
    await screen.findByText('Casey Cashier')
    await user.click(screen.getByRole('button', { name: '+ New staff member' }))
    const dialog = await screen.findByRole('dialog', { name: 'New staff member' })
    expect(within(dialog).queryByLabelText(/Owner/)).not.toBeInTheDocument()
  })
})
