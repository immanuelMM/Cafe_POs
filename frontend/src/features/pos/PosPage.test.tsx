import { http, HttpResponse } from 'msw'
import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it } from 'vitest'
import type { components } from '../../api/schema'
import { server } from '../../test/server'
import { renderWithProviders } from '../../test/render'
import { MAIN, UPTOWN, makeItem, makeOrder, paged, resetStores, signIn, url, variation } from '../../test/fixtures'
import { PosPage } from './PosPage'
import { useCartStore } from './cartStore'

type S = components['schemas']

const milk: S['ModifierListDto'] = {
  id: 'ml-1',
  name: 'Milk Type',
  minSelections: 1,
  maxSelections: 1,
  active: true,
  modifiers: [
    { id: 'm-oat', name: 'Oat Milk', priceDelta: 30, active: true },
    { id: 'm-alm', name: 'Almond Milk', priceDelta: 30, active: true },
  ],
}

const tea = makeItem()
const latte = makeItem({
  id: 'item-2',
  name: 'Latte',
  variations: [
    variation({ id: 'lat-s', itemId: 'item-2', name: 'Small', sku: 'LAT-S', price: 130, effectivePrice: 130 }),
    variation({ id: 'lat-m', itemId: 'item-2', name: 'Medium', sku: 'LAT-M', price: 150, effectivePrice: 150 }),
  ],
  modifierListIds: ['ml-1'],
})

function mockCatalog(items = [tea, latte]) {
  server.use(
    http.get(url('/api/v1/categories'), () =>
      HttpResponse.json([{ id: 'cat-1', name: 'Tea', sortOrder: 1, active: true }]),
    ),
    http.get(url('/api/v1/items'), () => HttpResponse.json(paged(items))),
    http.get(url('/api/v1/modifier-lists'), () => HttpResponse.json([milk])),
  )
}

