# Brew & Bean Cafe POS

Web-based point of sale and inventory system for a cafe business with one or more branches.

- **Frontend**: React 19 + TypeScript (Vite), see [`frontend/README.md`](frontend/README.md)
- **Backend**: .NET 10 Web API + EF Core + PostgreSQL, ASP.NET Core Identity + JWT, see [`backend/README.md`](backend/README.md)

## Status

| Phase | Scope | Status |
|---|---|---|
| 1 | Single-location core: auth, catalog, recipes, ingredients, POS order flow (cash only), printable receipt, low-stock and restock | Done |
| 2 | Multi-location: locations, per-location staff roles, location-scoped orders/stock, Owner role, per-location variation overrides, location switching | Done |
| 3 | Stripe Terminal card payments, email receipts | Planned |
| 4 | Offline sync | Planned |

Not built yet: `/reports/*` endpoints, a refund endpoint, enforcement of `lowStockThresholdOverride`. See the Phase 2 status note in [`docs/plan/cafe-pos-inventory-plan.md`](docs/plan/cafe-pos-inventory-plan.md).

## Quick start

### Everything in Docker

Prerequisite: Docker (running). From the repo root:

```bash
docker compose up -d --build   # frontend http://localhost:5173, API http://localhost:5080 (Swagger at /swagger), PostgreSQL localhost:5433
docker compose logs -f         # follow logs
docker compose down            # stop (data kept); `docker compose down -v` also wipes the database
```

The frontend container serves a production build, so re-run `docker compose up -d --build` after code changes.

### Local dev (hot reload)

Prerequisites: .NET SDK 10.0.x, Docker (running), Node 20+.

```bash
# Database (from the repo root)
docker compose up -d --wait db

# Backend (http://localhost:5080, Swagger at /swagger)
cd backend
dotnet run --project src/CafePos.Api

# Frontend (http://localhost:5173)
cd frontend
cp .env.example .env
npm install
npm run dev
```

In Development the backend migrates and seeds an empty database on startup. Dev logins are listed in [`backend/README.md`](backend/README.md#seeded-dev-accounts).

Tests: `docker compose up -d --wait db`, then `cd backend && dotnet test` (see the backend README).

## Roles and locations

A **location** is a cafe branch. Orders, stock levels, the stock ledger, staff assignments and receipt numbers are per location. The catalog (categories, items, variations, modifiers, recipes, ingredients) is shared by all locations, though a variation can have a per-location price, inventory-tracking flag and low-stock threshold override.

| Role | Scope | Can do |
|---|---|---|
| **Cashier** | Assigned location(s) | Take orders, view orders, receipts |
| **Manager** | Assigned location(s) | Cashier + catalog, recipes, inventory, per-location price overrides at their location |
| **Admin** | Assigned location(s) | Manager + edit their location's details and manage staff at that location |
| **Owner** | All current and future locations | Everything, at any location: create/edit locations, manage all users and assignments, see every location's data |

Cashier, Manager and Admin are **per-location roles**: a user is assigned to one or more locations, with one role at each (so the same person can be Manager at Main and Cashier at Uptown). Owner is a flag on the user, not an assignment. Owners have no assignment rows and are never limited to a location.

Admins are confined to their active location: they only see staff assigned there, and removing a user from the staff list removes the assignment at that location only.

### Location switching

Every session works in exactly one **active location**, carried in the access token (`location_id` claim). The one exception is an Owner "All locations" session, which has no active location.

- **Login**: a user with a single assignment is signed straight into that location. A user with several assignments must choose one (the login screen shows a picker). An Owner who picks nothing gets the All locations session.
- **Switching**: the header switcher calls `POST /auth/switch-location`, which issues a new access token and refresh cookie for the new location. Staff can switch between their assigned locations; Owners can switch to any active location or back to All locations.
- **All locations (Owner only)**: read-only overview. The order history spans every location, and the inventory page shows the global ingredient catalog without stock figures. Actions tied to one location (creating or editing orders, restocking, stock adjustments) are rejected with `400 Location required`, so the POS asks for a location first.
- The server never trusts a client-sent `locationId` on its own. Non-Owners can only use their own active location (`403` otherwise), and rows from other locations are invisible (`404`).

## Documentation

- API contracts: [`docs/api/phase1-api.md`](docs/api/phase1-api.md) (base contract) and [`docs/api/phase2-api.md`](docs/api/phase2-api.md) (multi-location changes, applied on top of Phase 1)
- Plan and decisions: [`docs/plan/cafe-pos-inventory-plan.md`](docs/plan/cafe-pos-inventory-plan.md)
- Interactive API docs (Development): http://localhost:5080/swagger
