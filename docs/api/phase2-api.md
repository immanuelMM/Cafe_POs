# Cafe POS - Phase 2 API contract (multi-location)

Additive to `phase1-api.md`; everything there still applies unless changed below. Base path `/api/v1`, JSON `camelCase`, enums as strings, GUIDs as strings, errors are ProblemDetails.

## Concepts

- **Location**: a cafe branch. Orders, stock levels, the stock ledger, staff assignments and receipt numbers are per location. Catalog (categories, items, variations, modifiers, recipes, ingredients) is global.
- **Roles**: `Cashier`, `Manager`, `Admin` are per-location roles stored as staff assignments (one role per user per location). `Owner` is a flag on the user (`isOwner`): unrestricted, all current and future locations, no assignment rows.
- **Active location**: every session works in one location (the `location_id` claim), except an Owner "cross-location" session which has none.
- The server never trusts a client-sent `locationId` alone. A non-Owner can only pass their own active location; anything else is `403`. Other locations' rows are invisible (`404`).

Role capability summary: Cashier = Phase 1 cashier. Manager = Phase 1 manager at their location. Admin = Manager + location settings + staff management at their location. Owner = everything, at any location.

## JWT access token claims

| Claim | Value |
|---|---|
| `sub` | user id (GUID) |
| `role` | `Cashier` / `Manager` / `Admin` (role at the active location) or `Owner` |
| `location_id` | active location GUID. **Absent** for an Owner cross-location session |
| `jti`, `exp`, `iss`, `aud` | standard |

(Email/display name are also present under their long .NET claim URIs; rely on the table above, or call `GET /auth/me`.) The token lifetime is unchanged (30 min). Treat `GET /auth/me` as the source of truth for the UI; decode the JWT only if needed.

## Login / switch-location flow

1. `POST /auth/login` `{ email, password, locationId? }`
   - **Non-owner, exactly one assignment**: that location becomes active automatically (`locationId` optional).
   - **Non-owner, several assignments**: send `locationId`. Without it: `400` ProblemDetails with extension `locations: [{ id, name }]` to render a picker. A `locationId` the user is not assigned to: `403`.
   - **Owner**: `locationId` given -> that (active) location; omitted -> **cross-location session** (`user.location` is `null`, no `location_id` claim). Unknown/inactive location: `403`.
   - No assignment at all (non-owner): `403`.
2. `POST /auth/switch-location` (Bearer) `{ "locationId": "guid" | null }` -> `AuthResponse` with a **new** access token and a **new refresh cookie** (the old cookie is revoked). Replace the stored access token with the new one and refetch location-scoped data.
   - Non-owner: must be a location they are assigned to (`403` otherwise); `null` -> `400`.
   - Owner: any active location, or `null` to go back to the cross-location session.
3. `POST /auth/refresh` (cookie) preserves the active location of the session it rotates (including "none" for an Owner).
4. A cross-location Owner session cannot do location-scoped writes (create order, restock, adjustment, create stock...): `400` "Location required". Switch to a location first.

### `AuthResponse` / `GET /auth/me` (changed)
```json
{
  "accessToken": "eyJ...", "expiresAt": "2026-10-18T05:00:00Z",
  "user": {
    "id": "guid", "email": "manager@cafe.local", "displayName": "Morgan Manager",
    "role": "Manager", "isOwner": false,
    "location": { "id": "guid", "name": "Brew & Bean - Main Branch" },
    "locations": [ { "locationId": "guid", "locationName": "Brew & Bean - Main Branch", "role": "Manager" },
                   { "locationId": "guid", "locationName": "Brew & Bean - Uptown Branch", "role": "Manager" } ]
  }
}
```
Changes vs Phase 1: new `isOwner`; `location` is `null` in a cross-location Owner session; for an Owner `locations` lists **all active locations** with role `Owner`. `GET /auth/me` returns the same `user` object (no token).

## Dev credentials (Development seed)

| Email | Password | Access |
|---|---|---|
| `admin@cafe.local` | `Admin#Dev12345` | **Owner** (was the Phase 1 Admin). Login without `locationId` = cross-location; pass `locationId` to work in a branch |
| `manager@cafe.local` | `Manager#Dev12345` | Manager at Main **and** Uptown (login needs `locationId`) |
| `cashier@cafe.local` | `Cashier#Dev12345` | Cashier at Main only |
| `cashier.uptown@cafe.local` | `Cashier#Dev12345` | Cashier at Uptown only |

Locations: Main `11111111-1111-1111-1111-111111111111`, Uptown `22222222-2222-2222-2222-222222222222`. Each has its own stock levels (Tea Bags is low at Main, healthy at Uptown).

## Locations

`LocationDto`: `{ id, name, address, timeZone, contactPhone, vatRatePercent, active, stripeTerminalLocationId }` (new: `stripeTerminalLocationId`, nullable).

| Method | Path | Who | Notes |
|---|---|---|---|
| GET | `/locations` | any | Owner: all locations (incl. inactive). Others: their assigned active locations |
| GET | `/locations/current` | any with an active location | Active location. Owner cross-location session: `400` |
| POST | `/locations` | Owner | Body `{ name, address?, timeZone, contactPhone?, vatRatePercent, stripeTerminalLocationId? }` -> 201 `LocationDto`. `422` for an unknown IANA time zone |
| PUT | `/locations/{id}` | Owner (any), Admin (own active location only, else 403) | Body `{ name, address, timeZone, contactPhone, vatRatePercent, active?, stripeTerminalLocationId? }`. `active` and `stripeTerminalLocationId` are Owner only (`403` if an Admin sends them). Omit = unchanged; empty string clears the Stripe id |
| GET | `/locations/{id}/staff` | Owner (any), Admin (own location) | `[ { userId, email, displayName, role, active } ]`. `403` for another location |