describe('PosPage', () => {
  beforeEach(() => {
    resetStores()
    signIn()
    mockCatalog()
  })

  it('lists menu tiles with "from" pricing for multi-variation items', async () => {
    renderWithProviders(<PosPage />)
    expect(await screen.findByRole('button', { name: /Iced Tea/ })).toHaveTextContent('₱100.00')
    expect(screen.getByRole('button', { name: /Latte/ })).toHaveTextContent('from ₱130.00')
  })

  it('shows an empty state when the search matches nothing', async () => {
    server.use(http.get(url('/api/v1/items'), () => HttpResponse.json(paged([]))))
    renderWithProviders(<PosPage />)
    expect(await screen.findByText('No items found.')).toBeInTheDocument()
  })

  it('shows an error box with retry when the menu fails to load', async () => {
    server.use(http.get(url('/api/v1/items'), () => HttpResponse.json({ title: 'Boom' }, { status: 500 })))
    renderWithProviders(<PosPage />)
    expect(await screen.findByText('Boom')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
  })

  it('adds a simple item straight to the cart and merges repeated taps into one line', async () => {
    const user = userEvent.setup()
    renderWithProviders(<PosPage />)
    const tile = await screen.findByRole('button', { name: /Iced Tea/ })
    await user.click(tile)
    await user.click(tile)
    const cart = screen.getByRole('complementary', { name: 'Current order' })
    expect(within(cart).getAllByRole('listitem')).toHaveLength(1)
    expect(within(cart).getByRole('status')).toHaveTextContent('2')
    expect(within(cart).getByText('Total (2 items)').nextElementSibling).toHaveTextContent('₱200.00')
    expect(within(cart).getByRole('button', { name: 'Charge ₱200.00' })).toBeEnabled()
  })

  it('requires a mandatory modifier and prices the chosen size plus modifier', async () => {
    const user = userEvent.setup()
    renderWithProviders(<PosPage />)
    await user.click(await screen.findByRole('button', { name: /Latte/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Latte' })
    const add = within(dialog).getByRole('button', { name: /^Add/ })
    expect(add).toBeDisabled()
    expect(within(dialog).getByRole('alert')).toHaveTextContent('Select the required options: Milk Type.')

    await user.click(within(dialog).getByRole('radio', { name: /Medium/ }))
    await user.click(within(dialog).getByRole('radio', { name: /Oat Milk/ }))
    expect(add).toBeEnabled()
    expect(add).toHaveTextContent('₱180.00') // 150 + 30
    await user.click(add)

    const cart = screen.getByRole('complementary', { name: 'Current order' })
    expect(within(cart).getByText('+ Oat Milk')).toBeInTheDocument()
    expect(within(cart).getByRole('button', { name: 'Charge ₱180.00' })).toBeInTheDocument()
  })

  it('changes quantity, removes lines and clears the cart', async () => {
    const user = userEvent.setup()
    renderWithProviders(<PosPage />)
    await user.click(await screen.findByRole('button', { name: /Iced Tea/ }))
    const cart = screen.getByRole('complementary', { name: 'Current order' })
    await user.click(within(cart).getByRole('button', { name: 'Increase Iced Tea' }))
    expect(within(cart).getByRole('button', { name: 'Charge ₱200.00' })).toBeInTheDocument()
    await user.click(within(cart).getByRole('button', { name: 'Decrease Iced Tea' }))
    await user.click(within(cart).getByRole('button', { name: 'Decrease Iced Tea' }))
    expect(within(cart).getByText('Tap an item to start an order.')).toBeInTheDocument()
    expect(within(cart).getByRole('button', { name: /^Charge/ })).toBeDisabled()
  })

  describe('checkout', () => {
    async function openPayment() {
      const user = userEvent.setup()
      renderWithProviders(<PosPage />)
      await user.click(await screen.findByRole('button', { name: /Iced Tea/ }))
      await user.click(screen.getByRole('button', { name: 'Charge ₱100.00' }))
      const dialog = await screen.findByRole('dialog', { name: 'Cash payment' })
      return { user, dialog }
    }

    it('blocks payment while tendered cash is short and shows change once enough', async () => {
      const { user, dialog } = await openPayment()
      const pay = within(dialog).getByRole('button', { name: /Complete payment/ })
      expect(pay).toBeDisabled()
      const input = within(dialog).getByLabelText(/Cash tendered/)
      await user.type(input, '99.99')
      expect(within(dialog).getByRole('alert')).toHaveTextContent('Tendered amount is less than the total.')
      expect(pay).toBeDisabled()
      await user.clear(input)
      await user.type(input, '150')
      expect(within(dialog).getByText('Change').nextElementSibling).toHaveTextContent('₱50.00')
      expect(pay).toBeEnabled()
    })

    it('creates then completes the order with an idempotency key and shows change due', async () => {
      const calls: string[] = []
      let created: { id: string; items: unknown[] } | undefined
      let completeBody: unknown
      let key: string | null = null
      server.use(
        http.post(url('/api/v1/orders'), async ({ request }) => {
          created = (await request.json()) as typeof created
          calls.push('create')
          return HttpResponse.json(makeOrder({ id: created!.id, status: 'Open' }), { status: 201 })
        }),
        http.post(url('/api/v1/orders/:id/complete'), async ({ request }) => {
          calls.push('complete')
          key = request.headers.get('idempotency-key')
          completeBody = await request.json()
          return HttpResponse.json(makeOrder({ id: created!.id }))
        }),
        http.get(url('/api/v1/orders/:id/receipt'), () => HttpResponse.json({ error: 'n/a' }, { status: 404 })),
      )
      const { user, dialog } = await openPayment()
      await user.click(within(dialog).getByRole('button', { name: 'Exact' }))
      await user.click(within(dialog).getByRole('button', { name: /Complete payment/ }))

      expect(await screen.findByRole('dialog', { name: 'Order #00007 paid' })).toBeInTheDocument()
      expect(calls).toEqual(['create', 'complete'])
      expect(created!.items).toEqual([{ itemVariationId: 'var-1', quantity: 1, modifierIds: [], note: null }])
      expect(completeBody).toEqual({ paymentMethod: 'cash', amountTendered: 100 })
      expect(key).toBeTruthy()
      expect(useCartStore.getState().lines).toHaveLength(0)
    })

    it('shows the server message for insufficient stock (409) and keeps the cart', async () => {
      server.use(
        http.post(url('/api/v1/orders'), async ({ request }) => {
          const b = (await request.json()) as { id: string }
          return HttpResponse.json(makeOrder({ id: b.id, status: 'Open' }), { status: 201 })
        }),
        http.post(url('/api/v1/orders/:id/complete'), () =>
          HttpResponse.json(
            { title: 'Insufficient stock', ingredient: 'Whole Milk', required: 200, available: 50 },
            { status: 409 },
          ),
        ),
      )
      const { user, dialog } = await openPayment()
      await user.click(within(dialog).getByRole('button', { name: 'Exact' }))
      await user.click(within(dialog).getByRole('button', { name: /Complete payment/ }))
      expect(await within(dialog).findByText('Not enough Whole Milk (need 200, only 50 left).')).toBeInTheDocument()
      expect(useCartStore.getState().lines).toHaveLength(1)
    })

    it('reuses the idempotency key after a 5xx so a retry cannot double charge', async () => {
      const keys: (string | null)[] = []
      let n = 0
      server.use(
        http.post(url('/api/v1/orders'), async ({ request }) => {
          const b = (await request.json()) as { id: string }
          return HttpResponse.json(makeOrder({ id: b.id, status: 'Open' }), { status: n ? 200 : 201 })
        }),
        http.post(url('/api/v1/orders/:id/complete'), ({ request }) => {
          keys.push(request.headers.get('idempotency-key'))
          n++
          return n === 1
            ? HttpResponse.json({ title: 'Server error' }, { status: 500 })
            : HttpResponse.json(makeOrder())
        }),
        http.get(url('/api/v1/orders/:id/receipt'), () => HttpResponse.json({}, { status: 404 })),
      )
      const { user, dialog } = await openPayment()
      await user.click(within(dialog).getByRole('button', { name: 'Exact' }))
      await user.click(within(dialog).getByRole('button', { name: /Complete payment/ }))
      expect(await within(dialog).findByText('Server error')).toBeInTheDocument()
      await user.click(within(dialog).getByRole('button', { name: /Complete payment/ }))
      await screen.findByRole('dialog', { name: /paid/ })
      expect(keys).toHaveLength(2)
      expect(keys[0]).toBe(keys[1])
    })
  })

  it('asks an Owner in the "All locations" session to pick a location first', async () => {
    resetStores()
    signIn({ role: 'Owner', isOwner: true, location: null, locations: [MAIN, UPTOWN] })
    renderWithProviders(<PosPage />)
    expect(await screen.findByRole('heading', { name: 'Choose a location' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Main Branch' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Uptown Branch' })).toBeInTheDocument()
    expect(screen.queryByRole('complementary', { name: 'Current order' })).not.toBeInTheDocument()
  })
})
