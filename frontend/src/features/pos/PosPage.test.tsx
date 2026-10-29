import { http, HttpResponse } from 'msw'
import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
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

  describe('category column', () => {
    const cats = [
      { id: 'cat-1', name: 'Tea', sortOrder: 1, active: true },
      { id: 'cat-2', name: 'Pastry', sortOrder: 2, active: true },
    ]
    it('is a vertical tablist: All plus every category, selecting one filters the menu', async () => {
      const asked: (string | null)[] = []
      server.use(
        http.get(url('/api/v1/categories'), () => HttpResponse.json(cats)),
        http.get(url('/api/v1/items'), ({ request }) => {
          asked.push(new URL(request.url).searchParams.get('categoryId'))
          return HttpResponse.json(paged([tea]))
        }),
      )
      const user = userEvent.setup()
      renderWithProviders(<PosPage />)
      const list = await screen.findByRole('tablist', { name: 'Categories' })
      expect(list).toHaveAttribute('aria-orientation', 'vertical')
      const tabs = await within(list).findAllByRole('tab')
      await vi.waitFor(() =>
        expect(
          within(list)
            .getAllByRole('tab')
            .map((t) => t.textContent),
        ).toEqual(['All', 'Tea', 'Pastry']),
      )
      expect(tabs[0]).toHaveAttribute('aria-selected', 'true')

      await user.click(within(list).getByRole('tab', { name: 'Pastry' }))
      expect(within(list).getByRole('tab', { name: 'Pastry' })).toHaveAttribute('aria-selected', 'true')
      expect(within(list).getByRole('tab', { name: 'All' })).toHaveAttribute('aria-selected', 'false')
      await vi.waitFor(() => expect(asked).toContain('cat-2'))
    })

    it('moves between categories with the arrow keys', async () => {
      server.use(http.get(url('/api/v1/categories'), () => HttpResponse.json(cats)))
      const user = userEvent.setup()
      renderWithProviders(<PosPage />)
      const tea = await screen.findByRole('tab', { name: 'Tea' })
      screen.getByRole('tab', { name: 'All' }).focus()
      await user.keyboard('{ArrowDown}')
      expect(tea).toHaveAttribute('aria-selected', 'true')
      expect(tea).toHaveFocus()
      await user.keyboard('{End}')
      expect(screen.getByRole('tab', { name: 'Pastry' })).toHaveAttribute('aria-selected', 'true')
    })
  })

  it('marks items with no active variation as disabled "Unavailable" tiles', async () => {
    const sold = makeItem({
      id: 'item-9',
      name: 'Banana Bread',
      variations: [variation({ id: 'bb', itemId: 'item-9', active: false })],
    })
    mockCatalog([tea, sold])
    renderWithProviders(<PosPage />)
    const tile = await screen.findByRole('button', { name: /Banana Bread/ })
    expect(tile).toBeDisabled()
    expect(tile).toHaveTextContent('Unavailable')
    expect(screen.getByRole('button', { name: /Iced Tea/ })).toBeEnabled()
  })

  it('clears the search with the clear button', async () => {
    const user = userEvent.setup()
    renderWithProviders(<PosPage />)
    const box = await screen.findByRole('searchbox', { name: 'Search items' })
    expect(screen.queryByRole('button', { name: 'Clear search' })).not.toBeInTheDocument()
    await user.type(box, 'lat')
    await user.click(screen.getByRole('button', { name: 'Clear search' }))
    expect(box).toHaveValue('')
    expect(box).toHaveFocus()
  })

  describe('search and categories', () => {
    const cats = [
      { id: 'cat-1', name: 'Tea', sortOrder: 1, active: true },
      { id: 'cat-2', name: 'Pastry', sortOrder: 2, active: true },
    ]
    it('searches across all categories (All selected) and restores the category when the search is cleared', async () => {
      const asked: { cat: string | null; q: string | null }[] = []
      server.use(
        http.get(url('/api/v1/categories'), () => HttpResponse.json(cats)),
        http.get(url('/api/v1/items'), ({ request }) => {
          const u = new URL(request.url)
          asked.push({ cat: u.searchParams.get('categoryId'), q: u.searchParams.get('search') })
          return HttpResponse.json(paged([tea]))
        }),
      )
      const user = userEvent.setup()
      renderWithProviders(<PosPage />)
      await user.click(await screen.findByRole('tab', { name: 'Pastry' }))
      await vi.waitFor(() => expect(asked.at(-1)).toEqual({ cat: 'cat-2', q: null }))

      await user.type(screen.getByRole('searchbox', { name: 'Search items' }), 'lat')
      await vi.waitFor(() => expect(asked.at(-1)).toEqual({ cat: null, q: 'lat' }))
      expect(screen.getByRole('tab', { name: 'All' })).toHaveAttribute('aria-selected', 'true')

      await user.click(screen.getByRole('button', { name: 'Clear search' }))
      await vi.waitFor(() => expect(asked.at(-1)).toEqual({ cat: 'cat-2', q: null }))
      expect(screen.getByRole('tab', { name: 'Pastry' })).toHaveAttribute('aria-selected', 'true')
    })

    it('keeps the category list in the DOM while searching (CSS only swaps it for the field on phones)', async () => {
      server.use(http.get(url('/api/v1/categories'), () => HttpResponse.json(cats)))
      const user = userEvent.setup()
      renderWithProviders(<PosPage />)
      await screen.findByRole('tab', { name: 'Tea' })
      await user.type(screen.getByRole('searchbox', { name: 'Search items' }), 'x')
      expect(screen.getByRole('tablist', { name: 'Categories' })).toBeInTheDocument()
    })
  })

  describe('clear order', () => {
    const add = (n: number) => {
      for (let i = 1; i <= n; i++)
        useCartStore.getState().add({
          itemName: `Item ${i}`,
          variationId: `v${i}`,
          variationName: 'Regular',
          basePrice: 10,
          modifiers: [],
          quantity: 1,
        })
    }

    it('clears at once when the order has 3 lines or fewer', async () => {
      add(3)
      const user = userEvent.setup()
      renderWithProviders(<PosPage />)
      const cart = screen.getByRole('complementary', { name: 'Current order' })
      await user.click(within(cart).getByRole('button', { name: 'Clear' }))
      expect(within(cart).queryByRole('group', { name: 'Confirm clearing the order' })).not.toBeInTheDocument()
      expect(useCartStore.getState().lines).toHaveLength(0)
    })

    it('asks "Clear all?" first when the order has more than 3 lines; Keep leaves it alone', async () => {
      add(4)
      const user = userEvent.setup()
      renderWithProviders(<PosPage />)
      const cart = screen.getByRole('complementary', { name: 'Current order' })
      await user.click(within(cart).getByRole('button', { name: 'Clear' }))
      const ask = within(cart).getByRole('group', { name: 'Confirm clearing the order' })
      expect(ask).toHaveTextContent('Clear all 4 lines?')
      expect(useCartStore.getState().lines).toHaveLength(4)
      expect(within(ask).getByRole('button', { name: 'Keep' })).toHaveFocus()

      await user.click(within(ask).getByRole('button', { name: 'Keep' }))
      expect(within(cart).queryByRole('group', { name: 'Confirm clearing the order' })).not.toBeInTheDocument()
      expect(useCartStore.getState().lines).toHaveLength(4)

      await user.click(within(cart).getByRole('button', { name: 'Clear' }))
      await user.click(within(cart).getByRole('button', { name: 'Yes, clear' }))
      expect(useCartStore.getState().lines).toHaveLength(0)
      expect(within(cart).getByText('Tap an item to start an order.')).toBeInTheDocument()
    })
  })

  it('does not bring the "Clear all?" question back by itself after the cart changes', async () => {
    for (let i = 1; i <= 4; i++)
      useCartStore.getState().add({
        itemName: `Item ${i}`,
        variationId: `v${i}`,
        variationName: 'Regular',
        basePrice: 10,
        modifiers: [],
        quantity: 1,
      })
    const user = userEvent.setup()
    renderWithProviders(<PosPage />)
    const cart = screen.getByRole('complementary', { name: 'Current order' })
    await user.click(within(cart).getByRole('button', { name: 'Clear' }))
    expect(within(cart).getByRole('group', { name: 'Confirm clearing the order' })).toBeInTheDocument()
    await user.click(within(cart).getByRole('button', { name: 'Remove Item 1' })) // 3 lines: question gone
    expect(within(cart).queryByRole('group', { name: 'Confirm clearing the order' })).not.toBeInTheDocument()
    await user.click(await screen.findByRole('button', { name: /Iced Tea/ })) // back to 4 lines
    expect(within(cart).queryByRole('group', { name: 'Confirm clearing the order' })).not.toBeInTheDocument()
  })

  describe('option sheet', () => {
    it('gives single-choice groups radio-group semantics and shows Required until chosen', async () => {
      const user = userEvent.setup()
      renderWithProviders(<PosPage />)
      await user.click(await screen.findByRole('button', { name: /Latte/ }))
      const dialog = await screen.findByRole('dialog', { name: 'Latte' })
      const group = within(dialog).getByRole('radiogroup', { name: 'Milk Type' })
      expect(group).toHaveTextContent('Required')
      expect(within(dialog).getByRole('radiogroup', { name: 'Size / variation' })).toBeInTheDocument()
      expect(within(dialog).getByRole('button', { name: /^Add to order/ })).toBeDisabled()

      await user.click(within(group).getByRole('radio', { name: /Oat Milk/ }))
      expect(group).not.toHaveTextContent('Required')
      expect(group).toHaveTextContent('Selected')
      expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument()
      expect(within(dialog).getByRole('button', { name: /^Add to order/ })).toBeEnabled()
    })

    it('uses a checkbox group for multi-select lists and locks the rest at the maximum', async () => {
      const extras: S['ModifierListDto'] = {
        id: 'ml-2',
        name: 'Extras',
        minSelections: 0,
        maxSelections: 2,
        active: true,
        modifiers: ['Syrup', 'Whip', 'Shot'].map((name, i) => ({ id: `x${i}`, name, priceDelta: 10, active: true })),
      }
      const withExtras = makeItem({ ...latte, modifierListIds: ['ml-2'] })
      server.use(
        http.get(url('/api/v1/modifier-lists'), () => HttpResponse.json([extras])),
        http.get(url('/api/v1/items'), () => HttpResponse.json(paged([withExtras]))),
      )
      const user = userEvent.setup()
      renderWithProviders(<PosPage />)
      await user.click(await screen.findByRole('button', { name: /Latte/ }))
      const dialog = await screen.findByRole('dialog', { name: 'Latte' })
      const group = within(dialog).getByRole('group', { name: 'Extras' })
      expect(group).not.toHaveTextContent('Required')
      expect(within(dialog).getByRole('button', { name: /^Add to order/ })).toBeEnabled() // optional list
      await user.click(within(group).getByRole('checkbox', { name: /Syrup/ }))
      await user.click(within(group).getByRole('checkbox', { name: /Whip/ }))
      expect(within(group).getByRole('checkbox', { name: /Shot/ })).toBeDisabled()
    })
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

    it('enters the cash amount with the on-screen keypad, shows change and gates Complete', async () => {
      const { user, dialog } = await openPayment() // total 100
      const pad = within(dialog).getByRole('group', { name: 'Cash keypad' })
      const input = within(dialog).getByLabelText(/Cash tendered/)
      const pay = within(dialog).getByRole('button', { name: /Complete payment/ })
      const press = async (...keys: string[]) => {
        for (const k of keys) await user.click(within(pad).getByRole('button', { name: k }))
      }
      await press('9', '9')
      expect(input).toHaveValue('99')
      expect(pay).toBeDisabled()
      expect(within(dialog).getByRole('alert')).toHaveTextContent('Tendered amount is less than the total.')
      await press('Backspace', '.', '.', '5', '0', '9')
      expect(input).toHaveValue('9.50') // one decimal point, two decimals max
      await press('Backspace', 'Backspace', 'Backspace', 'Backspace')
      expect(input).toHaveValue('')
      await press('5', '0', '0')
      expect(input).toHaveValue('500')
      expect(within(dialog).getByText('Change').nextElementSibling).toHaveTextContent('₱400.00')
      expect(pay).toBeEnabled()
    })

    it('normalises a typed leading "." and shows no shortfall error for unfinished entries', async () => {
      const { user, dialog } = await openPayment() // total 100
      const input = within(dialog).getByLabelText(/Cash tendered/)
      await user.type(input, '.')
      expect(input).toHaveValue('0.')
      expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument()
      await user.clear(input)
      await user.type(input, '.5')
      expect(input).toHaveValue('0.5')
      expect(within(dialog).queryByText(/NaN/)).not.toBeInTheDocument()
      await user.clear(input)
      await user.type(input, '12.')
      expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument()
      await user.type(input, '5')
      expect(within(dialog).getByRole('alert')).toHaveTextContent('Tendered amount is less than the total.')
    })

    it('starts a decimal with 0. and fills the amount from a quick-tender button', async () => {
      const { user, dialog } = await openPayment()
      const input = within(dialog).getByLabelText(/Cash tendered/)
      await user.click(within(dialog).getByRole('button', { name: '.' }))
      expect(input).toHaveValue('0.')
      await user.click(within(dialog).getByRole('button', { name: '₱1,000' }))
      expect(input).toHaveValue('1000')
      expect(within(dialog).getByText('Change').nextElementSibling).toHaveTextContent('₱900.00')
      await user.click(within(dialog).getByRole('button', { name: 'Exact' }))
      expect(input).toHaveValue('100')
    })

    it('after payment opens the print dialog exactly once, and New order resets the cart', async () => {
      const print = vi.spyOn(window, 'print').mockImplementation(() => undefined)
      const receipt = {
        locationName: 'Main Branch',
        locationAddress: null,
        locationPhone: null,
        orderNumber: 7,
        orderId: 'order-print-1',
        completedAt: '2026-01-01T02:01:00Z',
        timeZone: 'Asia/Manila',
        cashierName: 'Casey Cashier',
        lines: [{ description: 'Iced Tea', modifiers: null, quantity: 1, unitPrice: 100, lineTotal: 100 }],
        pricesIncludeVat: true,
        vatRatePercent: 12,
        vatableSales: 89.29,
        vatAmount: 10.71,
        total: 100,
        paymentMethod: 'cash',
        amountTendered: 200,
        changeGiven: 100,
        currency: 'PHP',
      }
      server.use(
        http.post(url('/api/v1/orders'), async ({ request }) => {
          const b = (await request.json()) as { id: string }
          return HttpResponse.json(makeOrder({ id: b.id, status: 'Open' }), { status: 201 })
        }),
        http.post(url('/api/v1/orders/:id/complete'), () => HttpResponse.json(makeOrder({ id: 'order-print-1' }))),
        http.get(url('/api/v1/orders/:id/receipt'), () => HttpResponse.json(receipt)),
      )
      const { user, dialog } = await openPayment()
      await user.click(within(dialog).getByRole('button', { name: '₱500' }))
      await user.click(within(dialog).getByRole('button', { name: /Complete payment/ }))
      const paid = await screen.findByRole('dialog', { name: 'Order #00007 paid' })
      expect(within(paid).getByRole('status')).toHaveTextContent('₱100.00')
      expect(await within(paid).findByRole('article')).toHaveTextContent('Iced Tea')
      await vi.waitFor(() => expect(print).toHaveBeenCalledTimes(1))
      await new Promise((r) => setTimeout(r, 600)) // no second print from re-renders
      expect(print).toHaveBeenCalledTimes(1)

      await user.click(within(paid).getByRole('button', { name: 'Print again' }))
      expect(print).toHaveBeenCalledTimes(2)

      await user.click(within(paid).getByRole('button', { name: 'New order' }))
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
      expect(useCartStore.getState().lines).toHaveLength(0)
      print.mockRestore()
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
