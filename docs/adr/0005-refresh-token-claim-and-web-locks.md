# 0005. Refresh token atomic claim and cross-tab Web Locks

- **Status:** Accepted
- **Date:** 2026-10-29

## Context
Refresh tokens rotate on every use and a replayed token is treated as theft (all sessions revoked). Two concurrent refreshes with the same token, from
parallel requests or from two browser tabs sharing the cookie, would otherwise either both succeed or look like theft and log the user out.

## Decision
- **Server:** `AuthService.IssueAsync` claims the old token with a single conditional UPDATE (`TryClaimForRotationAsync`) inside the same transaction as
  the insert of its successor. Only one caller wins; the loser is handled as reuse. Reuse of a revoked token revokes all of the user's refresh tokens
  and clears the cookie. Identity lockout and the time clock PIN failure counters are also incremented with atomic SQL updates.
- **Client:** `src/lib/apiClient.ts` makes the refresh single-flight per tab and runs it under the Web Lock `cafepos-refresh`, so the waiting tab sends the
  already-rotated cookie. Without `navigator.locks` (jsdom, old browsers) it falls back to per-tab single-flight.
- **Session survival:** only 401 or 403 from refresh clears the session; 429, 5xx and network errors keep it and surface that error.

## Consequences
- Concurrent refreshes are safe on the server without relying on the client.
- Genuine replay still logs the user out everywhere, by design.
- The cross-tab lock is covered by unit tests of the client only; an E2E test with two tabs is a follow-up.
