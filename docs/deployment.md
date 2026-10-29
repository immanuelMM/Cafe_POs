# Deployment sketch

Status: **Render config is ready but not deployed; nothing has been run on a cloud host yet.** `render.yaml` (repo root) defines a
free-plan, single-service Render Blueprint (see "Render" below; the repo owner still has to connect it in the Render dashboard). The rest of
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

`render.yaml` at the repo root is a Render Blueprint: **one** Docker web service (`cafe-pos`: the API plus the built SPA) and one
Postgres 16 database. It validates against Render's published JSON schema (`https://render.com/schema/render.yaml.json`) and the
image was built and smoke-tested locally (below), but nothing has been deployed to Render yet. Items marked "unverified" must be
checked on the first deploy.

```
browser --https--> Render load balancer (TLS ends here) --http:8080--> cafe-pos container
                                                                        |  /api/*, /health/*  -> API
                                                                        |  everything else    -> built SPA (index.html fallback)
                                                                        +--internal URL (TLS)--> cafe-pos-db (Postgres 16)
```

Files: `render.yaml`, `deploy/Dockerfile` (single image, build context = repo root, `.dockerignore` at the root),
`deploy/smoke-single-image.sh` (the smoke check CI also runs). The stock `backend/` and `frontend/` Dockerfiles and
`docker-compose.yml` are unchanged and still used for local dev.

### Why one service

- **Same origin, so the refresh cookie works.** `onrender.com` is on the Public Suffix List: two services (`api-x.onrender.com`
  and `web-x.onrender.com`) are different sites, and the `SameSite=Lax` refresh cookie (path `/api/v1/auth`) would not be sent
  between them. The image builds the SPA with `VITE_API_BASE_URL=/api/v1` (root-relative, resolved against the page origin at
  run time) and the API serves it (`Spa__RootPath=/app/wwwroot-spa`), so the browser talks to a single origin.
- **Correct client IP, so rate limits work.** A separate static site proxying `/api` would put every visitor behind the proxy's
  public egress address, and one anonymous attacker could exhaust the shared per-IP limits (login, refresh, time-clock punches)
  for everyone. With one service the only proxy in front of the container is Render's load balancer.
- **No host name is configured anywhere.** No `Cors__Origins__*` (same origin) and no hard-coded `onrender.com` URL, so there is
  nothing to edit if Render appends a suffix to the service name, and no way to point the app at someone else's service. The URL
  is whatever Render assigns (shown on the service page).

### Other design decisions

- **Image:** `deploy/Dockerfile` has three stages: Node (frontend build), .NET SDK (publish the API, same steps as
  `backend/src/CafePos.Api/Dockerfile`), then the `aspnet` runtime with the non-root `app` user, port 8080 and the stock
  `dotnet CafePos.Api.dll` entrypoint. Keep it in sync with the two stock Dockerfiles. CI builds and smoke-tests it on every push.
- **Database connection:** Render exposes `postgresql://user:pw@host:port/db`; the Blueprint passes it as `DATABASE_URL` from
  `fromDatabase.connectionString` and the API parses it (used only while `ConnectionStrings__Default` is empty; defaults to
  SSL required and GSS disabled). Render's internal Postgres TLS is self-signed and `verify-ca`/`verify-full` are unsupported
  there, so the link is encrypted but the certificate is not verified.
- **Database is internal-only:** `ipAllowList: []` on the database (no connections from the internet). To use `psql` from your
  machine, add your own address temporarily: database -> Settings (Access Control / Inbound IP rules) -> add `<your-ip>/32`, run
  `psql` with the **External** connection string from the Connect menu, then remove the rule again. Do not edit the Blueprint
  for this: a later Blueprint sync would reset a dashboard change to `[]`.
- **Health check:** `/health/live` (process only). `/health/ready` checks the database; using it would make Render restart the
  app during a database blip.
- **Region:** `oregon` for the database and service (change both together before the first deploy; it cannot be changed later).
  The internal database URL only works within one region.
- **Deploys:** `autoDeployTrigger: checksPass`: Render deploys a commit only when its GitHub checks (the CI workflow) pass.
  `buildFilter` rebuilds only for changes under `backend/**`, `frontend/**`, `deploy/**` or `.dockerignore`.

### Deploy it (click by click)

1. Sign in at <https://dashboard.render.com> (GitHub login is simplest) and authorize the Render GitHub app for the
   `immanuelMM/Cafe_POs` repository when asked. Make sure the commit with `render.yaml` is pushed to `main` and CI is green.
