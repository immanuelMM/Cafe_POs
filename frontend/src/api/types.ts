/**
 * Short names for the generated schema types (src/api/schema.d.ts). These are aliases, not copies: when the
 * backend contract changes and `npm run gen:api` is re-run, every use is re-checked by the compiler.
 *
 * The contract types enum-like fields (roles, order status, stock movement reason, ...) as plain `string`.
 * The unions below are UI-side vocabularies for building requests and branching; response fields stay `string`.
 */
import type { components } from './schema'

type S = components['schemas']

export type User = S['UserInfoDto']
export type AuthResponse = S['AuthResponse']
export type UserLocation = S['LocationRoleDto']
export type UserDto = S['UserDto']
export type StaffMember = S['StaffMemberDto']
export type LocationDto = S['LocationDto']
export type LocationOverride = S['LocationOverrideDto']

export type Category = S['CategoryDto']
export type Variation = S['VariationDto']
export type Item = S['ItemDto']
export type Modifier = S['ModifierDto']
export type ModifierList = S['ModifierListDto']
export type Recipe = S['RecipeDto']

export type Ingredient = S['IngredientDto']
export type StockMovement = S['StockMovementDto']

export type Order = S['OrderDto']
export type OrderLine = S['OrderItemDto']
export type Payment = S['PaymentDto']
export type Receipt = S['ReceiptDto']

export type Branding = S['BrandingDto']
export type TimeClockStaff = S['TimeClockStaffDto']
export type TimeEntry = S['TimeEntryDto']
export type PunchResult = S['PunchResultDto']

/** Per-location roles that can be assigned to a user. */
export type AssignableRole = 'Cashier' | 'Manager' | 'Admin'
/** `Owner` is a user flag; it is the session role for Owners. */
export type Role = AssignableRole | 'Owner'
export type MovementReason = 'Sale' | 'Restock' | 'Adjustment' | 'Waste'
export type OrderStatus = 'Open' | 'Completed' | 'Canceled'
