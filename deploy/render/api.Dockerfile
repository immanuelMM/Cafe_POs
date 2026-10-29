# Render-specific API image. Build context is backend/ (render.yaml: dockerContext ./backend):
#   docker build -f deploy/render/api.Dockerfile backend
#
# Same build and runtime as backend/src/CafePos.Api/Dockerfile (keep the two in sync), plus a tiny entrypoint that turns the
# postgresql:// URL Render exposes into the Npgsql keyword connection string. Why not render.yaml `dockerCommand`? Render
# runs it in place of the Dockerfile CMD, and the stock image has an ENTRYPOINT (dotnet CafePos.Api.dll), so the command would
# become arguments to dotnet. Here the entrypoint itself is the wrapper, so nothing depends on how Render combines the two.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig CafePos.slnx ./
COPY src/CafePos.Domain/CafePos.Domain.csproj src/CafePos.Domain/
COPY src/CafePos.Application/CafePos.Application.csproj src/CafePos.Application/
COPY src/CafePos.Infrastructure/CafePos.Infrastructure.csproj src/CafePos.Infrastructure/
COPY src/CafePos.Api/CafePos.Api.csproj src/CafePos.Api/
RUN dotnet restore src/CafePos.Api/CafePos.Api.csproj
COPY src/ src/
RUN dotnet publish src/CafePos.Api/CafePos.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Entrypoint: build ConnectionStrings__Default from Render's variables, then exec the app (dotnet stays PID 1 and gets SIGTERM).
#   DATABASE_URL = postgresql://user:password@host:port/database   (fromDatabase connectionString: used only for host and port)
#   DB_USER, DB_PASSWORD, DB_NAME                                  (fromDatabase user, password, database)
# SSL Mode=Require + Trust Server Certificate=true: Render's internal Postgres uses a self-signed certificate (verify-ca and
# verify-full are not supported there), so the link is encrypted but the certificate is not verified. GSS Encryption Mode=Disable:
# the runtime image has no Kerberos library (docs/deployment.md, "Startup resilience").
RUN printf '%s\n' \
  '#!/bin/sh' \
  'set -eu' \
  ': "${DATABASE_URL:?DATABASE_URL is required}" "${DB_USER:?}" "${DB_PASSWORD:?}" "${DB_NAME:?}"' \
  'hostport="${DATABASE_URL#*@}"' \
  'hostport="${hostport%%/*}"' \
  'host="${hostport%%:*}"' \
  'port="${hostport#*:}"' \
  '[ "$port" = "$hostport" ] && port=5432' \
  'export ConnectionStrings__Default="Host=${host};Port=${port};Database=${DB_NAME};Username=${DB_USER};Password=${DB_PASSWORD};SSL Mode=Require;Trust Server Certificate=true;GSS Encryption Mode=Disable"' \
  'exec dotnet CafePos.Api.dll' \
  > /app/render-entrypoint.sh && chmod 755 /app/render-entrypoint.sh
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app
ENTRYPOINT ["/bin/sh", "/app/render-entrypoint.sh"]
