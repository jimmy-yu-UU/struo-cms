# Site Settings — Branding Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a super-admin change the CMS brand name and logo from inside the admin UI, persisted in the database, overriding the deploy-time `appsettings` defaults.

**Architecture:** A singleton `site_settings` DB row (`BrandName`, `LogoFileId`) overrides `BrandingOptions` field-by-field. Anonymous `GET /api/config` composes the effective config (DB → appsettings fallback); a super-admin-only `PUT /api/settings/branding` upserts the row. The logo is a normal media `File` (published files are anonymously downloadable via `/api/files/{id}/content`, so the login page can show it). Frontend adds a super-admin `SettingsView` reusing `FilePicker` + `MediaUploadDropzone`.

**Tech Stack:** .NET 10 / C# · SqlSugarCore · ASP.NET Core Controllers · PostgreSQL (prod) + SQLite (tests) · Vue 3 + PrimeVue + Pinia + vue-i18n · Vitest + xUnit.

## Global Constraints

- All DB access via SqlSugar ORM; zero vendor SQL. Raw SQL only in reviewed `db/migrations/*.sql`. (CLAUDE.md §17.4)
- `InitTables` is Development-only; production schema changes go through numbered migration scripts. Next ordinal: **012** (011 is the latest).
- Outbound JSON = camelCase; all `/api/*` responses use the unified envelope `{success, data}` / `{success:false, error:{code,message}}`. Controllers return `Ok(obj)` / `ApiResults.Fail(...)`; a filter wraps success.
- Cookie-authenticated mutations require the `X-Struo-CSRF` header (enforced globally by `CsrfProtectionMiddleware`; the test auth clients already add it).
- Nullable columns in SqlSugar updates MUST use the entity-typed `SetColumns(s => new Entity { ... })` form (typed-NULL Postgres 42804 gotcha, fix b76f453).
- `text`-typed string columns for any content-bearing string (avoid the varchar(255) Postgres bug class).
- brandName max length = **100**.
- Frontend gate: verify with `cd frontend && pnpm build` (vue-tsc) in addition to vitest — type errors don't surface in unit tests.

---

## File Structure

**Backend — new**
- `src/Struo.Infrastructure/Settings/SiteSettings.cs` — singleton entity.
- `src/Struo.Application/Settings/ISiteSettingsStore.cs` — store interface + `SiteSettingsRecord`.
- `src/Struo.Infrastructure/Settings/SqlSugarSiteSettingsStore.cs` — SqlSugar impl.
- `src/Struo.Api/Controllers/SettingsController.cs` — `PUT /api/settings/branding`.
- `db/migrations/012-site-settings-table.sql` — prod PostgreSQL table.

**Backend — modified**
- `src/Struo.Infrastructure/Metadata/FrameworkEntityTypes.cs` — register `SiteSettings`.
- `src/Struo.Infrastructure/DependencyInjection/DataServiceCollectionExtensions.cs` — register store.
- `src/Struo.Api/Controllers/ConfigController.cs` — read DB → fallback.

**Frontend — new**
- `frontend/src/api/settingsApi.ts` — `updateBranding`.
- `frontend/src/views/SettingsView.vue` — settings page skeleton (branding section).

**Frontend — modified**
- `frontend/src/stores/appConfigStore.ts` — `saveBranding` action.
- `frontend/src/router/index.ts` — `/settings` route.
- `frontend/src/components/shell/TheSidebar.vue` — super-admin nav item.
- `frontend/src/locales/zh-TW.ts`, `frontend/src/locales/en.ts` — `nav.settings` + `settings` namespace.

**Tests — new**
- `tests/Struo.Tests/Settings/SiteSettingsStoreTests.cs`
- `tests/Struo.Tests/Api/ConfigBrandingTests.cs`
- `tests/Struo.Tests/Api/SettingsControllerTests.cs`
- `frontend/src/api/settingsApi.test.ts`
- `frontend/src/views/SettingsView.test.ts`
- (extend) `frontend/src/stores/appConfigStore.test.ts`, `frontend/src/components/shell/TheSidebar.test.ts`

> **Shared-fixture hazard:** the xUnit `ApiIntegration` collection shares one DB. Any test that writes `site_settings` MUST delete the singleton row again at the end, or `ConfigEndpointTests.Config_is_anonymous_and_returns_defaults` (asserts `"StruoCMS"`) can flake depending on order. Cleanup steps are included below.

---

## Task 1: Persistence layer (entity + registration + migration + store)

**Files:**
- Create: `src/Struo.Infrastructure/Settings/SiteSettings.cs`
- Create: `src/Struo.Application/Settings/ISiteSettingsStore.cs`
- Create: `src/Struo.Infrastructure/Settings/SqlSugarSiteSettingsStore.cs`
- Create: `db/migrations/012-site-settings-table.sql`
- Modify: `src/Struo.Infrastructure/Metadata/FrameworkEntityTypes.cs`
- Modify: `src/Struo.Infrastructure/DependencyInjection/DataServiceCollectionExtensions.cs`
- Test: `tests/Struo.Tests/Settings/SiteSettingsStoreTests.cs`

