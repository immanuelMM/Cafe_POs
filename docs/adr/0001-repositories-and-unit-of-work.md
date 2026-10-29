# 0001. Repositories plus unit of work

- **Status:** Accepted (approved by the user; overrides the original plan's "no repositories" advice)
- **Date:** 2026-10-29

## Context
The refactor moved a single-project API into Domain, Application, Infrastructure and Api. Application services need to be unit tested without a
database, and the multi-location rules (query filters, write guard) must stay in one place. The workspace backend standard allows using
`DbContext` directly, and the original plan leaned that way.

## Decision
Application defines one `I<X>Repository` port per aggregate root, with domain-shaped methods and no `IQueryable` or `SaveChanges`. Infrastructure
implements them over `CafeDbContext`, which also implements `IUnitOfWork` (save, explicit transactions, tracker clear). Services depend on the ports
and `IUnitOfWork` only.

## Consequences
- Services are tested with NSubstitute mocks (Application tests: 129) while integration tests cover the real EF behavior.
- Queries are written once in the repository, next to the `IgnoreQueryFilters()` calls that deliberately bypass location filters.
- More files and some forwarding code than using `DbContext` in services; accepted for testability and a hard Application/EF boundary.
- Cross-aggregate atomic work (order completion, refresh rotation) uses `IUnitOfWork.BeginTransactionAsync`.
