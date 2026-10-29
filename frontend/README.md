# Brew & Bean POS - frontend (Phase 1 + Phase 2 multi-location)

Vite + React 19 + TypeScript, React Router v7, TanStack Query (server state), Zustand (cart, auth session, UI only),
React Hook Form + Zod. Plain CSS, mobile-first (breakpoints 768px and 1100px). Tablet (10-12") is the primary target.

## Run

Prerequisites: Node 20+, the backend running at http://localhost:5080.

```bash
# 1. Backend (from repo root)
docker compose up -d --wait db
cd backend
dotnet run --project src/CafePos.Api

# 2. Frontend
cd frontend
cp .env.example .env        # VITE_API_BASE_URL=http://localhost:5080/api/v1
npm install
npm run dev                 # http://localhost:5173 (port is fixed: backend CORS only allows it)
```

Other scripts: `npm run build` (type-check + production build), `npm run lint`, `npm run preview`.

Dev logins (seeded by the backend, dev only): `admin@cafe.local` / `Admin#Dev12345` (Owner), `manager@cafe.local` / `Manager#Dev12345` (Manager at Main and Uptown), `cashier@cafe.local` / `Cashier#Dev12345` (Main), `cashier.uptown@cafe.local` / `Cashier#Dev12345` (Uptown).

## Screens

| Route                                | Roles                         | What                                                                                                                                                                                                                |
| ------------------------------------ | ----------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `/login`                             | public                        | Sign in. If the account has several locations the API answers `400` with a list and the form shows a location picker                                                                                                |
| `/pos`                               | all                           | Category tabs, item tiles, variation/modifier sheet (min/max enforced), cart sidebar (bottom sheet under 768px), cash payment with change, receipt + print. Prices shown are the active location's effective prices |
| `/orders`, `/orders/:id/receipt`     | all (cashiers see own orders) | History with status/date filters and pagination, receipt view/print, cancel open orders. Owners get a location filter                                                                                               |
| `/admin/catalog`                     | Manager, Admin, Owner         | Categories, items + variations, modifier lists, recipe editor, and a **Location prices** dialog per variation                                                                                                       |
| `/admin/inventory`                   | Manager, Admin, Owner         | Stock dashboard (red/amber/green), new ingredient, restock, adjustment/waste, stock ledger                                                                                                                          |
| `/admin/users` (nav label **Staff**) | Admin, Owner                  | Staff at the active location: create users, assign roles, remove from the location                                                                                                                                  |
| `/admin/locations`                   | Owner                         | All locations: create/edit, and the per-location staff roster                                                                                                                                                       |
| `/admin/settings`                    | Admin, Owner                  | Active location's details and VAT rate                                                                                                                                                                              |

Owners pass every role check; the Locations screen is Owner only.

## Locations

- **At login**: a multi-location account picks a location on the login form. Single-location accounts are signed in directly. An Owner who leaves the location empty gets the "All locations" session.
- **Header switcher** (`LocationSwitcher`): Owners and multi-location staff get a dropdown; single-location staff see a fixed label. Switching calls `POST /auth/switch-location`, replaces the in-memory access token and refetches location-scoped data. If the cart has lines, the user is asked to confirm because switching clears the current order.
- **Owner "All locations" mode** (the first option of the switcher, no active location):
  - `/pos` is blocked and shows a "Choose a location" screen with a button per location, because orders are location-scoped.
  - `/admin/inventory` shows the global ingredient catalog without stock figures; pick a location in the page's filter to see its stock. Viewing a location other than the active one is read-only: restock, adjustment and new stock always post to the active location.
  - `/orders` lists orders from every location with a location tag on each row. Cancelling an order needs an active location.
  - The Location prices dialog lists every active location for the Owner. Other roles see only their active location.
- **Location prices** (per variation, in the Items tab): optional price override, inventory-tracking override and low-stock threshold override for one location. Clearing all three removes the override. The low-stock threshold override is stored but not enforced by the backend yet.

## Contract notes

- Access token is kept in memory only (Zustand, never localStorage). The HttpOnly refresh cookie is used on app load and
  on any 401 (single-flight refresh, then one retry). All requests use `credentials: 'include'`.
- Checkout: the order UUID is generated on the client; `POST /orders` is safe to repeat. One `Idempotency-Key` is
  generated per checkout attempt and reused on network errors/5xx; a fresh key is used after a definitive 4xx. Editing
  the cart after a failed attempt (e.g. 409 insufficient stock) cancels the abandoned open order (best effort) and
  starts a new order id.
- Errors are parsed as ProblemDetails (`ApiError`), including validation `errors` and the 409 `ingredient/required/available`
  extension members.
- Money is formatted with `Intl.NumberFormat('en-PH', {style:'currency', currency:'PHP'})`. Prices are VAT-inclusive;
  the receipt shows the VAT portion and never adds it on top. The receipt says it is not an official BIR receipt.
- Printing: the receipt is also rendered into `#print-root`; the print stylesheet hides the app and uses
  `@page { size: 80mm auto; margin: 0 }`, so the browser print dialog outputs only the 80mm receipt.
- Order lines are limited to 1-100 units by the API; the UI caps quantity at 100.
- `crypto.randomUUID` only exists in secure contexts, so `src/lib/uuid.ts` has a fallback for tablets opening the dev
  server over `http://<lan-ip>:5173` (note the backend CORS allowlist and cookie `SameSite=Lax` would also need changing for that).

## Layout

```
src/
  lib/         api client (auth refresh, ProblemDetails), types, money/date formatting, uuid
  components/  AppShell, Modal, shared ui bits
  features/    auth, pos, orders, catalog, inventory, settings, locations, users
```
