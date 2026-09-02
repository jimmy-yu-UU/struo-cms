# Three deliberate decisions:
# 1. The build context is the whole repo, not src/Struo.Api/ alone: a fork adds its content
#    project as a ProjectReference from Struo.Api.csproj, so the build needs every project
#    that reference can point at, plus the root Directory.Build.props/Directory.Packages.props.
# 2. db/migrations is copied into the image so Database__MigrationsPath can point at it, but
#    that setting is left unset here, matching the appsettings.json default of not running
#    migrations automatically on boot.
# 3. The runtime stage is Debian-based (aspnet:10.0), not alpine: SqlSugar/globalization need
#    full ICU, which the alpine variant does not ship.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/Struo.Api/Struo.Api.csproj
RUN dotnet publish src/Struo.Api/Struo.Api.csproj --configuration Release --no-restore --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# The base image has neither curl nor wget (verified by probing it directly), so install curl
# here for the HEALTHCHECK below; --no-install-recommends and clearing apt lists keep the layer small.
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish ./
COPY db/migrations ./db/migrations
# /app/logs is where Serilog's File sink (logs/struo-.log, relative to the content root) writes;
# without a writable directory here it silently drops file logging under the non-root user below.
RUN mkdir -p /app/App_Data /app/logs && chown -R app:app /app/App_Data /app/logs
USER app
EXPOSE 8080
VOLUME ["/app/App_Data"]
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD curl --fail --silent http://localhost:8080/health/live || exit 1
ENTRYPOINT ["dotnet", "Struo.Api.dll"]