2. **New -> Blueprint.** Choose the `Cafe_POs` repo, branch `main`. Render reads `render.yaml` and lists: database `cafe-pos-db`
   and web service `cafe-pos`.
3. **Enter the prompted values** (the `sync: false` variables; asked only now, at creation, and ignored on later syncs):

   | Variable | Value |
   |---|---|
   | `Bootstrap__OwnerEmail` | the owner's email address (login name) |
   | `Bootstrap__OwnerPassword` | a strong password |
   | `Bootstrap__OwnerName` | display name (optional, may be left empty) |
   | `Bootstrap__LocationName` | first location name (optional; default `Main`) |
   | `Bootstrap__LocationTimeZone` | IANA time zone id of the cafe, for example `Asia/Manila` (optional; default `Asia/Manila`) |
   | `Bootstrap__LocationVatRatePercent` | VAT rate for the first location, 0 to 100 (optional; default `12`) |

   `Jwt__Key` is generated by Render; the database connection comes from the database. Never put these values in the repo.
4. Click **Apply** (Deploy Blueprint). Render creates the database, then builds the image (Node and .NET stages; several
   minutes) and starts the service.
5. Open the service's URL (top of the service page, `https://<service>.onrender.com`) and sign in with the owner email and
   password. The first request after idle takes about a minute (cold start); reload once if the first attempt fails.
6. **Remove the bootstrap variables (required):** service -> Environment -> delete all six `Bootstrap__*` variables -> Save (this
   redeploys). Why: the bootstrap runs whenever they are set and no user exists, and the API logs a warning on every start while
   the password is still set. A free database expires after 30 days; if you recreate it, an old `Bootstrap__OwnerPassword` would
   silently recreate the owner with that old password. The bootstrap only creates the owner when no users exist, and never
   crash-loops on a bad value (it logs and continues), so check the logs if no owner appears.
7. Run the verification checklist below once.

### Forwarded headers on Render (read this)

TLS ends at Render's load balancer, so the API needs `ForwardedHeaders__*` or it loops on HTTPS redirects and rate-limits every
user as one IP (see "Behind the ingress"). The Blueprint sets:

```
ForwardedHeaders__KnownNetworks__0..4 = 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16, 100.64.0.0/10, fc00::/7
ForwardedHeaders__ForwardLimit        = 1     (one proxy hop: Render's load balancer)
```

**Honest status: the exact address Render's load balancer connects from is not published in Render's documentation, so "it is
a private address" is an unverified assumption.** It is a safe one: Render states a web service "is not directly reachable via
the public internet", so only Render's own proxy (and your own private network) can connect from private ranges. This is not
"trust everyone". If the assumption is wrong the symptom is a redirect loop (login fails; `curl -I https://<service>.onrender.com/api/v1/auth/login`
shows a 307/308 to https) or every user sharing one IP.

**Rule: trust only the specific private address or CIDR the load balancer actually connects from. Never add a public or CDN
range**: that would let any client behind it spoof `X-Forwarded-For` and dodge the rate limits.

**One-time confirmation after the first deploy** (no code change needed; the refresh-token row stores the client IP):

