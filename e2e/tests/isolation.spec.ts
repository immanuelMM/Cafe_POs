import { expect, test } from '@playwright/test'
import { apiLogin, bearer, completeOrderViaApi, findVariation, newApi } from '../support/api'
import { LOCATIONS, USERS } from '../support/env'
import { loginViaUi } from '../support/ui'

test.describe('multi-location isolation', () => {
  test('a user cannot read another location order, by list or by id @smoke', async () => {
    const uptownApi = await newApi()
    const mainApi = await newApi()
    const uptown = await apiLogin(uptownApi, USERS.uptownCashier)
    const main = await apiLogin(mainApi, USERS.cashier)
    expect(uptown.user.location?.id).toBe(LOCATIONS.uptown.id)
    expect(main.user.location?.id).toBe(LOCATIONS.main.id)

    const espresso = await findVariation(uptownApi, uptown, 'ESP-1')
    const order = await completeOrderViaApi(uptownApi, uptown, [{ variationId: espresso.id, quantity: 1 }], 100)
    expect(order.locationId).toBe(LOCATIONS.uptown.id)

    // The Main cashier's list never contains it...
    const list = await mainApi.get('/api/v1/orders?pageSize=100', { headers: bearer(main) })
    expect(list.ok()).toBeTruthy()
    const rows = ((await list.json()) as { data: { id: string; locationId: string }[] }).data
    expect(rows.every((o) => o.locationId === LOCATIONS.main.id)).toBe(true)
    expect(rows.map((o) => o.id)).not.toContain(order.id)

    // ...and fetching it directly (order and receipt) is refused.
    for (const path of [`/api/v1/orders/${order.id}`, `/api/v1/orders/${order.id}/receipt`]) {
      const r = await mainApi.get(path, { headers: bearer(main) })
      expect([403, 404], `${path} -> ${r.status()}`).toContain(r.status())
    }
    // Cancelling it from the wrong location must not work either.
    const cancel = await mainApi.post(`/api/v1/orders/${order.id}/cancel`, { headers: bearer(main) })
    expect([403, 404, 409], `cancel -> ${cancel.status()}`).toContain(cancel.status())
    await uptownApi.dispose()
    await mainApi.dispose()
  })

  test('a single-location user cannot sign in to or switch to an unassigned location', async () => {
    const api = await newApi()
    const login = await api.post('/api/v1/auth/login', {
      data: { ...USERS.cashier, locationId: LOCATIONS.uptown.id },
    })
    expect(login.status()).toBeGreaterThanOrEqual(400)
    expect(login.status()).toBeLessThan(500)

    const main = await apiLogin(api, USERS.cashier)
    const sw = await api.post('/api/v1/auth/switch-location', {
      headers: bearer(main),
      data: { locationId: LOCATIONS.uptown.id },
    })
    expect([400, 403, 404], `switch -> ${sw.status()}`).toContain(sw.status())
    await api.dispose()
  })

  test('the orders page shows only the active location; the manager sees Uptown orders after switching', async ({
    page,
  }) => {
    const uptownApi = await newApi()
    const uptown = await apiLogin(uptownApi, USERS.uptownCashier)
    const croissant = await findVariation(uptownApi, uptown, 'CRO-1')
    // 7 croissants = 665.00 at Uptown: a recognisable order that only exists there.
    const order = await completeOrderViaApi(uptownApi, uptown, [{ variationId: croissant.id, quantity: 7 }], 700)
    await uptownApi.dispose()
    const tag = `#${String(order.orderNumber).padStart(5, '0')}`

    await loginViaUi(page, USERS.manager, { locationName: LOCATIONS.main.name })
    await page.getByRole('link', { name: 'Orders' }).click()
    await expect(page.getByText(/\d+ orders/)).toBeVisible()
    await expect(page.locator('.order-card', { hasText: '7 × Butter Croissant' })).toHaveCount(0)

    await page.getByRole('combobox', { name: 'Active location' }).selectOption(LOCATIONS.uptown.id)
    await expect(page.getByRole('combobox', { name: 'Active location' })).toHaveValue(LOCATIONS.uptown.id)
    const card = page.locator('.order-card', { hasText: tag }).first()
    await expect(card).toBeVisible()
    await expect(card).toContainText('₱665.00')
  })

  test('stock levels are tracked independently per location', async () => {
    const ownerApi = await newApi()
    const owner = await apiLogin(ownerApi, USERS.owner)
    const main = await ownerApi.get(`/api/v1/ingredients?locationId=${LOCATIONS.main.id}`, { headers: bearer(owner) })
    const up = await ownerApi.get(`/api/v1/ingredients?locationId=${LOCATIONS.uptown.id}`, { headers: bearer(owner) })
    expect(main.ok() && up.ok()).toBeTruthy()
    const tea = async (res: typeof main) =>
      ((await res.json()) as { name: string; quantityOnHand: number; isLowStock: boolean }[]).find(
        (r) => r.name === 'Tea Bags',
      )!
    // Seeded: Tea Bags is 8 at Main (below its threshold of 10) but 60 at Uptown. Nothing in the suite restocks it.
    const m = await tea(main)
    const u = await tea(up)
    expect(m.isLowStock).toBe(true)
    expect(u.isLowStock).toBe(false)
    expect(u.quantityOnHand).toBeGreaterThan(m.quantityOnHand)
    await ownerApi.dispose()
  })
})
