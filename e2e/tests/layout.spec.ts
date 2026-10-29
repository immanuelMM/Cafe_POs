import { expect, test, type Page } from '@playwright/test'
import { LOCATIONS, USERS } from '../support/env'
import {
  expectBodyDoesNotScroll,
  expectMinFontSize,
  expectNoHorizontalScroll,
  expectTapTargets,
  findSmallTargets,
  MIN_TAP_PX,
} from '../support/layout'
import { loginViaUi } from '../support/ui'

/**
 * Layout checks that run on every project (viewport = project). The hard ones pass on the current UI.
 * The test.fixme ones state the plan's targets; each names the milestone that turns it into a hard assertion.
 */
const SCREENS = [
  { name: 'POS', path: '/pos', ready: 'Iced Tea', user: 'cashier' },
  { name: 'Orders', path: '/orders', ready: 'Order history', user: 'cashier' },
  { name: 'Time clock', path: '/timeclock', ready: 'Time clock', user: 'cashier' },
  { name: 'Inventory', path: '/admin/inventory', ready: 'Tea Bags', user: 'manager' },
  { name: 'Catalog', path: '/admin/catalog', ready: 'Catalog', user: 'manager' },
  { name: 'Staff', path: '/admin/users', ready: 'Staff', user: 'owner' },
] as const

async function open(page: Page, s: (typeof SCREENS)[number]) {
  await loginViaUi(page, USERS[s.user], s.user === 'manager' ? { locationName: LOCATIONS.main.name } : {})
  await page.goto(s.path)
  await expect(page.getByText(s.ready).first()).toBeVisible()
  await page.waitForLoadState('networkidle')
}

test.describe('layout @layout', () => {
  test('login has no horizontal scroll', async ({ page }) => {
    await page.goto('/login')
    await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible()
    await expectNoHorizontalScroll(page, 'login')
  })

  for (const s of SCREENS) {
    test(`${s.name}: no horizontal scroll`, async ({ page }) => {
      await open(page, s)
      await expectNoHorizontalScroll(page, s.name)
    })
  }

  // ---- Targets for later milestones (red today, so fixme; remove the fixme when the milestone lands) ----

  // M2 (app shell): the shell fills the viewport and only inner panels scroll.
  test.fixme('POS: body does not scroll (M2)', async ({ page }) => {
    await open(page, SCREENS[0])
    await expectBodyDoesNotScroll(page, 'POS')
  })

  // M3 (tap sizes): every control >= 48px.
  test.fixme('POS: every control is at least 48px (M3)', async ({ page }) => {
    await open(page, SCREENS[0])
    await expectTapTargets(page, MIN_TAP_PX)
  })

  // M3 (type scale): tile labels 20px per the plan; 16px is the floor asserted here.
  test.fixme('POS: tile labels are at least 16px (M3)', async ({ page }) => {
    await open(page, SCREENS[0])
    await expectMinFontSize(page.locator('button.tile'), 16, 'tile labels')
  })

  // Not an assertion: records the controls under 48px on the POS screen in the report, as M3's work list.
  test('POS: lists controls under the 48px tap floor (informational)', async ({ page }, testInfo) => {
    await open(page, SCREENS[0])
    const small = await findSmallTargets(page, MIN_TAP_PX)
    testInfo.annotations.push({
      type: 'small-targets',
      description: `${small.length}: ${small.map((s) => `${s.desc} ${s.width}x${s.height}`).join('; ')}`,
    })
  })
})
