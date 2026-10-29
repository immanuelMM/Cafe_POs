import fs from 'node:fs'
import path from 'node:path'
import { expect, test, type BrowserContext, type Page } from '@playwright/test'
import { apiLogin, bearer, newApi } from '../support/api'
import { LOCATIONS, USERS } from '../support/env'
import { diffAgainstBaseline, formatSummary, resetCompareDirs, VISUAL_DIR, VISUAL_MODE } from '../support/visual'

/**
 * M1 visual baseline (docs/plan/tablet-ui-plan.md section 5 M1). Skipped unless VISUAL_MODE is set:
 *   node visual.mjs capture   writes PNGs to e2e/.visual-baseline-m1/  (git-ignored, Windows-rendered, same machine only)
 *   node visual.mjs compare   re-captures into _current/, pixel-diffs against the baseline, writes _diff/, prints %.
 * Volatile server data (order numbers/timestamps, stock levels, clock-in state) is rewritten in flight to fixed values.
 */
const VIEWPORTS = [
  { tag: '390x844', width: 390, height: 844 },
  { tag: '820x1180', width: 820, height: 1180 },
  { tag: '1180x820', width: 1180, height: 820 },
  { tag: '1366x1024', width: 1366, height: 1024 },
] as const

const FIXED_ISO = '2026-01-15T03:30:00.000Z'
const PIN = '4821'

test.skip(!VISUAL_MODE, 'set VISUAL_MODE=capture|compare (node visual.mjs) to run the M1 visual baseline')

/** Stable data for the endpoints whose real content changes from run to run. */
async function stabilise(context: BrowserContext) {
  await context.route(/\/api\/v1\/orders(\?[^/]*)?$/, async (route) => {
    if (route.request().method() !== 'GET') return route.fallback()
    const res = await route.fetch()
    const body = await res.json()
    const data = (body.data as Record<string, unknown>[]).slice(0, 8).map((o, i) => ({
      ...o,
      orderNumber: 1001 + i,
      createdAt: FIXED_ISO,
      completedAt: o.completedAt ? FIXED_ISO : null,
      canceledAt: o.canceledAt ? FIXED_ISO : null,
    }))
    await route.fulfill({ response: res, json: { ...body, data, page: 1, totalCount: data.length, totalPages: 1 } })
  })
  await context.route(/\/api\/v1\/orders\/[^/]+\/receipt$/, async (route) => {
    const res = await route.fetch()
    const body = await res.json()
    await route.fulfill({
      response: res,
      json: {
        ...body,
        orderNumber: 1001,
        completedAt: FIXED_ISO,
        cashierName: 'Casey Cashier',
        lines: [
          { description: 'Latte (Medium)', modifiers: 'Oat Milk', quantity: 2, unitPrice: 180, lineTotal: 360 },
          { description: 'Iced Tea', modifiers: null, quantity: 1, unitPrice: 100, lineTotal: 100 },
          { description: 'Butter Croissant', modifiers: null, quantity: 1, unitPrice: 95, lineTotal: 95 },
        ],
        vatRatePercent: 12,
        vatableSales: 495.54,
        vatAmount: 59.46,
        total: 555,
        paymentMethod: 'cash',
        amountTendered: 600,
        changeGiven: 45,
      },
    })
  })
  const stock: Record<string, number> = {
    'Butter Croissant': 20,
    'Espresso Beans': 2000,
    'Paper Cups': 100,
    'Tea Bags': 12,
    'Whole Milk': 8000,
  }
  await context.route(/\/api\/v1\/ingredients(\?[^/]*)?$/, async (route) => {
    if (route.request().method() !== 'GET') return route.fallback()
    const res = await route.fetch()
    const rows = (await res.json()) as { name: string; lowStockThreshold: number; quantityOnHand: number | null }[]
    const out = rows.map((r) => {
      const q = stock[r.name] ?? r.quantityOnHand
      return { ...r, quantityOnHand: q, isLowStock: q !== null && q <= r.lowStockThreshold }
    })
    await route.fulfill({ response: res, json: out })
  })
  await context.route(/\/api\/v1\/timeclock\/staff$/, async (route) => {
    const res = await route.fetch()
    const rows = (await res.json()) as Record<string, unknown>[]
    await route.fulfill({
      response: res,
      json: rows.map((r) => ({ ...r, hasPin: r.displayName === USERS.cashier.name, clockedIn: false, openSince: null })),
    })
  })
}

async function shot(page: Page, tag: string, name: string) {
  await page.evaluate(() => document.fonts.ready)
  await page.mouse.move(0, 0)
  const file = `${name}__${tag}.png`
  const buf = await page.screenshot({ fullPage: true, animations: 'disabled', caret: 'hide' })
  if (VISUAL_MODE === 'capture') {
    fs.mkdirSync(VISUAL_DIR, { recursive: true })
    fs.writeFileSync(path.join(VISUAL_DIR, file), buf)
  } else {
    const row = diffAgainstBaseline(file, buf)
    console.log(`${row.percent.toFixed(4)}%  ${file}${row.note ? `  [${row.note}]` : ''}`)
  }
}

async function settle(page: Page) {
  await page.waitForLoadState('networkidle')
  await page.evaluate(() => document.fonts.ready)
}

async function signIn(page: Page, creds: { email: string; password: string }, location?: string) {
  await page.goto('/login')
  await page.getByLabel('Email').fill(creds.email)
  await page.getByLabel('Password').fill(creds.password)
  await page.getByRole('button', { name: 'Sign in' }).click()
  if (location) {
    await page.getByLabel('Location').selectOption({ label: location })
    await page.getByRole('button', { name: 'Sign in' }).click()
  }
  await expect(page.getByRole('button', { name: 'Log out' })).toBeVisible()
}

