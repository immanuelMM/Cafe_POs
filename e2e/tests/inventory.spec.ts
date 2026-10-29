import { randomUUID } from 'node:crypto'
import { expect, test } from '@playwright/test'
import { apiLogin, bearer, completeOrderViaApi, findVariation, newApi, stockByName } from '../support/api'
import { LOCATIONS, USERS } from '../support/env'
import { loginViaUi } from '../support/ui'

// Seeded recipe: Latte Medium (LAT-M) = 18 g Espresso Beans + 200 ml Whole Milk + 1 Paper Cup.
const LATTE_M = { sku: 'LAT-M', beans: 18, milk: 200, cups: 1 }

test.describe('inventory deduction', () => {
  test('completing an order deducts the recipe from that location only @smoke', async () => {
    const mainApi = await newApi()
    const ownerApi = await newApi()
    const main = await apiLogin(mainApi, USERS.cashier)
    const owner = await apiLogin(ownerApi, USERS.owner)
    const latte = await findVariation(mainApi, main, LATTE_M.sku)

    const mainBefore = await stockByName(mainApi, main)
    const uptownBefore = await stockByName(ownerApi, owner, LOCATIONS.uptown.id)

    await completeOrderViaApi(mainApi, main, [{ variationId: latte.id, quantity: 3 }], 1000)

    const mainAfter = await stockByName(mainApi, main)
    const uptownAfter = await stockByName(ownerApi, owner, LOCATIONS.uptown.id)
    expect(mainAfter['Espresso Beans']).toBe(mainBefore['Espresso Beans'] - 3 * LATTE_M.beans)
    expect(mainAfter['Whole Milk']).toBe(mainBefore['Whole Milk'] - 3 * LATTE_M.milk)
    expect(mainAfter['Paper Cups']).toBe(mainBefore['Paper Cups'] - 3 * LATTE_M.cups)
    expect(mainAfter['Tea Bags']).toBe(mainBefore['Tea Bags']) // not in the recipe
    expect(uptownAfter).toEqual(uptownBefore) // other location untouched
    await mainApi.dispose()
    await ownerApi.dispose()
  })

  test('a cancelled or never-completed order does not consume stock', async () => {
    const api = await newApi()
    const s = await apiLogin(api, USERS.cashier)
    const latte = await findVariation(api, s, LATTE_M.sku)
    const before = await stockByName(api, s)

    const id = randomUUID()
    const created = await api.post('/api/v1/orders', {
      headers: bearer(s),
      data: { id, items: [{ itemVariationId: latte.id, quantity: 2, modifierIds: [], note: null }] },
    })
    expect(created.status()).toBe(201)
    expect(await stockByName(api, s)).toEqual(before) // Open order: nothing deducted yet
    const cancel = await api.post(`/api/v1/orders/${id}/cancel`, { headers: bearer(s) })
    expect(cancel.ok(), await cancel.text()).toBeTruthy()
    expect(await stockByName(api, s)).toEqual(before)
    await api.dispose()
  })

  test('insufficient stock is rejected with 409 and leaves stock unchanged', async () => {
    const api = await newApi()
    const s = await apiLogin(api, USERS.cashier)
    const tea = await findVariation(api, s, 'TEA-R') // needs 1 Tea Bag each; Main never has anywhere near 100
    const before = await stockByName(api, s)
    expect(before['Tea Bags']).toBeLessThan(100)

    const id = randomUUID()
    await api.post('/api/v1/orders', {
      headers: bearer(s),
      data: { id, items: [{ itemVariationId: tea.id, quantity: 100, modifierIds: [], note: null }] },
    })
    const done = await api.post(`/api/v1/orders/${id}/complete`, {
      headers: { ...bearer(s), 'Idempotency-Key': randomUUID() },
      data: { paymentMethod: 'cash', amountTendered: 100_000 },
    })
    expect(done.status()).toBe(409)
    const problem = (await done.json()) as { ingredient?: string }
    expect(problem.ingredient).toBe('Tea Bags')
    expect(await stockByName(api, s)).toEqual(before)
    await api.post(`/api/v1/orders/${id}/cancel`, { headers: bearer(s) })
    await api.dispose()
  })

  test('replaying the same Idempotency-Key does not charge or deduct twice', async () => {
    const api = await newApi()
    const s = await apiLogin(api, USERS.cashier)
    const latte = await findVariation(api, s, LATTE_M.sku)
    const before = await stockByName(api, s)
    const id = randomUUID()
    const key = randomUUID()
    const create = await api.post('/api/v1/orders', {
      headers: bearer(s),
      data: { id, items: [{ itemVariationId: latte.id, quantity: 1, modifierIds: [], note: null }] },
    })
    expect(create.status()).toBe(201)
    const complete = () =>
      api.post(`/api/v1/orders/${id}/complete`, {
        headers: { ...bearer(s), 'Idempotency-Key': key },
        data: { paymentMethod: 'cash', amountTendered: 500 },
      })
    const first = await complete()
    const second = await complete()
    expect(first.status()).toBe(200)
    expect(second.status()).toBe(200)
    expect(((await second.json()) as { payments: unknown[] }).payments).toHaveLength(1)
    const after = await stockByName(api, s)
    expect(after['Espresso Beans']).toBe(before['Espresso Beans'] - LATTE_M.beans)
    await api.dispose()
  })

  test('a manager sees the deduction and the Sale ledger entry in the Inventory UI', async ({ page }) => {
    const api = await newApi()
    const s = await apiLogin(api, USERS.manager, LOCATIONS.main.id)
    const latte = await findVariation(api, s, LATTE_M.sku)
    await completeOrderViaApi(api, s, [{ variationId: latte.id, quantity: 1 }], 200)
    const stock = await stockByName(api, s)
    await api.dispose()

    await loginViaUi(page, USERS.manager, { locationName: LOCATIONS.main.name })
    await page.getByRole('link', { name: 'Inventory' }).click()
    const row = page.locator('.stock-row', { hasText: 'Paper Cups' })
    await expect(row).toBeVisible()
    await expect(row.locator('.stock-qty strong')).toContainText(`${stock['Paper Cups']} each`)
    await row.getByRole('button', { name: 'Ledger' }).click()
    const ledger = page.getByRole('dialog', { name: /Stock ledger: Paper Cups/ })
    await expect(ledger.locator('.ledger-row').first()).toContainText('Sale')
    await expect(ledger.locator('.ledger-row').first()).toContainText('-1 each')
  })

  test('a manager can restock and the on-hand quantity increases; waste needs a note', async ({ page }) => {
    await loginViaUi(page, USERS.manager, { locationName: LOCATIONS.main.name })
    await page.getByRole('link', { name: 'Inventory' }).click()
    const row = page.locator('.stock-row', { hasText: 'Butter Croissant' })
    const before = Number((await row.locator('.stock-qty strong').innerText()).replace(/[^\d.]/g, ''))

    await row.getByRole('button', { name: 'Restock' }).click()
    const dialog = page.getByRole('dialog', { name: 'Restock Butter Croissant' })
    await dialog.getByRole('button', { name: 'Record restock' }).click()
    await expect(dialog.getByText('Enter a number')).toBeVisible()
    await dialog.getByLabel(/^Quantity received/).fill('5')
    await dialog.getByRole('button', { name: 'Record restock' }).click()
    await expect(dialog).toBeHidden()
    await expect(row.locator('.stock-qty strong')).toContainText(`${before + 5} each`)

    await row.getByRole('button', { name: 'Adjust / waste' }).click()
    const adj = page.getByRole('dialog', { name: /Adjust \/ waste/ })
    await adj.getByLabel(/^Quantity/).fill('1')
    await adj.getByRole('button', { name: 'Save' }).click()
    await expect(adj.getByText('Please add a note explaining why')).toBeVisible()
    await adj.getByLabel(/^Note/).fill('Dropped on floor')
    await adj.getByRole('button', { name: 'Save' }).click()
    await expect(adj).toBeHidden()
    await expect(row.locator('.stock-qty strong')).toContainText(`${before + 4} each`)
  })

  test('a cashier cannot open the inventory page or call the restock API (403)', async ({ page }) => {
    await loginViaUi(page, USERS.cashier)
    await expect(page.getByRole('link', { name: 'Inventory' })).toHaveCount(0)
    await page.goto('/admin/inventory')
    await expect(page.getByRole('heading', { name: 'Not allowed' })).toBeVisible()

    const api = await newApi()
    const s = await apiLogin(api, USERS.cashier)
    const anon = await api.get('/api/v1/ingredients')
    expect(anon.status()).toBe(401)
    const res = await api.post('/api/v1/restocks', {
      headers: bearer(s),
      data: { ingredientId: randomUUID(), quantity: 1 },
    })
    expect(res.status()).toBe(403)
    await api.dispose()
  })
})
