import { defineConfig, devices } from '@playwright/test'
import { BASE_URL } from './support/env'

/**
 * Runs against the real stack (docker compose: PostgreSQL + API + nginx-served frontend), seeded by the API's
 * DevDataSeeder. Tests share one database, so they run serially in one worker and assert on deltas, not absolutes.
 *
 * Projects = the target viewports of docs/plan/tablet-ui-plan.md section 1.6 (CSS px, W x H):
 *  - tablet-landscape (1180x820) is the primary design size and runs the FULL functional suite.
 *  - every other project runs only specs tagged @layout or @a11y, to keep CI time sane.
 * Run one project at a time (`npx playwright test --project=phone`); CI runs them in sequence.
 * Reports/output go to PW_REPORT_DIR / PW_OUTPUT_DIR so sequential runs do not overwrite each other.
 */
const LAYOUT_ONLY = /@layout|@a11y/
const touch = { ...devices['Desktop Chrome'], hasTouch: true }

export default defineConfig({
  testDir: './tests',
  globalSetup: './global-setup.ts',
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  timeout: 45_000,
  expect: { timeout: 10_000 },
  outputDir: process.env.PW_OUTPUT_DIR ?? 'test-results',
  reporter: process.env.CI
    ? [['list'], ['html', { open: 'never', outputFolder: process.env.PW_REPORT_DIR ?? 'playwright-report' }]]
    : [['list']],
  use: {
    baseURL: BASE_URL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    { name: 'tablet-landscape', use: { ...touch, viewport: { width: 1180, height: 820 } } },
    { name: 'large', grep: LAYOUT_ONLY, use: { ...touch, viewport: { width: 1366, height: 1024 } } },
    { name: 'small-landscape', grep: LAYOUT_ONLY, use: { ...touch, viewport: { width: 1133, height: 744 } } },
    { name: 'tablet-portrait', grep: LAYOUT_ONLY, use: { ...touch, viewport: { width: 820, height: 1180 } } },
    { name: 'phone', grep: LAYOUT_ONLY, use: { ...touch, isMobile: true, viewport: { width: 390, height: 844 } } },
  ],
})