**Interfaces:**
- Produces: `SiteSettings` entity with static `SiteSettings.SingletonId` (Guid); `record SiteSettingsRecord(string BrandName, Guid? LogoFileId)`; `ISiteSettingsStore` with `Task<SiteSettingsRecord?> GetAsync(CancellationToken)` and `Task UpsertAsync(string brandName, Guid? logoFileId, Guid? updatedBy, CancellationToken)`.

- [ ] **Step 1: Write the failing test**

`tests/Struo.Tests/Settings/SiteSettingsStoreTests.cs`:

```csharp
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Application.Settings;
using Struo.Infrastructure.Settings;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Settings;

[Collection("ApiIntegration")]
public class SiteSettingsStoreTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private async Task<T> WithStoreAsync<T>(Func<ISiteSettingsStore, ISqlSugarClient, Task<T>> body)
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISiteSettingsStore>();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        return await body(store, db);
    }

    [Fact]
    public async Task Upsert_inserts_then_updates_a_single_row()
    {
        var logo = Guid.CreateVersion7();
        try
        {
            await WithStoreAsync<object?>(async (store, db) =>
            {
                await store.UpsertAsync("Acme", logo, null, default);
                await store.UpsertAsync("Acme 2", null, null, default);

                var count = await db.Queryable<SiteSettings>()
                    .Where(s => s.Id == SiteSettings.SingletonId).CountAsync();
                count.Should().Be(1);

                var rec = await store.GetAsync(default);
                rec.Should().NotBeNull();
                rec!.BrandName.Should().Be("Acme 2");
                rec.LogoFileId.Should().BeNull();
                return null;
            });
        }
        finally
        {
            await WithStoreAsync<object?>(async (_, db) =>
            {
                await db.Deleteable<SiteSettings>().Where(s => s.Id == SiteSettings.SingletonId).ExecuteCommandAsync();
                return null;
            });
        }
    }

    [Fact]
    public async Task Get_returns_null_when_no_row()
    {
        await WithStoreAsync<object?>(async (_, db) =>
        {
            await db.Deleteable<SiteSettings>().Where(s => s.Id == SiteSettings.SingletonId).ExecuteCommandAsync();
            return null;
        });
        var rec = await WithStoreAsync((store, _) => store.GetAsync(default));
        rec.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter "FullyQualifiedName~SiteSettingsStoreTests"`
Expected: FAIL — compile error, `SiteSettings` / `ISiteSettingsStore` do not exist.

- [ ] **Step 3: Create the entity**

`src/Struo.Infrastructure/Settings/SiteSettings.cs`:

```csharp
using SqlSugar;

namespace Struo.Infrastructure.Settings;

/// <summary>
/// Singleton site settings that override the deploy-time <see cref="Struo.Application.Configuration.BrandingOptions"/>
/// defaults. Internal framework table — NOT a <c>[CmsCollection]</c>, never browsable through the generic
/// item API. Exactly one row, keyed by the well-known <see cref="SingletonId"/>; absence of the row means
/// "use appsettings defaults".
/// </summary>
[SugarTable("site_settings")]
public sealed class SiteSettings
{
    /// <summary>Fixed PK for the single settings row (the upsert target).</summary>
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-000000000001");

    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

    // text: avoids the recurring Postgres varchar(255) mapping (see Revision.Snapshot).
    [SugarColumn(ColumnDataType = "text")] public string BrandName { get; set; } = "";

    [SugarColumn(IsNullable = true)] public Guid? LogoFileId { get; set; }

    public DateTime UpdatedAt { get; set; }
    [SugarColumn(IsNullable = true)] public Guid? UpdatedBy { get; set; }
}
```

- [ ] **Step 4: Create the store interface + record**

`src/Struo.Application/Settings/ISiteSettingsStore.cs`:

```csharp
namespace Struo.Application.Settings;

/// <summary>The effective persisted settings, or absent when no row exists.</summary>
public sealed record SiteSettingsRecord(string BrandName, Guid? LogoFileId);

/// <summary>Reads/writes the singleton site-settings row.</summary>
public interface ISiteSettingsStore
{
    Task<SiteSettingsRecord?> GetAsync(CancellationToken ct = default);
    Task UpsertAsync(string brandName, Guid? logoFileId, Guid? updatedBy, CancellationToken ct = default);
}
```

- [ ] **Step 5: Create the store implementation**

`src/Struo.Infrastructure/Settings/SqlSugarSiteSettingsStore.cs`:

```csharp
using SqlSugar;
using Struo.Application.Settings;

namespace Struo.Infrastructure.Settings;

public sealed class SqlSugarSiteSettingsStore(ISqlSugarClient db) : ISiteSettingsStore
{
    public async Task<SiteSettingsRecord?> GetAsync(CancellationToken ct = default)
    {
        var row = await db.Queryable<SiteSettings>()
            .Where(s => s.Id == SiteSettings.SingletonId).FirstAsync(ct);
        return row is null ? null : new SiteSettingsRecord(row.BrandName, row.LogoFileId);
    }

    public async Task UpsertAsync(string brandName, Guid? logoFileId, Guid? updatedBy, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var exists = await db.Queryable<SiteSettings>()
            .Where(s => s.Id == SiteSettings.SingletonId).AnyAsync(ct);
        if (exists)
        {
            // Entity-typed SetColumns so the nullable logofileid gets a typed NULL (Postgres 42804 fix).
            await db.Updateable<SiteSettings>()
                .SetColumns(s => new SiteSettings
                {
                    BrandName = brandName, LogoFileId = logoFileId, UpdatedAt = now, UpdatedBy = updatedBy
                })
                .Where(s => s.Id == SiteSettings.SingletonId)
                .ExecuteCommandAsync(ct);
        }
        else
        {
            await db.Insertable(new SiteSettings
            {
                Id = SiteSettings.SingletonId, BrandName = brandName, LogoFileId = logoFileId,
                UpdatedAt = now, UpdatedBy = updatedBy
            }).ExecuteCommandAsync(ct);
        }
    }
}
```

- [ ] **Step 6: Register the entity for InitTables**

`src/Struo.Infrastructure/Metadata/FrameworkEntityTypes.cs` — add to the `All` list:

```csharp
        typeof(Struo.Infrastructure.Revisions.Revision),
        typeof(Struo.Infrastructure.Settings.SiteSettings),
    ];
```

- [ ] **Step 7: Register the store in DI**

`src/Struo.Infrastructure/DependencyInjection/DataServiceCollectionExtensions.cs` — after the revision store registration (line ~37):

```csharp
        services.AddScoped<Struo.Application.Settings.ISiteSettingsStore, Struo.Infrastructure.Settings.SqlSugarSiteSettingsStore>();
```

- [ ] **Step 8: Create the production migration**

`db/migrations/012-site-settings-table.sql`:

```sql
-- Site Settings — singleton `site_settings` table backing in-app branding edits.
--
-- CONTEXT: InitTables (CodeFirst, dev/test only) creates this table from SiteSettings.cs on a fresh
-- Development database. This script provisions it on an existing production PostgreSQL database.
-- Identifiers are LOWERCASE and unquoted (SqlSugar emits unquoted identifiers on Postgres, which folds
-- them to lower case). timestamptz per the DB-7 timestamp convention. No seed row: absence means
-- "use appsettings defaults"; the first save inserts the singleton row.
CREATE TABLE IF NOT EXISTS site_settings (
    id         uuid        PRIMARY KEY,
    brandname  text        NOT NULL DEFAULT '',
    logofileid uuid        NULL,
    updatedat  timestamptz NOT NULL,
    updatedby  uuid        NULL
);
```

- [ ] **Step 9: Run tests to verify they pass**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter "FullyQualifiedName~SiteSettingsStoreTests"`
Expected: PASS (2 tests).

- [ ] **Step 10: Commit**

```bash
git add src/Struo.Infrastructure/Settings src/Struo.Application/Settings src/Struo.Infrastructure/Metadata/FrameworkEntityTypes.cs src/Struo.Infrastructure/DependencyInjection/DataServiceCollectionExtensions.cs db/migrations/012-site-settings-table.sql tests/Struo.Tests/Settings
git commit -m "feat: site-settings singleton persistence (entity, store, migration)"
```

---

## Task 2: ConfigController reads DB with field-wise fallback

**Files:**
- Modify: `src/Struo.Api/Controllers/ConfigController.cs`
- Test: `tests/Struo.Tests/Api/ConfigBrandingTests.cs`

**Interfaces:**
- Consumes: `ISiteSettingsStore.GetAsync`.
- Produces: `GET /api/config` → `{ oidcEnabled, brandName, brandLogoUrl }` where DB values override appsettings field-by-field; `brandLogoUrl = /api/files/{LogoFileId}/content` when set.

- [ ] **Step 1: Write the failing test**

`tests/Struo.Tests/Api/ConfigBrandingTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Struo.Application.Settings;
using Struo.Infrastructure.Settings;
using SqlSugar;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

