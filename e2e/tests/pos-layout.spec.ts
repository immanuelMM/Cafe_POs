import { expect, test, type Locator, type Page } from '@playwright/test'
import { apiLogin, bearer, newApi, type Session } from '../support/api'
import { LOCATIONS, USERS } from '../support/env'
import { expectFullyInViewport, expectNoHorizontalScroll } from '../support/layout'
import { loginViaUi } from '../support/ui'

/**
 * M3 POS redesign checks. Layout-related, so they run on every project (viewport = project).
 * "phone" is the only compact project: chip row of categories, order in a bottom sheet.
 */
const tile = (page: Page, name: string) => page.locator('button.tile', { hasText: name })
const cartOf = (page: Page) => page.getByRole('complementary', { name: 'Current order' })

async function addPlain(page: Page, name: string) {
  await tile(page, name).click()
}

/** Open the option sheet for an item, choose a variation, add it. */
async function addVariant(page: Page, name: string, variation: RegExp) {
  await tile(page, name).click()
  const sheet = page.getByRole('dialog', { name })
  await sheet.getByRole('radio', { name: variation }).check()
  await sheet.getByRole('button', { name: /^Add to order/ }).click()
  await expect(sheet).toBeHidden()
}

/** Seven distinct lines: Iced Tea, Croissant, Latte S/M/L, Espresso single/double. */
async function fillCart(page: Page) {
  await addPlain(page, 'Iced Tea')
  await addPlain(page, 'Butter Croissant')
  await addVariant(page, 'Latte', /Small/)
  await addVariant(page, 'Latte', /Medium/)
  await addVariant(page, 'Latte', /Large/)
  await addVariant(page, 'Espresso', /Single/)
  await addVariant(page, 'Espresso', /Double/)
}

/** The docked cart on tablets; on the phone the order lives in a bottom sheet opened from the bar. */
async function openCart(page: Page): Promise<Locator> {
  const bar = page.getByRole('button', { name: 'Open current order' })
  if (await bar.isVisible()) await bar.click()
  const cart = cartOf(page)
  await expect(cart).toBeVisible()
  return cart
}

async function posAsCashier(page: Page) {
  await loginViaUi(page, USERS.cashier)
  await expect(tile(page, 'Iced Tea')).toBeVisible()
}

test.describe('POS layout (M3) @layout', () => {
  test('categories: vertical column on tablets, chip row on phone; picking one filters the tiles', async ({ page }, testInfo) => {
    await posAsCashier(page)
    const tabs = page.getByRole('tablist', { name: 'Categories' })
    await expect(tabs).toBeVisible()
    const phone = testInfo.project.name === 'phone'
    await expect(tabs).toHaveAttribute('aria-orientation', phone ? 'horizontal' : 'vertical')
    const boxes = await tabs.getByRole('tab').evaluateAll((els) => els.map((e) => e.getBoundingClientRect()))
    expect(boxes.length).toBeGreaterThanOrEqual(4)
    if (phone) {
      expect(new Set(boxes.map((b) => Math.round(b.top))).size, 'chips share one row').toBe(1)
    } else {
      expect(new Set(boxes.map((b) => Math.round(b.left))).size, 'tabs share one column').toBe(1)
    }

    await tabs.getByRole('tab', { name: 'Pastries' }).click()
    await expect(tile(page, 'Butter Croissant')).toBeVisible()
    await expect(tile(page, 'Iced Tea')).toBeHidden()
    await tabs.getByRole('tab', { name: 'Tea' }).click()
    await expect(tile(page, 'Iced Tea')).toBeVisible()
    await expect(tile(page, 'Butter Croissant')).toBeHidden()
    await tabs.getByRole('tab', { name: 'All' }).click()
    await expect(tile(page, 'Butter Croissant')).toBeVisible()
  })

  test('tiles: enough columns, no horizontal overflow, even with a full cart', async ({ page }) => {
    await posAsCashier(page)
    const vp = page.viewportSize()!
    const xs = await page.locator('button.tile').evaluateAll((els) => els.map((e) => Math.round(e.getBoundingClientRect().left)))
    const columns = new Set(xs).size
    // 1133x744 (small tablet) must still get 3 columns; the phone gets 2.
    expect(columns, `tile columns at ${vp.width}`).toBeGreaterThanOrEqual(vp.width >= 800 ? 3 : 2)
    await expectNoHorizontalScroll(page, 'POS empty')
    await fillCart(page)
    await expectNoHorizontalScroll(page, 'POS with 7 lines')
  })

  test('Charge is fully in the viewport with one cart line', async ({ page }) => {
    await posAsCashier(page)
    await addPlain(page, 'Iced Tea')
    const cart = await openCart(page)
    await expectFullyInViewport(page, cart.getByRole('button', { name: /^Charge/ }), 'Charge')
  })

  test('Charge stays fully in the viewport with 7 cart lines', async ({ page }) => {
    await posAsCashier(page)
    await fillCart(page)
    const cart = await openCart(page)
    await expect(cart.getByRole('listitem')).toHaveCount(7)
    await expectFullyInViewport(page, cart.getByRole('button', { name: /^Charge/ }), 'Charge')
  })

  test('Clear asks first with more than 3 lines: Keep keeps them, Yes clear empties the order', async ({ page }) => {
    await posAsCashier(page)
    await fillCart(page)
    const cart = await openCart(page)
    await expect(cart.getByRole('listitem')).toHaveCount(7)
    await cart.getByRole('button', { name: 'Clear', exact: true }).click()
    await expect(cart.getByText('Clear all 7 lines?')).toBeVisible()
    await cart.getByRole('button', { name: 'Keep' }).click()
    await expect(cart.getByText(/Clear all/)).toBeHidden()
    await expect(cart.getByRole('listitem')).toHaveCount(7)

    await cart.getByRole('button', { name: 'Clear', exact: true }).click()
    await cart.getByRole('button', { name: 'Yes, clear' }).click()
    await expect(cart.getByRole('listitem')).toHaveCount(0)
    await expect(cart.getByText('Tap an item to start an order.')).toBeVisible()
  })

  test('Clear with 3 lines or fewer clears immediately (no question)', async ({ page }) => {
    await posAsCashier(page)
    await addPlain(page, 'Iced Tea')
    await addPlain(page, 'Butter Croissant')
    const cart = await openCart(page)
    await cart.getByRole('button', { name: 'Clear', exact: true }).click()
    await expect(cart.getByText(/Clear all/)).toBeHidden()
    await expect(cart.getByRole('listitem')).toHaveCount(0)
  })
})

