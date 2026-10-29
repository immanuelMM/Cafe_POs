FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props CafePos.slnx ./
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
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "CafePos.Api.dll"]
