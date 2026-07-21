# Site Settings — Branding Editor (name + logo)

**Date:** 2026-07-21
**Status:** Design approved (pending spec review)
**Scope:** In-app editing of the CMS brand **name** and **logo**, delivered inside a future-extensible
"Site Settings" page skeleton. Backend persistence + write API + super-admin authorization + frontend
settings page.

---

## 1. Problem & Context

Branding (`brandName`, `brandLogoUrl`) is currently **deploy-time config only**. It is bound from the
`appsettings` `Branding` section into `BrandingOptions` and exposed read-only via anonymous
`GET /api/config`. `BrandingOptions.cs` documents this explicitly: *"The write side (in-app editing) is
a future Site-Settings phase; this is deploy-time config only."*

There is **no** write endpoint, **no** DB persistence, and **no** UI to change the name or upload a logo.
This slice adds them.

### Confirmed constraint (already solved by existing infra)

The login page is **anonymous** and must display the logo. `FilesController.Get` / `Download`
(`GET /api/files/{id}/content`) carry **no `[Authorize]`** — published files are anonymously
downloadable ("public image serving"), and uploads publish by default. Therefore the logo can be stored
as an ordinary media `File` and referenced by id; `brandLogoUrl = /api/files/{id}/content` works on the
anonymous login page with **no new storage or public-serving mechanism**.

---

## 2. Architecture Overview

```
Deploy default (BrandingOptions / appsettings)  ──field-wise fallback──┐
                                                                        ▼
DB singleton `site_settings` row (BrandName, LogoFileId) ─► ConfigController.Get ─► GET /api/config
   ▲                                                                    │  (anonymous)
   │                                                                    ▼
   │                                            appConfigStore ─► LoginView / TheTopbar / tab title
   │
   └── PUT /api/settings/branding (super-admin only) ◄── SettingsView (FilePicker: pick/upload/remove logo)
```

**Key reuse:** the logo is a normal media `File` chosen via the existing `FilePicker`; settings persist
only `LogoFileId` (Guid). No bespoke logo storage.

---

## 3. Backend Design

### 3.1 Entity — `SiteSettings`

- Namespace `Struo.Infrastructure.Settings`; style mirrors `Revision.cs` (internal framework table, **not**
  a `[CmsCollection]`, never browsable through the generic item API).
- `[SugarTable("site_settings")]`.
- Columns:
  - `Id` — `Guid` PK, a **fixed constant** (well-known singleton id). Enforces the single-row invariant.
  - `BrandName` — `[SugarColumn(ColumnDataType = "text")] string` (avoids the recurring varchar(255)
    Postgres bug class; matches `Revision.Snapshot` precedent).
  - `LogoFileId` — `[SugarColumn(IsNullable = true)] Guid?`.
  - `UpdatedAt` — `DateTime` (timestamptz per DB-7 convention).
  - `UpdatedBy` — `[SugarColumn(IsNullable = true)] Guid?`.
- Registered in `FrameworkEntityTypes.All` so `InitTables` creates it on dev/test (SQLite) databases.

### 3.2 Migration — `db/migrations/012-site-settings-table.sql`

- Creates `site_settings` for production PostgreSQL (lowercase unquoted identifiers; `timestamptz`), per
  the existing migration conventions in `db/migrations/README.md`. Next ordinal after `011`.
- No seed row: absence of a row means "use appsettings defaults". A row is created on first save (upsert).

### 3.3 Store — `ISiteSettingsStore`

- Interface in `Struo.Application` (e.g. `Struo.Application.Settings`); impl
  `SqlSugarSiteSettingsStore` in `Struo.Infrastructure.Settings`, registered in
  `DataServiceCollectionExtensions`.
- `Task<SiteSettingsRecord?> GetAsync(CancellationToken)` — returns the singleton row or `null`.
- `Task UpsertAsync(string brandName, Guid? logoFileId, Guid? updatedBy, CancellationToken)` — insert if
  the fixed-id row is missing, else update. Single-row upsert; no race on multiple rows because the id is
  constant.

### 3.4 Read path — `ConfigController.Get`

Inject `ISiteSettingsStore` alongside the existing `IOptions<BrandingOptions>` / `IOptions<OidcOptions>`.
Compose the effective config with **field-wise fallback** (stays anonymous, unchanged route):

- `brandName` = DB `BrandName` when a row exists and the value is non-empty (trimmed); else
  `BrandingOptions.Name` (default `"StruoCMS"`).
- `brandLogoUrl` = `/api/files/{LogoFileId}/content` when `LogoFileId` is set; else
  `BrandingOptions.LogoUrl` (may be null).
- `oidcEnabled` unchanged.

### 3.5 Write endpoint — `SettingsController`

`PUT /api/settings/branding`

- **Authorization:** super-admin only, mirroring `UsersController.RequireAdmin()`
  (`permissions.Current.IsSuperAdmin` → else `403 Forbidden`). Cookie writes require the existing
  `X-Struo-CSRF` header (presence-only) per the established convention.
