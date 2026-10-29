import { expect, test, type Page } from '@playwright/test'
import { LOCATIONS, USERS } from '../support/env'
import {
  expectBodyDoesNotScroll,
  expectFullyInViewport,
  expectMinFontSize,
  expectNoHorizontalScroll,
  expectTapTargets,
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
  // Wait on content inside main, never on nav text (nav labels are hidden in the compact tab bar).
  await expect(page.getByRole('main').getByText(s.ready).first()).toBeVisible()
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

  // ---- M2 (app shell): hard assertions ----

  // The shell fills the viewport and only inner panels scroll.
  for (const s of SCREENS.filter((x) => x.name === 'POS' || x.name === 'Orders')) {
    test(`${s.name}: body does not scroll (M2)`, async ({ page }) => {
      await open(page, s)
      await expectBodyDoesNotScroll(page, s.name)
    })
  }

  // Rail on tablets, bottom tab bar on phones; Log out and the location switcher are reachable at every size.
  test('shell: rail or tab bar, Log out and Active location are visible (M2)', async ({ page }, testInfo) => {
    // Manager: assigned to two locations, so the switcher is a real combobox (single-location staff see a label).
    await loginViaUi(page, USERS.manager, { locationName: LOCATIONS.main.name })
    await page.goto('/pos')
    await expect(page.getByRole('main').getByText('Iced Tea').first()).toBeVisible()
    const phone = testInfo.project.name === 'phone'
    const vp = page.viewportSize()!
    const rail = phone ? page.getByRole('navigation', { name: 'Main' }) : page.locator('aside.rail')
    await expect(rail).toBeVisible()
    const box = (await rail.boundingBox())!
    if (phone) {
      // Bottom tab bar: pinned to the bottom edge, spans the width.
      expect(box.y + box.height).toBeGreaterThanOrEqual(vp.height - 1)
      expect(box.width).toBeGreaterThanOrEqual(vp.width * 0.8)
    } else {
      // Left rail: pinned to the left edge, full height, narrow.
      expect(box.x).toBeLessThanOrEqual(1)
      expect(box.height).toBeGreaterThanOrEqual(vp.height - 1)
      expect(box.width).toBeLessThan(vp.width / 3)
    }
    await expect(page.getByRole('button', { name: 'Log out' })).toBeVisible()
    await expect(page.getByRole('combobox', { name: 'Active location' })).toBeVisible()
  })

  // The owner has every manage link: on tablets the rail must fit without scrolling (tightest at 1133x744).
  test('shell: the owner rail needs no scrolling (M2)', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name === 'phone', 'phones use the tab bar and the More sheet')
    await loginViaUi(page, USERS.owner)
    await expect(page.getByRole('main')).toBeVisible()
    const overflow = await page.evaluate(() =>
      Array.from(document.querySelectorAll<HTMLElement>('aside.rail, aside.rail .rail-nav')).map(
        (e) => e.scrollHeight - e.clientHeight,
      ),
    )
    expect(overflow.every((d) => d <= 1), `rail overflow px: ${overflow.join(',')}`).toBe(true)
    await expectFullyInViewport(page, page.getByRole('button', { name: 'Log out' }), 'Log out')
  })

  // nginx has no .webmanifest mime type by default; frontend/nginx.conf sets it explicitly.
  test('PWA manifest is served as application/manifest+json (M2)', async ({ request }) => {
    const res = await request.get('/manifest.webmanifest')
    expect(res.status()).toBe(200)
    expect(res.headers()['content-type']).toContain('application/manifest+json')
    expect(await res.json()).toHaveProperty('name')
  })

  // ---- M3 (POS): tap sizes and type scale. Scoped to POS; other screens get these at M4-M9. ----

  for (const withLine of [false, true]) {
    const state = withLine ? 'with a cart line' : 'empty cart'
    test(`POS (${state}): every control is at least 48px and tile text is readable (M3)`, async ({ page }, testInfo) => {
      await open(page, SCREENS[0])
      if (withLine) {
        await page.locator('button.tile', { hasText: 'Iced Tea' }).click()
        await expect(page.getByRole('complementary', { name: 'Current order' }).or(page.getByRole('button', { name: 'Open current order' })).first()).toBeAttached()
      }
      await expectTapTargets(page, MIN_TAP_PX)
      // Plan: 18px on tablets, 16px on the phone.
      await expectMinFontSize(page.locator('button.tile .tile-name'), testInfo.project.name === 'phone' ? 16 : 18, 'tile names')
    })
  }

  // Controls inside the open cart sheet on phones are checked too (the sheet is part of POS).
  test('POS: the order sheet controls are at least 48px (M3)', async ({ page }) => {
    await open(page, SCREENS[0])
    await page.locator('button.tile', { hasText: 'Iced Tea' }).click()
    const bar = page.getByRole('button', { name: 'Open current order' })
    if (await bar.isVisible()) await bar.click()
    await expect(page.getByRole('complementary', { name: 'Current order' })).toBeVisible()
    await expectTapTargets(page, MIN_TAP_PX)
  })
})