## Users (Admin or Owner)

Admins are confined to their active location: they see only non-owner users assigned there, may only create/assign at that location, cannot create or edit Owners (`403`), and users outside their location are `404`.

`UserDto`: `{ id, email, displayName, active, isOwner, locations: [ { locationId, locationName, role } ] }`

| Method | Path | Notes |
|---|---|---|
| GET | `/users?locationId=&search=&page=&pageSize=` | Paged `UserDto`. Owner: all (optionally filtered by `locationId`). Admin: own location only (other `locationId` -> 403) |
| GET | `/users/{id}` | `UserDto` |
| POST | `/users` | Body `{ email, displayName, password (min 8, Identity rules), isOwner?, assignments: [ { locationId, role } ] }`. `role` is `Cashier`/`Manager`/`Admin`. Admin callers must include at least one assignment, all at their location. 201 `UserDto`; `409` duplicate email; `400` weak password |
| PUT | `/users/{id}` | Body `{ displayName, active?, isOwner? }` (`isOwner` Owner only). Cannot deactivate/demote yourself (422). Deactivating revokes refresh tokens |
| DELETE | `/users/{id}` | Deactivate (not a hard delete) -> 204 |
| POST | `/users/{id}/locations` | Body `{ locationId, role }`. Adds the assignment or changes its role. Returns `UserDto`. Owners cannot be assigned (422) |
| DELETE | `/users/{id}/locations/{locationId}` | -> 204. `404` if no such assignment |

## Variation location overrides

Catalog stays global. An override changes how one variation behaves at one location.

`LocationOverrideDto`: `{ variationId, locationId, locationName, priceOverride, trackInventoryOverride, lowStockThresholdOverride, effectivePrice }` (`effectivePrice` = `priceOverride ?? global price`).

| Method | Path | Who | Notes |
|---|---|---|---|
| GET | `/variations/{id}/location-overrides` | Manager+ | Owner: every location's override. Others: the override at their active location (0 or 1 items) |
| PUT | `/variations/{id}/location-overrides/{locationId}` | Manager+ (Owner: any location; others: own active location, else 403) | Body `{ priceOverride?, trackInventoryOverride?, lowStockThresholdOverride? }` -> 200 `LocationOverrideDto`. **All three null/omitted removes the override -> 204** |

Semantics:
- `priceOverride`: VAT-inclusive price (same rules as catalog prices). Order lines at that location use it (`unitPrice = priceOverride + modifier deltas`). Existing open orders keep their line prices until the line is re-added/replaced.
- `trackInventoryOverride = false`: sales of the variation at that location do not consume recipe stock. `null`/`true` = tracked.
- `lowStockThresholdOverride`: stored and returned only. **Not enforced yet** (ingredient low-stock thresholds remain global).

### Catalog DTO change
`VariationDto` gains `effectivePrice` (the active location's price; equals `price` when there is no override or no active location). The POS must display/charge `effectivePrice`.
```json
{ "id": "guid", "itemId": "guid", "name": "Medium", "sku": "LAT-M", "price": 150.00, "active": true, "effectivePrice": 140.00 }
```

## Location-scoped data and the `locationId` filter

By default everything is scoped to the token's active location. Owners (only) can look at other locations with an explicit `locationId` query parameter; everyone else may only send their own active location (otherwise `403`).

| Endpoint | Behaviour |
|---|---|
| `GET /orders?...&locationId=` | Owner: `locationId` = that location; with no `locationId` and an active location = that location; with **no active location and no `locationId` = all locations** (use `locationId` on each `OrderDto` to group). Others: own location |
| `GET /orders/{id}`, `GET /orders/{id}/receipt` | Owners can read orders of any location. Others: `404` for another location. Every mutation (items, complete, cancel) only works inside the active location (`404` otherwise) |
| `GET /ingredients?locationId=`, `GET /ingredients/low-stock?locationId=` | Stock levels / low-stock computed for that location. Owner without active location **must** pass `locationId` (else 400) |
| `GET /ingredients/{id}/movements?locationId=&page=&pageSize=` | Ledger of that location (Manager+) |
| `POST /restocks`, `POST /ingredients/{id}/adjustments`, `POST /ingredients` (initialQuantity > 0) | Always the **active** location. No active location -> `400` |

`StockMovementDto` gains `locationId`: `{ id, ingredientId, locationId, quantityDelta, reason, sourceDocumentRef, note, supplier, unitCost, createdAt, userId }`. `OrderDto` already carried `locationId`.

Note: `GET /ingredients` for a location with no stock row yet reports `quantityOnHand: 0`. A new ingredient is created globally and gets a stock row (and opening balance) only at the active location.

## Status codes added/clarified

| Status | When |
|---|---|
| 400 `Location required` | Location-scoped action in an Owner cross-location session; non-owner switch with `null`; multi-location login without `locationId` (extension `locations`) |
| 403 | Role too low, or another location's id from a non-Owner, or a login/switch to an unassigned location, or Admin touching Owner-only fields |
| 404 | Row of another location (orders, users for Admins), unknown ids |

## Not in this phase
Reports endpoints (`/reports/*` in the plan) do not exist yet; use `GET /orders`, `GET /ingredients/low-stock?locationId=` and movements in the meantime.
