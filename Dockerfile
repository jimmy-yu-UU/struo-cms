# Four deliberate decisions:
# 1. The build context is the whole repo, not src/Struo.Api/ alone: a fork adds its content
#    project as a ProjectReference from Struo.Api.csproj, so the build needs every project
#    that reference can point at, plus the root Directory.Build.props/Directory.Packages.props.
# 2. The image applies no migrations on boot, matching the appsettings.json default
#    (Database__MigrateOnStartup=false): run `dotnet Struo.Api.dll migrate` once against the
#    database, or set Database__MigrateOnStartup=true.
# 3. The runtime stage is Ubuntu-based (aspnet:10.0), not alpine: globalization needs full ICU,
#    which the alpine variant does not ship.
# 4. The publish is framework-dependent and runtime-specific: restore and publish take the runtime
#    identifier linux-x64 or linux-arm64 (glibc, as the Ubuntu runtime stage is), so only that
#    platform's native files (SQLite, libvips) land in /app next to the assemblies; /app/runtimes/
#    does not exist. The architecture comes from TARGETARCH when the builder sets it, which also
#    holds when a fork pins this stage to --platform=$BUILDPLATFORM; the legacy builder leaves it
#    empty and uname -m is the fallback. A musl (alpine) runtime stage needs linux-musl-x64 or
#    linux-musl-arm64 here. The runtime stage checks every /app/*.so* with ldd and stops the build
#    when one does not load there.
#
# COPY . . before dotnet restore (rather than copying each .csproj first to prime a
# restore-only layer) trades away NuGet layer caching -- any source edit invalidates the
# restore step too, not just publish -- for staying generic across forks: a per-csproj
# pre-copy would have to enumerate paths, and it cannot preserve an arbitrary fork's project
# layout the way copying everything does.

# Both stages follow the floating 10.0 tag. The trade-off:
# docs/ai/decisions/dockerfile-base-image-choices.md
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY . .
# restore and publish take the same runtime identifier, so publish needs no second restore.
RUN set -eu; \
    arch="${TARGETARCH:-$(uname -m)}"; \
    case "$arch" in \
      amd64|x86_64) rid=linux-x64 ;; \
      arm64|aarch64) rid=linux-arm64 ;; \
      *) echo "unsupported architecture: $arch" >&2; exit 1 ;; \
    esac; \
    echo "Publishing for runtime identifier $rid"; \
    dotnet restore src/Struo.Api/Struo.Api.csproj --runtime "$rid"; \
    dotnet publish src/Struo.Api/Struo.Api.csproj --configuration Release --runtime "$rid" \
      --self-contained false --no-restore --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# The runtime stage upgrades the OS packages first: Ubuntu publishes package fixes before the
# upstream image is rebuilt, and this keeps every build on the current fixes. It is permanent, not a
# workaround for a particular CVE: when the base image already carries the fixes it upgrades nothing.
# The base image has neither curl nor wget (verified by probing it directly), so install curl
# here for the HEALTHCHECK below; --no-install-recommends and clearing apt lists keep the layer small.
# See docs/ai/decisions/dockerfile-base-image-choices.md for what that costs.
RUN apt-get update && DEBIAN_FRONTEND=noninteractive apt-get upgrade -y \
    && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish ./
# A native library for another architecture or C library fails only when first loaded (the first
# image transform or SQLite call), so the build checks them all here.
RUN set -eu; \
    for f in /app/*.so /app/*.so.*; do \
      [ -e "$f" ] || continue; \
      if out=$(ldd "$f" 2>&1) && ! printf '%s\n' "$out" | grep -q 'not found'; then continue; fi; \
      echo "Native library $f does not load on this runtime image" \
           "($(uname -m), $(ldd --version 2>&1 | head -n 1))." >&2; \
      printf '%s\n' "$out" | head -n 5 >&2; \
      echo "The build stage's runtime identifier must match the runtime stage's platform" \
           "and C library." >&2; \
      exit 1; \
    done
# The licenses of the bundled third-party components travel with the image.
COPY THIRD-PARTY-NOTICES.md ./
# /app/logs is where Serilog's File sink (logs/struo-.log, relative to the content root) writes;
# without a writable directory here it silently drops file logging under the non-root user below.
# The DataProtection key ring (cookie encryption keys) lives under the app user's home; owning the
# directory here lets a named volume mounted on it start writable, so sessions survive a restart.
RUN mkdir -p /app/App_Data /app/logs /home/app/.aspnet/DataProtection-Keys \
    && chown -R app:app /app/App_Data /app/logs /home/app/.aspnet \
    && chmod 644 /app/THIRD-PARTY-NOTICES.md
USER app
EXPOSE 8080
# A derived image cannot undo this VOLUME: docs/ai/decisions/dockerfile-base-image-choices.md.
VOLUME ["/app/App_Data"]
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD p=${ASPNETCORE_HTTP_PORTS:-8080}; curl --fail --silent "http://localhost:${p%%;*}/health/live" || exit 1
ENTRYPOINT ["dotnet", "Struo.Api.dll"]
