import { http, HttpResponse } from 'msw'
import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it } from 'vitest'
import { server } from '../../test/server'
import { renderWithProviders } from '../../test/render'
import { makeItem, paged, resetStores, signIn, url } from '../../test/fixtures'
import { CatalogPage } from './CatalogPage'

const cat = { id: 'cat-1', name: 'Tea', sortOrder: 1, active: true }

describe('CatalogPage', () => {
  beforeEach(() => {
    resetStores()
    signIn({ role: 'Manager' })
    server.use(
      http.get(url('/api/v1/categories'), () => HttpResponse.json([cat])),
      http.get(url('/api/v1/items'), () => HttpResponse.json(paged([makeItem()]))),
      http.get(url('/api/v1/modifier-lists'), () => HttpResponse.json([])),
    )
  })

  it('lists items with category and active variation prices', async () => {
    renderWithProviders(<CatalogPage />)
    expect(await screen.findByText('Iced Tea')).toBeInTheDocument()
    expect(screen.getByText('Regular ₱100.00')).toBeInTheDocument()
  })

  it('validates the category form (name required, whole-number sort order)', async () => {
    let posted = false
    server.use(
      http.post(url('/api/v1/categories'), () => {
        posted = true
        return HttpResponse.json(cat, { status: 201 })
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(<CatalogPage />)
    await user.click(screen.getByRole('tab', { name: 'Categories' }))
    await user.click(await screen.findByRole('button', { name: '+ New category' }))
    const dialog = await screen.findByRole('dialog', { name: 'New category' })
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))
    expect(await within(dialog).findByText('Name is required')).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText(/^Name/), 'Pastries')
    await user.clear(within(dialog).getByLabelText(/^Sort order/))
    await user.type(within(dialog).getByLabelText(/^Sort order/), '1.5')
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))
    expect(await within(dialog).findByText('Whole number')).toBeInTheDocument()
    expect(posted).toBe(false)
  })

  it('saves a valid category and closes the dialog', async () => {
    let body: unknown
    server.use(
      http.post(url('/api/v1/categories'), async ({ request }) => {
        body = await request.json()
        return HttpResponse.json(cat, { status: 201 })
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(<CatalogPage />)
    await user.click(screen.getByRole('tab', { name: 'Categories' }))
    await user.click(await screen.findByRole('button', { name: '+ New category' }))
    const dialog = await screen.findByRole('dialog', { name: 'New category' })
    await user.type(within(dialog).getByLabelText(/^Name/), 'Pastries')
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))
    await screen.findByText('Tea')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(body).toEqual({ name: 'Pastries', sortOrder: 0, active: true })
  })

  it('validates the item form including variation name, SKU and price', async () => {
    const user = userEvent.setup()
    renderWithProviders(<CatalogPage />)
    await screen.findByText('Iced Tea')
    await user.click(screen.getByRole('button', { name: '+ New item' }))
    const dialog = await screen.findByRole('dialog', { name: 'New item' })
    await user.click(within(dialog).getByRole('button', { name: '+ Add variation' }))
    await user.click(within(dialog).getByRole('button', { name: 'Create item' }))
    expect(await within(dialog).findByText('Choose a category')).toBeInTheDocument()
    expect(within(dialog).getByText('Name is required')).toBeInTheDocument()
    expect(within(dialog).getAllByText('Name required')).toHaveLength(1)
    expect(within(dialog).getAllByText('SKU required')).toHaveLength(2) // default row (pre-named) and the added blank row
  })

  it('surfaces a server error when saving a category fails', async () => {
    server.use(
      http.post(url('/api/v1/categories'), () =>
        HttpResponse.json({ title: 'Conflict', detail: 'A category with that name exists.' }, { status: 409 }),
      ),
    )
    const user = userEvent.setup()
    renderWithProviders(<CatalogPage />)
    await user.click(screen.getByRole('tab', { name: 'Categories' }))
    await user.click(await screen.findByRole('button', { name: '+ New category' }))
    const dialog = await screen.findByRole('dialog', { name: 'New category' })
    await user.type(within(dialog).getByLabelText(/^Name/), 'Tea')
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))
    expect(await within(dialog).findByText('A category with that name exists.')).toBeInTheDocument()
  })
})