[Collection("ApiIntegration")]
public class ConfigBrandingTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private async Task SeedAsync(string name, Guid? logo)
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISiteSettingsStore>();
        await store.UpsertAsync(name, logo, null, default);
    }

    private async Task ClearAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        await db.Deleteable<SiteSettings>().Where(s => s.Id == SiteSettings.SingletonId).ExecuteCommandAsync();
    }

    [Fact]
    public async Task Config_reflects_saved_brand_name_and_logo_file()
    {
        var logo = Guid.CreateVersion7();
        await SeedAsync("Saved Brand", logo);
        try
        {
            using var doc = JsonDocument.Parse(
                await (await _factory.CreateClient().GetAsync("/api/config")).Content.ReadAsStringAsync());
            var data = doc.RootElement.GetProperty("data");
            data.GetProperty("brandName").GetString().Should().Be("Saved Brand");
            data.GetProperty("brandLogoUrl").GetString().Should().Be($"/api/files/{logo}/content");
        }
        finally { await ClearAsync(); }
    }

    [Fact]
    public async Task Config_falls_back_to_appsettings_logo_when_no_logo_file()
    {
        await SeedAsync("Name Only", null);
        try
        {
            using var doc = JsonDocument.Parse(
                await (await _factory.CreateClient().GetAsync("/api/config")).Content.ReadAsStringAsync());
            var data = doc.RootElement.GetProperty("data");
            data.GetProperty("brandName").GetString().Should().Be("Name Only");
            // No appsettings LogoUrl configured in the default test host → null.
            data.GetProperty("brandLogoUrl").ValueKind.Should().Be(JsonValueKind.Null);
        }
        finally { await ClearAsync(); }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter "FullyQualifiedName~ConfigBrandingTests"`
Expected: FAIL — `brandName` is still `"StruoCMS"` (DB not read yet).

- [ ] **Step 3: Update ConfigController**

Replace the body of `src/Struo.Api/Controllers/ConfigController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Struo.Application.Configuration;
using Struo.Application.Security;
using Struo.Application.Settings;

namespace Struo.Api.Controllers;

/// <summary>Anonymous public bootstrap config for the SPA: whether external OIDC login is available
/// and the effective branding (DB singleton overrides the deploy-time appsettings defaults,
/// field-by-field). A dedicated controller keeps the route as <c>api/config</c>.</summary>
[ApiController]
[Route("api/config")]
public sealed class ConfigController : ControllerBase
{
    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromServices] IOptions<OidcOptions> oidc,
        [FromServices] IOptions<BrandingOptions> branding,
        [FromServices] ISiteSettingsStore settings,
        CancellationToken ct)
    {
        var saved = await settings.GetAsync(ct);
        var name = saved is not null && !string.IsNullOrWhiteSpace(saved.BrandName)
            ? saved.BrandName
            : branding.Value.Name;
        var logoUrl = saved?.LogoFileId is { } id
            ? $"/api/files/{id}/content"
            : branding.Value.LogoUrl;
        return Ok(new { oidcEnabled = oidc.Value.Enabled, brandName = name, brandLogoUrl = logoUrl });
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter "FullyQualifiedName~ConfigBrandingTests|FullyQualifiedName~ConfigEndpointTests"`
Expected: PASS (5 tests — the 3 existing default/config/oidc tests stay green because cleanup removes the seeded row).

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Api/Controllers/ConfigController.cs tests/Struo.Tests/Api/ConfigBrandingTests.cs
git commit -m "feat: config endpoint reads site-settings with appsettings fallback"
```

---

## Task 3: SettingsController — PUT /api/settings/branding

**Files:**
- Create: `src/Struo.Api/Controllers/SettingsController.cs`
- Test: `tests/Struo.Tests/Api/SettingsControllerTests.cs`

**Interfaces:**
- Consumes: `ISiteSettingsStore.UpsertAsync`, `FileService.GetAsync`, `ICurrentPermissions.Current.IsSuperAdmin`, `ICurrentUserAccessor.GetCurrentUserId`.
- Produces: `PUT /api/settings/branding` body `{ brandName, logoFileId }` → `Ok({ brandName, brandLogoUrl })`; 403 non-admin; 400 empty/over-100 name; 400 unknown/unpublished logo file.

- [ ] **Step 1: Write the failing test**

`tests/Struo.Tests/Api/SettingsControllerTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Infrastructure.Settings;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

[Collection("ApiIntegration")]
public class SettingsControllerTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private async Task ClearAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        await db.Deleteable<SiteSettings>().Where(s => s.Id == SiteSettings.SingletonId).ExecuteCommandAsync();
    }

    [Fact]
    public async Task Put_requires_super_admin()
    {
        var (client, _) = await _factory.CreateRolelessClientAsync();
        var resp = await client.PutAsJsonAsync("/api/settings/branding", new { brandName = "X", logoFileId = (string?)null });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Put_rejects_empty_name()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var resp = await client.PutAsJsonAsync("/api/settings/branding", new { brandName = "   ", logoFileId = (string?)null });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Put_rejects_name_over_100_chars()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var resp = await client.PutAsJsonAsync("/api/settings/branding",
            new { brandName = new string('a', 101), logoFileId = (string?)null });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Put_rejects_unknown_logo_file()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var resp = await client.PutAsJsonAsync("/api/settings/branding",
            new { brandName = "Brand", logoFileId = Guid.NewGuid() });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Put_saves_and_returns_effective_config()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        try
        {
            var resp = await client.PutAsJsonAsync("/api/settings/branding",
                new { brandName = "My Brand", logoFileId = (string?)null });
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var data = doc.RootElement.GetProperty("data");
            data.GetProperty("brandName").GetString().Should().Be("My Brand");

            // Anonymous /api/config now reflects the saved name.
            using var cfg = JsonDocument.Parse(
                await (await _factory.CreateClient().GetAsync("/api/config")).Content.ReadAsStringAsync());
            cfg.RootElement.GetProperty("data").GetProperty("brandName").GetString().Should().Be("My Brand");
        }
        finally { await ClearAsync(); }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter "FullyQualifiedName~SettingsControllerTests"`
Expected: FAIL — route `/api/settings/branding` returns 404 (controller missing).

- [ ] **Step 3: Create the controller**

`src/Struo.Api/Controllers/SettingsController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Struo.Api.Auth;
using Struo.Api.Http;
using ErrorCodes = Struo.Api.Http.ErrorCodes;
using Struo.Application.Abstractions;
using Struo.Application.Configuration;
using Struo.Application.Security;
using Struo.Application.Settings;
using FileService = Struo.Infrastructure.Files.FileService;

namespace Struo.Api.Controllers;

public sealed record UpdateBrandingRequest(string? BrandName, Guid? LogoFileId);

/// <summary>Super-admin site settings. Writes go to the singleton <c>site_settings</c> row that
/// <see cref="ConfigController"/> then reflects to every SPA. Cookie writes require the
/// <c>X-Struo-CSRF</c> header (enforced globally by CsrfProtectionMiddleware).</summary>
[ApiController]
[Route("api/settings")]
[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]
public sealed class SettingsController(
    ISiteSettingsStore settings, FileService files, IOptions<BrandingOptions> branding,
    ICurrentPermissions permissions, ICurrentUserAccessor currentUser) : ControllerBase
{
    private const int MaxBrandNameLength = 100;

    [HttpPut("branding")]
    public async Task<IActionResult> UpdateBranding([FromBody] UpdateBrandingRequest body, CancellationToken ct)
    {
        if (!permissions.Current.IsSuperAdmin)
            return ApiResults.Fail(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden, "Admin role required.");

        var name = body.BrandName?.Trim() ?? "";
        if (name.Length == 0)
            return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput, "Brand name is required.");
        if (name.Length > MaxBrandNameLength)
            return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput,
                $"Brand name must be at most {MaxBrandNameLength} characters.");

        if (body.LogoFileId is { } fileId)
        {
            var file = await files.GetAsync(fileId, ct);
            if (file is null || file.Status != "published")
                return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput,
                    "Logo file not found or not published.");
        }

        await settings.UpsertAsync(name, body.LogoFileId, currentUser.GetCurrentUserId(), ct);

        var logoUrl = body.LogoFileId is { } id ? $"/api/files/{id}/content" : branding.Value.LogoUrl;
        return Ok(new { brandName = name, brandLogoUrl = logoUrl });
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj --filter "FullyQualifiedName~SettingsControllerTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Api/Controllers/SettingsController.cs tests/Struo.Tests/Api/SettingsControllerTests.cs
git commit -m "feat: super-admin PUT /api/settings/branding"
```

---

## Task 4: Frontend API + store action

**Files:**
- Create: `frontend/src/api/settingsApi.ts`
- Modify: `frontend/src/stores/appConfigStore.ts`
- Test: `frontend/src/api/settingsApi.test.ts`
- Test (extend): `frontend/src/stores/appConfigStore.test.ts`

**Interfaces:**
- Produces: `updateBranding({ brandName, logoFileId }): Promise<{ brandName, brandLogoUrl }>`; `appConfigStore.saveBranding(body)` updates `brandName` / `brandLogoUrl` from the response.

- [ ] **Step 1: Write the failing test (api)**

`frontend/src/api/settingsApi.test.ts`:

```ts
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { apiClient } from './apiClient'
import { updateBranding } from './settingsApi'

describe('settingsApi', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('PUTs branding and returns the effective config', async () => {
    const put = vi.spyOn(apiClient, 'put').mockResolvedValue({ brandName: 'B', brandLogoUrl: '/api/files/x/content' })
    const res = await updateBranding({ brandName: 'B', logoFileId: 'x' })
    expect(put).toHaveBeenCalledWith('/settings/branding', { brandName: 'B', logoFileId: 'x' })
    expect(res.brandLogoUrl).toBe('/api/files/x/content')
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && pnpm vitest run src/api/settingsApi.test.ts`
Expected: FAIL — cannot import `./settingsApi`.

- [ ] **Step 3: Create settingsApi**

`frontend/src/api/settingsApi.ts`:

```ts
import { apiClient } from './apiClient'

export type BrandingUpdate = { brandName: string; logoFileId: string | null }
export type BrandingResult = { brandName: string; brandLogoUrl: string | null }

export function updateBranding(body: BrandingUpdate): Promise<BrandingResult> {
  return apiClient.put<BrandingResult>('/settings/branding', body)
}
```

- [ ] **Step 4: Add the store action + its test**

Add to `frontend/src/stores/appConfigStore.ts` — import and a new action:

```ts
import { updateBranding, type BrandingUpdate } from '../api/settingsApi'
```

Inside `actions`, after `load()`:

```ts
    async saveBranding(body: BrandingUpdate): Promise<void> {
      const res = await updateBranding(body)
      this.brandName = res.brandName
      this.brandLogoUrl = res.brandLogoUrl
    },
```

Add to `frontend/src/stores/appConfigStore.test.ts`:

```ts
import * as settingsApi from '../api/settingsApi'

it('saveBranding updates store state from the response', async () => {
  setActivePinia(createPinia())
  const store = useAppConfigStore()
  vi.spyOn(settingsApi, 'updateBranding').mockResolvedValue({ brandName: 'New', brandLogoUrl: null })
  await store.saveBranding({ brandName: 'New', logoFileId: null })
  expect(store.brandName).toBe('New')
  expect(store.brandLogoUrl).toBeNull()
})
```

> If `setActivePinia`/`createPinia`/`vi` aren't already imported at the top of `appConfigStore.test.ts`, add them: `import { setActivePinia, createPinia } from 'pinia'` and `import { describe, it, expect, vi } from 'vitest'`.

- [ ] **Step 5: Run tests to verify they pass**

Run: `cd frontend && pnpm vitest run src/api/settingsApi.test.ts src/stores/appConfigStore.test.ts`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/api/settingsApi.ts frontend/src/api/settingsApi.test.ts frontend/src/stores/appConfigStore.ts frontend/src/stores/appConfigStore.test.ts
git commit -m "feat(frontend): settingsApi.updateBranding + appConfigStore.saveBranding"
```

---

## Task 5: SettingsView + i18n

**Files:**
- Create: `frontend/src/views/SettingsView.vue`
- Modify: `frontend/src/locales/zh-TW.ts`, `frontend/src/locales/en.ts`
- Test: `frontend/src/views/SettingsView.test.ts`

**Interfaces:**
- Consumes: `appConfigStore.saveBranding`, `appConfigStore.brandName/brandLogoUrl`, `authStore.user.isSuperAdmin`, `FilePicker` (`v-model` string|null, `image`), `MediaUploadDropzone` (`@uploaded` → `FileMeta`).
- Produces: route component `SettingsView` (used by Task 6).

- [ ] **Step 1: Add i18n keys**

In `frontend/src/locales/zh-TW.ts`: add `settings: '設定'` under the existing `nav:` object, and add a top-level `settings` namespace:

```ts
  settings: {
    title: '站台設定',
    branding: '品牌',
    brandName: '網站名稱',
    logo: 'Logo',
    uploadLogo: '上傳新 Logo',
    removeLogo: '移除 Logo',
    save: '儲存變更',
    saved: '設定已儲存',
    saveFailed: '儲存失敗',
    nameRequired: '網站名稱為必填',
    nameTooLong: '網站名稱不可超過 100 字',
    notPermitted: '需要管理員權限',
  },
```

In `frontend/src/locales/en.ts`: mirror it — `settings: 'Settings'` under `nav:`, and:

```ts
  settings: {
    title: 'Site Settings',
    branding: 'Branding',
    brandName: 'Site name',
    logo: 'Logo',
    uploadLogo: 'Upload new logo',
    removeLogo: 'Remove logo',
    save: 'Save changes',
    saved: 'Settings saved',
    saveFailed: 'Save failed',
    nameRequired: 'Site name is required',
    nameTooLong: 'Site name must be at most 100 characters',
    notPermitted: 'Admin role required',
  },
```

- [ ] **Step 2: Write the failing test**

`frontend/src/views/SettingsView.test.ts`:

```ts
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import SettingsView from './SettingsView.vue'
import { useAppConfigStore } from '../stores/appConfigStore'
import { useAuthStore } from '../stores/authStore'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en: {
  settings: { title: 'Site Settings', branding: 'Branding', brandName: 'Site name', logo: 'Logo',
    uploadLogo: 'Upload new logo', removeLogo: 'Remove logo', save: 'Save changes', saved: 'Settings saved',
    saveFailed: 'Save failed', nameRequired: 'Site name is required', nameTooLong: 'too long',
    notPermitted: 'Admin role required' },
} } })

