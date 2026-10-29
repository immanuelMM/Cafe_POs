# Cafe POS backend (Phase 1 + Phase 2 multi-location)

.NET 10 Web API (controllers, `/api/v1`), EF Core + **PostgreSQL** (Docker), ASP.NET Core Identity + JWT.
API contract for the frontend: `openapi/v1.json` (source of truth), with narrative notes in [`../docs/api/`](../docs/api/README.md). How the layers fit together: [`../docs/architecture.md`](../docs/architecture.md); decisions: [`../docs/adr/`](../docs/adr/README.md).

## Prerequisites
- .NET SDK 10.0.x (pinned by `global.json`)
- Docker (Docker Desktop must be running)

## Run
```bash
docker compose up -d --wait db          # from the repo root: PostgreSQL 16 on localhost:5433 (user/db: cafepos)
cd backend
# One-time: the dev signing key is no longer committed; store one (32+ chars) in your user-secrets
dotnet user-secrets set "Jwt:Key" "DEV-ONLY-signing-key-do-not-use-in-production-0123456789" --project src/CafePos.Api
dotnet run --project src/CafePos.Api    # http://localhost:5080  (Development)
```
In Development the app applies migrations and seeds dev data on startup (empty DB only). Scalar API docs: http://localhost:5080/scalar/v1

### Seeded dev accounts
Created by `src/CafePos.Infrastructure/Persistence/DevDataSeeder.cs` on an empty database. Passwords are dev-only seed values.

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
| `ConnectionStrings:Default` | Dev value lives in `appsettings.Development.json` (docker compose credentials, local only). Always include `GSS Encryption Mode=Disable` (Npgsql would otherwise load Kerberos, which the runtime image lacks) |
| `Jwt:Key` | **Required, min 32 chars; never committed.** `dotnet run`: `dotnet user-secrets set "Jwt:Key" "<key>" --project src/CafePos.Api`. Docker compose: set by `docker-compose.yml` (a clearly named dev-only key, override with `JWT_DEV_KEY`). Anywhere else: `Jwt__Key` from the host's secret store. Startup fails fast without it |
| `Cors:Origins` | Defaults to `http://localhost:5173` |
| `ForwardedHeaders:KnownProxies[]` / `KnownNetworks[]` | Reverse proxies whose `X-Forwarded-*` headers are trusted. Empty (default) means not trusted; required behind a TLS-terminating ingress. See [`../docs/deployment.md`](../docs/deployment.md) |
| `RateLimiting:*` | `Enabled`, `Login`, `LoginPerIp`, `Refresh`, `SwitchLocation`, `TimeclockPunch` (each `PermitLimit` + `WindowSeconds`) |
| `Database:MigrateOnStartup` / `Database:SeedDevData` | `true` only in Development; run migrations explicitly elsewhere |

## Tests
```bash
cd backend
dotnet build
dotnet test
```
Integration tests boot the real API against a throwaway PostgreSQL database (created from the migrations, dropped afterwards). A PostgreSQL 16 container is started automatically with Testcontainers, so only a running Docker engine is needed (the compose db does not have to be up). Set `TEST_PG_CONNECTION` to use an existing server instead (the `Database=` part is replaced). Current counts: Domain 10, Application 129 (NSubstitute, no database), Api.IntegrationTests 139 (including tenant-isolation tests for cross-location access, contract drift and auth tests). Run one project with `dotnet test tests/CafePos.Application.Tests`.

## Migrations
EF tool is a local tool (`dotnet tool restore`).

Phase 2 added the `Phase2MultiLocation` migration (`src/CafePos.Infrastructure/Persistence/Migrations/20261018040804_Phase2MultiLocation`): the `IsOwner` flag on users, `StripeTerminalLocationId` on locations, the `ItemVariationLocationOverrides` table (per-location price/tracking/threshold overrides), and a nullable `RefreshTokens.LocationId` (for Owner cross-location sessions). Its data step promotes the sole Admin, if there is exactly one, to Owner. Orders, stock and the ledger already carried `LocationId` from Phase 1, so no backfill is needed. It applies automatically in Development; elsewhere run `dotnet ef database update`.

