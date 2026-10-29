# 0002. Built-in OpenAPI and Scalar instead of Swashbuckle

- **Status:** Accepted
- **Date:** 2026-10-29

## Context
The app used Swashbuckle 6.6.2 (Swagger UI at `/swagger`). Swashbuckle needs rework on .NET 10, the workspace standard uses the framework generator, and
a build-time document is needed so the frontend can generate types without a running server.

## Decision
Use `Microsoft.AspNetCore.OpenApi` (OpenAPI 3.1) with document, schema and operation transformers under `CafePos.Api/OpenApi/`, and
`Microsoft.Extensions.ApiDescription.Server` to write `backend/openapi/v1.json` at build time. Scalar serves the UI at `/scalar/v1` and the document
at `/openapi/v1.json`, in Development only. Swashbuckle is removed completely.

## Consequences
- Developer URLs changed (`/swagger` becomes `/scalar/v1`; Development only, no production impact).
- Build-time generation skips the JWT key check, migrations and seeding, so it needs no database or secrets.
- The generator is less opinionated than Swashbuckle: nullability and `required` are fixed with transformers, and enum-like fields and 4xx responses are
  still not described (tracked as follow-ups).
