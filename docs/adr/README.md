# Architecture decision records

Short records of decisions that shape the code. Format: Status, Context, Decision, Consequences. Add a new numbered file for a new
decision; to reverse one, add an ADR that supersedes it and update the old status line.

| # | Decision | Status |
|---|---|---|
| [0001](0001-repositories-and-unit-of-work.md) | Repositories plus unit of work in Clean Architecture | Accepted |
| [0002](0002-builtin-openapi.md) | Built-in OpenAPI and Scalar instead of Swashbuckle | Accepted |
| [0003](0003-committed-contract-snapshot.md) | Committed contract snapshot with drift tests | Accepted |
| [0004](0004-generated-frontend-types.md) | Generated frontend types with openapi-fetch | Accepted |
| [0005](0005-refresh-token-claim-and-web-locks.md) | Refresh token atomic claim and cross-tab Web Locks | Accepted |
| [0006](0006-rate-limit-partitions-and-proxy-trust.md) | Rate limit partitions and forwarded-headers trust | Accepted |
| [0007](0007-keep-four-digit-timeclock-pins.md) | Keep 4-digit time clock PINs | Accepted |

See also [`../architecture.md`](../architecture.md).
