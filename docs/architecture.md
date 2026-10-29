# Architecture

How the Cafe POS is put together. For endpoint details see [`api/`](api/README.md); for hosting see [`deployment.md`](deployment.md); for
the reasoning behind the main choices see the [ADRs](adr/README.md).

```
browser (React SPA)  --HTTPS/JSON-->  ASP.NET Core API (.NET 10)  -->  PostgreSQL 16
  src/lib/apiClient.ts                 backend/src/CafePos.Api           EF Core + Npgsql
  types from backend/openapi/v1.json
```

## Backend layers

Solution `backend/CafePos.slnx`. Dependencies point inward only.

```
Api  ->  Infrastructure  ->  Application  ->  Domain
 \_________________________->  Application
```

| Project | Holds | May reference |
|---|---|---|
| `CafePos.Domain` | Entities (one per file), `Roles`, enums, `PricingCalculator`. No package dependencies | nothing |
| `CafePos.Application` | Use cases: `Features/<X>/` with DTO records, FluentValidation validators, `I<X>Repository` ports and `<X>Service`; `Common/` with `Result`/`Error`, `ICurrentUser`, `ICurrentLocation`, `IUnitOfWork`, `PagedResult` | Domain |
| `CafePos.Infrastructure` | `CafeDbContext` (also the `IUnitOfWork`), entity configurations, migrations, repository implementations, Identity (`ApplicationUser`, `IdentityService`, `PinHasher`), `JwtTokenService`, `DevDataSeeder`, `DatabaseStartup` | Application, Domain |
| `CafePos.Api` | Thin controllers, JWT bearer setup and policies, `HttpCurrentUser`, rate limiting, forwarded headers, OpenAPI setup, exception handler, `Program.cs` | all |

Rules that hold in the code:

- Application never sees EF Core, ASP.NET Core or Npgsql. Repositories expose domain-shaped methods (no `IQueryable`, no
  `SaveChanges`); persistence is committed through `IUnitOfWork`, which also offers explicit transactions.
- Services return `Result` / `Result<T>` carrying an `Error` with an `ErrorKind`; they do not throw for expected failures. The
  Api layer maps an `Error` to a ProblemDetails response (`Http/ResultExtensions.cs`).
- Controllers only bind, call a service and map the result. Routes and authorization policies live on the controllers.
- Each layer registers itself through an extension method (`AddApplication`, `AddInfrastructure`, `AddApi`).

Tests mirror the layers: `tests/CafePos.Domain.Tests`, `tests/CafePos.Application.Tests` (services with mocked repositories) and
`tests/CafePos.Api.IntegrationTests` (the real API on a Testcontainers PostgreSQL).

## Request flow

1. Kestrel receives the request. Outside Development: forwarded headers (only from configured trusted proxies), HSTS, HTTPS redirection.
2. Rate limiter (per-route policies, see [ADR 0006](adr/0006-rate-limit-partitions-and-proxy-trust.md)); a hit returns `429` with `Retry-After`.
3. JWT bearer authentication validates the access token; authorization policies check the role claim.
4. `HttpCurrentUser` exposes the user id, role, `IsOwner` and the `location_id` claim as `ICurrentUser` / `ICurrentLocation`.
5. Model binding (duplicate JSON keys are rejected with `400`), then `FluentValidationFilter` validates the request DTO (`400` with an `errors` object).
6. The controller calls a service. The service uses repositories and `IUnitOfWork`; `CafeDbContext` applies the location filters.
7. The `Result` becomes a response; unexpected exceptions go to `GlobalExceptionHandler` as ProblemDetails.

Health endpoints `/health`, `/health/live` and `/health/ready` (database check) sit outside the API contract.

## Authentication and refresh

- **Access token**: JWT, 30 minutes (`Jwt:AccessTokenMinutes`), sent as `Authorization: Bearer`. The SPA keeps it in memory only. It carries the
  user, role and active `location_id` (claim set is pinned by `ContractTests`).
- **Refresh token**: random value stored hashed in `RefreshTokens`, delivered as the HttpOnly cookie `cafepos_refresh`
  (path `/api/v1/auth`, `SameSite=Lax`, `Secure` outside Development, 7 days by default via `Jwt:RefreshTokenDays`). It is bound to the
  location it was issued for and rotates on every use.
- **Rotation and reuse detection** (`AuthService`): the old token is claimed with one conditional UPDATE in the same transaction that inserts
  its successor, so two concurrent refreshes cannot both win. Presenting an already-revoked token, or losing the claim race, revokes every
  session of that user and clears the cookie.
- **Lockout**: ASP.NET Identity lockout (5 failures, 5 minutes) with the failure counter incremented atomically in SQL. A locked account gets `423`.
- **Revocation window**: an issued access token is not re-checked against the database, so a deactivated user's token works until it expires
  (accepted; see `backend/README.md`).

