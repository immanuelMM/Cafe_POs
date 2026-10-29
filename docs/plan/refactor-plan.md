# Cafe POS System - Refactor Plan (bring to workspace standards)

- **App:** Cafe POS System (`C:\Users\merin\AI\Agentic-fullstack-web-dev\cafe-pos-system\`)
- **Type:** Full stack web app (existing; Phases 1-2 plus branding, time clock, design studio shipped)
- **Written:** 2026-10-29 by research-planner. Scope approved by the user: FULL refactor (backend, frontend, devops/QA/docs). Nothing is implemented by this plan.
- **Standards followed:** `full-stack-agent/CLAUDE.md`, `~/.claude/skills/dotnet-backend-standards/SKILL.md` (note: it lives at user level, not in `full-stack-agent/.claude/skills/`), `full-stack-agent/.claude/skills/react-frontend-standards/SKILL.md`.

## Status (updated 2026-10-29)

All milestones are **done** except M10, which was **skipped** (the user's default: rich domain entities are deferred). M0-M9, F0-F7, QA-T2, M11, M12, D1, D2 and M13 are complete. Outputs: [`../architecture.md`](../architecture.md), [`../adr/`](../adr/README.md), [`../deployment.md`](../deployment.md).

Decisions taken on the open questions: access token stays 30 min (Q2), refresh cookie stays `SameSite=Lax` (Q3), 4-digit time clock PINs are kept with rate limiting and atomic lockout as the mitigation (ADR 0007).

### Known follow-ups (not scheduled)

- ~~Add a `ForwardLimit` setting to `ForwardedHeadersSetup`~~ done (`ForwardedHeaders__ForwardLimit`, default 1); the Render Blueprint uses 2 (see `docs/deployment.md`, "Render").
- Add an E2E test (two tabs) for the cross-tab refresh Web Lock; today only the client unit tests cover it.
- CI (`.github/workflows/ci.yml`) first ran on GitHub on 2026-10-29 (remote `origin`); keep it green before new feature work.
- The modifier `active` toggle in the UI is dead: the backend ignores `active` on an individual modifier. Decide whether to honor it or remove the toggle.
- Enum-like fields (roles, order status, stock movement reason, ...) are plain strings in the OpenAPI contract; model them as enums and regenerate.
- 4xx responses (400, 401, 403, 404, 409, 422, 423, 429) are not documented in the OpenAPI contract.
- Recommended: optional M10 (rich domain entities) if the team wants it later.

## 0. Hard constraint and how it is enforced

**Existing API behavior and features must be preserved** (multi-location scoping, auth, payments, branding, timeclock, design studio). Enforcement mechanisms, built in the first milestones so every later milestone is checked automatically:

1. **Baseline capture (M0):** record test counts and the current Swashbuckle OpenAPI JSON (`/swagger/v1/swagger.json`) as the "golden" contract.
2. **Committed OpenAPI snapshot (M3):** `backend/openapi/v1.json`, generated at build time and diffed against the golden file once. After that, a test and a CI step fail on ANY drift. The Clean Architecture restructure (M4-M9) therefore cannot silently change a route, status code or DTO shape.
3. **Claims/cookie contract test (M3):** decode an issued access token and assert the exact claim set; assert refresh cookie attributes (see section 5).
4. The existing integration suite (about 99 tests per README, including tenant-isolation tests) must pass at every milestone.

### Intentional contract changes (complete list; everything else must be identical)

| # | Change | Kind | Default |
|---|---|---|---|
| C1 | OpenAPI document URL `/swagger/v1/swagger.json` becomes `/openapi/v1.json`; interactive docs `/swagger` becomes Scalar at `/scalar/v1` | Dev tooling only, Development environment only | Do it |
| C2 | OpenAPI document is 3.1 (built-in generator default in .NET 10) instead of 3.0 | Doc format only | Do it |
| C3 | New health endpoints `/health/live` and `/health/ready` (DB check); `/health` stays | Additive | Do it |
| C4 | Rate limiting on `login`, `refresh`, `switch-location` returns `429` when exceeded | Additive behavior | Do it (limits generous enough for kiosk/shared-IP use; configurable) |
| C5 | `RefreshTokens` gains `ReplacedByTokenHash` and `CreatedByIp` columns (additive migration) | Schema, additive | Do it |
| C6 | Validation error message TEXT may differ if DataAnnotations are replaced by FluentValidation. Status code (400) and shape (`errors: { field: string[] }`, camelCase keys) must be identical | Wording only | Try to keep wording identical; list any diffs in the PR |
| C7 | Access token lifetime 30 min -> 15 min (standard says 5-15) | Behavior | **Do NOT change; needs user decision (Q2)** |
| C8 | Refresh cookie `SameSite=Lax` -> `Strict` (standard) | Behavior | **Do NOT change; needs user decision (Q3)** |

No endpoint, status code, DTO field, enum string value, claim name, cookie name/path or header is intentionally changed beyond this table.

## 1. Current state (findings from reading the repo)

### Backend (`backend/`)
- Single project `src/CafePos.Api` on **net8.0** (EF Core / Npgsql / Identity / JwtBearer 8.0.11, Swashbuckle 6.6.2). `global.json` pins SDK 8.0.404. Solution is `CafePos.sln` (needs `.slnx` on .NET 10). `.NET 8` support ends 10 Nov 2026 (12 days from today), so M1 is time-sensitive.
- Folders: `Controllers` (9 files, ~71 route attributes: Auth, Branding, Catalog x3 controllers in one file, Inventory, Locations, Orders, TimeClock, Users, VariationOverrides), `Data` (`CafeDbContext` + 6 migrations + `DevDataSeeder`), `Domain/Entities.cs` (all entities in one file), `Dtos/Dtos.cs`, `Infrastructure` (`ApiException`, `GlobalExceptionHandler`, location authorization policies, claims helpers, paging, uploads), `Payments` (`IPaymentProvider` + Cash), `Services` (`OrderService`, `StockService`, `TokenService`, `PricingCalculator`).
- Controllers use `CafeDbContext` directly (no repositories, no unit of work). Errors are thrown as `ApiException` and rendered by `IExceptionHandler` as RFC 9457 ProblemDetails.
- **Auth already largely matches the standard:** short-lived JWT (30 min) in memory on the client, HttpOnly refresh cookie (`cafepos_refresh`, `Path=/api/v1/auth`, `SameSite=Lax`), SHA-256 hashed refresh tokens in `RefreshTokens`, rotation on every refresh, reuse detection revoking all the user's tokens, lockout, `FallbackPolicy = RequireAuthenticatedUser`, named policies (`OwnerOnly`, `AdminUp`, `ManagerUp`, `LocationAccess`). So "implement JWT access/refresh" is mostly a **move and harden**, not a rewrite. Gaps vs standard: `ReplacedByTokenHash`/`CreatedByIp` columns, rate limiting, `ICurrentUser`, options validated on start, `TimeProvider`.
- Multi-location scoping: EF global query filters keyed on `CurrentLocationId` from the `location_id` claim; cross-location writes blocked in `SaveChanges`; Owner cross-location reads use `IgnoreQueryFilters()` via `ILocationAccess`. This is the highest-value behavior to protect.
- Inventory decrement is a conditional `UPDATE ... WHERE QuantityOnHand >= n` inside the order-completion transaction; `Idempotency-Key` on `POST /orders/{id}/complete`. Must survive the move into Application/Infrastructure unchanged.
- Tests: `tests/CafePos.Api.Tests` (xunit 2.5.3, `Mvc.Testing` 8.0.11), 8 files, boots the real API against a throwaway DB on the docker-compose Postgres (`localhost:5433`, or `TEST_PG_CONNECTION`). Not Testcontainers.
- Docker: `src/CafePos.Api/Dockerfile` on `dotnet/sdk:8.0` / `aspnet:8.0`; root `docker-compose.yml` (db `postgres:16-alpine`, api, web; volume `backend_cafepos-pgdata`). `.config/dotnet-tools.json` exists (EF tool). **No `.github/` folder: no CI today.**

### Frontend (`frontend/`)
- React 19, React Router 7, TanStack Query 5, RHF 7 + Zod 3 + `@hookform/resolvers` 3, Zustand 5, Vite 6, TS ~5.6, ESLint 9 flat config. Matches the standard's libraries.
- Gaps vs `react-frontend-standards`:
  1. **Hand-written API contract:** `src/lib/types.ts` (DTOs) + `src/lib/api.ts` (fetch wrapper with single-flight refresh) + per-feature `*Api.ts` files with inline types. No `src/api/schema.d.ts`, no `openapi-typescript`/`openapi-fetch`.
  2. **No `src/app/` folder:** `App.tsx`, `main.tsx`, `AppShell`, `LocationSwitcher` sit at root/`components/`. Only `DesignStudioPage` is lazy-loaded; every other page is eagerly imported.
  3. **No tests at all** (no Vitest/RTL/MSW/Playwright, no `test` script).
  4. **No Prettier**, no `.env.example` check against all `VITE_` vars (only `VITE_API_BASE_URL` is used; fine), no CI.
  5. Feature folders (`auth, branding, catalog, design-studio, inventory, locations, orders, pos, settings, timeclock, users`) are already feature-oriented. Deviations: `xApi.ts` mixes fetchers + hooks (standard: `useX.ts`), Zod schemas are inline in pages (standard: `xSchema.ts`), no colocated tests. `design-studio` (~35 files, Konva canvas, its own document types in `design-studio/types.ts`) is a deliberate large feature; treat as an exception, do not restructure.
  6. `frontend/Dockerfile` uses `node:20-alpine`; Node 20 reached end of life in April 2026. Move to Node 22 or 24 LTS (verify at implementation time).
  7. Auth store already keeps the access token in memory with an HttpOnly refresh cookie and no localStorage use for tokens (grep shows `localStorage`/`sessionStorage` hits only in design-studio `draft.ts`; devops/QA to confirm those are drafts, not tokens).

## 2. Research findings and decisions

### D1. OpenAPI: built-in `Microsoft.AspNetCore.OpenApi` + Scalar (replace Swashbuckle)
- Microsoft docs (updated 2026-10) state: built-in support since .NET 9 (`Microsoft.AspNetCore.OpenApi` for runtime generation, `Microsoft.Extensions.ApiDescription.Server` for build-time generation), **OpenAPI 3.1 is the default document version in .NET 10** (3.2 arrives with .NET 11), and the generator emits documents only, so a UI (Swagger UI or Scalar) must be added separately. Source: [Generate OpenAPI documents, ASP.NET Core 10 docs](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi?view=aspnetcore-10.0).
- Swashbuckle is not dead: v10.x supports .NET 10 with opt-in 3.1, but it moved to `Microsoft.OpenApi` 2.x namespaces and security-requirement changes (breaking `Microsoft.OpenApi.Models`, `Reference` removal), so staying means a rewrite of `Program.cs` OpenAPI setup anyway. Sources: [Microsoft Q&A: Swagger setup error after upgrade](https://learn.microsoft.com/en-us/answers/questions/5686409/swagger-setup-error-after-upgrade), [Swashbuckle migration to Scalar (DEV)](https://dev.to/jfmeyers/swashbuckle-is-dead-heres-how-to-migrate-to-scalar-in-net-10-155d) (blog; its "dead" claim is contradicted by the Q&A, so treated as opinion).
- **Decision:** move to the built-in generator + `Scalar.AspNetCore` (Development only), because (a) the workspace standard mandates it for new/upgraded code, (b) Swashbuckle needs a rewrite on .NET 10 regardless, (c) build-time generation gives a committed `openapi/v1.json` the frontend can generate from without a running server. Mixing `AddSwaggerGen` and `AddOpenApi` causes conflicts, so Swashbuckle is removed completely in M3.
- Confidence note: sources are Microsoft docs (strong) plus Q&A/blogs (weaker). Verify exact package versions (`Microsoft.AspNetCore.OpenApi` 10.0.x, `Scalar.AspNetCore`) on NuGet when implementing.
- Known gotchas to spike in M3: (1) build-time generation (`GetDocument.Insider`) runs the app's `Program.cs` up to `Run()`; today `Program.cs` migrates/seeds and validates the JWT key between `Build()` and `Run()`, so guard that code so build-time generation does not touch the DB (and supply a dummy `Jwt__Key` for the generation step). (2) Verify nullability/`required` output: the built-in generator may not mark non-nullable record properties as required, which would make `openapi-typescript` emit optional (`?`) properties; if so add a schema transformer (or `required` members) so generated types match reality. (3) Verify `JsonStringEnumConverter` is reflected as string enums in the schema. (4) File upload endpoints (`IFormFile`) and image-bytes endpoints (logo/tarpaulin/item photos) need explicit `[Consumes]`/`[Produces]` so the generated types are usable.

### D2. EF Core 10 / Npgsql
- Npgsql EF provider 10.0.x exists (NuGet lists 10.0.2; verify latest at implementation). Its only listed breaking change is the `cidr`/`IPNetwork` mapping, not used here. EF Core 10 breaking changes noted: parameterized collections use multiple parameters, `ExecuteUpdateAsync` accepts non-expression lambdas (source compatible). Sources: [Npgsql 10.0 release notes](https://www.npgsql.org/efcore/release-notes/10.0.html), [EF Core 10 breaking changes](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes).
- EF 9+ throws on `Migrate()` when the model differs from the snapshot ("pending model changes"), so M1 must run `dotnet ef migrations has-pending-model-changes` and resolve any diff before anything else.
- Keep **PostgreSQL 16** in compose (do not change the DB major version inside a refactor; existing dev volume `backend_cafepos-pgdata`).

### D3. Repository + Unit of Work vs the original plan
- The original plan (`cafe-pos-inventory-plan.md` section 3) recommended skipping repositories (use `DbContext` directly), citing 2026 community consensus. The workspace standard and the user's approval of this refactor override that for this app. **Record this as an ADR in docs** so future readers are not confused. Mitigation for the criticisms in that plan (hidden EF features): repositories expose intention-revealing methods incl. DTO projections with `AsNoTracking`, never `IQueryable`; multi-step stock/order logic keeps an explicit transaction through `IUnitOfWork`.

### D4. Frontend client generation
- `openapi-typescript` (7.x; supports OpenAPI 3.0 and 3.1) + `openapi-fetch` (typechecks params, body, responses against the generated `paths`; accepts a custom `fetch`). Recommend `noUncheckedIndexedAccess` per its docs, but enable only after migration to avoid a wave of unrelated compile errors. Sources: [openapi-ts.dev introduction](https://openapi-ts.dev/introduction), [openapi-fetch docs](https://openapi-ts.dev/openapi-fetch/). The docs page did not cover middleware or multipart; verify in the F2 spike (see risk R6).
- **Generate from the committed file** `../backend/openapi/v1.json` (works offline and in CI) with an optional second script that reads the live `http://localhost:5080/openapi/v1.json` (the standard's URL form).

### D5. Testing
- Replace the docker-compose-dependent fixture with **Testcontainers.PostgreSql** in an xUnit collection fixture (one container per run, a fresh database per `ApiFactory` to keep today's isolation). Confirmed pattern across several .NET 10 guides ([example](https://se-dotnet-ecosystem.pages.dev/articles/testing/asp-net-core-integration-testing-best-practises/)); none addressed xUnit v3 specifically, so **stay on xUnit v2 (latest 2.9.x) unless v3 is verified**; do not mix a test-framework migration into this refactor. Use image `postgres:16-alpine` to match production-like compose.

## 3. Target architecture

### Backend solution layout (per `dotnet-backend-standards`)
```
backend/
  global.json (10.0.x, rollForward latestFeature)  Directory.Build.props  Directory.Packages.props
  .editorconfig  CafePos.slnx  .config/dotnet-tools.json  openapi/v1.json (generated, committed)
  src/
    CafePos.Domain/          entities (one type per file), Roles, enums, domain errors. No package deps.
    CafePos.Application/     Common/ (Result, Error, PagedResult, IUnitOfWork, ICurrentUser, ICurrentLocation,
                             ITokenService, IClock via TimeProvider), Features/{Auth,Locations,Users,Catalog,
                             Inventory,Orders,Branding,DesignStudio,TimeClock}/ (records DTOs, I*Repository,
                             *Service, validators), Payments/ (IPaymentProvider, Cash)
    CafePos.Infrastructure/  Persistence/ (CafeDbContext, Configurations/, Migrations/, Repositories/, UnitOfWork,
                             Interceptors, DevDataSeeder), Identity/ (ApplicationUser, identity service),
                             Auth/ (JwtTokenService, refresh tokens), Storage/ (uploads), DependencyInjection.cs
    CafePos.Api/             Controllers (thin), middleware (GlobalExceptionHandler), auth policies, OpenAPI,
                             Program.cs, DependencyInjection.cs, Dockerfile
  tests/
    CafePos.Domain.Tests/  CafePos.Application.Tests/  CafePos.Api.IntegrationTests/ (renamed from CafePos.Api.Tests)
```
Dependency rule `Api -> Infrastructure -> Application -> Domain`.

Design decisions to apply:
- **Controllers stay controllers** (project's existing style, 71 routes; no switch to minimal APIs). Routes, route constraints and `[Authorize]` policies unchanged.
- **`ApplicationUser` lives in Infrastructure/Identity** (it derives from `IdentityUser<Guid>`, which would break Domain's "no dependencies" rule). Domain entities reference users by `Guid` FK only; if any entity currently has an `ApplicationUser` navigation property, replace it with the FK (no schema change). Application talks to users through an `IIdentityService` (create, find, check password, lockout) so it never references `UserManager`.
- **Global query filters stay in `CafeDbContext`** (Infrastructure), using `ICurrentLocation` (interface in Application, `HttpCurrentLocation` implementation in Api). Repositories take an explicit scope parameter for the Owner cross-location case (`IgnoreQueryFilters()` stays inside Infrastructure only); the `SaveChanges` cross-location-write guard stays.
- **Result pattern:** expected failures become `Result`/`Error` (NotFound, Validation, Conflict, Forbidden, Unauthorized, Locked 423, Unprocessable 422, `LocationRequired` 400, stock-conflict 409 with `ingredient/required/available` extension members). The Api layer maps each error to the **same** ProblemDetails title/detail/status/extensions as today's `ApiException` (the frontend displays `detail` and reads the stock-conflict extensions). `ApiException` may be kept temporarily as an adapter during migration and removed in M9.
- **Validation:** FluentValidation per request DTO, applied via a validation filter that returns the same `ValidationProblemDetails` shape (camelCase keys). Verify the current recommended integration at implementation time (the older `FluentValidation.AspNetCore` auto-validation package is no longer the recommended route; use `FluentValidation.DependencyInjectionExtensions` plus a filter).
- **Time:** inject `TimeProvider` for "now"; **do not** change entity `DateTime` columns to `DateTimeOffset` (would change JSON output and risk migrations).
- **Money/rounding, VAT extraction, receipt numbering, pricing (`PricingCalculator`), payment provider abstraction:** move verbatim (PricingCalculator and Cash provider become Application/Domain code with unit tests; they are pure logic and the best unit-test targets).
- **Entity encapsulation (private setters, behavior methods):** deferred to the last, optional milestone B10 because it carries the highest regression risk for the least user-visible value.

### Frontend target structure (`react-frontend-standards`)
```
frontend/src/
  app/        App.tsx, router (lazy route modules), providers (QueryClient), layout (AppShell, LocationSwitcher), main entry stays src/main.tsx
  api/        schema.d.ts (generated, never hand-edited)
  lib/        apiClient.ts (openapi-fetch + auth/refresh fetch wrapper), errors (ApiError/NetworkError), format.ts, color.ts, uuid.ts, zonedTime.ts
  components/ shared UI (ui.tsx, Modal.tsx)
  features/<feature>/  pages, useX.ts hooks, xSchema.ts (Zod), colocated *.test.tsx
```
- Types in features are aliases of `components['schemas'][...]` from the generated schema. `design-studio/types.ts` (document model stored as an opaque payload by the backend) stays hand-written; add a Zod schema at the load boundary if not present, because the backend contract cannot type it.
- Keep: `ApiError` display logic, `Idempotency-Key` behavior, single-flight refresh, in-memory access token, `credentials: 'include'`.

## 4. Milestones (ordered; each leaves the app building with tests passing and is one commit or a short series)

Agents: **BE** = backend-dotnet-dev, **FE** = frontend-react-dev, **QA** = qa-automation, **DOC** = docs-writer, **OPS** = devops-engineer. Work on a branch `refactor/clean-arch` in the app repo. Because BE and FE commit to the same repo in parallel, every commit must `git add` only its own paths (`backend/...` or `frontend/...`) and run builds/tests for its own side.

### Phase A - Foundation (backend serial; frontend prep in parallel)

**M0 - Baseline and safety net (QA, 0.5 day)**
- Record: clean `git status`; `dotnet build` + `dotnet test` result and test count; `npm run lint` + `npm run build`; confirm `frontend/.env` is untracked (root `.gitignore` ignores `.env`, `.env.*`, `bin/`, `obj/`, `node_modules/`, `dist/`).
- Start the stack, save Swashbuckle's `/swagger/v1/swagger.json` to `docs/plan/baseline/swagger-golden.json` (reference only, remove after G2).
- Save one decoded dev access token's claim names and the refresh `Set-Cookie` string as the auth baseline.
- Exit: baseline numbers written into the PR description; no code changes.

**M1 - .NET 10 upgrade in place (BE, 0.5-1 day).** Still a single project, Swashbuckle kept for now.
- `global.json` to the installed .NET 10 SDK (rollForward `latestFeature`; run `dotnet --list-sdks` first and tell the user if missing). TFM `net10.0` for src and tests. Packages: JwtBearer, Identity.EntityFrameworkCore, EF Design, `Npgsql.EntityFrameworkCore.PostgreSQL` to 10.0.x, `Microsoft.AspNetCore.Mvc.Testing` 10.0.x. Update the local `dotnet-ef` tool to 10.x.
- Dockerfile images to `sdk:10.0` / `aspnet:10.0`.
- Run `dotnet ef migrations has-pending-model-changes`; if the model differs, add a separate, reviewed migration (or snapshot-only refresh) rather than hiding it.
- If Swashbuckle 6.6.2 fails to run on .NET 10, pull the M3 swap forward instead of upgrading Swashbuckle.
- Exit: all tests pass on net10.0; `docker compose up -d --build` boots; Postgres still 16. Commit `chore(backend): upgrade to .NET 10 LTS`.

**M2 - Testcontainers fixture (QA, parallel-safe with M3 once M1 is merged, 0.5-1 day)**
- Add `Testcontainers.PostgreSql` (postgres:16-alpine), collection fixture owning the container, `ApiFactory` creates a unique database per factory in it (same isolation as today). Keep `TEST_PG_CONNECTION` as an override for CI service containers. Tests no longer need `docker compose up db`.
- Exit: same test count passes with the compose db stopped. Commit `test(backend): use Testcontainers PostgreSQL`.

**M3 - OpenAPI switch + contract snapshot (BE, 1-1.5 days) -> GATE G1**
- Remove Swashbuckle; add `AddOpenApi` (OpenApiVersion 3.1, Bearer scheme via document transformer, title/version), `MapOpenApi` and `Scalar.AspNetCore` (Development only); add `Microsoft.Extensions.ApiDescription.Server` to generate `backend/openapi/v1.json` on build into a committed folder.
- Guard `Program.cs` startup work (migrate, seed, key check) so build-time generation does not hit the DB.
- Annotate controllers so the document is accurate: `[ProducesResponseType]` for every success type, `ProducesProblem`/`ProducesResponseType<ProblemDetails>` for 400/401/403/404/409/423/422 as applicable, `[Consumes("multipart/form-data")]` on upload endpoints, `[Produces]` for image endpoints, `required`/nullability fixes (see D1 gotchas).
- Compare the new document with the M0 golden Swashbuckle file: same paths, methods, parameters, status codes, DTO property names/types/enums; list every difference in the PR (only C1/C2 allowed).
- Add contract tests: (a) `GET /openapi/v1.json` equals the committed snapshot; (b) decoded token claim set and refresh cookie attributes equal the M0 baseline.
- Exit: build + tests pass; snapshot committed; tell FE "contract ready". Commit `feat(api): built-in OpenAPI 3.1 + Scalar, committed contract snapshot`.

**F0 - Frontend prep (FE, parallel with M1-M3, no contract dependency, 1 day)**
- Add `src/app/` (move `App.tsx`, `AppShell`, `LocationSwitcher`; add `router.tsx`, `providers.tsx`); lazy-load route modules for the large pages (orders, catalog, inventory, settings, users, locations, timeclock, branding/menu banner, design studio, keep POS and login eager for first paint).
- Add Prettier + `format`/`format:check` scripts (format in a single isolated commit so diffs stay reviewable) and ensure ESLint config covers `typescript-eslint` + `react-hooks`.
- Add Vitest + React Testing Library + MSW + jsdom, `test` script (no tests yet beyond a smoke test).
- Move Docker image to Node 22/24 LTS (OPS can own this with D1).
- Exit: `npm run lint`, `npm run build`, `npm test` pass. Commit `refactor(frontend): app folder, lazy routes, tooling`.

**D1 - CI v1 (OPS, parallel, after M2, 0.5 day)**
- `.github/workflows/ci.yml`: backend job (setup-dotnet 10.x, restore, build `-warnaserror` once enabled, test; Testcontainers works on `ubuntu-latest`), frontend job (`npm ci`, lint, build, test). Add Dependabot (nuget, npm, docker, github-actions). Triggers: push + PR. Exit: green run on the branch.

### Phase B - Restructure (backend) in parallel with typed-API migration (frontend)

The contract is frozen by the M3 snapshot, so these tracks do not block each other. FE must not start F1 before G1.

**Backend track**

**M4 - Solution scaffolding (BE, 0.5 day).** `Directory.Build.props` (`Nullable`, `ImplicitUsings`, `AnalysisLevel=latest-recommended`, `TreatWarningsAsErrors` only in CI at first), `Directory.Packages.props` (CPM, `ManagePackageVersionsCentrally`), `.editorconfig`, convert `CafePos.sln` to `CafePos.slnx` (`dotnet sln migrate`), create empty `CafePos.Domain`, `CafePos.Application`, `CafePos.Infrastructure` and wire references. Update Dockerfile to copy all csproj files for restore caching. No code moves.

**M5 - Domain extraction (BE, 1 day).** Split `Entities.cs` into one file per type under `CafePos.Domain`; move `Roles`, enums. Move `ApplicationUser` to Infrastructure/Identity (replace navigation properties with Guid FKs if present). Namespaces change from `CafePos.Api.Domain` to `CafePos.Domain`; fix tests mechanically. Entities keep public setters in this milestone.

**M6 - Infrastructure persistence + Application/Common abstractions (BE, 1.5-2 days)**
- Move `CafeDbContext`, split `OnModelCreating` into `IEntityTypeConfiguration<T>` classes (query filters and `SaveChanges` guard remain in the context), move `DevDataSeeder`, uploads/storage helper, and **all migrations with their original IDs untouched**.
- Add `Application/Common`: `IUnitOfWork`, `ICurrentLocation`, `ICurrentUser`, `PagedResult<T>`, `Result`/`Error`. `CafeDbContext` implements `IUnitOfWork`.
- **Migration safety procedure (risk R2):** (1) `dotnet ef migrations has-pending-model-changes --project CafePos.Infrastructure --startup-project CafePos.Api`; (2) the snapshot and Designer files embed entity names with the old namespace (`CafePos.Api.Domain.*`), so if a diff appears, create a throwaway migration, confirm its `Up` is empty, delete it and keep only the refreshed snapshot (do not edit or renumber existing migrations); (3) upgrade test: restore a database created by the pre-refactor build (all six migrations applied, with data) and apply the new build's migrations: `__EFMigrationsHistory` unchanged, schema unchanged, app boots; (4) fresh-database run also passes (existing tests).
- Set `MigrationsAssembly` to Infrastructure and add an `IDesignTimeDbContextFactory` or documented `--project/--startup-project` flags; update README commands.

**M7a-M7f - Application layer, one feature per commit (BE, ~4-6 days total).** For each feature: records DTOs and requests into `Application/Features/<X>`, `I<X>Repository` (per aggregate root, no `IQueryable`, no `SaveChanges`) + Infrastructure implementation, `<X>Service` returning `Result<T>`, FluentValidation validators, controller reduced to bind -> validate -> call -> map. Routes/policies unchanged. Add Application unit tests (NSubstitute) alongside each. Order:
1. **M7a Locations + Users + VariationOverrides** (staff assignments, Owner/Admin scoping; lowest coupling to orders).
2. **M7b Catalog** (categories, items + photos, modifier lists).
3. **M7c Inventory + Stock** (ingredients, stock, movements, restock, adjustments, recipes).
4. **M7d Orders + Payments + Pricing** (the critical path: transaction + conditional UPDATE + idempotency record + payment provider + receipt; needs explicit transaction support on `IUnitOfWork`; run the isolation-adversarial and Phase2 fix-batch tests after every change).
5. **M7e Branding + DesignStudio + TimeClock** (uploads as bytes, PIN hashing, partial unique index for open time entries).
6. **M7f Auth** goes with M8 (below) to avoid touching tokens twice.
- Exit for each: build, full test suite, **contract snapshot unchanged**.

**M8 - Auth and token service (BE, 1.5 days)**
- Move `AuthController` logic to `AuthService` + `ITokenService` in Infrastructure/Auth. **Keep token claim names/values, cookie name/path/attributes, refresh rotation, reuse-detection and lockout semantics identical** (baseline test from M3 guards this). Keep `JwtSecurityTokenHandler` unless claim output is proven identical with `JsonWebTokenHandler` (outbound claim-type mapping can differ; see R3). Set `RoleClaimType`/`NameClaimType` and `MapInboundClaims` explicitly and verify `IsInRole` still works.
- Add C5 (`ReplacedByTokenHash`, `CreatedByIp`) via additive migration; add C4 rate limiting (`AddRateLimiter`, named policy on login/refresh/switch-location, configurable).
- `JwtOptions`, `DatabaseOptions` via `AddOptions<T>().BindConfiguration().ValidateDataAnnotations().ValidateOnStart()` (keeps "Jwt:Key min 32 chars" startup failure). Introduce `ICurrentUser` and `TimeProvider`.
- Tests: refresh rotation, reuse revokes all sessions, expired/revoked refresh, switch-location revokes old cookie, 401 no token, 403 wrong role, 429 limit.

**M9 - Composition root and cleanup (BE, 1 day).** `AddApplication()` / `AddInfrastructure(config)` / `AddApi()` extension methods, short `Program.cs`; health endpoints (C3); remove `ApiException` adapter, old folders (`Services/`, `Dtos/`, `Data/`); turn on `TreatWarningsAsErrors` and fix warnings; Dev-only migrate/seed retained (production uses a migration bundle). -> **GATE G2: restructure complete, contract snapshot unchanged vs M3.**

**M10 (OPTIONAL, needs user OK) - Rich domain entities (BE, 2-3 days).** Private setters + behavior methods for `Order` (status transitions), `StockMovement` (append-only), others as low-risk. Recommend deferring until after G2 and the frontend migration land.

**Frontend track (starts after G1)**

**F1 - Generated types (FE, 0.5 day).** Add `openapi-typescript` (dev) + `openapi-fetch`; scripts `api:types` (`openapi-typescript ../backend/openapi/v1.json -o src/api/schema.d.ts`) and `api:types:live` (URL form). Commit `src/api/schema.d.ts`. Add a CI check that regenerating produces no diff. Add `src/api/README` note "generated, do not edit". Exit: build passes (schema compiled, nothing consumes it yet).

**F2 - Typed client with the existing auth behavior (FE, 1-1.5 days).** `lib/apiClient.ts`: `createClient<paths>({ baseUrl: VITE_API_BASE_URL, credentials: 'include', fetch: customFetch })`, where `customFetch` reuses today's logic: attach in-memory bearer, on 401 do the single-flight `refreshSession()` then resend a **cloned** Request once, then clear session (-> login). Map non-2xx ProblemDetails to the existing `ApiError` so `errorMessage()` and stock-conflict messages keep working. Keep `api()`/`apiFetch()` as a compatibility shim so features migrate one at a time. Multipart uploads (item photo, logo, tarpaulin) can stay on `apiFetch` with `FormData` (typed by a small helper) if `openapi-fetch` multipart ergonomics are poor. Spike results (retry-with-body, FormData, 204 handling) go in the PR.
- Tests (Vitest + MSW): attaches bearer; 401 -> one refresh -> retry; concurrent 401s trigger ONE refresh; refresh failure clears the store.

**F3-F6 - Migrate features to generated types (FE, ~3-4 days total, one commit per slice, each builds and passes lint)**
- F3 auth + locations + users + settings (`authStore`'s `User`/`AuthResponse` from schema).
- F4 catalog + inventory.
- F5 orders + pos + receipt (cart store stays Zustand; server DTOs from schema; `Idempotency-Key` header kept).
- F6 branding + timeclock + design-studio data calls (design document payload stays hand-typed + Zod at the boundary).
- Per slice: replace inline types with schema aliases, move hooks to `useX.ts`, move Zod schemas to `xSchema.ts` (one per form), add loading/error/empty-state checks that are missing, remove the slice's imports from `lib/types.ts`.
- **F7 - Remove `lib/types.ts` and the `api()` shim**; enable `noUncheckedIndexedAccess` if it is cheap; confirm no hand-written DTO duplicates remain (grep). Commit `refactor(frontend): remove hand-written API types`.

**QA-T2 - Backend unit tests (QA, alongside M7; finish by G2).** Domain.Tests (pricing/VAT rounding, entity invariants as extracted), Application.Tests (services with mocked repositories: order completion success/insufficient stock 409/idempotent replay, location scoping rules, user/role rules, time-clock one-open-entry), integration tests renamed to `CafePos.Api.IntegrationTests`.

### Phase C - Verification, ops, docs

**M11 - Frontend component tests (QA/FE, after F3-F6, 2 days).** Vitest + RTL + MSW (query by role/label): LoginPage (including multi-location picker and 423 locked), ProtectedRoute role/owner gating, PosPage + CartPanel + cartStore totals, PaymentPanel cash change/shortfall, OrdersPage list/empty/error, InventoryPage low-stock states, LocationSwitcher, UsersPage form validation.

**M12 - Playwright E2E (QA, after G2 + F7, 1.5 days).** Against `docker compose up`; flows: cashier login -> add item with modifiers -> pay cash -> receipt -> stock decrement visible to manager; manager at two locations cannot see the other's orders; owner cross-location read-only + "Location required" on POS; branding save; time clock PIN in/out; token refresh after expiry. Viewports: 390px, 768px, 1280px. Keep a tag for a fast smoke subset on every PR and the full suite nightly/on main.

**D2 - Docker/Compose and CI v2 (OPS, after G2, 1 day).**
- Backend Dockerfile: multi-project restore caching, non-root user, `HEALTHCHECK` on `/health/live`; compose `api` healthcheck and `depends_on: service_healthy` for web; keep db `postgres:16-alpine`, port 5433 and the external volume name; compose uses an `.env.example` for non-secret dev values.
- Frontend Dockerfile: Node 22/24 LTS (verify), unchanged nginx.
- CI v2 jobs: `backend` (build with `TreatWarningsAsErrors`, unit + integration tests, `dotnet list package --vulnerable`), **`contract`** (rebuild, `git diff --exit-code backend/openapi/v1.json`, then `npm run api:types` + `git diff --exit-code frontend/src/api/schema.d.ts`), `frontend` (lint, build, test, `npm audit --omit=dev` as warning), `e2e` (compose up, Playwright, upload report), `docker` (build both images). Migration bundle artifact: `dotnet ef migrations bundle` on main.
- Deployment sketch (no choice made until Q5): container images to a registry, Azure Container Apps (or App Service) + managed PostgreSQL; run the migration bundle as a pre-deploy step (never auto-migrate in production); secrets (`Jwt__Key`, connection string) from the host's secret store; `Cors:Origins` set explicitly; serve frontend and API on the same site (or keep `SameSite=Lax`).

**M13 - Docs refresh (DOC, last, 1 day).** Verify every statement against the running system before writing.
- Root README, `backend/README.md` (net10, `.slnx`, projects, Scalar `/scalar/v1`, Testcontainers tests, migration commands with `--project/--startup-project`, seeded accounts unchanged), `frontend/README.md` (generated types, `api:types`, tests).
- `docs/api/`: add a short index stating `backend/openapi/v1.json` is the source of truth; keep `phase1-api.md`, `phase2-api.md`, `branding-timeclock-api.md` as behavioral/narrative docs and fix any drift found during verification.
- New `docs/architecture.md` (layers, dependency rule, request flow, multi-location filter, auth/refresh flow, migrations) and an ADR section (repository + UoW supersedes the original plan; Scalar vs Swashbuckle; Testcontainers).
- Update `cafe-pos-inventory-plan.md` header (stack now .NET 10, link to this plan).

**Final reviews (workspace workflow):** run `/code-review` on each phase's changes and `/security-review` after M8 (auth, tokens, rate limiting) and after M7e (PIN handling, uploads). Then commit/PR; do not push without asking.

## 5. Parallelism map

```
Time ->   M0 -> M1 -> M3 (GATE G1) -------------------------------------------> M13 docs
                 |     \                    
                 |      \-- Backend: M4 -> M5 -> M6 -> M7a..e -> M8 -> M9 (GATE G2) -> D2
                 |                         (QA-T2 unit tests alongside M7)
                 |-- M2 (QA, after M1)  
                 |-- D1 CI v1 (OPS, after M2)
   FE: F0 (anytime after M0, parallel to M1-M3) -> WAIT for G1 -> F1 -> F2 -> F3..F6 -> F7 -> M11
   After G2 + F7: M12 Playwright E2E -> D2 final CI -> M13 docs
```
- **FE can start immediately** (F0: folders, lazy routes, Prettier, Vitest/MSW) while BE does M1-M3.
- **FE must wait for G1** (M3 merged, `backend/openapi/v1.json` committed) before F1. FE F1-F7 then run fully in parallel with BE M4-M9 because the snapshot guards the contract.
- **If BE ever needs a contract change** (not planned): BE updates the snapshot in its own commit, tells FE, FE runs `npm run api:types`, and the CI `contract` job proves both sides agree.
- **QA** M2 early (unblocks CI), unit tests alongside M7, component tests after F3-F6, E2E after G2 + F7. **OPS** D1 early, D2 after G2. **DOC** last.

## 6. Risks and mitigations

| # | Risk | Impact | Mitigation |
|---|---|---|---|
| R1 | .NET 8 EOL on 10 Nov 2026 | Unsupported runtime in production | M1 first; do not wait for the restructure |
| R2 | **EF migrations under the new project layout:** moving `CafeDbContext`/entities changes namespaces embedded in the snapshot/Designer files; EF 9+ throws on `Migrate()` when the model differs; wrong `MigrationsAssembly` or tooling flags break `dotnet ef`; renumbering/editing old migrations would corrupt existing databases | Broken dev/prod databases, spurious migration dropping data | Never rename/edit existing migration IDs; `has-pending-model-changes` gate in M1 and M6; snapshot-only refresh via throwaway migration if needed; **upgrade test from a pre-refactor database with data**; production uses a reviewed migration bundle; CI runs `has-pending-model-changes` |
| R3 | **Refresh-token / auth changes break the frontend flow:** the frontend relies on cookie `cafepos_refresh` with `Path=/api/v1/auth`, `credentials: 'include'`, single-flight refresh, and claim `location_id`; token handler swap could change claim names (`JwtSecurityTokenHandler` vs `JsonWebTokenHandler` outbound mapping); `SameSite=Strict` or a shorter lifetime would change behavior; rate limiting can lock out a shared-IP kiosk | Everyone logged out; role checks (`IsInRole`) silently failing; kiosk 429s | Do not change cookie name/path/attributes/claims/lifetime (C7/C8 need user decision); M3 claim+cookie baseline test; keep `JwtSecurityTokenHandler`; explicit `RoleClaimType`; generous, configurable rate limits partitioned by IP+email; E2E token-refresh test; FE F2 tests for concurrent-401 single refresh |
| R4 | Multi-location filters or `IgnoreQueryFilters()` leaking when logic moves into repositories | Cross-location data exposure | Keep filters in `CafeDbContext`; `IgnoreQueryFilters()` only in Infrastructure behind an explicit scope parameter; keep and extend `IsolationAdversarialTests` and Phase2 tests; run them after every M7 slice; `/security-review` |
| R5 | Order completion transaction/idempotency regress when split across Application/Infrastructure (partial stock decrement, double charge on retry) | Wrong stock/sales | M7d is its own milestone; explicit transaction in UoW; keep the conditional UPDATE; add tests for concurrent completion, replayed `Idempotency-Key`, insufficient stock rollback |
| R6 | OpenAPI generator output not ideal for codegen (optional-vs-required properties, enums, file uploads, bytes responses, 204/`null` bodies); `openapi-fetch` retry-with-body and multipart ergonomics unverified in sources | Ugly types, runtime/typing mismatch | M3 annotation + schema transformer; F2 spike with clone-before-send; keep `apiFetch` for multipart if needed; contract snapshot makes any fix visible |
| R7 | Build-time OpenAPI generation executes `Program.cs` (migrations/seed/key check between `Build()` and `Run()`) | CI build touching a DB or failing on missing key | Guard startup work; dummy key in generation step; verify in M3 |
| R8 | FluentValidation changes error shape/text vs current model validation | Frontend field errors (`ApiError.errors`) break | Same `ValidationProblemDetails` shape via filter; tests asserting shape; list wording diffs (C6) |
| R9 | Test migration (namespaces, Testcontainers) causes a long red period | Blocked commits | M2 does fixtures first, in isolation; each M5-M9 commit includes mechanical test namespace fixes so the suite is green per commit |
| R10 | Parallel BE/FE commits in one repo collide | Messy history | Path-scoped `git add`; branch per agent merged at gates, or sequential rebase; each side builds only its own tree |
| R11 | Scope creep: entity encapsulation, zod 4, TS bump, xUnit v3, DB major version | Delays and regressions | Explicitly out of scope unless user asks (M10 optional) |
| R12 | Scalar/UI path change surprises developers; docs say `/swagger` | Confusion | Update READMEs/compose comments in M3 (BE) and M13 (DOC) |

## 7. Tech stack table (target versions; verify the exact latest patch on NuGet/npm at implementation time)

| Layer | Now | Target |
|---|---|---|
| SDK / runtime | .NET 8 (SDK 8.0.404) | .NET 10 LTS (C# 14), `global.json` rollForward latestFeature |
| Solution | `CafePos.sln`, 1 project | `CafePos.slnx`; Domain / Application / Infrastructure / Api + 3 test projects; Central Package Management |
| ORM / DB | EF Core 8.0.11, Npgsql 8.0.11, PostgreSQL 16 | EF Core 10.0.x, Npgsql EF 10.0.x (10.0.2 listed; check latest), PostgreSQL 16 (unchanged) |
| Auth | Identity 8.0.11 + JwtBearer 8.0.11 | Identity/JwtBearer 10.0.x; behavior unchanged |
| OpenAPI | Swashbuckle 6.6.2 (Swagger UI) | `Microsoft.AspNetCore.OpenApi` 10.0.x + `Microsoft.Extensions.ApiDescription.Server` + `Scalar.AspNetCore`; OpenAPI 3.1 |
| Validation / mapping | DataAnnotations, hand mapping | FluentValidation, hand-written `ToDto()` (no reflection mappers) |
| Backend tests | xunit 2.5.3, Mvc.Testing 8.0.11, compose DB | xUnit v2 latest, Shouldly/FluentAssertions-style, NSubstitute, `Testcontainers.PostgreSql`, Mvc.Testing 10.0.x |
| Frontend | React 19, RR 7, TanStack Query 5, RHF 7 + Zod 3, Zustand 5, Vite 6, TS 5.6 | Same; add `openapi-typescript` 7.x, `openapi-fetch`, Vitest, RTL, MSW, Prettier, Playwright. (Zod 4 / TS bump out of scope) |
| Node (build) | 20 (EOL) | 22 or 24 LTS (verify) |
| CI | none | GitHub Actions: backend, frontend, contract-drift, e2e, docker; Dependabot |
| Hosting | local compose | Container host + managed PostgreSQL (decision pending, Q5) |

## 8. Open questions (user decisions)

1. **Q1 - Optional rich-domain milestone (M10):** do now, later, or never? (Recommended: later.)
2. **Q2 - Access token lifetime:** keep 30 min (current, accepted window documented in `phase2-api.md`) or move to 15 min per standard (a longer 401 -> refresh cadence; no frontend change needed)? (Default: keep.)
3. **Q3 - Refresh cookie `SameSite`:** keep `Lax` or move to `Strict`? `Strict` breaks if the frontend and API are ever on different sites in production. (Default: keep Lax.)
4. **Q4 - FluentValidation wording:** accept minor message-text differences (C6), or must messages be byte-identical? (Default: aim for identical, tolerate and list diffs.)
5. **Q5 - Hosting target** for D2 deployment (Azure Container Apps / App Service / other) and whether CD (auto-deploy) is wanted now or only CI.
6. **Q6 - Node version:** 22 or 24 LTS for the frontend build.
7. **Q7 - Rate limiting numbers** for login/refresh (suggested starting point: 10 requests/minute per IP+email partition for login; 30/min for refresh), and whether a shared-tablet kiosk behind one IP needs a higher cap.
8. **Q8 - Phase 3 payments (Stripe Terminal) status:** `Payments.cs` comments say Stripe Terminal is unavailable in the Philippines and the card provider is undecided, while the old plan still lists Stripe. Not part of this refactor (the `IPaymentProvider` abstraction is preserved); confirm no payment-provider work should be folded in.

## 9. Effort summary (rough, sequential-equivalent)

Backend M1-M9: ~12-16 days; frontend F0-F7: ~7-9 days (parallel after G1); QA (M2, QA-T2, M11, M12): ~5-6 days; OPS (D1, D2): ~1.5 days; DOC: ~1 day. With BE and FE in parallel: roughly 3-4 calendar weeks for one agent per role.

## 10. Sources

- ASP.NET Core 10 OpenAPI docs: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi?view=aspnetcore-10.0
- Swashbuckle on .NET 10 (Q&A): https://learn.microsoft.com/en-us/answers/questions/5686409/swagger-setup-error-after-upgrade
- Swashbuckle to Scalar (blog, opinionated): https://dev.to/jfmeyers/swashbuckle-is-dead-heres-how-to-migrate-to-scalar-in-net-10-155d
- Npgsql EF Core 10.0 release notes: https://www.npgsql.org/efcore/release-notes/10.0.html
- EF Core 10 breaking changes: https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes
- openapi-typescript: https://openapi-ts.dev/introduction ; openapi-fetch: https://openapi-ts.dev/openapi-fetch/
- Testcontainers + WebApplicationFactory (.NET 10 guides): https://se-dotnet-ecosystem.pages.dev/articles/testing/asp-net-core-integration-testing-best-practises/

**Unverified / flagged:** exact latest NuGet/npm patch versions; whether the built-in generator marks non-nullable record properties as required; `openapi-fetch` retry-with-body and multipart behavior; FluentValidation's current recommended ASP.NET Core integration; xUnit v3 compatibility with Testcontainers (not needed; staying on v2); Node 22 vs 24 LTS status. Each is assigned to a spike in the relevant milestone.
