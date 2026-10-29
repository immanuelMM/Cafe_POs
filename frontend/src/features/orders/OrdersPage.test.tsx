import { http, HttpResponse } from 'msw'
import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it } from 'vitest'
import type { components } from '../../api/schema'
import { server } from '../../test/server'
import { renderWithProviders } from '../../test/render'
import { MAIN, UPTOWN, makeOrder, paged, resetStores, signIn, url } from '../../test/fixtures'
import { OrdersPage } from './OrdersPage'

type S = components['schemas']
const loc = (id: string, name: string): S['LocationDto'] => ({
  id,
  name,
  address: null,
  timeZone: 'Asia/Manila',
  contactPhone: null,
  vatRatePercent: 12,
  active: true,
  stripeTerminalLocationId: null,
})

describe('OrdersPage', () => {
  beforeEach(() => {
    resetStores()
    signIn({ role: 'Manager' })
    server.use(http.get(url('/api/v1/locations/current'), () => HttpResponse.json(loc('loc-main', 'Main Branch'))))
  })

  it('lists orders with number, status, total and a receipt link for completed ones', async () => {
    server.use(
      http.get(url('/api/v1/orders'), () =>
        HttpResponse.json(
          paged([
            makeOrder(),
            makeOrder({ id: 'o2', orderNumber: null, status: 'Open', total: 55, completedAt: null }),
          ]),
        ),
      ),
    )
    renderWithProviders(<OrdersPage />)
    expect(await screen.findByText('#00007')).toBeInTheDocument()
    expect(screen.getByText('Completed', { selector: '.badge' })).toBeInTheDocument()
    expect(screen.getByText('₱55.00')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'View receipt' })).toHaveAttribute('href', '/orders/order-1/receipt')
    expect(screen.getByRole('button', { name: 'Cancel order' })).toBeEnabled()
    expect(screen.getByText('2 orders')).toBeInTheDocument()
  })

  it('shows the empty state', async () => {
    server.use(http.get(url('/api/v1/orders'), () => HttpResponse.json(paged([]))))
    renderWithProviders(<OrdersPage />)
    expect(await screen.findByText('No orders match these filters.')).toBeInTheDocument()
  })

  it('shows an error with a retry that recovers', async () => {
    let fail = true
    server.use(
      http.get(url('/api/v1/orders'), () =>
        fail ? HttpResponse.json({ title: 'Database down' }, { status: 500 }) : HttpResponse.json(paged([makeOrder()])),
      ),
    )
    const user = userEvent.setup()
    renderWithProviders(<OrdersPage />)
    expect(await screen.findByText('Database down')).toBeInTheDocument()
    fail = false
    await user.click(screen.getByRole('button', { name: 'Retry' }))
    expect(await screen.findByText('#00007')).toBeInTheDocument()
  })

  it('sends the status filter to the API', async () => {
    const seen: (string | null)[] = []
    server.use(
      http.get(url('/api/v1/orders'), ({ request }) => {
        seen.push(new URL(request.url).searchParams.get('status'))
        return HttpResponse.json(paged([]))
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(<OrdersPage />)
    await screen.findByText('No orders match these filters.')
    await user.selectOptions(screen.getByLabelText('Status'), 'Canceled')
    await screen.findByText('No orders match these filters.')
    expect(seen.at(-1)).toBe('Canceled')
  })

  it('does not show the location filter to non-owners', async () => {
    server.use(http.get(url('/api/v1/orders'), () => HttpResponse.json(paged([]))))
    renderWithProviders(<OrdersPage />)
    await screen.findByText('No orders match these filters.')
    expect(screen.queryByLabelText('Location')).not.toBeInTheDocument()
  })

  it('tags each order with its location for an Owner in the cross-location view and disables Cancel', async () => {
    resetStores()
    signIn({ role: 'Owner', isOwner: true, location: null, locations: [MAIN, UPTOWN] })
    server.use(
      http.get(url('/api/v1/locations'), () =>
        HttpResponse.json([loc('loc-main', 'Main Branch'), loc('loc-up', 'Uptown Branch')]),
      ),
      http.get(url('/api/v1/orders'), () =>
        HttpResponse.json(
          paged([
            makeOrder({ id: 'a', locationId: 'loc-main', status: 'Open', orderNumber: null }),
            makeOrder({ id: 'b', locationId: 'loc-up', orderNumber: 9 }),
          ]),
        ),
      ),
    )
    renderWithProviders(<OrdersPage />)
    const second = (await screen.findByText('#00009')).closest('li')!
    expect(within(second).getByText('Uptown Branch')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Cancel order' })).toBeDisabled()
    expect(screen.getByLabelText('Location')).toBeInTheDocument()
  })
})
