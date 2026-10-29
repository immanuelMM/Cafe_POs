import AxeBuilder from '@axe-core/playwright'
import { expect, test, type Page } from '@playwright/test'
import { LOCATIONS, USERS } from '../support/env'
import { loginViaUi, openPage } from '../support/ui'

/**
 * Pre-existing violations the tablet redesign is expected to fix. Each rule is listed with the milestone that fixes
 * it; remove the entry when that milestone lands. Anything NOT listed here fails the test. Allowlisted findings are
 * still attached to the test as an annotation so they stay visible in the report.
 */
const KNOWN: Record<string, string> = {}

/** axe runs at the primary tablet size and on the phone only. */
test.beforeEach(({}, testInfo) => {
  test.skip(!['tablet-landscape', 'phone'].includes(testInfo.project.name), 'axe runs at tablet-landscape and phone')
})

/** WCAG 2.0/2.1/2.2 A and AA rules (wcag22aa adds target-size, SC 2.5.8). */
async function scan(page: Page) {
  const info = test.info()
  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
    .analyze()
  expect(results.passes.length, 'axe did not run any rules').toBeGreaterThan(0)
  for (const v of results.violations) {
    console.log(`AXE|${info.project.name}|${info.title}|${v.id}|${v.nodes.length}`)
    if (v.id in KNOWN) {
      info.annotations.push({
        type: 'axe-known',
        description: `${info.project.name} / ${info.title}: ${v.id} x${v.nodes.length} (fixed in ${KNOWN[v.id]})`,
      })
    }
  }
  const summary = results.violations.filter((v) => !(v.id in KNOWN)).map(
    (v) =>
      `${v.id} (${v.impact}): ${v.help}\n    ${v.nodes
        .map((n) => n.target.join(' '))
        .slice(0, 4)
        .join('\n    ')}`,
  )
  expect(summary, summary.join('\n')).toEqual([])
}

test.describe('accessibility (axe) @a11y', () => {
  test('login page', async ({ page }) => {
    await page.goto('/login')
    await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible()
    await scan(page)
  })

  test('POS menu and cart', async ({ page }) => {
    await loginViaUi(page, USERS.cashier)
    await expect(page.getByRole('button', { name: /Iced Tea/ })).toBeVisible()
    await scan(page)
  })

  test('POS option sheet', async ({ page }) => {
    await loginViaUi(page, USERS.cashier)
    await page.locator('button.tile', { hasText: 'Latte' }).click()
    await expect(page.getByRole('dialog', { name: 'Latte' })).toBeVisible()
    await scan(page)
  })

  test('POS payment dialog', async ({ page }) => {
    await loginViaUi(page, USERS.cashier)
    await page.getByRole('button', { name: /Iced Tea/ }).click()
    const bar = page.getByRole('button', { name: 'Open current order' })
    if (await bar.isVisible()) await bar.click() // phone: the order lives in a bottom sheet
    await page.getByRole('button', { name: /^Charge/ }).click()
    await expect(page.getByRole('dialog', { name: 'Cash payment' })).toBeVisible()
    await scan(page)
  })

  test('POS done state (paid, receipt shown)', async ({ page }) => {
    await loginViaUi(page, USERS.cashier)
    await page.locator('button.tile', { hasText: 'Butter Croissant' }).click()
    const bar = page.getByRole('button', { name: 'Open current order' })
    if (await bar.isVisible()) await bar.click()
    await page.getByRole('button', { name: /^Charge/ }).click()
    const pay = page.getByRole('dialog', { name: 'Cash payment' })
    await pay.getByRole('button', { name: 'Exact' }).click()
    await pay.getByRole('button', { name: /Complete payment/ }).click()
    const paid = page.getByRole('dialog', { name: /Order #\d+ paid/ })
    await expect(paid.getByRole('article')).toBeVisible()
    await scan(page)
  })

  test('order history', async ({ page }) => {
    await loginViaUi(page, USERS.cashier)
    await page.getByRole('link', { name: 'Orders' }).click()
    await expect(page.getByText(/\d+ orders/)).toBeVisible()
    await scan(page)
  })

  test('time clock', async ({ page }) => {
    await loginViaUi(page, USERS.cashier)
    await page.getByRole('link', { name: 'Time clock' }).click()
    await expect(page.getByRole('heading', { name: 'Time clock' })).toBeVisible()
    await scan(page)
  })

  test('inventory (manager)', async ({ page }) => {
    await loginViaUi(page, USERS.manager, { locationName: LOCATIONS.main.name })
    await openPage(page, 'Inventory')
    await expect(page.locator('.stock-row').first()).toBeVisible()
    await scan(page)
  })

  test('catalog (manager)', async ({ page }) => {
    await loginViaUi(page, USERS.manager, { locationName: LOCATIONS.main.name })
    await openPage(page, 'Catalog')
    await expect(page.getByRole('main').getByRole('heading', { name: 'Catalog' })).toBeVisible()
    await expect(page.locator('.rows .row').first()).toBeVisible()
    await scan(page)
  })

  test('staff (owner)', async ({ page }) => {
    await loginViaUi(page, USERS.owner)
    await openPage(page, 'Staff')
    await expect(page.getByRole('main').getByRole('heading', { name: 'Staff' })).toBeVisible()
    await expect(page.locator('.row-item').first()).toBeVisible()
    await scan(page)
  })
})