1. Sign in once from your own machine. Find your public IP (<https://ifconfig.me>).
2. Temporarily allow your /32 on the database (see "Database is internal-only"), connect with the External connection string and
   run: `SELECT "CreatedByIp", "CreatedAt" FROM "RefreshTokens" ORDER BY "CreatedAt" DESC LIMIT 3;`
   (`\d "RefreshTokens"` shows the real names if they differ.) Then remove the allow rule.
3. Interpret:
   - Your own public IP: forwarded headers work. Done.
   - A private address (for example `10.x.y.z`): the header was not honored, so that address is outside the trusted list. Replace
     the five broad entries with exactly that address's network (for example `10.x.y.0/24`) in the dashboard's environment, then
     repeat the check; it must now show your public IP.
   - A public address that is not yours: do **not** trust it. Open an issue; something other than Render's load balancer is in
     front of the container.

### First-deploy verification checklist

1. `https://<service>.onrender.com/health/ready` returns `Healthy` (database reachable; migrations ran at startup).
2. The site loads at `/`, sign in as the owner. In DevTools -> Network the login request goes to the same origin
   (`/api/v1/auth/login`) and the response sets a refresh `Set-Cookie` (`Secure`, `HttpOnly`, `SameSite=Lax`).
3. Reload: you stay signed in. Deep-link to a client route (for example `/orders`) and reload: the SPA loads.
4. `https://<service>.onrender.com/api/v1/nope` returns a 404 JSON problem, not the SPA.
5. Run the forwarded-headers confirmation above.

### Free plan caveats

- **The whole app sleeps after 15 minutes without traffic, the page included** (it is served by the same service); the next
  request waits about a minute while it starts (Render shows a loading page). In-memory rate-limit counters reset on restart.
  Not suitable for a live till.
- **Free Postgres expires 30 days after creation** (14 days grace to upgrade it to a paid plan, then the database and its data
  are deleted) and has 1 GB storage. Upgrade the database before day 30 to keep the data; export with `pg_dump` if it matters.
  If you recreate it, remember the bootstrap variables (step 6).
- 750 free instance hours per month per workspace (one always-awake service fits).
- Free services: no `preDeployCommand`, no persistent disk, no scaling to several instances.
- `Database__MigrateOnStartup=true` is on: the app applies pending EF migrations itself on every start. Safe with one instance;
  not for several.

### Upgrading to paid

1. **Plans:** in `render.yaml` change `plan: free` to a paid plan on the database and on the service (plan IDs are in the
   Blueprint spec's compute-plan tables), commit and push so the Blueprint sync applies it, or change the plan in the dashboard.
   A paid database does not expire and has backups; a paid service does not sleep. Do not change `postgresMajorVersion` or
   `region` (immutable).
2. **Migrate before deploy instead of at startup** (`preDeployCommand` is paid-only; recommended before running more than one
   instance):
   - In `deploy/Dockerfile`'s `build` stage, after `COPY backend/src/ src/`, build the bundle the way CI's `migration-bundle` job
     does (restore tools and the RID-specific assets, then `dotnet ef migrations bundle --project src/CafePos.Infrastructure
     --self-contained -r linux-x64 --force -o /app/efbundle`; this needs `backend/.config/dotnet-tools.json` and the solution
     files in the stage). `/app` is copied into the runtime image, so `/app/efbundle` ships with it. Untested in the image; run
     the smoke check after adding it.
   - In `render.yaml` on the service add `preDeployCommand: ./efbundle --connection "<Npgsql connection string>"`. The bundle
     takes a keyword connection string, not a URL; build it from the database's host, port, user, password and database (add them
     as separate `fromDatabase` env vars, for example `PG_HOST`... , and run it via `sh -c`) with `SSL Mode=Require;Trust Server
     Certificate=true;GSS Encryption Mode=Disable`.
   - Set `Database__MigrateOnStartup` to `"false"`.
   - The first-owner bootstrap does **not** depend on `MigrateOnStartup`: it runs whenever the `Bootstrap__*` variables are set and
     no users exist, so it still works on paid. Remove the variables after the first login, as on free.
3. **Several instances:** in-memory rate limits are then per instance (see "Rate limiting").
4. Optional later: a custom domain; nothing in the app is tied to a host name, so no setting changes.

### Sources checked (Render docs, fetched 2026-10-29)

Verified: Blueprint spec and JSON schema (`render.com/docs/blueprint-spec`, `render.com/schema/render.yaml.json`: the file
validates with ajv; `fromDatabase.connectionString`; `sync: false` is prompted only at creation; `generateValue`;
`autoDeployTrigger: checksPass`; `dockerfilePath`/`dockerContext`; database `ipAllowList` including `[]`; `buildFilter`), free
instances (`render.com/docs/free`: 15-minute sleep, about 1 minute spin-up, 30-day DB expiry with 14-day grace, 1 GB),
Postgres connections (`render.com/docs/postgresql-creating-connecting`: internal URL needs the same region, self-signed internal
TLS, `verify-*` unsupported), health checks (`render.com/docs/health-checks`), web services (`render.com/docs/web-services`: the
load balancer terminates TLS and forwards plain HTTP; the service is not directly reachable from the internet).
Not documented, so unverified: the source address Render's load balancer connects from (hence the confirmation step); the exact
dashboard labels for the database IP allow list.
Verified locally (Docker, 2026-10-29): `deploy/Dockerfile` builds, runs as uid 1654 (non-root), and `deploy/smoke-single-image.sh`
passes against a throwaway Postgres 16 via `DATABASE_URL`: `/health/ready` 200, `/` and `/orders` return `index.html`,
`/api/v1/nope` is a 404 JSON problem.