function mountView() {
  return mount(SettingsView, {
    global: {
      plugins: [i18n],
      stubs: { FilePicker: true, MediaUploadDropzone: true, PageHeader: true,
        Button: { template: '<button @click="$emit(\'click\')"><slot/></button>' },
        InputText: { props: ['modelValue'], template: '<input :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)"/>' },
        Toast: true },
    },
  })
}

describe('SettingsView', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('shows a not-permitted state for non-super-admins', () => {
    const auth = useAuthStore(); auth.user = { id: '1', isSuperAdmin: false, permissions: {} } as never
    const wrapper = mountView()
    expect(wrapper.text()).toContain('Admin role required')
  })

  it('saves branding for a super-admin', async () => {
    const auth = useAuthStore(); auth.user = { id: '1', isSuperAdmin: true, permissions: {} } as never
    const cfg = useAppConfigStore(); cfg.brandName = 'Old'
    const save = vi.spyOn(cfg, 'saveBranding').mockResolvedValue()
    const wrapper = mountView()
    await wrapper.find('input').setValue('New Brand')
    await wrapper.find('[data-test="save"]').trigger('click')
    expect(save).toHaveBeenCalledWith({ brandName: 'New Brand', logoFileId: null })
  })

  it('blocks save when the name is empty', async () => {
    const auth = useAuthStore(); auth.user = { id: '1', isSuperAdmin: true, permissions: {} } as never
    const cfg = useAppConfigStore(); cfg.brandName = ''
    const save = vi.spyOn(cfg, 'saveBranding').mockResolvedValue()
    const wrapper = mountView()
    await wrapper.find('input').setValue('   ')
    await wrapper.find('[data-test="save"]').trigger('click')
    expect(save).not.toHaveBeenCalled()
  })
})
```

- [ ] **Step 3: Run test to verify it fails**

Run: `cd frontend && pnpm vitest run src/views/SettingsView.test.ts`
Expected: FAIL — cannot import `./SettingsView.vue`.

- [ ] **Step 4: Create SettingsView**

`frontend/src/views/SettingsView.vue`:

```vue
<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Toast from 'primevue/toast'
import PageHeader from '../components/common/PageHeader.vue'
import FilePicker from '../components/fields/FilePicker.vue'
import MediaUploadDropzone from '../components/media/MediaUploadDropzone.vue'
import { useAppConfigStore } from '../stores/appConfigStore'
import { useAuthStore } from '../stores/authStore'
import type { FileMeta } from '../api/filesApi'

