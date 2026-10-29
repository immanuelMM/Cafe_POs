# Cafe POS - Phase 1 API contract

Base URL (dev): `http://localhost:5080`  
All routes are under `/api/v1`. JSON in and out, `camelCase` property names, enums as strings, GUIDs as strings.  
Interactive API docs (Development only): `http://localhost:5080/scalar/v1`; OpenAPI document: `http://localhost:5080/openapi/v1.json`.  
CORS allows `http://localhost:5173` (Vite) with credentials. Response header `Idempotent-Replayed` is exposed to the browser.

Phase 1 scope: one location, online only, cash only. Every location-scoped row already carries a `locationId`; the server derives it from the token (never send it from the client).

## Conventions

### Money and tax (Philippines, PHP)
- Currency is always PHP. Amounts are JSON numbers with 2 decimals (`decimal(18,2)` in the DB).
- **All prices are VAT-inclusive** (variation price, modifier `priceDelta`, line totals, order `total`). VAT is never added on top.
- `vatAmount` is the VAT contained in `total`: `total * rate / (100 + rate)`, rounded to 2 dp (half away from zero). `vatableSales = total - vatAmount`.
- VAT rate is per location (`vatRatePercent`, default `12`); each order snapshots the rate. Stock quantities use 3 decimals.
- BIR accreditation / official-receipt requirements are NOT implemented (open item in the plan).

### Errors (RFC 9457 ProblemDetails, `application/problem+json`)
```json
{ "type": "...", "title": "Insufficient stock", "status": 409, "detail": "Not enough Whole Milk ...",
  "ingredient": "Whole Milk", "required": 400, "available": 120 }
```
Validation failures (400) use `ValidationProblemDetails` with an `errors` object (`{ "field": ["message"] }`).

