import { expect, test, type Locator, type Page } from '@playwright/test'
import { apiLogin, completeOrderViaApi, findVariation, newApi } from '../support/api'
import { USERS } from '../support/env'
import {
  expectBodyDoesNotScroll,
  expectFullyInViewport,
  expectMinFontSize,
  expectNoHorizontalScroll,
  expectTapTargets,
  MIN_TAP_PX,
} from '../support/layout'
import { loginViaUi, peso } from '../support/ui'

/**
 * M4 payment: cash keypad dialog, done state with auto-print, receipt page.
 * Functional tests run on tablet-landscape only (project grep keeps the others out unless tagged @layout/@a11y).
 * Butter Croissant (PHP 95) is used so the suite does not drain Tea Bags, which must stay low at Main.
 */
const tile = (page: Page, name: string) => page.locator('button.tile', { hasText: name })
const PRICE = 95

/** Count window.print() calls without opening a real print dialog. Must run before the app loads. */
async function stubPrint(page: Page) {
  await page.addInitScript(() => {
    ;(window as unknown as { __prints: number }).__prints = 0
    window.print = () => {
      ;(window as unknown as { __prints: number }).__prints++
    }
  })
}
const prints = (page: Page) => page.evaluate(() => (window as unknown as { __prints: number }).__prints)

async function openPayment(page: Page): Promise<Locator> {
  await loginViaUi(page, USERS.cashier)
  await tile(page, 'Butter Croissant').click()
  const bar = page.getByRole('button', { name: 'Open current order' })
  if (await bar.isVisible()) await bar.click()
  await page.getByRole('complementary', { name: 'Current order' }).getByRole('button', { name: /^Charge/ }).click()
  const pay = page.getByRole('dialog', { name: 'Cash payment' })
  await expect(pay).toBeVisible()
  return pay
}

const keys = (pay: Locator) => pay.getByRole('group', { name: 'Cash keypad' })
async function tap(pay: Locator, digits: string) {
  for (const d of digits) await keys(pay).getByRole('button', { name: d, exact: true }).click()
}
const tendered = (pay: Locator) => pay.getByLabel(/Cash tendered/)
const complete = (pay: Locator) => pay.getByRole('button', { name: /Complete payment/ })

