# Single image: the API serves the built SPA (Spa__RootPath), so the browser sees one origin (same-site refresh cookie and the
# real client IP behind Render's load balancer). Build context is the REPO ROOT:
#   docker build -f deploy/Dockerfile -t cafepos:single .
# Build steps mirror backend/src/CafePos.Api/Dockerfile and frontend/Dockerfile; keep them in sync.
FROM node:22-alpine AS web
WORKDIR /app
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
# Root-relative: resolved against the page origin at run time, so no host name is baked into the bundle.
ENV VITE_API_BASE_URL=/api/v1
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/global.json backend/Directory.Build.props backend/Directory.Packages.props backend/.editorconfig backend/CafePos.slnx ./
COPY backend/src/CafePos.Domain/CafePos.Domain.csproj src/CafePos.Domain/
COPY backend/src/CafePos.Application/CafePos.Application.csproj src/CafePos.Application/
COPY backend/src/CafePos.Infrastructure/CafePos.Infrastructure.csproj src/CafePos.Infrastructure/
COPY backend/src/CafePos.Api/CafePos.Api.csproj src/CafePos.Api/
RUN dotnet restore src/CafePos.Api/CafePos.Api.csproj
COPY backend/src/ src/
RUN dotnet publish src/CafePos.Api/CafePos.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
COPY --from=web /app/dist /app/wwwroot-spa
ENV ASPNETCORE_URLS=http://+:8080 \
    Spa__RootPath=/app/wwwroot-spa
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "CafePos.Api.dll"]
