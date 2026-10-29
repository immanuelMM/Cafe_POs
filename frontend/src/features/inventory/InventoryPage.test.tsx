import { http, HttpResponse } from 'msw'
import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it } from 'vitest'
import { server } from '../../test/server'
import { renderWithProviders } from '../../test/render'
import { MAIN, UPTOWN, makeIngredient, resetStores, signIn, url } from '../../test/fixtures'
import { InventoryPage } from './InventoryPage'

const stock = [
  makeIngredient({ id: 'i1', name: 'Whole Milk', quantityOnHand: 1000, lowStockThreshold: 100 }),
  makeIngredient({
    id: 'i2',
    name: 'Tea Bags',
    unit: 'each',
    quantityOnHand: 8,
    lowStockThreshold: 10,
    isLowStock: true,
  }),
  makeIngredient({
    id: 'i3',
    name: 'Paper Cups',
    unit: 'each',
    quantityOnHand: 0,
    lowStockThreshold: 50,
    isLowStock: true,
  }),
  makeIngredient({ id: 'i4', name: 'Espresso Beans', unit: 'g', quantityOnHand: 120, lowStockThreshold: 100 }),
]

describe('InventoryPage', () => {
  beforeEach(() => {
    resetStores()
    signIn({ role: 'Manager' })
    server.use(http.get(url('/api/v1/ingredients'), () => HttpResponse.json(stock)))
  })

  it('shows stock status per ingredient and sorts low items first', async () => {
    renderWithProviders(<InventoryPage />)
    await screen.findByText('Tea Bags')
    const names = screen.getAllByRole('listitem').map((li) => li.querySelector('.stock-name strong')?.textContent)
    expect(names.slice(0, 2).sort()).toEqual(['Paper Cups', 'Tea Bags'])
    expect(names.slice(2)).toEqual(['Espresso Beans', 'Whole Milk'])

    const row = (name: string) => screen.getByText(name).closest('li')!
    expect(within(row('Paper Cups')).getByText('Out of stock')).toBeInTheDocument()
    expect(within(row('Tea Bags')).getByText('Low stock')).toBeInTheDocument()
    expect(within(row('Espresso Beans')).getByText('Running low')).toBeInTheDocument()
    expect(within(row('Whole Milk')).getByText('In stock')).toBeInTheDocument()
    expect(screen.getByText('low or out of stock').previousElementSibling).toHaveTextContent('2')
  })

  it('filters by low stock only and by search text', async () => {
    const user = userEvent.setup()
    renderWithProviders(<InventoryPage />)
    await screen.findByText('Tea Bags')
    await user.click(screen.getByLabelText('Low stock only'))
    expect(screen.queryByText('Whole Milk')).not.toBeInTheDocument()
    expect(screen.getByText('Tea Bags')).toBeInTheDocument()
    await user.click(screen.getByLabelText('Low stock only'))
    await user.type(screen.getByLabelText('Search ingredients'), 'milk')
    expect(screen.queryByText('Tea Bags')).not.toBeInTheDocument()
    await user.clear(screen.getByLabelText('Search ingredients'))
    await user.type(screen.getByLabelText('Search ingredients'), 'zzz')
    expect(screen.getByText('Nothing to show.')).toBeInTheDocument()
  })

  it('validates the new-ingredient form with Zod messages and does not call the API', async () => {
    let posted = false
    server.use(
      http.post(url('/api/v1/ingredients'), () => {
        posted = true
        return HttpResponse.json(makeIngredient(), { status: 201 })
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(<InventoryPage />)
    await screen.findByText('Tea Bags')
    await user.click(screen.getByRole('button', { name: '+ New ingredient' }))
    const dialog = await screen.findByRole('dialog', { name: 'New ingredient' })
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))
    expect(await within(dialog).findByText('Name is required')).toBeInTheDocument()
    expect(within(dialog).getByText('Unit is required (e.g. ml, g, each)')).toBeInTheDocument()

    await user.type(within(dialog).getByLabelText(/^Name/), 'Sugar')
    await user.type(within(dialog).getByLabelText(/^Unit/), 'g')
    await user.clear(within(dialog).getByLabelText(/^Opening quantity/))
    await user.type(within(dialog).getByLabelText(/^Opening quantity/), '-5')
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))
    expect(await within(dialog).findByText('Must be 0 or more')).toBeInTheDocument()
    expect(posted).toBe(false)
  })

  it('creates an ingredient with the typed payload and closes the dialog', async () => {
    let body: unknown
    server.use(
      http.post(url('/api/v1/ingredients'), async ({ request }) => {
        body = await request.json()
        return HttpResponse.json(makeIngredient({ name: 'Sugar' }), { status: 201 })
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(<InventoryPage />)
    await screen.findByText('Tea Bags')
    await user.click(screen.getByRole('button', { name: '+ New ingredient' }))
    const dialog = await screen.findByRole('dialog', { name: 'New ingredient' })
    await user.type(within(dialog).getByLabelText(/^Name/), 'Sugar')
    await user.type(within(dialog).getByLabelText(/^Unit/), 'g')
    await user.clear(within(dialog).getByLabelText(/^Low-stock/))
    await user.type(within(dialog).getByLabelText(/^Low-stock/), '25')
    await user.clear(within(dialog).getByLabelText(/^Opening quantity/))
    await user.type(within(dialog).getByLabelText(/^Opening quantity/), '500')
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))
    await screen.findByText('Tea Bags')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(body).toEqual({ name: 'Sugar', unit: 'g', lowStockThreshold: 25, initialQuantity: 500 })
  })

  it('rejects a zero or empty restock quantity', async () => {
    const user = userEvent.setup()
    renderWithProviders(<InventoryPage />)
    await screen.findByText('Tea Bags')
    await user.click(within(screen.getByText('Tea Bags').closest('li')!).getByRole('button', { name: 'Restock' }))
    const dialog = await screen.findByRole('dialog', { name: 'Restock Tea Bags' })
    await user.click(within(dialog).getByRole('button', { name: 'Record restock' }))
    expect(await within(dialog).findByText('Enter a number')).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText(/^Quantity received/), '0')
    await user.click(within(dialog).getByRole('button', { name: 'Record restock' }))
    expect(await within(dialog).findByText('Must be greater than 0')).toBeInTheDocument()
  })

  it('requires a note when adjusting stock and sends a negative delta for waste', async () => {
    let body: unknown
    server.use(
      http.post(url('/api/v1/ingredients/:id/adjustments'), async ({ request }) => {
        body = await request.json()
        return HttpResponse.json(makeIngredient())
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(<InventoryPage />)
    await screen.findByText('Tea Bags')
    await user.click(
      within(screen.getByText('Tea Bags').closest('li')!).getByRole('button', { name: 'Adjust / waste' }),
    )
    const dialog = await screen.findByRole('dialog', { name: /Adjust \/ waste: Tea Bags/ })
    await user.type(within(dialog).getByLabelText(/^Quantity/), '2')
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))
    expect(await within(dialog).findByText('Please add a note explaining why')).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText(/^Note/), 'Spilled')
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))
    await screen.findByText('Tea Bags')
    expect(body).toEqual({ reason: 'Waste', quantityDelta: -2, note: 'Spilled' })
  })

  it('is read-only for an Owner with no active location (catalog only)', async () => {
    resetStores()
    signIn({ role: 'Owner', isOwner: true, location: null, locations: [MAIN, UPTOWN] })
    server.use(
      http.get(url('/api/v1/locations'), () => HttpResponse.json([])),
      http.get(url('/api/v1/ingredients'), () =>
        HttpResponse.json([makeIngredient({ quantityOnHand: null, isLowStock: null })]),
      ),
    )
    renderWithProviders(<InventoryPage />)
    expect(await screen.findByText(/Showing the ingredient catalog only/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '+ New ingredient' })).toBeDisabled()
    expect(await screen.findByRole('button', { name: 'Restock' })).toBeDisabled()
  })
})
