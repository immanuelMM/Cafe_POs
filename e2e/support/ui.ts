import { expect, type Page } from '@playwright/test'

export async function loginViaUi(
  page: Page,
  creds: { email: string; password: string },
  opts: { locationName?: string } = {},
) {
  await page.goto('/login')
  await page.getByLabel('Email').fill(creds.email)
  await page.getByLabel('Password').fill(creds.password)
  await page.getByRole('button', { name: 'Sign in' }).click()
  if (opts.locationName) {
    await page.getByLabel('Location').selectOption({ label: opts.locationName })
    await page.getByRole('button', { name: 'Sign in' }).click()
  }
  await expect(page.getByRole('button', { name: 'Log out' })).toBeVisible()
}

export const peso = (n: number) =>
  new Intl.NumberFormat('en-PH', { style: 'currency', currency: 'PHP' }).format(n).replace(/ /g, ' ')
