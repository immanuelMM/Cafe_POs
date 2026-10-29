# Deployment sketch

Status: **a sketch, not a running deployment.** No cloud resources exist, nothing is deployed, and continuous delivery is
not enabled (CI only builds and tests; see `.github/workflows/ci.yml`). The hosting target is still an open decision
(plan question Q5). This page records what any host must be configured with so the API behaves correctly, using Azure
Container Apps (ACA) as the worked example and App Service as the alternative. Nothing here has been run against Azure.

## Shape

```
browser --https--> [ingress / TLS termination] --http:8080--> cafepos-api container --> managed PostgreSQL 16
browser --https--> static host or the web container (nginx)  (the SPA; calls the API's public URL directly)
```

- Images: `backend/src/CafePos.Api/Dockerfile` (build context `backend/`) and `frontend/Dockerfile`. Push them to a registry
  (for example Azure Container Registry) from CD once it is enabled.
- The API image runs as the non-root `app` user, listens on 8080, and contains no `appsettings.Development.json`.
- The web image bakes the API URL in at build time:
  `docker build --build-arg VITE_API_BASE_URL=https://api.example.com/api/v1 frontend` (it must end in `/api/v1`).
- Serve the SPA and the API on the **same site** (for example `app.example.com` and `api.example.com`). The refresh cookie is
  `SameSite=Lax`, `HttpOnly`, `Secure`, path `/api/v1/auth`; a different site would not send it.
- Database: managed PostgreSQL 16 (for example Azure Database for PostgreSQL Flexible Server). Keep the major version at 16.

## Environment variables for the API container

ASP.NET Core maps `A__B__C` to `A:B:C`, and `Key__0`, `Key__1` to array items. Secrets come from the host's secret store
(ACA secrets referenced with `secretref:`, App Service Key Vault references), never from the image or the repo.

| Variable | Value | Notes |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` | Enables HSTS, HTTPS redirection and the `Secure` refresh cookie; disables Scalar and `/openapi`. Do not use `Development`. |
| `ASPNETCORE_URLS` | `http://+:8080` | Already the image default. TLS ends at the ingress. |
| `ConnectionStrings__Default` | secret: `Host=<server>.postgres.database.azure.com;Port=5432;Database=cafepos;Username=<user>;Password=<pw>;SSL Mode=Require;GSS Encryption Mode=Disable` | `GSS Encryption Mode=Disable` is required, see "Startup resilience" below. |
| `Jwt__Key` | secret, 32+ random characters | Production refuses to start without it. Generate with `openssl rand -base64 48`. Rotating it signs everyone out. |
| `Jwt__Issuer`, `Jwt__Audience` | optional | Defaults `CafePos` / `CafePos.Client`. |
| `Cors__Origins__0` | `https://app.example.com` | Exactly the SPA origin(s); no trailing slash, no wildcard. |
| `Database__MigrateOnStartup` | `false` (the default) | Production never auto-migrates; run the migration bundle (below). |
| `Database__SeedDevData` | `false` (the default) | Never seed dev accounts in production. |
| `ForwardedHeaders__KnownProxies__0` / `ForwardedHeaders__KnownNetworks__0` | the address or CIDR the **ingress connects from** | See "Behind the ingress". Required. |
| `RateLimiting__*` | optional, see below | Defaults are sensible; only set what you tune. |

### Behind the ingress: forwarded headers (required)

The API trusts `X-Forwarded-For` / `X-Forwarded-Proto` only from the proxies you list. With both lists empty (the default) the
middleware is not installed, and two things go wrong behind a TLS-terminating ingress:

1. **Every client looks like the proxy.** Rate limiting is keyed on the client IP (login per IP+email and per IP, refresh per
   IP, time-clock punches per IP+staff member) and the refresh-token rows store the IP, so all users would share one budget
   and a single misbehaving client could lock everyone out of login.
2. **HTTPS redirection loops.** The request reaches the container as plain `http`, so `UseHttpsRedirection` redirects to
   `https`, the ingress terminates TLS and forwards `http` again, and the browser sees a redirect loop. The forwarded scheme
   (`X-Forwarded-Proto: https`) is only honored from a trusted proxy.

Set one or both of:

```
ForwardedHeaders__KnownProxies__0=10.0.2.4          # a single proxy address
ForwardedHeaders__KnownNetworks__0=10.0.2.0/24      # or the subnet the ingress connects from (CIDR)
```

Never use `0.0.0.0/0` or `::/0`: that trusts every sender and lets any client spoof its IP and dodge the rate limits.

**Finding the right value (do this once per environment):** the API sees the peer address of the ingress, not the client. On ACA
that is an address inside the Container Apps environment's infrastructure subnet (workload-profiles environments in a custom
VNet: the subnet you delegated; consumption-only: the environment's internal range). Read it rather than guessing: deploy with
no trust list, call `/api/v1/auth/login` once, and look at the client IP stored on the new `RefreshTokens` row (or log
`Connection.RemoteIpAddress`); that is the proxy address. Use its subnet. Then redeploy with the list set and repeat: the stored
IP must now be your own public IP, and the request must no longer redirect.

Limits of the current implementation (code change needed if you hit them):

