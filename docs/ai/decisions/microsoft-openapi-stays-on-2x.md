# Microsoft.OpenApi is pinned directly and stays on the 2.x line

## Decision

`Microsoft.OpenApi` is a direct `PackageReference` of `src/Struo.Api/Struo.Api.csproj`, with its version
in `Directory.Packages.props` (2.13.0). It is not left to resolve as a transitive dependency of
`Microsoft.AspNetCore.OpenApi`. The pin stays on the 2.x line while the project targets .NET 10.
`.github/dependabot.yml` ignores `version-update:semver-major` for `Microsoft.OpenApi` in the `nuget`
entry, so Dependabot proposes 2.x updates only.

Nothing in this repository touches the `Microsoft.OpenApi` API surface; the host calls only
`AddOpenApi()` and `MapOpenApi()`.

## Why

A transitive reference takes whatever version `Microsoft.AspNetCore.OpenApi` allows, and only a direct
reference lets the central pin choose a version above that package's own lower bound. The pin therefore
works as a floor for security fixes in `Microsoft.OpenApi`.

The 2.x ceiling comes from `Microsoft.AspNetCore.OpenApi`, which is compiled against 2.x: its nuspec for
`net10.0` declares `Microsoft.OpenApi` as `[2.12.2, 3.0.0)`. A 3.x pin sits outside that range, and
the shipped assembly would run against a major it was not built for. Dependabot's major-version
proposal would ask for exactly that, which is what the ignore rule prevents.

## Evidence

Checked 2026-11-10 in the NuGet cache: `microsoft.aspnetcore.openapi/10.0.14/microsoft.aspnetcore.openapi.nuspec`
holds `<dependency id="Microsoft.OpenApi" version="[2.12.2, 3.0.0)" exclude="Build,Analyzers" />` in
the `net10.0` group. `Directory.Packages.props` pins `Microsoft.AspNetCore.OpenApi` at 10.0.14 and
`Microsoft.OpenApi` at 2.13.0, so the pin is inside that range.

The security floor is recorded from the earlier explanation in `Directory.Packages.props`, not
re-measured: `Microsoft.OpenApi` 2.0.0 carries advisory GHSA-v5pm-xwqc-g5wc, patched from 2.7.5.

## Unknowns

Whether the direct pin is still needed is open. `Microsoft.AspNetCore.OpenApi` 10.0.14 already requires
2.12.2 or newer, which is above the 2.7.5 advisory fix, so a transitive resolution would also land on a
patched version today. The pin keeps the version under the central file's control if a later
`Microsoft.AspNetCore.OpenApi` lowers its bound.

Whether `Microsoft.AspNetCore.OpenApi` 11.0.x ships against `Microsoft.OpenApi` 3.x is expected from
upstream's direction and not checked. Revisit the pin and the ignore rule when the project moves to
.NET 11.

## Referenced from

- `AGENTS.md`, "Prohibitions"
- `.github/dependabot.yml`, the `nuget` entry's `ignore` rule
