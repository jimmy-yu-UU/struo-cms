# Production with `Struo:Files:Backend=local` logs no startup warning

## Decision

The host does not log a warning when it starts in Production with `Struo:Files:Backend` set to `local`.
`StruoCmsStartupChecks.VerifyCustomFileStorage` checks the `custom` backend only. `AddStruoFiles`
(`src/Struo.Infrastructure/DependencyInjection/FileStorageServiceCollectionExtensions.cs`) builds
`LocalFileStorage` for `local`, the default (unknown values fail options validation at startup).

The requirement that more than one replica uses `s3` is written in
`docs/guide/en/20-deployment.md`, section "`Struo:Files:Backend`: more than one replica needs `s3`", and
in the matching section of `docs/guide/zh-TW/20-deployment.md`.

## Why

- A single replica on `local` is a supported deployment. A warning on every Production start would
  fire for a correct configuration, and an operator learns to ignore a warning that always fires.
- Using `local` with several replicas fails visibly: a file exists only on the replica that received
  the upload, so a request that lands on another replica gets `404` for it.
- The deployment chapter states the requirement where the operator configures the backend, so the
  warning would repeat documented text.

## Evidence

The Grok second-round recheck (2026-11-02, item OPS-R3) proposed a Production-only warning for `local`
and a unit test for it. The maintainer ruled on 2026-11-02 to keep startup silent for `local` and to
record the trade-off in this file. The ruling's reasons are the three under Why: a single replica is a
supported deployment, a warning on every start is noise, and several replicas on `local` fail visibly.

The same recheck records, from its own `docker compose` run, that the startup log carries no message
about local storage. `VerifyCustomFileStorage` in `src/Struo.Api/Hosting/StruoCmsStartupChecks.cs` has no
branch for `local`; `Struo:Files:Backend` defaults to `"local"` in `src/Struo.Api/appsettings.json`.

## Unknowns

Whether an operator who runs several replicas on `local` reads the deployment chapter first was not
measured. The decision rests on the failure being visible, not on the documentation being read.

## Referenced from

- `src/Struo.Infrastructure/DependencyInjection/FileStorageServiceCollectionExtensions.cs`
