export type Role = 'Cashier' | 'Manager' | 'Admin'

export interface UserLocation { locationId: string; locationName: string; role: Role }
export interface User {
  id: string
  email: string
  displayName: string
  role: Role
  location: { id: string; name: string }
  locations: UserLocation[]
}
export interface AuthResponse { accessToken: string; expiresAt: string; user: User }

export interface Paged<T> { data: T[]; page: number; pageSize: number; totalCount: number; totalPages: number }

export interface LocationDto {
  id: string
  name: string
  address: string | null
  timeZone: string
  contactPhone: string | null
  vatRatePercent: number
  active: boolean
}

export interface Category { id: string; name: string; sortOrder: number; active: boolean }
export interface Variation { id: string; itemId: string; name: string; sku: string; price: number; active: boolean }
export interface Item {
  id: string
  categoryId: string
  name: string
  description: string | null
  sortOrder: number
  active: boolean
  variations: Variation[]
  modifierListIds: string[]
}
export interface Modifier { id: string; name: string; priceDelta: number; active: boolean }
export interface ModifierList {
  id: string
  name: string
  minSelections: number
  maxSelections: number
  active: boolean
  modifiers: Modifier[]
}

export interface Ingredient {
  id: string
  name: string
  unit: string
  lowStockThreshold: number
  active: boolean
  quantityOnHand: number
  isLowStock: boolean
}
export type MovementReason = 'Sale' | 'Restock' | 'Adjustment' | 'Waste'
export interface StockMovement {
  id: string
  ingredientId: string
  quantityDelta: number
  reason: MovementReason
  sourceDocumentRef: string | null
  note: string | null
  supplier: string | null
  unitCost: number | null
  createdAt: string
  userId: string
}
export interface Recipe {
  variationId: string
  lines: { ingredientId: string; ingredientName: string; unit: string; quantity: number }[]
}

export type OrderStatus = 'Open' | 'Completed' | 'Canceled'
export interface OrderLine {
  id: string
  itemVariationId: string
  itemName: string
  variationName: string
  sku: string
  unitPrice: number
  quantity: number
  lineTotal: number
  note: string | null
  modifiers: { modifierId: string; name: string; priceDelta: number }[]
}
export interface Payment {
  id: string
  method: string
  status: string
  amount: number
  amountTendered: number | null
  changeGiven: number | null
  createdAt: string
}
export interface Order {
  id: string
  orderNumber: number | null
  status: OrderStatus
  locationId: string
  cashierId: string
  cashierName: string
  note: string | null
  vatRatePercent: number
  total: number
  vatableSales: number
  vatAmount: number
  createdAt: string
  completedAt: string | null
  canceledAt: string | null
  items: OrderLine[]
  payments: Payment[]
}
export interface Receipt {
  locationName: string
  locationAddress: string | null
  locationPhone: string | null
  orderNumber: number
  orderId: string
  completedAt: string
  timeZone: string
  cashierName: string
  lines: { description: string; modifiers: string | null; quantity: number; unitPrice: number; lineTotal: number }[]
  pricesIncludeVat: boolean
  vatRatePercent: number
  vatableSales: number
  vatAmount: number
  total: number
  paymentMethod: string
  amountTendered: number | null
  changeGiven: number | null
  currency: string
}
