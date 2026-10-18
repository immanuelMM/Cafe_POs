FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY global.json CafePos.sln ./
COPY src/CafePos.Api/CafePos.Api.csproj src/CafePos.Api/
RUN dotnet restore src/CafePos.Api/CafePos.Api.csproj
COPY src/CafePos.Api src/CafePos.Api
RUN dotnet publish src/CafePos.Api/CafePos.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "CafePos.Api.dll"]
