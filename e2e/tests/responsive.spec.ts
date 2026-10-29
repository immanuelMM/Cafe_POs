import { expect, test } from '@playwright/test'
import { USERS } from '../support/env'
import { loginViaUi } from '../support/ui'

const VIEWPORTS = [
  { name: 'mobile 390', width: 390, height: 844, wide: false },
  { name: 'tablet 768', width: 768, height: 1024, wide: true },
  { name: 'desktop 1280', width: 1280, height: 800, wide: true },
] as const

for (const vp of VIEWPORTS) {
  test.describe(`POS layout at ${vp.name}`, () => {
    test.use({ viewport: { width: vp.width, height: vp.height } })

    test(`menu, cart and checkout work and nothing overflows horizontally @smoke`, async ({ page }) => {
      await loginViaUi(page, USERS.cashier)
      const tile = page.getByRole('button', { name: /Iced Tea/ })
      await expect(tile).toBeVisible()

      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
      expect(overflow, 'page is wider than the viewport').toBeLessThanOrEqual(1)

      await tile.click()
      const cart = page.getByRole('complementary', { name: 'Current order' })
      const bar = page.getByRole('button', { name: 'Open current order' })
      if (vp.wide) {
        await expect(cart).toBeVisible() // side panel docked at >= 768px (CSS breakpoint)
        await expect(bar).toBeHidden()
      } else {
        await expect(bar).toBeVisible() // bottom bar opens the order sheet
        await bar.click()
        await expect(cart).toBeVisible()
      }
      await cart.getByRole('button', { name: /^Charge/ }).click()
      const pay = page.getByRole('dialog', { name: 'Cash payment' })
      await expect(pay).toBeVisible()
      const box = await pay.boundingBox()
      expect(box!.x).toBeGreaterThanOrEqual(0)
      expect(box!.x + box!.width).toBeLessThanOrEqual(vp.width + 1)
      await expect(pay.getByRole('button', { name: /Complete payment/ })).toBeVisible()
    })
  })
}
