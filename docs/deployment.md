# Deployment sketch

Status: **Render config is ready but not deployed; nothing has been run on a cloud host yet.** `render.yaml` (repo root) defines a
free-plan Render Blueprint (see "Render" below, which the repo owner still has to connect in the Render dashboard). The rest of
this page is the hosting-agnostic reference (Azure Container Apps as the worked example, App Service as the alternative); nothing
here has been run against Azure. Continuous delivery from this repo is not enabled: GitHub Actions only builds and tests
(`.github/workflows/ci.yml`); on Render the deploy is triggered by Render itself after CI passes.

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

- `ForwardedHeaders__ForwardLimit` (default 1) is how many proxy hops are unwound from `X-Forwarded-For`. With the default only
  **one trusted proxy hop** is supported: if you put Azure Front Door or Application Gateway in front of the ingress, the "client"
  becomes the edge service. Raise the limit to the hop count and list every hop's address range as trusted (an untrusted hop
  stops the walk, so spoofed entries are never honored).
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

## Render (free plan now, paid later)

`render.yaml` at the repo root is a Render Blueprint: one free Postgres 16, one Docker web service for the API, one static site
for the SPA. It validates against Render's published JSON schema (`https://render.com/schema/render.yaml.json`). It has **not**
been deployed; items marked "unverified" below must be checked on the first deploy.

```
browser --https--> cafe-pos-web.onrender.com (static site, CDN)
                     |  /api/*  rewrite (proxied) --https--> cafe-pos-api.onrender.com --http:8080--> API container
                     |  everything else: files, then /index.html (SPA fallback)                           |
                                                                          internal host, TLS, same region v
                                                                                           cafe-pos-db (Postgres 16)
```

Files: `render.yaml`, `deploy/render/api.Dockerfile` (the API image for Render; keep it in sync with
`backend/src/CafePos.Api/Dockerfile`), the stock `frontend/` build.

### Design decisions

- **Same-origin API.** `onrender.com` is on the Public Suffix List, so `cafe-pos-api.onrender.com` and `cafe-pos-web.onrender.com`
  are different sites and the `SameSite=Lax` refresh cookie (path `/api/v1/auth`) would never be sent. The static site therefore
  has a rewrite `/api/*` -> `https://cafe-pos-api.onrender.com/api/*` (before the SPA fallback; first match wins) and the SPA is
  built with `VITE_API_BASE_URL=https://cafe-pos-web.onrender.com/api/v1`, so the browser only ever talks to one origin.
  Render documents a rewrite destination as "a path or a full, publicly accessible URL" and that it "does not redirect the
  browser"; it does **not** document that method, body, cookies and `Set-Cookie` pass through. That is **unverified**: test it
  first (checklist below). If login works but the session is lost on reload, or `POST` fails, use the fallback.
- **Fallback if the rewrite does not proxy cookies/POST:** deploy `frontend/Dockerfile` (nginx) as a second free Docker *web
  service* instead of the static site, add to `frontend/nginx.conf` a block
  `location /api/ { proxy_pass https://cafe-pos-api.onrender.com; proxy_set_header Host cafe-pos-api.onrender.com; proxy_ssl_server_name on; proxy_set_header X-Forwarded-Proto $scheme; }`
  (before `location /`), and build with `--build-arg VITE_API_BASE_URL=https://<web-service>.onrender.com/api/v1`. It needs a
  small frontend change, so ask for it rather than editing by hand. The hop count for forwarded headers stays 2.
- **Connection string.** Render exposes Postgres as a `postgresql://` URL, which Npgsql does not accept. The Blueprint passes
  `DATABASE_URL` (host and port are only available inside that URL: the Blueprint spec has no separate `host`/`port` database
  property), `DB_USER`, `DB_PASSWORD`, `DB_NAME`, and the entrypoint in `deploy/render/api.Dockerfile` assembles
  `ConnectionStrings__Default` with `SSL Mode=Require;Trust Server Certificate=true;GSS Encryption Mode=Disable` and `exec`s the
  app. Render's internal Postgres certificates are self-signed and `verify-ca`/`verify-full` are unsupported there (Render docs),
  so the private link is encrypted but not certificate-verified. A `dockerCommand` was not used because the stock image has an
  `ENTRYPOINT`, and Render runs `dockerCommand` in place of the Dockerfile `CMD`, so it would become arguments to `dotnet`.
  Tested locally against a TLS-enabled Postgres 16 with a self-signed certificate: the API connected, migrated, and
  `/health/ready` returned 200.
- **Health check:** `/health/live` (process only). `/health/ready` checks the database; using it would make Render restart the API
  during a database blip.
- **Region:** `oregon` for the database and API (change both together before the first deploy; it cannot be changed later).
  Static sites have no region.
- **Deploys:** `autoDeployTrigger: checksPass` on both web services: Render deploys a commit only when its GitHub checks
  (the CI workflow) pass. `buildFilter` limits rebuilds to `backend/**` + `deploy/render/**` (API) and `frontend/**` (site).

