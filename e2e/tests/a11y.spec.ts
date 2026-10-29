import AxeBuilder from '@axe-core/playwright'
import { expect, test, type Page } from '@playwright/test'
import { LOCATIONS, USERS } from '../support/env'
import { loginViaUi } from '../support/ui'

/** WCAG 2.1 A/AA rules; any violation fails the test and the report lists the offending nodes. */
async function scan(page: Page) {
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()
  expect(results.passes.length, 'axe did not run any rules').toBeGreaterThan(0)
  const summary = results.violations.map(
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

  test('POS payment dialog', async ({ page }) => {
    await loginViaUi(page, USERS.cashier)
    await page.getByRole('button', { name: /Iced Tea/ }).click()
    await page.getByRole('button', { name: /^Charge/ }).click()
    await expect(page.getByRole('dialog', { name: 'Cash payment' })).toBeVisible()
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
    await page.getByRole('link', { name: 'Inventory' }).click()
    await expect(page.locator('.stock-row').first()).toBeVisible()
    await scan(page)
  })

  test('catalog (manager)', async ({ page }) => {
    await loginViaUi(page, USERS.manager, { locationName: LOCATIONS.main.name })
    await page.getByRole('link', { name: 'Catalog' }).click()
    await expect(page.getByRole('heading', { name: 'Catalog' })).toBeVisible()
    await expect(page.locator('.rows .row').first()).toBeVisible()
    await scan(page)
  })

  test('staff (owner)', async ({ page }) => {
    await loginViaUi(page, USERS.owner)
    await page.getByRole('link', { name: 'Staff' }).click()
    await expect(page.getByRole('heading', { name: 'Staff' })).toBeVisible()
    await expect(page.locator('.row-item').first()).toBeVisible()
    await scan(page)
  })
})
