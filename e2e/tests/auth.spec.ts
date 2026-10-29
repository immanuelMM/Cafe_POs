import { expect, test } from '@playwright/test'
import { LOCATIONS, USERS } from '../support/env'
import { loginViaUi } from '../support/ui'

test.describe('login, logout and session refresh', () => {
  test('cashier signs in and lands on the POS at their only location @smoke', async ({ page }) => {
    await loginViaUi(page, USERS.cashier)
    await expect(page).toHaveURL(/\/pos$/)
    await expect(page.getByRole('region', { name: 'Menu' })).toBeVisible()
    await expect(page.getByText(LOCATIONS.main.name)).toBeVisible()
    // Single-location staff cannot switch location.
    await expect(page.getByRole('combobox', { name: 'Active location' })).toHaveCount(0)
  })

  test('wrong password shows an error and stays on /login', async ({ page }) => {
    await page.goto('/login')
    await page.getByLabel('Email').fill(USERS.cashier.email)
    await page.getByLabel('Password').fill('definitely-wrong')
    await page.getByRole('button', { name: 'Sign in' }).click()
    await expect(page.getByRole('alert')).toContainText('Incorrect email or password.')
    await expect(page).toHaveURL(/\/login$/)
  })

  test('client-side validation blocks an empty form', async ({ page }) => {
    await page.goto('/login')
    await page.getByRole('button', { name: 'Sign in' }).click()
    await expect(page.getByText('Email is required')).toBeVisible()
    await expect(page.getByText('Password is required')).toBeVisible()
  })

  test('anonymous deep links redirect to /login and return after sign-in', async ({ page }) => {
    await page.goto('/orders')
    await expect(page).toHaveURL(/\/login$/)
    await page.getByLabel('Email').fill(USERS.cashier.email)
    await page.getByLabel('Password').fill(USERS.cashier.password)
    await page.getByRole('button', { name: 'Sign in' }).click()
    await expect(page).toHaveURL(/\/orders$/)
  })

  test('a multi-location manager must choose a location at login', async ({ page }) => {
    await page.goto('/login')
    await page.getByLabel('Email').fill(USERS.manager.email)
    await page.getByLabel('Password').fill(USERS.manager.password)
    await page.getByRole('button', { name: 'Sign in' }).click()
    await expect(page.getByRole('alert')).toContainText('Choose a location to sign in to.')
    await page.getByLabel('Location').selectOption({ label: LOCATIONS.uptown.name })
    await page.getByRole('button', { name: 'Sign in' }).click()
    await expect(page.getByRole('button', { name: 'Log out' })).toBeVisible()
    await expect(page.getByRole('combobox', { name: 'Active location' })).toHaveValue(LOCATIONS.uptown.id)
  })

  test('the session survives a page reload via the HttpOnly refresh cookie @smoke', async ({ page, context }) => {
    await loginViaUi(page, USERS.cashier)
    // The access token is memory-only: a reload can only stay signed in through POST /auth/refresh.
    const refreshed = page.waitForResponse((r) => r.url().endsWith('/api/v1/auth/refresh') && r.status() === 200)
    await page.reload()
    await refreshed
    await expect(page.getByRole('button', { name: 'Log out' })).toBeVisible()
    const cookies = await context.cookies()
    const refresh = cookies.find((c) => /refresh/i.test(c.name))
    expect(refresh?.httpOnly).toBe(true)
    // And no JWT leaks into web storage.
    const storage = await page.evaluate(
      () => JSON.stringify({ ...localStorage }) + JSON.stringify({ ...sessionStorage }),
    )
    expect(storage).not.toMatch(/eyJ[A-Za-z0-9_-]{10,}/)
  })

  test('an expired access token (401) is transparently refreshed and the request retried', async ({ page }) => {
    await loginViaUi(page, USERS.cashier)
    let rejected = false
    await page.route('**/api/v1/orders?*', async (route) => {
      if (!rejected) {
        rejected = true
        await route.fulfill({ status: 401, contentType: 'application/json', body: '{"title":"Unauthorized"}' })
      } else await route.continue()
    })
    const refresh = page.waitForResponse((r) => r.url().endsWith('/api/v1/auth/refresh') && r.status() === 200)
    await page.getByRole('link', { name: 'Orders' }).click()
    await refresh
    await expect(page.getByRole('heading', { name: 'Order history' })).toBeVisible()
    await expect(page.getByText(/\d+ orders/)).toBeVisible() // the retried request succeeded
    expect(rejected).toBe(true)
  })

  test('logout ends the session: the refresh cookie no longer restores it', async ({ page }) => {
    await loginViaUi(page, USERS.cashier)
    await page.getByRole('button', { name: 'Log out' }).click()
    await expect(page).toHaveURL(/\/login$/)
    await page.goto('/pos')
    await expect(page).toHaveURL(/\/login$/)
  })
})
