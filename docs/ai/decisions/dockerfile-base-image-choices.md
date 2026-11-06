# The root Dockerfile floats its base tags, installs curl and declares a VOLUME

## Decision

The root `Dockerfile` makes three choices, each with a cost a fork may not want to pay.

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

Alternatives for a fork that wants to change a choice:

- Pin the base image by digest in the `FROM` lines. The `docker` ecosystem in `dependabot.yml` can
  then update the digest.
- Replace the `HEALTHCHECK` with a probe that needs no curl, and drop the `apt-get` line. The probe
  has to reach `/health/live` on the port in `ASPNETCORE_HTTP_PORTS`.
- Mount a named volume (or a host path) on `/app/App_Data` explicitly in the `docker-compose.yml` or
  the orchestrator's spec instead of relying on the declaration.

## Why

A security patch should reach a build without a pull request to the Dockerfile, and the weekly
Dependabot check plus the major-version ignore keep the floating tag on .NET 10. Reproducibility matters
more to deployments that promote one built image through environments; those pin a digest.

The health probe needs an HTTP client in the container and the base image ships none, so curl is the
smallest addition. `VOLUME` is the safe default for a single-replica `local` file backend: forgetting
the mount does not delete the files on a plain container replacement.

## Evidence

Measured 2026-11-04: `docker run --rm --entrypoint sh mcr.microsoft.com/dotnet/aspnet:10.0 -c 'command -v curl wget'`
printed nothing and exited 127, so neither binary is on the base image's `PATH`. The `Dockerfile`
comment above the `apt-get` line records the same probe.

The Dependabot settings are in `.github/dependabot.yml` (the `docker` entry for `directory: "/"`). The
anonymous-volume behaviour is described in `docs/guide/en/20-deployment.md`, section "`Struo:Files:Backend`:
more than one replica needs `s3`".

## Unknowns

Whether the image's size or vulnerability scan changes materially without curl was not measured.

## Referenced from

- `Dockerfile`
- `docs/ai/conventions.md`
