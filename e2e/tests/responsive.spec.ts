import { expect, test } from '@playwright/test'
import { USERS } from '../support/env'
import { expectFullyInViewport, expectNoHorizontalScroll } from '../support/layout'
import { loginViaUi } from '../support/ui'

/**
 * Runs once per Playwright project (1180x820, 1366x1024, 1133x744, 820x1180, 390x844; see playwright.config.ts), so the
 * viewport comes from the project, not from this file. Assertions match the CURRENT UI: the order panel docks as a side
 * panel at >= 768px and is a bottom-bar + sheet below. Later milestones (M2 shell, M3 tap sizes, M4 POS) update them.
 */
test.describe('POS layout @layout @smoke', () => {
  test('menu, cart and checkout work and nothing overflows horizontally', async ({ page }) => {
    const vp = page.viewportSize()!
    const wide = vp.width >= 768 // current CSS breakpoint for the docked cart
    await loginViaUi(page, USERS.cashier)
    const tile = page.getByRole('button', { name: /Iced Tea/ })
    await expect(tile).toBeVisible()
    await expectNoHorizontalScroll(page, 'POS menu')

    await tile.click()
    const cart = page.getByRole('complementary', { name: 'Current order' })
    const bar = page.getByRole('button', { name: 'Open current order' })
    if (wide) {
      await expect(cart).toBeVisible()
      await expect(bar).toBeHidden()
    } else {
      await expect(bar).toBeVisible()
      await bar.click()
      await expect(cart).toBeVisible()
    }
    await cart.getByRole('button', { name: /^Charge/ }).click()
    const pay = page.getByRole('dialog', { name: 'Cash payment' })
    await expect(pay).toBeVisible()
    // The dialog must fit horizontally, and the pay button must be reachable on screen.
    const box = await pay.boundingBox()
    expect(box!.x).toBeGreaterThanOrEqual(0)
    expect(box!.x + box!.width).toBeLessThanOrEqual(vp.width + 1)
    await expectFullyInViewport(page, pay.getByRole('button', { name: /Complete payment/ }), 'Complete payment')
  })
})