test.beforeAll(async () => {
  if (VISUAL_MODE === 'compare') resetCompareDirs()
  // Known PIN for the cashier so the PIN pad shows its real "has PIN" state (same step the time clock spec uses).
  const api = await newApi()
  const s = await apiLogin(api, USERS.cashier)
  const r = await api.put('/api/v1/timeclock/my-pin', { headers: bearer(s), data: { pin: PIN } })
  expect(r.status()).toBe(204)
  await api.dispose()
})
test.afterAll(() => {
  if (VISUAL_MODE === 'compare') console.log(formatSummary())
})

for (const vp of VIEWPORTS) {
  test.describe(`visual M1 ${vp.tag} @visual-m1`, () => {
    test.use({ viewport: { width: vp.width, height: vp.height }, locale: 'en-PH', timezoneId: 'Asia/Manila' })
    test.describe.configure({ mode: 'serial' })

    test('login and location picker', async ({ page, context }) => {
      await stabilise(context)
      await page.goto('/login')
      await settle(page)
      await shot(page, vp.tag, 'login')
      await page.getByLabel('Email').fill(USERS.manager.email)
      await page.getByLabel('Password').fill(USERS.manager.password)
      await page.getByRole('button', { name: 'Sign in' }).click()
      await expect(page.getByLabel('Location')).toBeVisible()
      await settle(page)
      await shot(page, vp.tag, 'login-location-picker')
    })

    test('pos, cash payment, time clock (cashier)', async ({ page, context }) => {
      await stabilise(context)
      await signIn(page, USERS.cashier)
      await expect(page.locator('button.tile', { hasText: 'Iced Tea' })).toBeVisible()
      await settle(page)
      await shot(page, vp.tag, 'pos-empty')

      await page.locator('button.tile', { hasText: 'Iced Tea' }).click()
      await page.locator('button.tile', { hasText: 'Butter Croissant' }).click()
      await page.locator('button.tile', { hasText: 'Butter Croissant' }).click()
      await page.locator('button.tile', { hasText: 'Latte' }).click()
      const sheet = page.getByRole('dialog', { name: 'Latte' })
      await sheet.getByRole('radio', { name: /Medium/ }).check()
      await sheet.getByRole('radio', { name: /Oat Milk/ }).check()
      await settle(page)
      await shot(page, vp.tag, 'pos-option-sheet')
      await sheet.getByRole('button', { name: /^Add/ }).click()
      await expect(sheet).toBeHidden()

      const cart = page.getByRole('complementary', { name: 'Current order' })
      const bar = page.getByRole('button', { name: 'Open current order' })
      if (await bar.isVisible()) {
        await shot(page, vp.tag, 'pos-cart-bar')
        await bar.click()
      }
      await expect(cart).toBeVisible()
      await expect(cart).toContainText('Total (4 items)')
      await shot(page, vp.tag, 'pos-cart')

      await cart.getByRole('button', { name: /^Charge/ }).click()
      const pay = page.getByRole('dialog', { name: 'Cash payment' })
      await pay.getByLabel(/Cash tendered/).fill('100')
      await expect(pay.getByRole('alert')).toBeVisible()
      await shot(page, vp.tag, 'payment-dialog-short')
      await pay.getByLabel(/Cash tendered/).fill('1000')
      await expect(pay.getByRole('button', { name: /Complete payment/ })).toBeEnabled()
      await shot(page, vp.tag, 'payment-dialog')

      await page.goto('/timeclock')
      const tile = page.getByRole('button', { name: new RegExp(USERS.cashier.name) })
      await expect(tile).toBeEnabled()
      await settle(page)
      await shot(page, vp.tag, 'timeclock')
      await tile.click()
      const dialog = page.getByRole('dialog')
      await dialog.getByRole('button', { name: '4', exact: true }).click()
      await dialog.getByRole('button', { name: '8', exact: true }).click()
      await shot(page, vp.tag, 'timeclock-pinpad')
    })

    test('orders, receipt, inventory, catalog (manager)', async ({ page, context }) => {
      await stabilise(context)
      await signIn(page, USERS.manager, LOCATIONS.main.name)
      await page.goto('/orders')
      await expect(page.getByRole('heading', { name: 'Order history' })).toBeVisible()
      await expect(page.getByText('1001').first()).toBeVisible()
      await settle(page)
      await shot(page, vp.tag, 'orders')

      const receiptHref = await page.locator('a[href$="/receipt"]').first().getAttribute('href')
      expect(receiptHref).toBeTruthy()
      await page.goto(receiptHref!)
      await expect(page.getByRole('article')).toContainText('Latte')
      await settle(page)
      await shot(page, vp.tag, 'receipt')

      await page.goto('/admin/inventory')
      await expect(page.getByText('Tea Bags').first()).toBeVisible()
      await settle(page)
      await shot(page, vp.tag, 'inventory')

      await page.goto('/admin/catalog')
      await expect(page.getByText('Iced Tea').first()).toBeVisible()
      await settle(page)
      await shot(page, vp.tag, 'catalog')
    })

    test('staff / users (owner)', async ({ page, context }) => {
      await stabilise(context)
      await signIn(page, USERS.owner)
      await page.goto('/admin/users')
      await expect(page.getByText(USERS.cashier.name).first()).toBeVisible()
      await settle(page)
      await shot(page, vp.tag, 'users')
    })
  })
}
