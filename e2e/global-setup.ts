import { apiLogin, newApi, topUp } from './support/api'
import { API_URL, BASE_URL, LOCATIONS, USERS } from './support/env'

/** Fail fast with an actionable message when the compose stack is not running. */
async function waitFor(name: string, url: string) {
  const deadline = Date.now() + 60_000
  let last = ''
  while (Date.now() < deadline) {
    try {
      const r = await fetch(url)
      if (r.ok) return
      last = `HTTP ${r.status}`
    } catch (e) {
      last = (e as Error).message
    }
    await new Promise((r) => setTimeout(r, 1000))
  }
  throw new Error(
    `${name} is not reachable at ${url} (${last}). Start the stack first: ` +
      '`cd e2e && npm run stack:up` (docker compose with the E2E override).',
  )
}

export default async function globalSetup() {
  await waitFor('API', `${API_URL}/health`)
  await waitFor('Frontend', BASE_URL)

  // Tests consume stock; keep the shared DB usable across repeated runs. Tea Bags must stay LOW at Main (the isolation
  // and insufficient-stock tests rely on it; seeded at 8, threshold 10) but each Iced Tea checkout uses one, so repeated
  // runs drained it to 0 and broke checkout. Top it back up to 9 at Main only (still below the threshold). Uptown keeps its 60.
  const levels: Record<string, number> = { 'Espresso Beans': 2000, 'Whole Milk': 8000, 'Paper Cups': 100, 'Butter Croissant': 20 }
  for (const loc of [LOCATIONS.main, LOCATIONS.uptown]) {
    const api = await newApi()
    const s = await apiLogin(api, USERS.manager, loc.id)
    await topUp(api, s, loc === LOCATIONS.main ? { ...levels, 'Tea Bags': 9 } : levels)
    await api.dispose()
  }
}
