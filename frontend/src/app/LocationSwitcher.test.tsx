import { http, HttpResponse } from 'msw'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { server } from '../test/server'
import { renderWithProviders } from '../test/render'
import { MAIN, UPTOWN, makeAuth, resetStores, signIn, url } from '../test/fixtures'
import { useAuthStore } from '../features/auth/authStore'
import { useCartStore } from '../features/pos/cartStore'
import { LocationSwitcher } from './LocationSwitcher'

describe('LocationSwitcher', () => {
  beforeEach(resetStores)

  it('renders nothing when signed out', () => {
    const { container } = renderWithProviders(<LocationSwitcher />)
    expect(container).toBeEmptyDOMElement()
  })

  it('shows a locked label (no select) for single-location staff', () => {
    signIn()
    renderWithProviders(<LocationSwitcher />)
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument()
    expect(screen.getByText(/Main Branch/)).toBeInTheDocument()
  })

  it('lets a multi-location manager switch and swaps the session token', async () => {
    signIn({ role: 'Manager', locations: [MAIN, UPTOWN] })
    let body: unknown
    server.use(
      http.post(url('/api/v1/auth/switch-location'), async ({ request }) => {
        body = await request.json()
        return HttpResponse.json({
          ...makeAuth({
            role: 'Manager',
            location: { id: UPTOWN.locationId, name: UPTOWN.locationName },
            locations: [MAIN, UPTOWN],
          }),
          accessToken: 'token-uptown',
        })
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(<LocationSwitcher />)
    const select = screen.getByRole('combobox', { name: 'Active location' })
    expect(select).toHaveValue('loc-main')
    expect(screen.queryByRole('option', { name: 'All locations' })).not.toBeInTheDocument()
    await user.selectOptions(select, 'loc-up')
    await waitFor(() => expect(useAuthStore.getState().accessToken).toBe('token-uptown'))
    expect(body).toEqual({ locationId: 'loc-up' })
    expect(useAuthStore.getState().user?.location?.id).toBe('loc-up')
  })

  it('offers an Owner the "All locations" option and sends locationId null', async () => {
    signIn({ role: 'Owner', isOwner: true, locations: [MAIN, UPTOWN] })
    let body: unknown
    server.use(
      http.post(url('/api/v1/auth/switch-location'), async ({ request }) => {
        body = await request.json()
        return HttpResponse.json(makeAuth({ role: 'Owner', isOwner: true, location: null, locations: [MAIN, UPTOWN] }))
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(<LocationSwitcher />)
    await user.selectOptions(screen.getByRole('combobox', { name: 'Active location' }), '')
    await waitFor(() => expect(useAuthStore.getState().user?.location).toBeNull())
    expect(body).toEqual({ locationId: null })
  })

  it('asks before switching with items in the cart, and keeps everything when declined', async () => {
    signIn({ role: 'Manager', locations: [MAIN, UPTOWN] })
    useCartStore.setState({
      lines: [
        {
          key: 'k',
          itemName: 'Latte',
          variationId: 'v',
          variationName: 'S',
          basePrice: 100,
          modifiers: [],
          quantity: 1,
        },
      ],
    })
    let switched = false
    server.use(
      http.post(url('/api/v1/auth/switch-location'), () => {
        switched = true
        return HttpResponse.json(makeAuth())
      }),
    )
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false)
    const user = userEvent.setup()
    renderWithProviders(<LocationSwitcher />)
    await user.selectOptions(screen.getByRole('combobox', { name: 'Active location' }), 'loc-up')
    expect(confirm).toHaveBeenCalledOnce()
    expect(switched).toBe(false)
    expect(useCartStore.getState().lines).toHaveLength(1)
  })

  it('clears the cart once the switch is confirmed', async () => {
    signIn({ role: 'Manager', locations: [MAIN, UPTOWN] })
    useCartStore.setState({
      lines: [
        {
          key: 'k',
          itemName: 'Latte',
          variationId: 'v',
          variationName: 'S',
          basePrice: 100,
          modifiers: [],
          quantity: 1,
        },
      ],
    })
    server.use(
      http.post(url('/api/v1/auth/switch-location'), () =>
        HttpResponse.json(
          makeAuth({
            role: 'Manager',
            location: { id: UPTOWN.locationId, name: UPTOWN.locationName },
            locations: [MAIN, UPTOWN],
          }),
        ),
      ),
    )
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    const user = userEvent.setup()
    renderWithProviders(<LocationSwitcher />)
    await user.selectOptions(screen.getByRole('combobox', { name: 'Active location' }), 'loc-up')
    await waitFor(() => expect(useCartStore.getState().lines).toHaveLength(0))
  })

  it('surfaces a rejected switch (403) and keeps the current location', async () => {
    signIn({ role: 'Manager', locations: [MAIN, UPTOWN] })
    server.use(
      http.post(url('/api/v1/auth/switch-location'), () =>
        HttpResponse.json({ title: 'Forbidden', detail: 'You are not assigned to that location.' }, { status: 403 }),
      ),
    )
    const user = userEvent.setup()
    renderWithProviders(<LocationSwitcher />)
    await user.selectOptions(screen.getByRole('combobox', { name: 'Active location' }), 'loc-up')
    expect(await screen.findByRole('alert')).toHaveTextContent('You are not assigned to that location.')
    expect(useAuthStore.getState().user?.location?.id).toBe('loc-main')
  })
})
