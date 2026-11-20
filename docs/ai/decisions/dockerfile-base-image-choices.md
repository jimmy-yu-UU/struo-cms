# The root Dockerfile floats its base tags, installs curl, declares a VOLUME and publishes per platform

## Decision

The root `Dockerfile` makes five choices, each with a cost a fork may not want to pay.

- **Floating `10.0` tags.** Both stages (`mcr.microsoft.com/dotnet/sdk:10.0`,
  `mcr.microsoft.com/dotnet/aspnet:10.0`) follow the `10.0` tag. A build that pulls the base image
  (`docker build --pull`, or a fresh CI runner) picks up the runtime's patch releases with no edit. `.github/dependabot.yml` checks the `docker` ecosystem for `/` weekly
  and ignores a major version bump of `mcr.microsoft.com/dotnet/*`, because 11.0 would break `net10.0`.
  The cost is reproducibility: two builds of the same commit can contain different base layers.
- **curl in the runtime stage, for `HEALTHCHECK` only.** The `aspnet` base image has neither curl nor
  wget, so the runtime stage installs curl with `apt-get`. The cost is attack surface: one more
  binary and its libraries in the image.
- **`apt-get upgrade` before the curl install.** Ubuntu publishes package fixes before the upstream
  image is rebuilt, so every build upgrades the OS packages. The cost is that two builds of the same
  commit can differ in OS packages, and the layer is larger.
- **`VOLUME ["/app/App_Data"]`.** Without a mount, files written by the `local` backend (default root
  `App_Data/uploads`) live in an anonymous volume. It survives `docker rm` without `-v`, but a replacement
  container does not see it. The cost is that a derived image cannot remove the declaration.
- **A runtime-specific, framework-dependent publish.** The build stage runs `dotnet restore` and
  `dotnet publish` with `--runtime linux-x64` or `--runtime linux-arm64` and `--self-contained false`,
  so `/app` holds the native files (SQLite, libvips) of one platform next to the assemblies and has no
  `runtimes/` directory. The architecture is `TARGETARCH` when the builder sets it and `uname -m`
  otherwise: `uname -m` names the build machine, which is the target only while the stage runs on the
  target platform, and the legacy builder (`DOCKER_BUILDKIT=0`) never sets `TARGETARCH`. The
  identifiers are glibc ones, matching the Ubuntu runtime stage. After the publish output is copied,
  the runtime stage runs `ldd` over `/app/*.so*` and stops the build when a library does not load on
  that image. The cost is that a fork changes the build stage when it changes the platform story: a
  musl (alpine) runtime stage needs `linux-musl-x64` or `linux-musl-arm64`, and a build stage pinned
  to `$BUILDPLATFORM` relies on `TARGETARCH` for the target.

Alternatives for a fork that wants to change a choice:

- Pin the base image by digest in the `FROM` lines. The `docker` ecosystem in `dependabot.yml` can
  then update the digest.
- Replace the `HEALTHCHECK` with a probe that needs no curl, and drop the `apt-get` line. The probe
  has to reach `/health/live` on the port in `ASPNETCORE_HTTP_PORTS`.
- For a musl runtime stage, map the architecture to `linux-musl-x64` or `linux-musl-arm64` in the
  build stage; the `ldd` check in the runtime stage fails the build until the two agree.
- Mount a named volume (or a host path) on `/app/App_Data` explicitly in the `docker-compose.yml` or
  the orchestrator's spec instead of relying on the declaration.

## Why

A security patch should reach a build without a pull request to the Dockerfile, and the weekly
Dependabot check plus the major-version ignore keep the floating tag on .NET 10. Reproducibility matters
more to deployments that promote one built image through environments; those pin a digest.

The health probe needs an HTTP client in the container and the base image ships none, so curl is the
smallest addition. `VOLUME` is the safe default for a single-replica `local` file backend: forgetting
the mount does not delete the files on a plain container replacement.

A publish with no runtime identifier copies the native files of every platform into the image and
records them all in `Struo.Api.deps.json`. A runtime identifier keeps one platform's files, and
`--self-contained false` leaves the .NET runtime to the base image. A wrong native file otherwise
fails only at the first SQLite or image-transform call, so the build checks it.

## Evidence

Measured 2026-11-04: `docker run --rm --entrypoint sh mcr.microsoft.com/dotnet/aspnet:10.0 -c 'command -v curl wget'`
printed nothing and exited 127, so neither binary is on the base image's `PATH`. The `Dockerfile`
comment above the `apt-get` line records the same probe.

Measured 2026-11-20 on amd64. A publish with no runtime identifier put 41 files under `/app/runtimes/`,
including three `Microsoft.Data.SqlClient.SNI.dll` (win-x64, win-x86, win-arm64), and listed 26 runtime
identifiers in `Struo.Api.deps.json`; `/app` was 279,056,676 bytes. With `--runtime linux-x64` there
is no `/app/runtimes/`, `find /app -iname '*sni*'` finds nothing, the native files are
`/app/libe_sqlite3.so` and `/app/libvips.so.42`, and `/app` is 77,593,486 bytes. A transform
request (`width=128`) on a 640x480 PNG returned a 128x96 PNG, and PostgreSQL, SQL Server, MySQL,
MariaDB and SQLite each applied the migrations and answered `/health/ready` and `/api/config`.

`ldd` over a native library, run in a base image: on `aspnet:10.0` a glibc library exits 0, a library
for the other architecture exits 1 with `not a dynamic executable`, and a musl library exits 0 but
prints `not found` for `libc.so`, so the check reads the output as well as the status. On
`aspnet:10.0-alpine` a glibc library exits 127 with `Error loading shared library`. A copy of the
Dockerfile with an alpine runtime stage, and one that forces `arm64` on an amd64 host, each failed at
the check with the message that names the library, the platform and the C library of the runtime
image. Both builders (BuildKit and `DOCKER_BUILDKIT=0`) built the shipped Dockerfile and logged
`Publishing for runtime identifier linux-x64`.

The Dependabot settings are in `.github/dependabot.yml` (the `docker` entry for `directory: "/"`). The
anonymous-volume behaviour is described in `docs/guide/en/20-deployment.md`, section "`Struo:Files:Backend`:
more than one replica needs `s3`".

## Unknowns

The arm64 image build was not run: the build machine has no arm64 emulation and the first `RUN`
of the runtime stage fails with `exec format error`. A cross publish with `--runtime linux-arm64`
produced AArch64 `libvips.so.42` and `libe_sqlite3.so`, which shows the identifier mapping and nothing
about the rest of an arm64 build.

Whether the image's size or vulnerability scan changes materially without curl was not measured.

## Referenced from

- `Dockerfile`
- `docs/ai/conventions.md`