### Deploy it (click by click)

1. **Pick unique names (recommended).** Service names become the URLs, and `onrender.com` names are global. Names like
   `cafe-pos-api` may be taken. Before connecting, find/replace `cafe-pos-` in `render.yaml` with something unique (for example
   `cafe-pos-<yourhandle>-`), commit and push. Names appear in: the database `name`, both service names, `fromDatabase.name`
   (4 places), `Cors__Origins__0`, `VITE_API_BASE_URL` and the `/api/*` rewrite destination.
2. Sign in at <https://dashboard.render.com> (GitHub login is simplest) and authorize the Render GitHub app for the
   `immanuelMM/Cafe_POs` repository when asked.
3. **New -> Blueprint.** Choose the `Cafe_POs` repo, branch `main`. Render reads `render.yaml` and lists the resources: database
   `cafe-pos-db`, web services `cafe-pos-api` and `cafe-pos-web`.
4. **Enter the prompted values** (the `sync: false` variables, asked only now, at creation; Render ignores them on later syncs):

   | Variable | Value |
   |---|---|
   | `Bootstrap__OwnerEmail` | the owner's email address (login name) |
   | `Bootstrap__OwnerPassword` | a strong password (the bootstrap logs and skips a bad value instead of crashing, so check the API logs if no owner appears) |
   | `Bootstrap__OwnerName` | display name (optional, may be left empty) |
   | `Bootstrap__LocationName` | first location name (optional; default `Main`) |

   Everything else is pre-filled: `Jwt__Key` is generated by Render and the DB connection comes from the database. Do not put
   these values in the repo.
5. Click **Apply** (Deploy Blueprint). Render creates the database, then builds the API (Docker; several minutes) and the site
   (`npm ci && npm run build`).
6. If a name got a suffix (each service page shows its real URL), fix the three URL settings (next section).
7. Open `https://<web-site>.onrender.com` and sign in with the owner email and password. The first API request after idle takes
   about a minute (cold start); if the first attempt fails, reload once.
8. **Remove the bootstrap secrets:** API service -> Environment -> delete the four `Bootstrap__*` variables -> Save (this
   redeploys). The bootstrap only runs when no users exist, so leaving them is harmless but keeps a password in the dashboard.
9. Run the checklist below once.

### If Render added a suffix to a name

Render shows the actual `https://...onrender.com` URL of each service. Set, to those real URLs:

- API service -> Environment: `Cors__Origins__0` = the **site** origin (no trailing slash).
- Static site -> Environment: `VITE_API_BASE_URL` = `https://<site>.onrender.com/api/v1` (a rebuild is required: Manual Deploy).
- Static site -> Redirects/Rewrites: the `/api/*` rule destination = `https://<api>.onrender.com/api/*`.

### Forwarded headers on Render (read this)

TLS ends at Render's proxy, so the API needs `ForwardedHeaders__*` or it loops on HTTPS redirects and rate-limits every user as
one IP (see "Behind the ingress"). The Blueprint sets:

```
ForwardedHeaders__KnownNetworks__0..4 = 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16, 100.64.0.0/10, fc00::/7
ForwardedHeaders__ForwardLimit        = 2
```

**Honest status: the exact address Render's proxy connects from is not published in Render's documentation, so this is an
unverified assumption** that it is a private address. It is a safe one: Render states a web service "is not directly reachable
via the public internet", so only Render's proxy (and your own private network) can connect from private ranges. It is not
"trust everyone". If the guess is wrong, the symptom is a redirect loop (the site cannot log in; `curl -I https://<api>.onrender.com/api/v1/auth/login`
shows a 307/308 to https) or every user sharing one IP.

`ForwardLimit = 2` reasoning: a browser request crosses up to two hops before the app: the static site's `/api` proxy and the
API's own load balancer. An entry is only unwound when the address before it is trusted, so the second hop only counts once the
static site's egress addresses are also trusted; until then it behaves like 1 and cannot be spoofed. Whether Render's rewrite
proxy forwards the visitor's IP at all is **unverified**. Until it is confirmed, expect all browsers to appear as one IP: the
per-IP+email login limit still separates accounts, but the per-IP limits (`LoginPerIp` 50/min, `Refresh` 30/min) are shared by
everyone. For one cafe that is workable; raise them if needed (`RateLimiting__LoginPerIp__PermitLimit`,
`RateLimiting__Refresh__PermitLimit`).

**One-time confirmation after the first deploy** (no code change needed; the refresh-token row stores the client IP):