- The middleware uses the ASP.NET Core default `ForwardLimit = 1`, so it supports **exactly one trusted proxy hop**. If you put
  Azure Front Door or Application Gateway in front of the ingress, the "client" becomes the edge service. Either keep a single
  ingress, or add a `ForwardLimit` setting to `ForwardedHeadersSetup` first.
- App Service for Linux reaches the container from the platform front end (link-local `169.254.x.x` addresses) and may append a
  port to the client address in `X-Forwarded-For`. Verify with the same check before relying on it.

### Rate limiting

Fixed windows; a limit that is hit returns `429` with `Retry-After`. Defaults (from `RateLimitingOptions`):

| Key | Default | Partition |
|---|---|---|
| `RateLimiting__Login__PermitLimit` / `__WindowSeconds` | 10 / 60 | client IP + email |
| `RateLimiting__LoginPerIp__PermitLimit` / `__WindowSeconds` | 50 / 60 | client IP (all emails) |
| `RateLimiting__Refresh__PermitLimit` / `__WindowSeconds` | 30 / 60 | client IP |
| `RateLimiting__SwitchLocation__PermitLimit` / `__WindowSeconds` | 30 / 60 | signed-in user |
| `RateLimiting__TimeclockPunch__PermitLimit` / `__WindowSeconds` | 10 / 60 | client IP + target staff member |
| `RateLimiting__Enabled` | `true` | never `false` in production |

The partitions are only meaningful once the forwarded-headers trust above is correct. A shared kiosk behind one office NAT
shares the per-IP budgets (`LoginPerIp`, `Refresh`, and the IP part of the keyed ones), so raise `LoginPerIp` and `Refresh` for
a branch with many tablets. The counters live in process memory: with more than one replica each replica counts separately
(the effective limit is multiplied by the replica count), and a restart resets them.

### Cookie, HSTS and TLS

Outside Development the refresh cookie is always `Secure`, HSTS is sent, and HTTPS redirection is on. Configure the ingress
to accept HTTPS only (ACA: ingress `allowInsecure: false`; App Service: "HTTPS Only"), with a managed certificate for the API
and SPA domains.

## Azure Container Apps (worked example)

- Container app: image from the registry, `targetPort: 8080`, external ingress, `allowInsecure: false`, min replicas 1.
- Probes (ACA ignores the Dockerfile `HEALTHCHECK`): liveness `GET /health/live`, readiness and startup `GET /health/ready`
  (it checks the database), all on port 8080.
- Secrets: `jwt-key` and `db-connection` as app secrets (or Key Vault references); expose them as `Jwt__Key` and
  `ConnectionStrings__Default` through `secretRef`.
- PostgreSQL: Flexible Server, private access or a firewall rule for the environment's egress addresses, TLS required.
- Logs: container stdout goes to Log Analytics; the API logs JSON-free console lines at Information.

App Service alternative: a Linux Web App for Containers with the same image, `WEBSITES_PORT=8080`, "HTTPS Only" on, the same
application settings (secrets as Key Vault references), and health check path `/health/ready`.

## Database migrations (never automatic in production)

CI builds a reviewed, self-contained migration bundle on every push to `main` (job `migration-bundle`, artifact
`efbundle-linux-x64`). Apply it as a pre-deploy step, before the new revision receives traffic:

```bash
chmod +x efbundle
./efbundle --connection "Host=...;Database=cafepos;Username=...;Password=...;SSL Mode=Require;GSS Encryption Mode=Disable"
```

Run it from a trusted runner or a Container Apps Job with network access to the database. Roll forward only; never edit or
renumber an existing migration. The first deployment onto an empty database creates the schema but **seeds no accounts**
(`Database__SeedDevData=false`); create the first Owner through your own provisioning step.

## Startup resilience (why GSS is disabled)

Npgsql 8+ defaults to `GSS Encryption Mode=Prefer`, which makes it load the Kerberos library `libgssapi_krb5.so.2` on every
new connection. The `mcr.microsoft.com/dotnet/aspnet` runtime image does not contain it (every start logs
`Cannot load library libgssapi_krb5.so.2`). Nothing in this stack uses Kerberos (PostgreSQL password authentication over TLS),
so the chosen fix is to **disable GSS** in every connection string rather than add a package to the image: no extra
attack surface or image size, and the behaviour is explicit. Any connection string you supply in a new environment must
include `GSS Encryption Mode=Disable`.

The crash itself (container exit code 139 right after `NpgsqlException: The operation has timed out` at the startup
`Migrate()`) happens whenever the database does not answer within Npgsql's 15 s connect timeout and the exception is
unhandled; exit 139 is how this runtime image reports any unhandled exception here (the missing-JWT-key failure exits 139 too).
The API now retries the startup migration/seed for transient connectivity errors (8 attempts with a growing delay, see
`DatabaseStartup`), and compose restarts the API on failure. Request-time database access is unchanged.

## What CD would add (not enabled)

A `deploy` workflow on tags or `main`: log in to Azure with OIDC (no stored secrets), push the images, run the bundle, update
the container app revision. This needs a registry, a resource group and a federated identity, none of which exist; enabling it
is a separate decision.