- **Request body:** `{ brandName: string, logoFileId: string | null }`.
- **Validation** (fail with `400 BadUserInput`, unified envelope):
  - `brandName` required; trimmed length ≥ 1 and ≤ **100**.
  - `logoFileId` optional/nullable. When provided it must reference an **existing published** file
    (looked up via the file service) — otherwise 400. `null` clears the logo (**remove-logo**).
- **Behaviour:** `UpsertAsync(brandName.Trim(), logoFileId, currentUserId, ct)`.
- **Response:** unified success envelope; `data` = the effective config `{ brandName, brandLogoUrl }`
  (same shape the store consumes) so the frontend updates without a re-fetch.

---

## 4. Frontend Design

### 4.1 Routing & gating

- New route `/settings` (name `settings`) as an `AppShell` child in `router/index.ts`.
- **Super-admin gate:** keep it consistent with the existing sidebar pattern rather than extending router
  meta — the nav entry is shown only when `auth.user?.isSuperAdmin === true`, and `SettingsView` guards
  its own content the same way (mirrors `TheSidebar`'s `canReadMedia` computed). A non-admin hitting
  `/settings` directly sees an empty/"not permitted" state, not the editor.

### 4.2 Sidebar

- Add a `nav.settings` item in `TheSidebar.vue`, pinned in the System group (near `media`), rendered only
  for super-admins. Icon: `pi pi-cog`.

### 4.3 `SettingsView.vue`

- Site-settings page **skeleton**: section-based layout; this slice ships only the **Branding** section
  (future sections — favicon, default locale, SEO defaults — drop in later without restructuring).
- Branding section:
  - `brandName` text input (maxlength 100, required, inline validation).
  - `FilePicker` (reused) to pick an existing image or upload a new one → yields `logoFileId`.
  - Current-logo preview + a **"Remove logo"** button that clears the picked file (save then sends
    `logoFileId: null`).
  - Save button; dirty guard (disable/confirm on unsaved changes, consistent with item form); success
    Toast on save, error Toast on failure.

### 4.4 API & store

- `settingsApi.ts`: `updateBranding({ brandName, logoFileId })` → `PUT /api/settings/branding`. Reads
  reuse the existing `/config` (via `appConfigStore`), no separate GET needed.
- `appConfigStore`: add a `saveBranding(...)` action that calls `settingsApi.updateBranding` and, on
  success, updates `brandName` / `brandLogoUrl` from the response — so the topbar `BrandMark` and the
  browser tab title reflect the change immediately, no reload.

### 4.5 i18n

- New `settings` namespace (zh-TW / en): page/section titles, field labels, remove-logo, save,
  validation and success/error messages.

---

## 5. Testing (TDD; matches repo conventions)

**Backend**
- `SqlSugarSiteSettingsStore`: upsert creates then updates a single row; `GetAsync` null when absent.
- `ConfigController` fallback matrix: (a) no row → appsettings defaults; (b) row with name only → DB name
  + config logo; (c) row with `LogoFileId` → `/api/files/{id}/content`.
- `SettingsController` `PUT`: 403 for non-super-admin; 400 for empty/over-100 `brandName`; 400 for a
  `logoFileId` that is missing/unpublished; 200 + effective config on success; CSRF-header requirement.
- Schema parity: `SchemaGuard`/`InitTables` map for the new entity; migration `012` present.

**Frontend**
- `settingsApi.updateBranding` request shape.
- `appConfigStore.saveBranding` updates store state from the response (incl. remove-logo → null).
- `SettingsView`: renders branding section, name validation, save flow, FilePicker interaction,
  remove-logo, non-admin gated state.
- `TheSidebar`: settings item visible only for super-admin.

**Live verification (required before "done")** — per [[db-verify-live-postgres]] and the FE-R7 lesson
that unit tests structurally miss integration bugs:
- Live PostgreSQL + Playwright smoke: change name and see topbar/tab title update; upload a logo and see
  it on the **anonymous login page** (`/api/files/{id}/content` served without auth); remove the logo and
  confirm fallback to `BrandMark`; verify CSRF-header write path.

---

## 6. Out of Scope (YAGNI)

- Other site settings (OIDC parameters, favicon, default locale, SEO defaults) — the page is structured to
  accept them later, but they are not built now.
- Config caching for `GET /api/config` — a single indexed singleton-row read per SPA bootstrap is cheap;
  add caching only if profiling shows a need.
- Localised (per-locale) brand name — single string only.

---

## 7. Affected / New Files (indicative)

**Backend (new):** `Struo.Infrastructure/Settings/SiteSettings.cs`,
`Struo.Application/Settings/ISiteSettingsStore.cs` (+ record),
`Struo.Infrastructure/Settings/SqlSugarSiteSettingsStore.cs`,
`Struo.Api/Controllers/SettingsController.cs`, `db/migrations/012-site-settings-table.sql`.
**Backend (edit):** `FrameworkEntityTypes.cs`, `ConfigController.cs`, `DataServiceCollectionExtensions.cs`.

**Frontend (new):** `views/SettingsView.vue`, `api/settingsApi.ts`, `i18n` `settings` ns (zh-TW/en).
**Frontend (edit):** `router/index.ts`, `components/shell/TheSidebar.vue`, `stores/appConfigStore.ts`.
