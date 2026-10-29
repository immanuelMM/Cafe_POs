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

/**
 * Navigate to a page by its nav label the way the current viewport's user would: the rail/tab-bar link when it is
 * visible, otherwise (phones, manage pages) through the More sheet. Wait for page content in `main`, never for nav text.
 */
export async function openPage(page: Page, label: string) {
  const link = page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: label })
  if (await link.isVisible()) {
    await link.click()
    return
  }
  await page.getByRole('button', { name: 'More' }).click()
  await page.getByRole('dialog', { name: 'More' }).getByRole('link', { name: label }).click()
}