The `BrandingAssetsAndTimeClock` migration (`src/CafePos.Infrastructure/Persistence/Migrations/*_BrandingAssetsAndTimeClock`) adds tagline, primary color and logo/tarpaulin image columns (`bytea`) to `BrandSettings`, the clock PIN columns on users, and the `TimeEntries` table (with a partial unique index allowing one open entry per user and location). See `docs/api/branding-timeclock-api.md`.
```bash
# migrations live in CafePos.Infrastructure (a design-time factory means no startup project is needed)
dotnet ef migrations add <Name> --project src/CafePos.Infrastructure
dotnet ef migrations has-pending-model-changes --project src/CafePos.Infrastructure   # must report no changes
dotnet ef database update --project src/CafePos.Infrastructure --connection "<connection string>"   # or run the app in Development
```
Never edit or renumber an existing migration. `RefreshTokenReplacementAndIp` (plan C5) is the only migration added by the refactor.

## Layout
Clean Architecture, `Api -> Infrastructure -> Application -> Domain` (solution `CafePos.slnx`, central package management in `Directory.Packages.props`).
```
src/CafePos.Domain          entities (one per file), Roles, enums, PricingCalculator. No package dependencies.
src/CafePos.Application     Common/ (Result, Error, ICurrentUser, IUnitOfWork, validation helpers, uploads)
                            Features/{Auth,Locations,Users,VariationOverrides,Catalog,Inventory,Orders,Branding,TimeClock}/
                              (DTO records, FluentValidation validators, I*Repository ports, *Service), Payments/
src/CafePos.Infrastructure  Persistence/ (CafeDbContext + unit of work, Configurations/, Migrations/, Repositories/, DevDataSeeder),
                            Identity/ (ApplicationUser, IdentityService, PinHasher), Auth/ (JwtTokenService, JwtOptions)
src/CafePos.Api            thin Controllers, Http/ (current user, problem results), Validation/ (FluentValidation filter),
                            OpenApi/, RateLimiting/, Auth/ (policies), DependencyInjection.cs, Program.cs
tests/CafePos.Domain.Tests  tests/CafePos.Application.Tests (NSubstitute)  tests/CafePos.Api.IntegrationTests (Testcontainers)
```

## Key design points
- **Money**: `decimal(18,2)`, PHP, all prices VAT-inclusive; VAT (default 12%, per location) is extracted from totals.
- **Multi-location ready**: `LocationId` on Orders, OrderItems, Payments, StockMovements, IngredientStocks, IdempotencyRecords; EF global query filters from the JWT `location_id` claim; cross-location writes are blocked in `SaveChanges`.
- **Inventory**: per-location `IngredientStock` + append-only `StockMovement` ledger; sale decrement is a conditional `UPDATE ... WHERE QuantityOnHand >= n` inside the order-completion transaction.
- **Roles**: `Cashier`/`Manager`/`Admin` are per-location roles stored as staff assignments; `Owner` is a flag on the user (no assignments, all locations). Admins are confined to their active location.
- **Access token revocation**: access tokens (30 min) are not re-checked against the database per request. After a user is deactivated or an assignment is removed, an already-issued access token keeps working until it expires. Lockout is enforced at `login`, `refresh`, `switch-location` and `GET /auth/me`, and the user's refresh tokens (for an Admin's location-scoped removal, those bound to that location) are revoked. This window is accepted.
- **Refresh token**: HttpOnly cookie `cafepos_refresh` (path `/api/v1/auth`, `SameSite=Lax`, `Secure` outside Development, 7 days), rotated with an atomic claim and reuse detection. Lockout and time clock PIN failure counters are atomic. PINs stay 4-6 digits (rate limit plus lockout mitigate guessing).
- **Request hardening**: JSON bodies with duplicate property names get `400` (`AllowDuplicateProperties=false`); outside Development HSTS and HTTPS redirection are on; health endpoints are `/health`, `/health/live`, `/health/ready`.
- **Orders**: client-generated order GUIDs; `Idempotency-Key` header on `POST /orders/{id}/complete`.

## OpenAPI contract

The API contract is `backend/openapi/v1.json` (OpenAPI 3.1, committed; the frontend generates its types from it). Regenerate it after changing routes or DTOs:

```
cd backend && dotnet build src/CafePos.Api -p:OpenApiGenerateDocuments=true
```

`/health` (kept), `/health/live` and `/health/ready` (database) are not part of the contract document. `ContractTests` fail when the live document differs from the committed file, and also pin the access-token claims and refresh-cookie attributes. Build-time generation skips the JWT key check, migrations and seeding, so it needs no database or secrets.
