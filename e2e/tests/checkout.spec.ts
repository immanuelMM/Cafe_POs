import { expect, test } from '@playwright/test'
import { USERS } from '../support/env'
import { loginViaUi, peso } from '../support/ui'

test.describe('POS checkout', () => {
  test('cashier rings up an order with modifiers, pays cash and gets a receipt @smoke', async ({ page }) => {
    await loginViaUi(page, USERS.cashier)

    // Iced Tea has one variation and no modifiers: one tap adds it.
    await page.getByRole('button', { name: /Iced Tea/ }).click()

    // Latte opens the option sheet: Medium (150) + Oat Milk (+30) = 180.
    await page.getByRole('button', { name: /^Latte/ }).click()
    const sheet = page.getByRole('dialog', { name: 'Latte' })
    await sheet.getByRole('radio', { name: /Medium/ }).check()
    await sheet.getByRole('radio', { name: /Oat Milk/ }).check()
    await expect(sheet.getByRole('button', { name: /^Add/ })).toContainText(peso(180))
    await sheet.getByRole('button', { name: /^Add/ }).click()

    const cart = page.getByRole('complementary', { name: 'Current order' })
    await expect(cart).toContainText('Total (2 items)')
    await expect(cart).toContainText('+ Oat Milk')
    // 100 + 180 = 280
    await cart.getByRole('button', { name: `Charge ${peso(280)}` }).click()

    const pay = page.getByRole('dialog', { name: 'Cash payment' })
    const complete = pay.getByRole('button', { name: /Complete payment/ })
    await expect(complete).toBeDisabled()
    await pay.getByLabel(/Cash tendered/).fill('100')
    await expect(pay.getByRole('alert')).toContainText('Tendered amount is less than the total.')
    await expect(complete).toBeDisabled()
    await pay.getByLabel(/Cash tendered/).fill('500')
    await expect(pay.getByText('Change', { exact: true }).locator('xpath=following-sibling::dd')).toHaveText(peso(220))
    await complete.click()

    const paid = page.getByRole('dialog', { name: /Order #\d+ paid/ })
    await expect(paid).toBeVisible()
    await expect(paid.getByRole('status').first()).toContainText(peso(220)) // change due
    // The receipt rendered inside the dialog shows the lines, VAT-inclusive totals and tender.
    const receipt = paid.getByRole('article')
    await expect(receipt).toContainText('Iced Tea')
    await expect(receipt).toContainText('Latte')
    await expect(receipt).toContainText(peso(280))
    await expect(receipt).toContainText(peso(500))
    await expect(receipt).toContainText(peso(220))
    await expect(receipt).toContainText('Casey Cashier')

    await paid.getByRole('button', { name: 'New order' }).click()
    await expect(cart.getByText('Tap an item to start an order.')).toBeVisible()
  })

  test('the completed order appears in history and its receipt page matches', async ({ page }) => {
    await loginViaUi(page, USERS.cashier)
    await page.getByRole('button', { name: /Butter Croissant/ }).click()
    await page.getByRole('button', { name: /^Charge/ }).click()
    const pay = page.getByRole('dialog', { name: 'Cash payment' })
    await pay.getByRole('button', { name: 'Exact' }).click()
    await pay.getByRole('button', { name: /Complete payment/ }).click()
    const paid = page.getByRole('dialog', { name: /Order #\d+ paid/ })
    const title = await paid.getAttribute('aria-label')
    const number = /#(\d+)/.exec(title ?? '')![1]
    await paid.getByRole('button', { name: 'New order' }).click()

    await page.getByRole('link', { name: 'Orders' }).click()
    const card = page.locator('.order-card', { hasText: `#${number}` })
    await expect(card).toBeVisible()
    await expect(card).toContainText('Completed')
    await card.locator('summary').click()
    await card.getByRole('link', { name: 'View receipt' }).click()
    await expect(page).toHaveURL(/\/orders\/.+\/receipt$/)
    const receipt = page.getByRole('article')
    await expect(receipt).toContainText(number)
    await expect(receipt).toContainText('Butter Croissant')
    await expect(receipt).toContainText(peso(95))
    // VAT is included, not added: 95 * 12 / 112 = 10.18
    await expect(receipt).toContainText(peso(10.18))
  })

  test('an empty cart cannot be charged and quantity controls update the total', async ({ page }) => {
    await loginViaUi(page, USERS.cashier)
    const cart = page.getByRole('complementary', { name: 'Current order' })
    await expect(cart.getByRole('button', { name: /^Charge/ })).toBeDisabled()
    await page.getByRole('button', { name: /Iced Tea/ }).click()
    await cart.getByRole('button', { name: 'Increase Iced Tea' }).click()
    await expect(cart.getByRole('button', { name: `Charge ${peso(200)}` })).toBeEnabled()
    await cart.getByRole('button', { name: 'Remove Iced Tea' }).click()
    await expect(cart.getByRole('button', { name: /^Charge/ })).toBeDisabled()
  })
})
