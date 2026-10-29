import { expect, test } from '@playwright/test'
import { LOCATIONS, USERS } from '../support/env'
import { loginViaUi } from '../support/ui'

test.describe('owner across locations', () => {
  test('owner signs in to "All locations" and must pick a location before using the POS @smoke', async ({ page }) => {
    await loginViaUi(page, USERS.owner)
    await expect(page.getByRole('heading', { name: 'Choose a location' })).toBeVisible()
    await expect(page.getByRole('complementary', { name: 'Current order' })).toHaveCount(0)
    const switcher = page.getByRole('combobox', { name: 'Active location' })
    await expect(switcher).toHaveValue('')
    await expect(switcher.locator('option', { hasText: 'All locations' })).toHaveCount(1)

    await page.getByRole('button', { name: LOCATIONS.main.name }).click()
    await expect(page.getByRole('region', { name: 'Menu' })).toBeVisible()
    await expect(switcher).toHaveValue(LOCATIONS.main.id)
  })

  test('owner switches between locations from the header and the data follows', async ({ page }) => {
    await loginViaUi(page, USERS.owner)
    const switcher = page.getByRole('combobox', { name: 'Active location' })
    await switcher.selectOption(LOCATIONS.main.id)
    await page.getByRole('link', { name: 'Inventory' }).click()
    await expect(page.locator('.stock-row').first()).toBeVisible()
    const mainBeans = await page
      .locator('.stock-row', { hasText: 'Espresso Beans' })
      .locator('.stock-qty strong')
      .innerText()

    await switcher.selectOption(LOCATIONS.uptown.id)
    await expect(switcher).toHaveValue(LOCATIONS.uptown.id)
    await expect
      .poll(async () =>
        page.locator('.stock-row', { hasText: 'Espresso Beans' }).locator('.stock-qty strong').innerText(),
      )
      .not.toBe(mainBeans)
  })

  test('in the All locations view the owner reads across locations but cannot write', async ({ page }) => {
    await loginViaUi(page, USERS.owner)
    // Orders: cross-location with a location tag per order.
    await page.getByRole('link', { name: 'Orders' }).click()
    await expect(page.locator('.filters').getByLabel('Location')).toBeVisible()
    // Inventory: catalog only, writes disabled.
    await page.getByRole('link', { name: 'Inventory' }).click()
    await expect(page.getByText(/Showing the ingredient catalog only/)).toBeVisible()
    await expect(page.getByRole('button', { name: '+ New ingredient' })).toBeDisabled()
    await expect(page.getByRole('button', { name: 'Restock' }).first()).toBeDisabled()
    // Time clock needs a location.
    await page.getByRole('link', { name: 'Time clock' }).click()
    await expect(page.getByText(/works per location/)).toBeVisible()
  })

  test('owner-only pages: Locations is available to the owner, hidden and blocked for a manager', async ({ page }) => {
    await loginViaUi(page, USERS.owner)
    await page.getByRole('link', { name: 'Locations' }).click()
    await expect(page.getByRole('heading', { name: 'Locations' })).toBeVisible()
    await page.getByRole('button', { name: 'Log out' }).click()

    await loginViaUi(page, USERS.manager, { locationName: LOCATIONS.main.name })
    await expect(page.getByRole('link', { name: 'Locations' })).toHaveCount(0)
    await page.goto('/admin/locations')
    await expect(page.getByRole('heading', { name: 'Not allowed' })).toBeVisible()
    // Managers also have no Staff or Settings (Admin only).
    await expect(page.getByRole('link', { name: 'Staff' })).toHaveCount(0)
  })
})