| Status | Meaning |
|---|---|
| 400 | Malformed/invalid request, missing `Idempotency-Key`, unsupported payment method |
| 401 | Missing/invalid/expired token or bad credentials |
| 403 | Role not allowed (or user has no access to the requested location) |
| 404 | Not found (also returned for rows that belong to another location, or another cashier's order for a Cashier) |
| 409 | State conflict: insufficient stock, order not open, duplicate SKU/name, order id used by someone else |
| 422 | Business rule failure: payment failed (cash tendered too low), invalid modifier selection, item not for sale, reused idempotency key on a different order |
| 423 | Account temporarily locked (5 failed logins -> 5 minutes) |

### Pagination (offset)
Query `?page=1&pageSize=20` (pageSize max 100). Paged responses:
```json
{ "data": [ ... ], "page": 1, "pageSize": 20, "totalCount": 57, "totalPages": 3 }
```

### Roles
`Cashier`, `Manager`, `Admin` (Admin includes everything Manager can do). A user has one role per location; the token carries the role for the active location.

## Auth flow

1. `POST /api/v1/auth/login` with email + password. Response contains `accessToken` (JWT, 30 min) and sets an **HttpOnly refresh cookie** `cafepos_refresh` (7 days, `Path=/api/v1/auth`, `SameSite=Lax`).
2. Send `Authorization: Bearer <accessToken>` on every other call.
3. On 401 (or shortly before `expiresAt`), call `POST /api/v1/auth/refresh` (no body, cookie sent automatically). Returns a new `AuthResponse` and rotates the cookie. Reusing an old cookie revokes all of that user's sessions.
4. `POST /api/v1/auth/logout` revokes the cookie (204).

The browser client must use `fetch(..., { credentials: "include" })` (axios: `withCredentials: true`) for the auth calls so the cookie is stored/sent. Keep the access token in memory, not localStorage. If you serve the SPA from a different registrable domain in production, the cookie needs `SameSite=None; Secure` (configure then); on localhost `:5173` -> `:5080` it works as is.

### Dev credentials (seeded in Development only)
| Role | Email | Password |
|---|---|---|
| Admin | `admin@cafe.local` | `Admin#Dev12345` |
| Cashier | `cashier@cafe.local` | `Cashier#Dev12345` |

Seed data: location "Brew & Bean - Main Branch" (id `11111111-1111-1111-1111-111111111111`, VAT 12%), categories Coffee/Tea/Pastries, items Latte (S/M/L), Espresso (Single/Double), Cappuccino, Iced Tea, Butter Croissant, modifier lists "Milk Type" and "Extras", ingredients with recipes (Tea Bags is intentionally below its low-stock threshold).

### Endpoints

#### `POST /auth/login` (anonymous)
Request: `{ "email": "cashier@cafe.local", "password": "...", "locationId": null }` (`locationId` only needed if the user has several locations; otherwise the API answers 400 with a `locations` array to choose from).
Response 200 `AuthResponse`:
```json
{
  "accessToken": "eyJ...",
  "expiresAt": "2026-10-16T14:30:00Z",
  "user": {
    "id": "guid", "email": "cashier@cafe.local", "displayName": "Casey Cashier", "role": "Cashier",
    "location": { "id": "guid", "name": "Brew & Bean - Main Branch" },
    "locations": [ { "locationId": "guid", "locationName": "...", "role": "Cashier" } ]
  }
}
```
#### `POST /auth/refresh` (cookie) -> `AuthResponse`;  `POST /auth/logout` (cookie) -> 204;  `GET /auth/me` -> `user` object above.

## Locations
| Method | Path | Roles | Notes |
|---|---|---|---|
| GET | `/locations` | any | Locations the caller is assigned to |
| GET | `/locations/current` | any | Active location |
| PUT | `/locations/{id}` | Admin | Own active location only. Body: `{ name, address, timeZone, contactPhone, vatRatePercent }` |

`LocationDto`: `{ id, name, address, timeZone, contactPhone, vatRatePercent, active }`

## Catalog (read: any role; write: Manager/Admin; DELETE = deactivate, returns 204)
Cashiers only ever see active records; Manager/Admin can pass `includeInactive=true`.

| Method | Path | Notes |
|---|---|---|
| GET | `/categories` | `[CategoryDto]` sorted by sortOrder. `CategoryDto { id, name, sortOrder, active }` |
| POST/PUT | `/categories`, `/categories/{id}` | Body `{ name, sortOrder, active? }` |
| DELETE | `/categories/{id}` | |
| GET | `/items?categoryId=&search=&page=&pageSize=` | Paged `ItemDto` |
| GET | `/items/{id}` | |
| POST | `/items` | Needs >= 1 variation. Body below |
| PUT | `/items/{id}` | Same body; `variations` ignored (use variation endpoints). `modifierListIds` replaces the links when present |
| DELETE | `/items/{id}` | |
| GET | `/items/{id}/variations` | |
| POST | `/items/{id}/variations` | Body `{ name, sku, price, active? }`; SKU unique (409 otherwise) |
| PUT / DELETE | `/variations/{id}` | |
| GET | `/modifier-lists` | `[ModifierListDto]` |
| POST / PUT | `/modifier-lists`, `/modifier-lists/{id}` | PUT replaces the modifier set: known `id` = update, none = create, omitted = deactivated |

```json
// ItemDto
{ "id": "guid", "categoryId": "guid", "name": "Latte", "description": "...", "sortOrder": 1, "active": true,
  "variations": [ { "id": "guid", "itemId": "guid", "name": "Medium", "sku": "LAT-M", "price": 150.00, "active": true } ],
  "modifierListIds": [ "guid" ] }
// POST /items body
{ "categoryId": "guid", "name": "Mocha", "description": null, "sortOrder": 4, "active": true,
  "modifierListIds": [ "guid" ], "variations": [ { "name": "Medium", "sku": "MOC-M", "price": 160 } ] }
// ModifierListDto
{ "id": "guid", "name": "Milk Type", "minSelections": 0, "maxSelections": 1, "active": true,
  "modifiers": [ { "id": "guid", "name": "Oat Milk", "priceDelta": 30.00, "active": true } ] }
```
Modifier selection rules enforced when ordering: each chosen modifier must belong to a list linked to the item, and the count per linked list must be within `[minSelections, maxSelections]` (a list with `minSelections > 0` is required).

## Inventory (stock is per location)
| Method | Path | Roles | Notes |
|---|---|---|---|
| GET | `/ingredients` | any | `[IngredientDto]` |
| GET | `/ingredients/low-stock` | any | Only ingredients with `quantityOnHand <= lowStockThreshold` |
| POST | `/ingredients` | Manager+ | `{ name, unit, lowStockThreshold, initialQuantity }` (initial > 0 posts an "Opening balance" adjustment) |
| PUT | `/ingredients/{id}` | Manager+ | `{ name, unit, lowStockThreshold, active }` |
| GET | `/ingredients/{id}/movements?page=&pageSize=` | Manager+ | Paged ledger, newest first |
| POST | `/restocks` | Manager+ | `{ ingredientId, quantity, unitCost?, supplier?, note? }` -> 201 `StockMovementDto` |
| POST | `/ingredients/{id}/adjustments` | Manager+ | `{ quantityDelta, reason: "Adjustment"\|"Waste", note }`. Waste must be negative. 409 if stock would go below 0 |
| GET | `/variations/{id}/recipe` | Manager+ | `{ variationId, lines: [{ ingredientId, ingredientName, unit, quantity }] }` |
| PUT | `/variations/{id}/recipe` | Manager+ | `{ lines: [{ ingredientId, quantity }] }` replaces the recipe (quantity per ONE unit sold, in the ingredient's unit). `[]` clears it |

```json
// IngredientDto
{ "id": "guid", "name": "Whole Milk", "unit": "ml", "lowStockThreshold": 3000.000, "active": true,
  "quantityOnHand": 19800.000, "isLowStock": false }
// StockMovementDto
{ "id": "guid", "ingredientId": "guid", "quantityDelta": -200.000, "reason": "Sale", "sourceDocumentRef": "order:<orderId>",
  "note": null, "supplier": null, "unitCost": null, "createdAt": "2026-10-16T05:00:00Z", "userId": "guid" }
```
`reason` is one of `Sale`, `Restock`, `Adjustment`, `Waste`. The ledger is append-only.

## Orders
Cashiers see/act on only their own orders; Manager/Admin see all orders of the location.

| Method | Path | Notes |
|---|---|---|
| POST | `/orders` | Create OPEN order. **Client supplies the order `id` (UUID).** 201 when created; 200 with the existing order if that id was already created by you (safe retry) |
| GET | `/orders?status=&from=&to=&page=&pageSize=` | Paged `OrderDto`, newest first. `status`: `Open`/`Completed`/`Canceled`; `from`/`to` ISO timestamps |
| GET | `/orders/{id}` | |
| POST | `/orders/{id}/items` | Add a line (body = order line) -> `OrderDto` |
| PUT | `/orders/{id}/items/{itemId}` | Replace a line (the line gets a NEW id) -> `OrderDto` |
| DELETE | `/orders/{id}/items/{itemId}` | -> `OrderDto` (cannot remove the last line; cancel instead) |
| POST | `/orders/{id}/complete` | Pay + finalize. **Requires `Idempotency-Key` header.** See below |
| POST | `/orders/{id}/cancel` | OPEN orders only -> `OrderDto` |
| GET | `/orders/{id}/receipt` | Completed orders only -> `ReceiptDto` |

Create body:
```json
{ "id": "c0f6c5a0-...-client-uuid", "note": "for here",
  "items": [ { "itemVariationId": "guid", "quantity": 2, "modifierIds": [ "guid" ], "note": null } ] }
```
Prices are always computed server-side from the catalog (unit price = variation price + sum of modifier deltas); client-sent prices do not exist.

`OrderDto`:
```json
{ "id": "guid", "orderNumber": null, "status": "Open", "locationId": "guid", "cashierId": "guid", "cashierName": "Casey Cashier",
  "note": null, "vatRatePercent": 12.00, "total": 300.00, "vatableSales": 267.86, "vatAmount": 32.14,
  "createdAt": "...Z", "completedAt": null, "canceledAt": null,
  "items": [ { "id": "guid", "itemVariationId": "guid", "itemName": "Latte", "variationName": "Medium", "sku": "LAT-M",
               "unitPrice": 150.00, "quantity": 2, "lineTotal": 300.00, "note": null,
               "modifiers": [ { "modifierId": "guid", "name": "Oat Milk", "priceDelta": 30.00 } ] } ],
  "payments": [ { "id": "guid", "method": "cash", "status": "Succeeded", "amount": 300.00, "amountTendered": 500.00,
                  "changeGiven": 200.00, "createdAt": "...Z" } ] }
```
`orderNumber` is the per-location receipt number, assigned on completion.

### Completing an order (cash)
```
POST /api/v1/orders/{id}/complete
Idempotency-Key: 5d3b0c52-6d0f-4f0a-9a54-0d6f6a1c2f77     (generate once per checkout attempt, reuse on retry)
{ "paymentMethod": "cash", "amountTendered": 500 }
```
- Single transaction: claim the order (only one completion can ever win), assign receipt number, decrement stock with a conditional UPDATE per ingredient (+ `Sale` ledger rows), record payment.
- 200 -> `OrderDto` (status `Completed`, `payments[0].changeGiven`).
- **Retry with the same key** for the same order returns the original response with header `Idempotent-Replayed: true`; no second charge or stock decrement. Network failure after sending? Just resend with the same key.
- Same key on a different order -> 422. Missing key -> 400. Different key on an already-completed order -> 409.
- 422 `Payment failed` if `amountTendered` is missing or below the total. 409 `Insufficient stock` (with `ingredient`, `required`, `available` extensions) if a recipe ingredient is short; the order stays Open and nothing is changed.
- Only `"cash"` is accepted in Phase 1 (`400 Unsupported payment method` otherwise). Payments sit behind a server-side `IPaymentProvider`; the card/QR provider for the Philippines is undecided.

### Receipt data
`GET /orders/{id}/receipt` -> the frontend renders/prints it (80mm layout per the plan):
```json
{ "locationName": "Brew & Bean - Main Branch", "locationAddress": "...", "locationPhone": "...", "orderNumber": 17,
  "orderId": "guid", "completedAt": "...Z", "timeZone": "Asia/Manila", "cashierName": "Casey Cashier",
  "lines": [ { "description": "Latte (Medium)", "modifiers": "Oat Milk", "quantity": 2, "unitPrice": 150.00, "lineTotal": 300.00 } ],
  "pricesIncludeVat": true, "vatRatePercent": 12.00, "vatableSales": 267.86, "vatAmount": 32.14, "total": 300.00,
  "paymentMethod": "cash", "amountTendered": 500.00, "changeGiven": 200.00, "currency": "PHP" }
```
Timestamps are UTC (`Z`); format in `timeZone` for display.

## Health
`GET /health` (anonymous) -> `{ "status": "ok" }`.
