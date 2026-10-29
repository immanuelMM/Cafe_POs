import { beforeEach, describe, expect, it } from 'vitest'
import { cartCount, cartTotal, useCartStore } from './cartStore'

const line = { itemName: 'Latte', variationId: 'v1', variationName: 'Small', basePrice: 100, modifiers: [], quantity: 1 }

describe('cartStore', () => {
  beforeEach(() => useCartStore.getState().clear())

  it('merges identical lines and totals them', () => {
    useCartStore.getState().add(line)
    useCartStore.getState().add(line)
    const { lines } = useCartStore.getState()
    expect(lines).toHaveLength(1)
    expect(cartCount(lines)).toBe(2)
    expect(cartTotal(lines)).toBe(200)
  })
})
