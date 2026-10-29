import { randomUUID } from 'node:crypto'
import { expect, request as pwRequest, type APIRequestContext } from '@playwright/test'
import { API_URL } from './env'

export interface Session {
  accessToken: string
  user: { id: string; role: string; isOwner: boolean; location: { id: string; name: string } | null }
}

/** A fresh, cookie-isolated API context (so one user's refresh cookie never leaks into another's). */
export const newApi = () => pwRequest.newContext({ baseURL: API_URL })

export async function apiLogin(
  api: APIRequestContext,
  creds: { email: string; password: string },
  locationId?: string,
): Promise<Session> {
  const res = await api.post('/api/v1/auth/login', { data: { ...creds, locationId: locationId ?? null } })
  expect(res.status(), `login ${creds.email}: ${await res.text()}`).toBe(200)
  return (await res.json()) as Session
}

export const bearer = (s: Session) => ({ Authorization: `Bearer ${s.accessToken}` })

export interface Variation {
  id: string
  name: string
  sku: string
  price: number
}
export interface ItemDto {
  id: string
  name: string
  variations: Variation[]
}

export async function findVariation(api: APIRequestContext, s: Session, sku: string): Promise<Variation> {
  const res = await api.get('/api/v1/items?pageSize=100', { headers: bearer(s) })
  expect(res.ok()).toBeTruthy()
  const items = ((await res.json()) as { data: ItemDto[] }).data
  const v = items.flatMap((i) => i.variations).find((x) => x.sku === sku)
  if (!v) throw new Error(`Seeded variation ${sku} not found`)
  return v
}

export interface Ingredient {
  id: string
  name: string
  quantityOnHand: number | null
  isLowStock: boolean | null
}

/** Stock by ingredient name. For an Owner session without an active location pass `locationId`. */
export async function stockByName(
  api: APIRequestContext,
  s: Session,
  locationId?: string,
): Promise<Record<string, number>> {
  const q = locationId ? `?locationId=${locationId}` : ''
  const res = await api.get(`/api/v1/ingredients${q}`, { headers: bearer(s) })
  expect(res.ok(), await res.text()).toBeTruthy()
  const rows = (await res.json()) as Ingredient[]
  return Object.fromEntries(rows.map((r) => [r.name, r.quantityOnHand ?? Number.NaN]))
}

/** Create + complete a cash order through the API. Returns the completed order. */
export async function completeOrderViaApi(
  api: APIRequestContext,
  s: Session,
  lines: { variationId: string; quantity: number; modifierIds?: string[] }[],
  tendered: number,
) {
  const id = randomUUID()
  const created = await api.post('/api/v1/orders', {
    headers: bearer(s),
    data: {
      id,
      items: lines.map((l) => ({
        itemVariationId: l.variationId,
        quantity: l.quantity,
        modifierIds: l.modifierIds ?? [],
        note: null,
      })),
    },
  })
  expect(created.status(), await created.text()).toBe(201)
  const done = await api.post(`/api/v1/orders/${id}/complete`, {
    headers: { ...bearer(s), 'Idempotency-Key': randomUUID() },
    data: { paymentMethod: 'cash', amountTendered: tendered },
  })
  expect(done.status(), await done.text()).toBe(200)
  return (await done.json()) as { id: string; orderNumber: number; total: number; locationId: string }
}

/** Restock (as a manager at the session's location) so repeated runs against the same DB never run dry. */
export async function topUp(api: APIRequestContext, s: Session, minimums: Record<string, number>) {
  const res = await api.get('/api/v1/ingredients', { headers: bearer(s) })
  expect(res.ok()).toBeTruthy()
  const rows = (await res.json()) as { id: string; name: string; quantityOnHand: number }[]
  for (const r of rows) {
    const min = minimums[r.name]
    if (min !== undefined && r.quantityOnHand < min) {
      const post = await api.post('/api/v1/restocks', {
        headers: bearer(s),
        data: { ingredientId: r.id, quantity: min - r.quantityOnHand, note: 'E2E top-up' },
      })
      expect(post.ok(), await post.text()).toBeTruthy()
    }
  }
}
