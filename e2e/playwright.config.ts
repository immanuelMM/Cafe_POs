import { defineConfig, devices } from '@playwright/test'
import { BASE_URL } from './support/env'

/**
 * Runs against the real stack (docker compose: PostgreSQL + API + nginx-served frontend), seeded by the API's
 * DevDataSeeder. Tests share one database, so they run serially in one worker and assert on deltas, not absolutes.
 */
export default defineConfig({
  testDir: './tests',
  globalSetup: './global-setup.ts',
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  timeout: 45_000,
  expect: { timeout: 10_000 },
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : [['list']],
  use: {
    baseURL: BASE_URL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    ...devices['Desktop Chrome'],
    viewport: { width: 1280, height: 800 },
  },
  projects: [{ name: 'chromium' }],
})