/**
 * The seed has no required option group, so the test creates its own item with a required modifier list through the API
 * and deletes the item afterwards (modifier lists cannot be deleted, so the list is found by name and reused on re-runs).
 */
test.describe('POS option sheet: required group (M3) @layout', () => {
  const ITEM = 'E2E Required Item'
  const LIST = 'E2E Required Size'
  let itemId: string | undefined

  async function api(): Promise<{ s: Session; ctx: Awaited<ReturnType<typeof newApi>> }> {
    const ctx = await newApi()
    return { ctx, s: await apiLogin(ctx, USERS.manager, LOCATIONS.main.id) }
  }

  test.beforeAll(async () => {
    const { ctx, s } = await api()
    const h = bearer(s)
    const lists = (await (await ctx.get('/api/v1/modifier-lists', { headers: h })).json()) as { id: string; name: string }[]
    let listId = lists.find((l) => l.name === LIST)?.id
    if (!listId) {
      const res = await ctx.post('/api/v1/modifier-lists', {
        headers: h,
        data: {
          name: LIST,
          minSelections: 1,
          maxSelections: 1,
          modifiers: [
            { name: 'Hot', priceDelta: 0 },
            { name: 'Iced', priceDelta: 10 },
          ],
        },
      })
      expect(res.ok(), await res.text()).toBeTruthy()
      listId = ((await res.json()) as { id: string }).id
    }
    const cats = (await (await ctx.get('/api/v1/categories', { headers: h })).json()) as { id: string; name: string }[]
    const old = ((await (await ctx.get('/api/v1/items?pageSize=100', { headers: h })).json()) as { data: { id: string; name: string }[] }).data
    for (const o of old.filter((i) => i.name === ITEM)) await ctx.delete(`/api/v1/items/${o.id}`, { headers: h })
    const res = await ctx.post('/api/v1/items', {
      headers: h,
      data: {
        categoryId: cats.find((c) => c.name === 'Coffee')!.id,
        name: ITEM,
        modifierListIds: [listId],
        variations: [{ name: 'Regular', sku: 'E2E-REQ-' + Date.now(), price: 50 }],
      },
    })
    expect(res.ok(), await res.text()).toBeTruthy()
    itemId = ((await res.json()) as { id: string }).id
    await ctx.dispose()
  })

  test.afterAll(async () => {
    if (!itemId) return
    const { ctx, s } = await api()
    await ctx.delete(`/api/v1/items/${itemId}`, { headers: bearer(s) })
    await ctx.dispose()
  })

  test('Add is blocked until the required group is satisfied', async ({ page }) => {
    await posAsCashier(page)
    await tile(page, ITEM).click()
    const sheet = page.getByRole('dialog', { name: ITEM })
    const add = sheet.getByRole('button', { name: /^Add to order/ })
    await expect(sheet.locator('.req-missing')).toHaveText('Required')
    await expect(add).toBeDisabled()
    await expect(sheet.getByRole('alert')).toContainText(LIST)
    await sheet.getByRole('radio', { name: /Iced/ }).check()
    await expect(add).toBeEnabled()
    await expect(add).toContainText('60') // 50 + 10 Iced
    await add.click()
    await expect(sheet).toBeHidden()
    const cart = await openCart(page)
    await expect(cart).toContainText('+ Iced')
  })
})
