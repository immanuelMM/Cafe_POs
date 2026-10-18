# Cafe POS backend (Phase 1)

.NET 8 Web API (controllers, `/api/v1`), EF Core + **PostgreSQL** (Docker), ASP.NET Core Identity + JWT.
API contract for the frontend: [`../docs/api/phase1-api.md`](../docs/api/phase1-api.md).

## Prerequisites
- .NET SDK 8.0.x (pinned by `global.json`)
- Docker (Docker Desktop must be running)

## Run
```bash
cd backend
docker compose up -d --wait db          # PostgreSQL 16 on localhost:5433 (user/db: cafepos)
dotnet run --project src/CafePos.Api    # http://localhost:5080  (Development)
```
In Development the app applies migrations and seeds dev data on startup (empty DB only). Swagger: http://localhost:5080/swagger

Seeded dev logins: `admin@cafe.local` / `Admin#Dev12345`, `cashier@cafe.local` / `Cashier#Dev12345` (dev only).

Run everything in containers instead: `docker compose --profile full up --build` (API on http://localhost:5080).
Reset the database: `docker compose down -v`.

## Configuration
| Key | Notes |
|---|---|
| `ConnectionStrings:Default` | Dev value lives in `appsettings.Development.json` (docker compose credentials, local only) |
| `Jwt:Key` | **Required, min 32 chars.** Dev key is in `appsettings.Development.json`; in any other environment set `Jwt__Key` (env var / secret store) |
| `Cors:Origins` | Defaults to `http://localhost:5173` |
| `Database:MigrateOnStartup` / `Database:SeedDevData` | `true` only in Development; run migrations explicitly elsewhere |

## Tests
```bash
cd backend
docker compose up -d --wait db
dotnet build
dotnet test
```
Integration tests boot the real API against a throwaway PostgreSQL database (created from the migrations, dropped afterwards), so the Docker db must be running. Use `TEST_PG_CONNECTION` to point at another server (the `Database=` part is replaced).

## Migrations
EF tool is a local tool (`dotnet tool restore`).
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
- **Orders**: client-generated order GUIDs; `Idempotency-Key` header on `POST /orders/{id}/complete`.
