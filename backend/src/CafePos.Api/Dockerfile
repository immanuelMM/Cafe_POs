# Build context is backend/ (see docker-compose.yml):  docker build -f src/CafePos.Api/Dockerfile backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Restore layer: only manifests and project files, so it is cached until a dependency changes.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig CafePos.slnx ./
COPY src/CafePos.Domain/CafePos.Domain.csproj src/CafePos.Domain/
COPY src/CafePos.Application/CafePos.Application.csproj src/CafePos.Application/
COPY src/CafePos.Infrastructure/CafePos.Infrastructure.csproj src/CafePos.Infrastructure/
COPY src/CafePos.Api/CafePos.Api.csproj src/CafePos.Api/
RUN dotnet restore src/CafePos.Api/CafePos.Api.csproj
COPY src/ src/
# appsettings.Development.json is excluded from publish in the csproj; secrets are supplied at run time.
RUN dotnet publish src/CafePos.Api/CafePos.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
# Non-root user built into the base image (uid 1654).
USER app
# The runtime image has no curl/wget; bash is enough to speak HTTP to the liveness endpoint.
HEALTHCHECK --interval=10s --timeout=5s --start-period=30s --retries=5 \
  CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health/live HTTP/1.0\r\nHost: localhost\r\n\r\n' >&3 && head -n1 <&3 | grep -q ' 200 '"]
ENTRYPOINT ["dotnet", "CafePos.Api.dll"]
