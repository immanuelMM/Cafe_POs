# 0004. Generated frontend types with openapi-fetch

- **Status:** Accepted
- **Date:** 2026-10-29

## Context
The frontend used hand-written DTO types and a bespoke fetch wrapper that could silently drift from the backend.

## Decision
`npm run gen:api` runs `openapi-typescript ../backend/openapi/v1.json -o src/api/schema.d.ts`; the generated file is committed and never edited. All
calls go through `openapi-fetch` clients in `src/lib/apiClient.ts` typed with `paths`: `client` (bearer token, refresh-on-401) and `anonClient`
(login, logout, refresh, public branding). `src/api/types.ts` only aliases schema types. `VITE_API_BASE_URL` must be an absolute URL ending in
`/api/v1`; the app throws at startup otherwise. Multipart uploads use the `fileBody` helper so a 401 retry can rebuild the body.

## Consequences
- A backend contract change shows up as TypeScript errors after `gen:api`; CI regenerates and fails on a diff.
- The client rejects with `ApiError` / `NetworkError` on every non-2xx or network failure, so callers never handle an `{ error }` result.
- Enum-like fields arrive as `string`, so `types.ts` keeps UI-side unions for building requests (follow-up: type them in the contract).
- `src/features/design-studio` document payloads are opaque to the backend and stay hand-typed.
