# 0003. Committed contract snapshot with drift tests

- **Status:** Accepted
- **Date:** 2026-10-29

## Context
The restructure into four projects must not change any route, status code or DTO, and the frontend must know when the contract moves.

## Decision
Commit the generated document as `backend/openapi/v1.json`. Two checks fail on any difference between the code and the file: the `ContractTests`
integration tests (live `/openapi/v1.json` versus the snapshot; the same class also pins the access-token claims and refresh-cookie attributes) and the CI
`contract` job (rebuild, then `git diff --exit-code`). Regenerate with
`cd backend && dotnet build src/CafePos.Api -p:OpenApiGenerateDocuments=true`.

## Consequences
- Every contract change is an explicit, reviewable diff in the same commit as the code.
- Forgetting to regenerate fails the build, not production.
- The snapshot only covers what the generator describes; behavior outside it (4xx bodies, claims) relies on the dedicated tests.
