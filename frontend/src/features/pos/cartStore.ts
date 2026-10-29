import { create } from 'zustand'
import { api } from '../../lib/api'
import { round2 } from '../../lib/format'
import { uuid } from '../../lib/uuid'

/** Backend allows 1-100 per order line. */
export const MAX_QTY = 100

export interface CartLine {
  key: string
  itemName: string
  variationId: string
  variationName: string
  basePrice: number
  modifiers: { id: string; name: string; priceDelta: number }[]
  quantity: number
}

/** One checkout attempt: the order id is generated client-side; the idempotency key is reused on retry. */
export interface Checkout {
  orderId: string
  idempotencyKey: string
  /** true once POST /orders succeeded, i.e. an OPEN order exists server-side */
  created: boolean
}

interface CartState {
  lines: CartLine[]
  checkout: Checkout | null
  sheetOpen: boolean
  add: (line: Omit<CartLine, 'key'>) => void
  setQuantity: (key: string, q: number) => void
  remove: (key: string) => void
  clear: () => void
  /** Get (or lazily create) the current checkout attempt. */
  ensureCheckout: () => Checkout
  markCreated: () => void
  /** Definitive failure: next attempt gets a fresh idempotency key (same order id). */
  newAttemptKey: () => void
  /** Order completed; reset everything. */
  finish: () => void
  setSheetOpen: (o: boolean) => void
}

export const lineUnitPrice = (l: CartLine) => round2(l.basePrice + l.modifiers.reduce((s, m) => s + m.priceDelta, 0))
export const lineTotal = (l: CartLine) => round2(lineUnitPrice(l) * l.quantity)
export const cartTotal = (lines: CartLine[]) => round2(lines.reduce((s, l) => s + lineTotal(l), 0))
export const cartCount = (lines: CartLine[]) => lines.reduce((s, l) => s + l.quantity, 0)

const lineKey = (l: Omit<CartLine, 'key' | 'quantity'>) =>
  `${l.variationId}|${l.modifiers
    .map((m) => m.id)
    .sort()
    .join(',')}`

/** If an OPEN order was already created for the old cart contents, cancel it (best effort). */
function abandon(c: Checkout | null) {
  if (c?.created) void api(`/orders/${c.orderId}/cancel`, { method: 'POST' }).catch(() => undefined)
}

export const useCartStore = create<CartState>((set, get) => ({
  lines: [],
  checkout: null,
  sheetOpen: false,
  add: (line) => {
    abandon(get().checkout)
    set((s) => {
      const key = lineKey(line)
      const existing = s.lines.find((l) => l.key === key)
      const lines = existing
        ? s.lines.map((l) => (l.key === key ? { ...l, quantity: Math.min(MAX_QTY, l.quantity + line.quantity) } : l))
        : [...s.lines, { ...line, key }]
      return { lines, checkout: null }
    })
  },
  setQuantity: (key, q) => {
    abandon(get().checkout)
    set((s) => ({
      lines:
        q <= 0
          ? s.lines.filter((l) => l.key !== key)
          : s.lines.map((l) => (l.key === key ? { ...l, quantity: Math.min(MAX_QTY, q) } : l)),
      checkout: null,
    }))
  },
  remove: (key) => {
    abandon(get().checkout)
    set((s) => ({ lines: s.lines.filter((l) => l.key !== key), checkout: null }))
  },
  clear: () => {
    abandon(get().checkout)
    set({ lines: [], checkout: null, sheetOpen: false })
  },
  ensureCheckout: () => {
    const existing = get().checkout
    if (existing) return existing
    const c = { orderId: uuid(), idempotencyKey: uuid(), created: false }
    set({ checkout: c })
    return c
  },
  markCreated: () => set((s) => (s.checkout ? { checkout: { ...s.checkout, created: true } } : s)),
  newAttemptKey: () => set((s) => (s.checkout ? { checkout: { ...s.checkout, idempotencyKey: uuid() } } : s)),
  finish: () => set({ lines: [], checkout: null, sheetOpen: false }),
  setSheetOpen: (o) => set({ sheetOpen: o }),
}))
