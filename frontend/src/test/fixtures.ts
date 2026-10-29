import type { components } from '../api/schema'
import { API_ORIGIN } from '../lib/apiClient'
import { useAuthStore } from '../features/auth/authStore'
import { useCartStore } from '../features/pos/cartStore'

type S = components['schemas']

/** Absolute URL for an API path as the generated client requests it, e.g. `url('/api/v1/items')`. */
export const url = (path: string) => `${API_ORIGIN}${path}`

export const MAIN: S['LocationRoleDto'] = { locationId: 'loc-main', locationName: 'Main Branch', role: 'Manager' }
export const UPTOWN: S['LocationRoleDto'] = { locationId: 'loc-up', locationName: 'Uptown Branch', role: 'Manager' }

export function makeUser(over: Partial<S['UserInfoDto']> = {}): S['UserInfoDto'] {
  return {
    id: 'u1',
    email: 'cashier@cafe.local',
    displayName: 'Casey Cashier',
    role: 'Cashier',
    isOwner: false,
    location: { id: MAIN.locationId, name: MAIN.locationName },
    locations: [{ ...MAIN, role: 'Cashier' }],
    ...over,
  }
}

export const makeAuth = (over: Partial<S['UserInfoDto']> = {}): S['AuthResponse'] => ({
  accessToken: 'token-1',
  expiresAt: '2099-01-01T00:00:00Z',
  user: makeUser(over),
})

export function signIn(over: Partial<S['UserInfoDto']> = {}) {
  const r = makeAuth(over)
  useAuthStore.setState({ user: r.user, accessToken: r.accessToken, ready: true })
  return r.user
}

export function resetStores() {
  useAuthStore.setState({ user: null, accessToken: null, ready: true })
  useCartStore.setState({ lines: [], checkout: null, sheetOpen: false })
}

export const variation = (over: Partial<S['VariationDto']> = {}): S['VariationDto'] => ({
  id: 'var-1',
  itemId: 'item-1',
  name: 'Regular',
  sku: 'SKU-1',
  price: 100,
  active: true,
  effectivePrice: 100,
  ...over,
})

export const makeItem = (over: Partial<S['ItemDto']> = {}): S['ItemDto'] => ({
  id: 'item-1',
  categoryId: 'cat-1',
  name: 'Iced Tea',
  description: null,
  sortOrder: 1,
  active: true,
  variations: [variation()],
  modifierListIds: [],
  imageUrl: null,
  ...over,
})

export const paged = <T>(data: T[]) => ({ data, page: 1, pageSize: 100, totalCount: data.length, totalPages: 1 })

export const makeIngredient = (over: Partial<S['IngredientDto']> = {}): S['IngredientDto'] => ({
  id: 'ing-1',
  name: 'Whole Milk',
  unit: 'ml',
  lowStockThreshold: 100,
  active: true,
  quantityOnHand: 1000,
  isLowStock: false,
  ...over,
})

export const makeOrder = (over: Partial<S['OrderDto']> = {}): S['OrderDto'] => ({
  id: 'order-1',
  orderNumber: 7,
  status: 'Completed',
  locationId: MAIN.locationId,
  cashierId: 'u1',
  cashierName: 'Casey Cashier',
  note: null,
  vatRatePercent: 12,
  total: 100,
  vatableSales: 89.29,
  vatAmount: 10.71,
  createdAt: '2026-01-01T02:00:00Z',
  completedAt: '2026-01-01T02:01:00Z',
  canceledAt: null,
  items: [
    {
      id: 'oi-1',
      itemVariationId: 'var-1',
      itemName: 'Iced Tea',
      variationName: 'Regular',
      sku: 'SKU-1',
      unitPrice: 100,
      quantity: 1,
      lineTotal: 100,
      note: null,
      modifiers: [],
    },
  ],
  payments: [
    {
      id: 'p1',
      method: 'cash',
      status: 'Completed',
      amount: 100,
      amountTendered: 200,
      changeGiven: 100,
      createdAt: '2026-01-01T02:01:00Z',
    },
  ],
  ...over,
})