1. Sign in once from your own machine. Find your public IP (<https://ifconfig.me>).
2. Database `cafe-pos-db` -> Connect -> External connection command (free DBs allow external connections), run in `psql`:
   `SELECT "CreatedByIp", "CreatedAt" FROM "RefreshTokens" ORDER BY "CreatedAt" DESC LIMIT 3;`
   (If the table or column names differ, `\d "RefreshTokens"` shows them.)
3. Interpret:
   - Your own IP: forwarded headers work end to end. Done.
   - A private address (10.x, 172.x, 100.64.x) or an address from a range not listed: headers are not being honored for that
     hop. Add its range as the next `ForwardedHeaders__KnownNetworks__N` and redeploy.
   - A public address that is not yours (the static site proxy's egress): the first hop is trusted and the second is not. Add
     that range as `ForwardedHeaders__KnownNetworks__5` (`<ip>/32`, or the provider range if it changes between requests);
     `ForwardLimit=2` is already set. Repeat the check; it must now show your IP.

### First-deploy verification checklist

1. `https://<api>.onrender.com/health/ready` returns `Healthy` (database reachable; migrations ran at startup).
2. Open the site and sign in as the owner. Browser DevTools -> Network: the login request goes to
   `https://<site>/api/v1/auth/login` (same origin) and the response has a `Set-Cookie` for the refresh token (`Secure`,
   `HttpOnly`, `SameSite=Lax`).
3. Reload the page: you stay signed in (this proves the cookie is sent back through the rewrite; if you are signed out, use the
   nginx fallback above).
4. Deep-link to a client route (for example `/orders`) and reload: the SPA loads (the fallback rewrite works).
5. Run the forwarded-headers confirmation above.

### Free plan caveats

- **The API sleeps after 15 minutes without traffic**; the next request waits about a minute while it starts (Render shows a
  loading page to browsers). In-memory rate-limit counters reset on every restart. Not suitable for a live till.
- **Free Postgres expires 30 days after creation** (14 days grace to upgrade to a paid plan, then the database and its data are
  deleted), 1 GB storage, one free database per workspace. Upgrade the database to a paid plan before day 30 to keep the
  data, and export with `pg_dump` (external connection) if the data matters.
- 750 free instance hours per month per workspace (one always-awake API fits); static sites are free.
- Free services: no `preDeployCommand`, no persistent disk, no scaling to several instances.
- `Database__MigrateOnStartup=true` is on: the API applies pending EF migrations itself on every start. Safe with one instance;
  not for several.

### Upgrading to paid

1. **Plans:** in `render.yaml` change `plan: free` to a paid plan on the database and on the API service (plan IDs are in the
   Blueprint spec's compute plan tables, for example `1c-2g` style for services and `0.5c-1g` style for Postgres), commit and
   push so the Blueprint sync applies it, or change the plan in the dashboard (Settings). A paid database does not expire and
   gets backups; a paid API does not sleep. The static site stays free. Do not change `postgresMajorVersion` or `region`
   (immutable).
2. **Migrations before deploy instead of at startup (paid only: `preDeployCommand`).** Optional (startup migration keeps working
   on paid), recommended before you run more than one instance:
   - Add an EF migration bundle to `deploy/render/api.Dockerfile`'s build stage (untested here; CI's `migration-bundle` job in
     `.github/workflows/ci.yml` has the exact, known-good `dotnet ef migrations bundle` arguments, so copy those) and write it
     to `/app/efbundle`, which the final stage already copies.
   - In `render.yaml` on the API service add a `preDeployCommand` that runs `./efbundle --connection "<same keyword string the
     entrypoint builds>"` (factor the entrypoint's string building into a script both use), and change
     `Database__MigrateOnStartup` to `"false"`.
   - Roll forward only. The first owner is still created by the `Bootstrap__*` variables on first start.
3. **Replicas / rate limiting:** with more than one instance the in-memory rate limits are per instance (see "Rate limiting").
4. Optional later: a custom domain, then set `Cors__Origins__0` and `VITE_API_BASE_URL` to it.

### Sources checked (Render docs, fetched 2026-10-29)

Verified: Blueprint spec and JSON schema (`render.com/docs/blueprint-spec`, `render.com/schema/render.yaml.json`: the file
validates; `fromDatabase` properties are `connectionString`, `user`, `password`, `database`; `sync: false` is prompted only at
creation; `generateValue`; `autoDeployTrigger: checksPass`; static-site `routes` with `type: rewrite`; `staticPublishPath`
relative to the repo root; `buildFilter`), static-site rewrites (`render.com/docs/redirects-rewrites`: full-URL destination
allowed, first matching rule wins, existing files win over rules), free instances (`render.com/docs/free`: 15-minute sleep,
about 1 minute spin-up, 30-day DB expiry with 14-day grace, 1 GB), Postgres connections
(`render.com/docs/postgresql-creating-connecting`: internal URL needs the same region, self-signed internal TLS, `verify-*`
unsupported), health checks (`render.com/docs/health-checks`: 2xx/3xx within 5 s, restart after 60 s of failures), Docker
commands (`render.com/docs/docker`: a custom command is used instead of the Dockerfile `CMD`).
Not documented, so unverified: that rewrites to an external URL pass method, body and `Set-Cookie`; Render's proxy source
address and whether the static-site proxy forwards the visitor IP; "one free database per workspace" (from the task brief; not
found on the pages above).
