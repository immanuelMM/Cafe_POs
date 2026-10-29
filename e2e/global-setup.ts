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

  // Tests consume stock; keep the shared DB usable across repeated runs. Tea Bags is deliberately NOT topped up:
  // it is the seeded low-stock item at Main and the insufficient-stock test relies on it.
  const levels = { 'Espresso Beans': 2000, 'Whole Milk': 8000, 'Paper Cups': 100, 'Butter Croissant': 20 }
  for (const loc of [LOCATIONS.main, LOCATIONS.uptown]) {
    const api = await newApi()
    const s = await apiLogin(api, USERS.manager, loc.id)
    await topUp(api, s, levels)
    await api.dispose()
  }
}
