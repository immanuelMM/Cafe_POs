# Cafe POS - Phase 2 API contract (multi-location)

Additive to `phase1-api.md`; everything there still applies unless changed below. Base path `/api/v1`, JSON `camelCase`, enums as strings, GUIDs as strings, errors are ProblemDetails.

## Changes (fix batch, after the first Phase 2 release)

Frontend: please adapt to these contract changes.

1. **`IngredientDto.quantityOnHand` and `isLowStock` are now nullable.** They are `null` only for the global catalog view: `GET /ingredients` in an Owner cross-location session **without** `locationId` now returns the ingredient catalog (HTTP 200) with no stock fields instead of `400`. With `locationId`, or in any session with an active location, they are always numbers/booleans. `GET /ingredients/low-stock` still needs a location (400 otherwise). `POST /ingredients` and `PUT /ingredients/{id}` return `null` stock fields in a cross-location session.
2. **`POST /orders` in an Owner cross-location session is `400 Location required`** (it was a 500). Add-item / edit-item / remove-item / cancel / complete on an existing order in that session are also `400 Location required` (they were `404`). Reads (`GET /orders/{id}`, receipt, list) still work across locations for an Owner.
3. **Admin deactivation is location-scoped.** For an Admin (not Owner), `DELETE /users/{id}` and `PUT /users/{id}` with `active: false` remove that user's assignment at the Admin's active location and revoke sessions bound to it; the account stays active globally. Only if the user has no assignment anywhere else is the account also deactivated globally. Owners still deactivate globally. An Admin sending `active: true` for a user who is assigned to other locations gets `403`.
4. **Admin visibility is location-scoped.** `GET /users` and `GET /users/{id}` for an Admin return `locations` containing only the assignment at the Admin's own location (no other-branch names or roles). Owners see everything.
5. **New endpoint `POST /locations/{id}/staff`** (Owner any location, Admin own location) assigns an existing user found by **exact email** to the location, or changes their role there. This replaces assigning by user GUID for Admins: `POST /users/{id}/locations` only works for Admins on users already visible at their location (otherwise `404`; another `locationId` is `403`).
6. **A non-Owner user must have at least one assignment**: `POST /users` without `assignments` (and `isOwner` false) is now `400` (it was `422` for Admins and allowed for Owners). `PUT /users/{id}` with `isOwner: false` on an Owner who has no assignments is `400`.
7. **Access token window (documented behavior, unchanged):** deactivating a user or removing an assignment does not invalidate access tokens already issued. Those keep working until they expire (30 min). Lockout is enforced at `login`, `refresh`, `switch-location` and `GET /auth/me`; deactivation also revokes the user's refresh tokens (for an Admin's location-scoped removal: the tokens bound to that location).

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
| POST | `/locations/{id}/staff` | Owner (any), Admin (own location, else 403) | Body `{ email, role }` (`role` Cashier/Manager/Admin). Finds an existing user by exact email and adds the assignment at this location (or changes the role). 200 `{ userId, email, displayName, role, active }`. `404` unknown email or location, `422` for an Owner, inactive user, own account, or invalid role |

## Users (Admin or Owner)

Admins are confined to their active location: they see only non-owner users assigned there and **only the assignments at that location** (other branches' names/roles are never returned), may only create/assign at that location (`403` otherwise), cannot create or edit Owners (`403`), and users outside their location are `404`. To bring an existing user from another branch in, an Admin uses `POST /locations/{id}/staff` with the user's exact email.

`UserDto`: `{ id, email, displayName, active, isOwner, locations: [ { locationId, locationName, role } ] }`

| Method | Path | Notes |
|---|---|---|
| GET | `/users?locationId=&search=&page=&pageSize=` | Paged `UserDto`. Owner: all (optionally filtered by `locationId`). Admin: own location only (other `locationId` -> 403) |
| GET | `/users/{id}` | `UserDto` |
| POST | `/users` | Body `{ email, displayName, password (min 8, Identity rules), isOwner?, assignments: [ { locationId, role } ] }`. `role` is `Cashier`/`Manager`/`Admin`. Every non-Owner needs at least one assignment (`400` otherwise). Admin callers: all assignments at their location. 201 `UserDto`; `409` duplicate email; `400` weak password |
| PUT | `/users/{id}` | Body `{ displayName, active?, isOwner? }` (`isOwner` Owner only). Cannot deactivate/demote yourself (422). `active: false`: Owner = global deactivation; Admin = removes the assignment at their location (global only if none remain elsewhere). `active: true` by an Admin is `403` if the user has assignments elsewhere. Returns `UserDto` (for an Admin after removal, `locations` is empty) |
| DELETE | `/users/{id}` | Same semantics as `active: false` above (never a hard delete) -> 204 |
| POST | `/users/{id}/locations` | Body `{ locationId, role }`. Adds the assignment or changes its role. Returns `UserDto`. Owners cannot be assigned (422). Admin: `locationId` must be their location (403) and the user must already be visible to them (404) |
| DELETE | `/users/{id}/locations/{locationId}` | -> 204. `403` for another location (Admin), `404` if no such assignment |

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
| `GET /ingredients?locationId=` | Stock levels for that location. Owner without active location and without `locationId`: the global catalog, `quantityOnHand`/`isLowStock` = `null` |
| `GET /ingredients/low-stock?locationId=` | Low-stock for that location. Owner without active location **must** pass `locationId` (else 400) |
| `GET /ingredients/{id}/movements?locationId=&page=&pageSize=` | Ledger of that location (Manager+) |
| `POST /restocks`, `POST /ingredients/{id}/adjustments`, `POST /ingredients` (initialQuantity > 0) | Always the **active** location. No active location -> `400` |

`StockMovementDto` gains `locationId`: `{ id, ingredientId, locationId, quantityDelta, reason, sourceDocumentRef, note, supplier, unitCost, createdAt, userId }`. `OrderDto` already carried `locationId`.

`IngredientDto`: `{ id, name, unit, lowStockThreshold, active, quantityOnHand: number | null, isLowStock: boolean | null }` (null only in the catalog view described above).

Note: `GET /ingredients` for a location with no stock row yet reports `quantityOnHand: 0`. A new ingredient is created globally and gets a stock row (and opening balance) only at the active location.

## Access token lifetime and revocation

Access tokens are not re-checked against the database on each request. After a user is deactivated or an assignment is removed, an already-issued access token keeps working until it expires (up to 30 minutes). The lockout is effective at the next `login`, `refresh`, `switch-location` and `GET /auth/me`; deactivation (and a location-scoped removal by an Admin) revokes the matching refresh tokens, so the session cannot be renewed. Owners accept this window.

## Status codes added/clarified

| Status | When |
|---|---|
| 400 `Location required` | Location-scoped action in an Owner cross-location session (including create/edit/cancel/complete of orders); non-owner switch with `null`; multi-location login without `locationId` (extension `locations`) |
| 403 | Role too low, or another location's id from a non-Owner, or a login/switch to an unassigned location, or Admin touching Owner-only fields |
| 404 | Row of another location (orders, users for Admins), unknown ids |

## Not in this phase
Reports endpoints (`/reports/*` in the plan) do not exist yet; use `GET /orders`, `GET /ingredients/low-stock?locationId=` and movements in the meantime.