const { t } = useI18n()
const toast = useToast()
const cfg = useAppConfigStore()
const auth = useAuthStore()

const isAdmin = computed(() => auth.user?.isSuperAdmin === true)

const brandName = ref('')
const logoFileId = ref<string | null>(null)
const saving = ref(false)

onMounted(() => {
  brandName.value = cfg.brandName
  // Recover the logo file id from the config URL (/api/files/{id}/content), if any.
  const m = cfg.brandLogoUrl?.match(/\/api\/files\/([^/]+)\/content/)
  logoFileId.value = m ? m[1] : null
})

function onUploaded(meta: FileMeta): void {
  logoFileId.value = meta.id
}

async function save(): Promise<void> {
  const name = brandName.value.trim()
  if (name.length === 0) { toast.add({ severity: 'warn', summary: t('settings.nameRequired'), life: 3000 }); return }
  if (name.length > 100) { toast.add({ severity: 'warn', summary: t('settings.nameTooLong'), life: 3000 }); return }
  saving.value = true
  try {
    await cfg.saveBranding({ brandName: name, logoFileId: logoFileId.value })
    toast.add({ severity: 'success', summary: t('settings.saved'), life: 3000 })
  } catch (e) {
    toast.add({ severity: 'error', summary: t('settings.saveFailed'),
      detail: e instanceof Error ? e.message : undefined, life: 5000 })
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <Toast />
  <div v-if="!isAdmin" class="settings-denied" role="alert">{{ t('settings.notPermitted') }}</div>
  <template v-else>
    <PageHeader :title="t('settings.title')" />
    <section class="settings-section">
      <h2>{{ t('settings.branding') }}</h2>

      <label class="settings-field">
        <span>{{ t('settings.brandName') }}</span>
        <InputText v-model="brandName" maxlength="100" />
      </label>

      <div class="settings-field">
        <span>{{ t('settings.logo') }}</span>
        <FilePicker v-model="logoFileId" image />
        <MediaUploadDropzone @uploaded="onUploaded" />
      </div>

      <div class="settings-actions">
        <Button data-test="save" :label="t('settings.save')" :loading="saving" @click="save" />
      </div>
    </section>
  </template>
</template>

<style scoped>
.settings-section { max-width: 640px; display: flex; flex-direction: column; gap: 20px; }
.settings-field { display: flex; flex-direction: column; gap: 8px; }
.settings-actions { margin-top: 8px; }
.settings-denied { padding: 24px; color: var(--text); }
</style>
```

> **Remove-logo** is handled by `FilePicker`'s built-in "Clear" button (emits `null`); saving then sends `logoFileId: null`. No extra control needed. If the design later wants a distinct labelled "Remove logo" button, add one that calls `logoFileId = null` using `t('settings.removeLogo')`.
> Verify `FileMeta` is exported from `frontend/src/api/filesApi.ts` (the dropzone imports it there); if the field is named differently than `id`, adjust `onUploaded`.

- [ ] **Step 5: Run tests to verify they pass**

Run: `cd frontend && pnpm vitest run src/views/SettingsView.test.ts src/locales/locales.test.ts`
Expected: PASS (locale parity test still green because both files got the same keys).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/views/SettingsView.vue frontend/src/views/SettingsView.test.ts frontend/src/locales/zh-TW.ts frontend/src/locales/en.ts
git commit -m "feat(frontend): SettingsView branding editor + settings i18n"
```

---

## Task 6: Route + sidebar nav (super-admin gated)

**Files:**
- Modify: `frontend/src/router/index.ts`
- Modify: `frontend/src/components/shell/TheSidebar.vue`
- Test (extend): `frontend/src/components/shell/TheSidebar.test.ts`

**Interfaces:**
- Consumes: `SettingsView` (Task 5), `auth.user.isSuperAdmin`.
- Produces: route name `settings` at `/settings`; sidebar item visible only to super-admins.

- [ ] **Step 1: Add the route**

`frontend/src/router/index.ts` — add the import and a child route:

```ts
import SettingsView from '../views/SettingsView.vue'
```

Inside the `AppShell` `children` array (after `media`):

```ts
        { path: 'settings', name: 'settings', component: SettingsView },
```

- [ ] **Step 2: Write the failing test**

Add to `frontend/src/components/shell/TheSidebar.test.ts` (mirror the existing media-visibility test structure in that file):

```ts
it('shows the settings item only for super-admins', () => {
  // super-admin → visible
  const admin = mountSidebar({ isSuperAdmin: true, permissions: {} })
  expect(admin.text()).toContain('Settings')
  // non-admin → hidden
  const editor = mountSidebar({ isSuperAdmin: false, permissions: {} })
  expect(editor.text()).not.toContain('Settings')
})
```

> Use whatever `mountSidebar`/auth-seeding helper the existing tests in this file already use; match the label the i18n stub returns for `nav.settings` (`'Settings'`).

- [ ] **Step 3: Run test to verify it fails**

Run: `cd frontend && pnpm vitest run src/components/shell/TheSidebar.test.ts`
Expected: FAIL — no settings item rendered.

- [ ] **Step 4: Add the sidebar item**

`frontend/src/components/shell/TheSidebar.vue` — add a computed near `canReadMedia`:

```ts
const isSuperAdmin = computed(() => auth.user?.isSuperAdmin === true)
```

In the template, after the media `SidebarNavItem` and before the `<hr class="nav-sep" ...>`:

```html
      <SidebarNavItem
        v-if="isSuperAdmin"
        :label="t('nav.settings')"
        icon="pi pi-cog"
        :active="route.name === 'settings'"
        @activate="go({ name: 'settings' })"
      />
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `cd frontend && pnpm vitest run src/components/shell/TheSidebar.test.ts`
Expected: PASS.

- [ ] **Step 6: Full frontend gate**

Run: `cd frontend && pnpm build && pnpm vitest run`
Expected: build (vue-tsc) clean; all vitest suites PASS.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/router/index.ts frontend/src/components/shell/TheSidebar.vue frontend/src/components/shell/TheSidebar.test.ts
git commit -m "feat(frontend): /settings route + super-admin sidebar nav"
```

---

## Task 7: Live PostgreSQL + Playwright verification gate

**No new code — this is the acceptance gate.** Unit tests run on SQLite and stub components; the branding feature has three integration risks they structurally cannot catch (per the FE-R7 lesson): the anonymous logo serving, the CSRF-guarded write path, and the store→topbar/tab-title live update.

- [ ] **Step 1: Run the full backend + frontend suites on their normal runners**

Run: `dotnet test tests/Struo.Tests/Struo.Tests.csproj`
Run: `cd frontend && pnpm build && pnpm vitest run`
Expected: all green.

- [ ] **Step 2: Start the app against live PostgreSQL**

Backend: `ASPNETCORE_URLS=http://127.0.0.1:5080 dotnet run --no-launch-profile --project src/Struo.Api` (confirm migration `012` applies at startup — check the log line `MigrationRunner: applied migration '012-site-settings-table.sql'`).
Frontend: `cd frontend && pnpm dev --host 127.0.0.1`.

- [ ] **Step 3: Playwright smoke (use the plugin_playwright MCP)**

Verify each, capturing a screenshot per step:
1. Log in as a super-admin; the **Settings** item appears in the sidebar; navigate to `/settings`.
2. Change the site name, save → success toast; the **topbar brand** and the **browser tab title** update immediately (no reload).
3. Upload a new logo via the dropzone (or pick one via FilePicker), save; open the logo URL and confirm it is `/api/files/{id}/content`.
4. **Log out** and confirm the **login page shows the uploaded logo anonymously** (this exercises the no-auth published-file serving path).
5. Back in Settings, use FilePicker **Clear** → save; confirm the logo falls back (BrandMark initial) on the login page.
6. Confirm a non-super-admin (roleless/editor) does **not** see the Settings nav item and hitting `/settings` shows the not-permitted state.

- [ ] **Step 4: Update memory**

Write a `fe-r8-site-settings-branding-done.md` memory (mirroring the FE-R7 entry) and add an index line to `MEMORY.md` recording: files, the anonymous-logo/CSRF/live-update integration findings, and any deferred minors.

---

## Self-Review

**Spec coverage:** entity/migration/store (Task 1) ✓; field-wise fallback read (Task 2) ✓; super-admin PUT with name+file validation and remove-logo=null (Task 3) ✓; frontend api+store (Task 4) ✓; SettingsView skeleton + FilePicker reuse + upload + i18n + max-100 + remove (Task 5) ✓; route + super-admin sidebar gate (Task 6) ✓; live PG + Playwright + anonymous-logo/CSRF/live-update (Task 7) ✓. Out-of-scope items (other settings, caching, per-locale name) intentionally absent.

**Placeholder scan:** no TBD/TODO; every code step shows complete code. The two `>`-noted verifications (`FileMeta.id` field name; existing `mountSidebar` helper) are explicit "confirm against the real file" instructions, not placeholders — the surrounding code is complete and correct against what was read.

**Type consistency:** `SiteSettings.SingletonId`, `SiteSettingsRecord(BrandName, LogoFileId)`, `ISiteSettingsStore.GetAsync/UpsertAsync`, `updateBranding({brandName, logoFileId})→{brandName, brandLogoUrl}`, `saveBranding(body)`, route name `settings`, i18n `nav.settings`/`settings.*` are used identically across all tasks. `PUT /api/settings/branding` body `{ brandName, logoFileId }` matches `UpdateBrandingRequest(BrandName, LogoFileId)`.
