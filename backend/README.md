# Cafe POS backend (Phase 1 + Phase 2 multi-location)

.NET 10 Web API (controllers, `/api/v1`), EF Core + **PostgreSQL** (Docker), ASP.NET Core Identity + JWT.
API contract for the frontend: [`../docs/api/phase1-api.md`](../docs/api/phase1-api.md), plus the multi-location changes in [`../docs/api/phase2-api.md`](../docs/api/phase2-api.md).

## Prerequisites
- .NET SDK 10.0.x (pinned by `global.json`)
- Docker (Docker Desktop must be running)

## Run
```bash
docker compose up -d --wait db          # from the repo root: PostgreSQL 16 on localhost:5433 (user/db: cafepos)
cd backend
dotnet run --project src/CafePos.Api    # http://localhost:5080  (Development)
```
In Development the app applies migrations and seeds dev data on startup (empty DB only). Swagger: http://localhost:5080/swagger

### Seeded dev accounts
Created by `Data/DevDataSeeder.cs` on an empty database. Passwords are dev-only seed values.

> A database first seeded in Phase 1 is **not** re-seeded (the seeder skips when any location exists), so it won't get the Uptown branch or the Phase 2 accounts. Either drop and recreate the dev database (from the repo root: `docker compose down -v && docker compose up -d`), or create them as the Owner via `POST /locations` and `POST /users`.

| Email | Password | Access |
|---|---|---|
| `admin@cafe.local` | `Admin#Dev12345` | **Owner** (all locations). Log in without `locationId` for the All locations session, or pass one to work in a branch |
| `manager@cafe.local` | `Manager#Dev12345` | Manager at Main and Uptown (login needs a `locationId`) |
| `cashier@cafe.local` | `Cashier#Dev12345` | Cashier at Main only |
| `cashier.uptown@cafe.local` | `Cashier#Dev12345` | Cashier at Uptown only |

Seeded locations: `Brew & Bean - Main Branch` (`11111111-1111-1111-1111-111111111111`) and `Brew & Bean - Uptown Branch` (`22222222-2222-2222-2222-222222222222`). Each has independent stock levels; Tea Bags is below its threshold at Main and healthy at Uptown.

Run everything (db, API, frontend) in containers instead, from the repo root: `docker compose up -d --build` (API on http://localhost:5080, frontend on http://localhost:5173).
Reset the database (repo root): `docker compose down -v`.

## Configuration
| Key | Notes |
|---|---|
| `ConnectionStrings:Default` | Dev value lives in `appsettings.Development.json` (docker compose credentials, local only) |
| `Jwt:Key` | **Required, min 32 chars.** Dev key is in `appsettings.Development.json`; in any other environment set `Jwt__Key` (env var / secret store) |
| `Cors:Origins` | Defaults to `http://localhost:5173` |
| `Database:MigrateOnStartup` / `Database:SeedDevData` | `true` only in Development; run migrations explicitly elsewhere |

## Tests
```bash
docker compose up -d --wait db          # from the repo root
cd backend
dotnet build
dotnet test
```
Integration tests boot the real API against a throwaway PostgreSQL database (created from the migrations, dropped afterwards), so the Docker db container (`cafepos-db`) must be running. Use `TEST_PG_CONNECTION` to point at another server (the `Database=` part is replaced). The suite had 99 tests at the end of Phase 2, including tenant-isolation tests for cross-location access.

## Migrations
EF tool is a local tool (`dotnet tool restore`).

Phase 2 added the `Phase2MultiLocation` migration (`Data/Migrations/20261018040804_Phase2MultiLocation`): the `IsOwner` flag on users, `StripeTerminalLocationId` on locations, the `ItemVariationLocationOverrides` table (per-location price/tracking/threshold overrides), and a nullable `RefreshTokens.LocationId` (for Owner cross-location sessions). Its data step promotes the sole Admin, if there is exactly one, to Owner. Orders, stock and the ledger already carried `LocationId` from Phase 1, so no backfill is needed. It applies automatically in Development; elsewhere run `dotnet ef database update`.

The `BrandingAssetsAndTimeClock` migration (`Data/Migrations/*_BrandingAssetsAndTimeClock`) adds tagline, primary color and logo/tarpaulin image columns (`bytea`) to `BrandSettings`, the clock PIN columns on users, and the `TimeEntries` table (with a partial unique index allowing one open entry per user and location). See `docs/api/branding-timeclock-api.md`.
```bash
dotnet ef migrations add <Name> --project src/CafePos.Api --output-dir Data/Migrations
dotnet ef database update --project src/CafePos.Api     # or just run the app in Development
```

## Layout
```
src/CafePos.Api
  Controllers/    Auth, Catalog, Inventory (+ Locations, Recipes), Orders
  Domain/         Entities
  Data/           CafeDbContext (global query filters), migrations, dev seeder
  Services/       OrderService, StockService, TokenService, PricingCalculator
  Payments/       IPaymentProvider + Cash implementation
  Infrastructure/ ProblemDetails exception handler, current-location, paging
tests/CafePos.Api.Tests
```

## Key design points
- **Money**: `decimal(18,2)`, PHP, all prices VAT-inclusive; VAT (default 12%, per location) is extracted from totals.
- **Multi-location ready**: `LocationId` on Orders, OrderItems, Payments, StockMovements, IngredientStocks, IdempotencyRecords; EF global query filters from the JWT `location_id` claim; cross-location writes are blocked in `SaveChanges`.
- **Inventory**: per-location `IngredientStock` + append-only `StockMovement` ledger; sale decrement is a conditional `UPDATE ... WHERE QuantityOnHand >= n` inside the order-completion transaction.
- **Roles**: `Cashier`/`Manager`/`Admin` are per-location roles stored as staff assignments; `Owner` is a flag on the user (no assignments, all locations). Admins are confined to their active location.
- **Access token revocation**: access tokens (30 min) are not re-checked against the database per request. After a user is deactivated or an assignment is removed, an already-issued access token keeps working until it expires. Lockout is enforced at `login`, `refresh`, `switch-location` and `GET /auth/me`, and the user's refresh tokens (for an Admin's location-scoped removal, those bound to that location) are revoked. This window is accepted.
- **Orders**: client-generated order GUIDs; `Idempotency-Key` header on `POST /orders/{id}/complete`.
