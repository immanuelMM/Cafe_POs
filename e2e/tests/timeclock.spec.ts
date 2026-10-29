import { expect, test } from '@playwright/test'
import { apiLogin, bearer, newApi } from '../support/api'
import { USERS } from '../support/env'
import { loginViaUi } from '../support/ui'

const PIN = '4821'

async function enterPin(page: import('@playwright/test').Page, pin: string) {
  const dialog = page.getByRole('dialog')
  for (const d of pin) await dialog.getByRole('button', { name: d, exact: true }).click()
  await dialog.getByRole('button', { name: 'OK' }).click()
}

test.describe('time clock', () => {
  test.beforeAll(async () => {
    // Give the cashier a known PIN (write-only: the API never returns it).
    const api = await newApi()
    const s = await apiLogin(api, USERS.cashier)
    const res = await api.put('/api/v1/timeclock/my-pin', { headers: bearer(s), data: { pin: PIN } })
    expect(res.status(), await res.text()).toBe(204)
    await api.dispose()
  })

  test('staff punch in then out with their PIN; a wrong PIN is refused @smoke', async ({ page }) => {
    await loginViaUi(page, USERS.cashier)
    await page.getByRole('link', { name: 'Time clock' }).click()
    const tile = page.getByRole('button', { name: new RegExp(USERS.cashier.name) })
    await expect(tile).toBeEnabled()

    // Wrong PIN first.
    await tile.click()
    await enterPin(page, '0000')
    await expect(page.getByRole('dialog').getByRole('alert')).toContainText('Incorrect PIN')
    await page.keyboard.press('Escape')

    // Whatever state the shared DB is in, two correct punches must flip it twice: In/Out or Out/In.
    const wasIn = await tile.getByText(/^In since/).isVisible()
    const first = wasIn ? /Clocked OUT/ : /Clocked IN/
    const second = wasIn ? /Clocked IN/ : /Clocked OUT/

    await tile.click()
    await enterPin(page, PIN)
    await expect(page.locator('.clock-banner')).toContainText(first)
    await expect(page.locator('.clock-banner')).toBeHidden({ timeout: 10_000 }) // auto-dismisses

    await tile.click()
    await enterPin(page, PIN)
    await expect(page.locator('.clock-banner')).toContainText(second)
  })

  test('punching creates a time entry visible in the records for a manager', async ({ page }) => {
    // Ensure at least one entry: punch via API (toggle in, toggle out).
    const api = await newApi()
    const s = await apiLogin(api, USERS.cashier)
    const staff = (await (await api.get('/api/v1/timeclock/staff', { headers: bearer(s) })).json()) as {
      userId: string
      displayName: string
    }[]
    const me = staff.find((x) => x.displayName === USERS.cashier.name)!
    for (let i = 0; i < 2; i++) {
      const r = await api.post('/api/v1/timeclock/punch', { headers: bearer(s), data: { userId: me.userId, pin: PIN } })
      expect(r.ok(), await r.text()).toBeTruthy()
    }
    await api.dispose()

    await loginViaUi(page, USERS.manager, { locationName: 'Brew & Bean - Main Branch' })
    await page.goto('/timeclock/records')
    await expect(page.getByRole('cell', { name: USERS.cashier.name }).first()).toBeVisible()
    await expect(page.getByRole('region', { name: 'Totals' })).toContainText(USERS.cashier.name)
  })
})
