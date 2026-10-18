# Brew & Bean POS - frontend (Phase 1)

Vite + React 19 + TypeScript, React Router v7, TanStack Query (server state), Zustand (cart, auth session, UI only),
React Hook Form + Zod. Plain CSS, mobile-first (breakpoints 768px and 1100px). Tablet (10-12") is the primary target.

## Run

Prerequisites: Node 20+, the backend running at http://localhost:5080.

```bash
# 1. Backend (from repo root)
cd backend
docker compose up -d --wait db
dotnet run --project src/CafePos.Api

# 2. Frontend
cd frontend
cp .env.example .env        # VITE_API_BASE_URL=http://localhost:5080/api/v1
npm install
npm run dev                 # http://localhost:5173 (port is fixed: backend CORS only allows it)
```

Other scripts: `npm run build` (type-check + production build), `npm run lint`, `npm run preview`.

Dev logins: `cashier@cafe.local` / `Cashier#Dev12345`, `admin@cafe.local` / `Admin#Dev12345`.

## Screens

| Route | Roles | What |
|---|---|---|
| `/login` | public | Sign in (location picker appears if the API asks for one) |
| `/pos` | all | Category tabs, item tiles, variation/modifier sheet (min/max enforced), cart sidebar (bottom sheet under 768px), cash payment with change, receipt + print |
| `/orders`, `/orders/:id/receipt` | all (cashiers see own orders) | History with status/date filters and pagination, receipt view/print, cancel open orders |
| `/admin/catalog` | Manager, Admin | Categories, items + variations, modifier lists, recipe editor |
| `/admin/inventory` | Manager, Admin | Stock dashboard (red/amber/green), new ingredient, restock, adjustment/waste, stock ledger |
| `/admin/settings` | Admin | Location details and VAT rate |

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
  features/    auth, pos, orders, catalog, inventory, settings
```
