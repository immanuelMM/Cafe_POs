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
docker compose up -d --build   # frontend http://localhost:5173, API http://localhost:5080 (Scalar API docs at /scalar/v1), PostgreSQL localhost:5433
docker compose logs -f         # follow logs
docker compose down            # stop (data kept); `docker compose down -v` also wipes the database
```

The frontend container serves a production build, so re-run `docker compose up -d --build` after code changes.
Compose waits for the database and the API to be healthy, and restarts the API if it exits. Optional overrides (ports, dev
password, dev JWT key) are listed in [`.env.example`](.env.example); copy it to `.env` to use them. The image contains no dev
secrets: compose supplies the dev-only `Jwt__Key` and the dev-database switches as environment variables.
Deployment notes (images, production configuration, migrations): [`docs/deployment.md`](docs/deployment.md).

### Local dev (hot reload)

Prerequisites: .NET SDK 10.0.x, Docker (running), Node 22.

```bash
# Database (from the repo root)
docker compose up -d --wait db

# Backend (http://localhost:5080, Scalar at /scalar/v1)
cd backend
dotnet user-secrets set "Jwt:Key" "DEV-ONLY-signing-key-do-not-use-in-production-0123456789" --project src/CafePos.Api   # one-time
dotnet run --project src/CafePos.Api

# Frontend (http://localhost:5173)
cd frontend
cp .env.example .env
npm install
npm run dev
```

In Development the backend migrates and seeds an empty database on startup. Dev logins are listed in [`backend/README.md`](backend/README.md#seeded-dev-accounts).

### Tests

```bash
cd backend && dotnet test        # Domain 10, Application 129, Api.IntegrationTests 139; needs a running Docker engine (Testcontainers starts PostgreSQL)
cd frontend && npm test          # Vitest, 80 tests (also: npm run lint, npm run typecheck, npm run format:check)
cd e2e && npm install --legacy-peer-deps && npm run install:browsers   # one-time
cd e2e && npm run stack:up && npm run test:e2e                         # Playwright, 39 tests incl. axe a11y; see e2e/README.md
```

### Changing the API contract

Change the backend, then `cd backend && dotnet build src/CafePos.Api -p:OpenApiGenerateDocuments=true`, then `cd frontend && npm run gen:api`, and
commit `backend/openapi/v1.json` and `frontend/src/api/schema.d.ts` with the code. Details: [`docs/architecture.md`](docs/architecture.md#contract-first-workflow).

### CI

`.github/workflows/ci.yml` runs jobs `backend`, `contract`, `frontend`, `docker`, `e2e` (smoke on PRs, full suite on main and nightly) and
`migration-bundle` (main only). It has not run on GitHub yet because the repo has no remote.

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

- Architecture (layers, request flow, auth/refresh, multi-location isolation, contract workflow): [`docs/architecture.md`](docs/architecture.md)
- Decision records: [`docs/adr/`](docs/adr/README.md)
- API docs: [`docs/api/README.md`](docs/api/README.md) (index; `backend/openapi/v1.json` is the source of truth), with [`phase1-api.md`](docs/api/phase1-api.md) (base contract), [`phase2-api.md`](docs/api/phase2-api.md) (multi-location changes, applied on top of Phase 1) and [`branding-timeclock-api.md`](docs/api/branding-timeclock-api.md)
- Deployment sketch: [`docs/deployment.md`](docs/deployment.md)
- Plans: [`docs/plan/cafe-pos-inventory-plan.md`](docs/plan/cafe-pos-inventory-plan.md) (product), [`docs/plan/refactor-plan.md`](docs/plan/refactor-plan.md) (refactor, status and follow-ups)
- Interactive API docs (Development): http://localhost:5080/scalar/v1 (OpenAPI 3.1 document at /openapi/v1.json; committed snapshot in `backend/openapi/v1.json`)