test.describe('cash payment flow (M4)', () => {
  test('keypad digits and Backspace fill the tendered amount and show the change', async ({ page }) => {
    const pay = await openPayment(page)
    await expect(complete(pay)).toBeDisabled()
    await tap(pay, '55')
    await expect(tendered(pay)).toHaveValue('55')
    await keys(pay).getByRole('button', { name: 'Backspace' }).click()
    await expect(tendered(pay)).toHaveValue('5')
    await keys(pay).getByRole('button', { name: 'Backspace' }).click()
    await expect(tendered(pay)).toHaveValue('')
    await tap(pay, '200')
    await expect(tendered(pay)).toHaveValue('200')
    await expect(pay.getByText('Change', { exact: true }).locator('xpath=following-sibling::dd')).toHaveText(peso(200 - PRICE))
    await expect(complete(pay)).toBeEnabled()
  })

  test('a shortfall shows the error and blocks Complete; Exact fixes it', async ({ page }) => {
    const pay = await openPayment(page)
    await tap(pay, '5')
    await expect(pay.getByRole('alert')).toContainText('Tendered amount is less than the total.')
    await expect(complete(pay)).toBeDisabled()
    await pay.getByRole('button', { name: 'Exact' }).click()
    await expect(tendered(pay)).toHaveValue(String(PRICE))
    await expect(pay.getByRole('alert')).toHaveCount(0)
    await expect(complete(pay)).toBeEnabled()
  })

  test('quick-tender buttons are whole pesos (no decimals)', async ({ page }) => {
    const pay = await openPayment(page)
    const labels = await pay.locator('.quick-amounts button').allInnerTexts()
    expect(labels.length).toBe(4)
    expect(labels[0]).toBe('Exact')
    for (const l of labels.slice(1)) expect(l).toMatch(/^₱[\d,]+$/)
  })

  test('paying shows the done state, prints once, and New order empties the cart', async ({ page }) => {
    await stubPrint(page)
    const pay = await openPayment(page)
    await tap(pay, '500')
    await complete(pay).click()

    const paid = page.getByRole('dialog', { name: /Order #\d+ paid/ })
    await expect(paid).toBeVisible()
    await expect(paid.getByRole('status').first()).toContainText('Change due')
    await expect(paid.getByRole('status').first()).toContainText(peso(500 - PRICE))
    await expect(paid.getByRole('article')).toContainText('Butter Croissant')

    // Auto-print: exactly one window.print() once the receipt has rendered (the app delays it ~250ms).
    await expect.poll(() => prints(page), { message: 'auto-print should fire' }).toBe(1)
    await page.waitForTimeout(800) // negative check: no second print from re-renders/remounts
    expect(await prints(page)).toBe(1)

    await paid.getByRole('button', { name: 'Print again' }).click()
    expect(await prints(page)).toBe(2)

    await paid.getByRole('button', { name: 'New order' }).click()
    await expect(page.getByRole('complementary', { name: 'Current order' }).getByText('Tap an item to start an order.')).toBeVisible()
    expect(await prints(page)).toBe(2)
  })

  test('the receipt page does not print on open; its Print button prints once', async ({ page }) => {
    await loginViaUi(page, USERS.cashier)
    // Fresh page load below resets the counter, so only prints from the receipt page itself are counted.
    await page.goto('/orders')
    const href = await page.locator('a[href$="/receipt"]').first().getAttribute('href')
    expect(href).toBeTruthy()
    await stubPrint(page)
    await page.goto(href!)
    const receipt = page.getByRole('region', { name: 'Receipt' })
    await expect(receipt.getByRole('article')).toBeVisible()
    await page.waitForTimeout(800) // negative check: opening the page must not print
    expect(await prints(page)).toBe(0)
    await receipt.getByRole('button', { name: 'Print', exact: true }).click()
    expect(await prints(page)).toBe(1)
    await expect(receipt.getByRole('link', { name: /Back to orders/ }).or(receipt.getByRole('button', { name: /Back to orders/ }))).toBeVisible()
  })
})

test.describe('payment layout (M4) @layout', () => {
  test('cash dialog fits the viewport: dialog, every key and Complete visible, in view and at least 48px', async ({ page }) => {
    const pay = await openPayment(page)
    await expectFullyInViewport(page, pay, 'Cash payment dialog')
    for (const k of ['0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.', 'Backspace']) {
      await expectFullyInViewport(page, keys(pay).getByRole('button', { name: k, exact: true }), `key ${k}`)
    }
    await expectFullyInViewport(page, complete(pay), 'Complete payment')
    await expectTapTargets(page, MIN_TAP_PX, pay)
    await expectBodyDoesNotScroll(page, 'POS with payment dialog')
    await expectNoHorizontalScroll(page, 'payment dialog')
    await expectMinFontSize(keys(pay).getByRole('button'), 16, 'keypad keys')
  })

  test('done state: banner, New order and Print again are in view, big enough, and the page does not overflow', async ({ page }) => {
    await stubPrint(page)
    const pay = await openPayment(page)
    await tap(pay, '100')
    await complete(pay).click()
    const paid = page.getByRole('dialog', { name: /Order #\d+ paid/ })
    await expect(paid.getByRole('status').first()).toContainText('Change due')
    await expectFullyInViewport(page, paid.getByRole('button', { name: 'New order' }), 'New order')
    await expectTapTargets(page, MIN_TAP_PX, paid)
    await expectMinFontSize(paid.getByRole('status').first(), 16, 'change banner')
    await expectNoHorizontalScroll(page, 'done state')
    await expectBodyDoesNotScroll(page, 'done state')
  })

  test('receipt page: no horizontal overflow, Print in view, controls at least 48px', async ({ page }) => {
    const api = await newApi()
    const s = await apiLogin(api, USERS.cashier)
    const v = await findVariation(api, s, 'CRO-1')
    const order = await completeOrderViaApi(api, s, [{ variationId: v.id, quantity: 1 }], 500)
    await api.dispose()
    await loginViaUi(page, USERS.cashier)
    await page.goto(`/orders/${order.id}/receipt`)
    const receipt = page.getByRole('region', { name: 'Receipt' })
    await expect(receipt.getByRole('article')).toBeVisible()
    await expectNoHorizontalScroll(page, 'receipt page')
    await expectFullyInViewport(page, receipt.getByRole('button', { name: 'Print', exact: true }), 'Print')
    await expectTapTargets(page, MIN_TAP_PX, receipt.locator('.receipt-actions'))
  })

  test('small phone 375x667: the cash dialog still fits and Complete is reachable', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'phone', 'ad-hoc small-phone viewport, run inside the phone project')
    await page.setViewportSize({ width: 375, height: 667 })
    const pay = await openPayment(page)
    await expectFullyInViewport(page, pay, 'Cash payment dialog')
    await expectFullyInViewport(page, keys(pay).getByRole('button', { name: '0', exact: true }), 'key 0')
    await expectFullyInViewport(page, keys(pay).getByRole('button', { name: 'Backspace' }), 'Backspace')
    await expectFullyInViewport(page, complete(pay), 'Complete payment')
    await expectTapTargets(page, MIN_TAP_PX, pay)
    await expectNoHorizontalScroll(page, 'payment dialog 375x667')
  })
})
