# 0006. Rate limit partitions and forwarded-headers trust

- **Status:** Accepted
- **Date:** 2026-10-29

## Context
Login, refresh, location switching and time clock punches are guessable or abusable endpoints. Behind a TLS-terminating ingress every request
appears to come from the proxy unless forwarded headers are honored, but honoring them from anyone lets a client spoof its IP.

## Decision
Fixed-window limits via ASP.NET Core rate limiting (`429` with `Retry-After`), configured under `RateLimiting:*` (`Enabled` plus `Login`, `LoginPerIp`,
`Refresh`, `SwitchLocation`, `TimeclockPunch`, each `PermitLimit` and `WindowSeconds`). Partitions:

| Policy | Partition |
|---|---|
| `Login` (default 10 per 60 s) | client IP + email |
| `LoginPerIp` (50 per 60 s, global limiter on `POST /api/v1/auth/login`) | client IP |
| `Refresh` (30 per 60 s) | client IP |
| `SwitchLocation` (30 per 60 s) | signed-in user id |
| `TimeclockPunch` (10 per 60 s) | client IP + target staff member |

The email and punch target are captured early by `RateLimitKeyCaptureMiddleware`; a login body with duplicate keys is rejected with `400` so the
captured email cannot differ from the one bound. `X-Forwarded-For` / `X-Forwarded-Proto` are trusted only from `ForwardedHeaders:KnownProxies` /
`KnownNetworks`; with both empty (default) the middleware is not installed, because ASP.NET Core treats empty lists as "trust everyone".

## Consequences
- Production behind an ingress must list the proxy, otherwise all users share one IP budget and HTTPS redirection can loop (see `deployment.md`).
- Counters are in process memory: they reset on restart and multiply by the replica count.
- The default `ForwardLimit` of 1 supports one proxy hop; Front Door plus an ingress needs a `ForwardLimit` setting first (follow-up).
- The E2E stack turns limiting off because it performs many fresh logins from one IP.