```mermaid
sequenceDiagram
    autonumber
    participant UI as React app (tab)
    participant Lock as Web Lock "cafepos-refresh"
    participant API as API
    participant DB as PostgreSQL

    UI->>API: POST /auth/login (email, password)
    API->>DB: check password, record failures atomically
    API-->>UI: 200 access token + Set-Cookie cafepos_refresh
    Note over UI: access token held in memory only

    UI->>API: GET /orders (Bearer, expired)
    API-->>UI: 401
    UI->>Lock: request lock (serialises tabs)
    Lock-->>UI: granted
    UI->>API: POST /auth/refresh (cookie, no body)
    API->>DB: BEGIN; UPDATE RefreshTokens SET revoked WHERE hash = old AND not revoked; INSERT new token; COMMIT
    alt claim succeeded
        API-->>UI: 200 new access token + rotated cookie
        UI->>API: retry GET /orders once (Bearer, new)
    else token already revoked or claim lost (reuse)
        API->>DB: revoke all refresh tokens of the user
        API-->>UI: 401 + cookie cleared
        Note over UI: session cleared, user sent to login
    end
    Note over UI,API: 429 / 5xx / network failure on refresh keeps the session; only 401 or 403 clears it
```

Switching location (`POST /auth/switch-location`) revokes the current refresh token and issues a new access token and cookie for the target location.

## Multi-location isolation

A location is a branch. Two mechanisms enforce isolation, and both are needed:

1. **Global query filters** in `CafeDbContext`: `Order`, `OrderItem`, `OrderItemModifier`, `Payment`, `IdempotencyRecord`, `IngredientStock`,
   `StockMovement`, `TimeEntry` and `StaffAssignment` are filtered on `LocationId == CurrentLocationId`, taken from the token's `location_id`
   claim through `ICurrentLocation`. With no location (anonymous, or an Owner "All locations" session) the filter matches nothing; code that
   must read across locations (auth flows, Owner views) uses an explicit `IgnoreQueryFilters()` inside the repository.
2. **Write guard** in `SaveChangesAsync`: new rows implementing `ILocationScoped` get the current `LocationId` stamped, a write to a different
   location throws, and `StockMovement` is append-only.

On top of that, services check `ICurrentUser` for role and scope: non-Owners may only act on their own active location (`403`), rows of other
locations are invisible (`404`), and location-bound actions in an Owner "All locations" session fail with `400 Location required`. The server
never trusts a client-supplied `locationId` on its own. The catalog (categories, items, modifiers, recipes, ingredients) is shared;
per-location price/tracking/threshold overrides live in `ItemVariationLocationOverrides`.

## Contract-first workflow

The backend's OpenAPI 3.1 document is the contract. The generated snapshot `backend/openapi/v1.json` is committed, and the frontend generates
`frontend/src/api/schema.d.ts` from it. Decisions: [ADR 0002](adr/0002-builtin-openapi.md), [0003](adr/0003-committed-contract-snapshot.md), [0004](adr/0004-generated-frontend-types.md).

To change an endpoint or DTO:

1. Change the backend (controller, DTO record, validator). Keep `[ProducesResponseType]` accurate.
2. Regenerate the snapshot:
   ```bash
   cd backend && dotnet build src/CafePos.Api -p:OpenApiGenerateDocuments=true
   ```
3. Regenerate the frontend types:
   ```bash
   cd frontend && npm run gen:api
   ```
4. Fix the frontend compile errors the new types surface, run `dotnet test` and `npm test`.
5. Commit the code, `backend/openapi/v1.json` and `frontend/src/api/schema.d.ts` together.

Safety nets: the `ContractTests` integration test fails if the live document differs from the snapshot, and the CI `contract` job
rebuilds both artifacts and fails on any `git diff`.

## Frontend structure

```
src/app/        App, router (lazy routes), providers, query client, AppShell, LocationSwitcher
src/api/        schema.d.ts (generated, do not edit), types.ts (aliases of schema types)
src/lib/        apiClient.ts (openapi-fetch client, auth refresh), errors.ts (ApiError), format, uuid, ...
src/components/ shared UI
src/features/   one folder per feature (auth, pos, orders, catalog, inventory, settings, locations, users, timeclock, branding, design-studio)
src/test/       Vitest setup, MSW server, fixtures
```

State: server state in TanStack Query; Zustand only for the cart, the auth session and UI state. Forms use React Hook Form with Zod.

## Persistence and migrations

EF Core migrations live in `CafePos.Infrastructure/Persistence/Migrations` and are never edited or renumbered. In Development the API migrates
and seeds an empty database on startup (`Database:MigrateOnStartup`, `Database:SeedDevData`); elsewhere migrations are applied explicitly,
and CI builds an EF migration bundle on `main`. Startup migration retries transient connection failures (`DatabaseStartup`). Connection strings
must include `GSS Encryption Mode=Disable` (see `deployment.md`).

## Testing and CI

| Level | Tool | Where |
|---|---|---|
| Unit | xUnit (Domain 10, Application 129 with NSubstitute) | `backend/tests` |
| Integration | xUnit + Testcontainers PostgreSQL (139), including contract, isolation and auth tests | `backend/tests/CafePos.Api.IntegrationTests` |
| Frontend | Vitest + React Testing Library + MSW (80) | `frontend/src/**/*.test.ts(x)` |
| End to end | Playwright with axe accessibility checks (39) | `e2e/` |

`.github/workflows/ci.yml` runs jobs `backend`, `contract`, `frontend`, `docker`, `e2e` and `migration-bundle` (main only). CI has not been run
on GitHub yet because the repo has no remote.
