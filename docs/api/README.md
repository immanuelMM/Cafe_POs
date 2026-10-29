# API documentation

**Source of truth:** [`backend/openapi/v1.json`](../../backend/openapi/v1.json), the OpenAPI 3.1 document generated from the code and committed.
In Development the same document is served at `http://localhost:5080/openapi/v1.json` with an interactive UI (Scalar) at
`http://localhost:5080/scalar/v1`. If this folder disagrees with the snapshot, the snapshot wins.

The Markdown files here are narrative and behavioral notes (rules, flows, rationale) that the schema cannot express:

| File | Covers |
|---|---|
| [`phase1-api.md`](phase1-api.md) | Base contract: conventions, errors, auth flow, catalog, inventory, orders |
| [`phase2-api.md`](phase2-api.md) | Multi-location changes applied on top of Phase 1: token claims, Owner role, location switching |
| [`branding-timeclock-api.md`](branding-timeclock-api.md) | Branding assets, design studio, staff time clock |

Known gaps in the machine-readable contract (also listed in the refactor plan follow-ups): enum-like fields (role, order status, ...) are
plain strings, and 4xx responses (400, 401, 403, 404, 409, 422, 423, 429) are described in these files, not in the OpenAPI document.

To change the contract see the workflow in [`../architecture.md`](../architecture.md#contract-first-workflow).
