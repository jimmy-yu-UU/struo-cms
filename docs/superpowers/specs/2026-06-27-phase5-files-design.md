# StruoCMS — Phase 5 (Files / Media) Design

> Date: 2026-06-27
> Source of truth: the StruoCMS master spec (§5 files/media, `FieldInterface.File/Image/Files`).
> Covers **Phase 5 only**. Builds on Phases 0–4 (merged). Activates the placeholder
> `Article.SeoOgImageId` ("FK to files in Phase 5") and the reserved `FieldInterface.File/Image/Files`.

## 1. Goal & scope

Add **file/media management**: a framework-owned `File` collection storing per-asset metadata, a
dedicated multipart **upload** endpoint, **download/serve** endpoints with a `status` access gate, and a
pluggable **storage backend** (`IFileStorage`) with local-disk and S3-compatible (MinIO) implementations.
Other collections reference files by **reusing the Phase-3a relation machinery** (single = one-to-one FK,
multiple = ordered many-to-many). File primary keys are **UUIDs**, which generalizes the id pipeline to be
PK-type-aware.

**In scope (5):**
- `IFileStorage` abstraction; `LocalFileStorage` (disk) + `S3FileStorage` (AWS SDK, `ForcePathStyle` → MinIO).
  Single configured backend at a time (no per-row backend column).
- Framework built-in `File` collection (`Guid` PK) holding metadata: filename, content type, size,
  width/height (images), alt, title, status.
- Multipart upload endpoint; download (content) + info endpoints; `status` gate (`draft`/`published`);
  S3 download via short-lived **presigned URL** (302 redirect), local via API stream.
- Image **dimension extraction** (width/height) at upload via `IImageDimensionReader` (ImageSharp `Image.Identify`).
- Editable descriptive metadata: `alt`, `title`. System metadata (filename/contentType/size/width/height/
  storageKey) is set at upload and immutable thereafter.
- File **references** via Phase-3a relations: single image/file = one-to-one FK; multiple = ordered M2M.
  New `RelationInterface` UI hints: `FilePicker`, `ImagePicker`, `FilesPicker`.
- **PK-type generalization**: the id pipeline (route id parse, M2M id parse, conversion, create-time id
  assignment, filter coercion) handles `Guid` in addition to `long`; existing `long`-keyed collections unchanged.
- Sample wiring: convert `Article.SeoOgImageId` placeholder into a real single-image relation; add an
  ordered `Gallery` M2M to demonstrate the multiple-file case.
- Config (`FileStorageOptions`) validated at startup (fail-fast). Multi-DB Guid mapping verified live (PG `uuid`).

**Out of scope (deferred / rejected):**
- Authentication / authorization (separate later phase). With no auth yet there is a **single** route group;
  draft content is not externally downloadable (404). Draft management happens via the `/api/items/file`
  collection route, which will be locked down when auth lands.
- Thumbnails / image variants / transformations.
- Content-hash (SHA-256) deduplication.
- Translatable file metadata (alt/title localization).
- Per-row storage backend / multi-backend at once; storage migration tooling.
- A general per-collection PK-type configuration system (only `long` + `Guid` are supported).
- Admin UI (Phase 7).

## 2. Decisions locked in brainstorming

| Topic | Decision |
|---|---|
| Storage backend | `IFileStorage` interface; `LocalFileStorage` + `S3FileStorage` (MinIO for tests). Single global backend; **no per-row backend column**. |
| Presign capability | Method on `IFileStorage` (`SupportsPresignedUrls` + `GetPresignedUrlAsync`); local returns null → API streams; S3 returns presigned URL → 302. |
| File integration | Framework `File` collection (like `Language`) for metadata + dedicated multipart upload endpoint. |
| Reference model | Reuse Phase-3a relations: single = one-to-one FK; multiple = ordered M2M. Auto-expanded on read. |
| File PK | **`Guid` (UUID)** — non-enumerable; generalizes the id pipeline to be PK-type-aware. |
| Direct access URL | `/api/files/{id}` (info) and `/api/files/{id}/content` (download). |
| Access control | `status` (`draft`/`published`) gate on the file routes; non-published → 404. No admin/public route split (auth deferred). |
| File status | `draft` / `published` (consistent with `Article`). |
| In-scope extras | Image width/height extraction; alt/title descriptive fields. |
| Deferred extras | Content-hash dedup; thumbnails/variants; alt/title localization; auth. |

## 3. Architecture & component layout

Respects the dependency rule (§2): Domain → nothing; Application → Domain; Infrastructure → Application+Domain;
Api → Application+Infrastructure. Framework code never references `samples/*`.

**Application (abstractions, no external packages):**
- `Struo.Application.Files.IFileStorage` — `SaveAsync(key, stream, ct)`, `OpenReadAsync(key, ct)`,
  `DeleteAsync(key, ct)`, `bool SupportsPresignedUrls`, `Task<string?> GetPresignedUrlAsync(key, TimeSpan ttl, ct)`.
- `Struo.Application.Files.IImageDimensionReader` — `(int width, int height)? TryRead(Stream seekable, string contentType)`.
- `Struo.Application.Files.FileService` — orchestrates upload (validate → store → extract dims → insert row)
  and download authorization (status gate + presign-vs-stream decision).
- `Struo.Application.Files.FileStorageOptions` — strongly-typed config.
- `Struo.Application.Files.StoredUpload` — small result record (storageKey, size, contentType, width?, height?).

**Infrastructure (framework-owned, external packages):**
- `Struo.Infrastructure.Files.FileAsset` — `[CmsCollection("File", Group="System")]`, `[SugarTable("files")]`,
  `Guid` PK. (Named `FileAsset` to avoid clashing with `System.IO.File`.)
- `Struo.Infrastructure.Files.LocalFileStorage` — disk-backed; `SupportsPresignedUrls = false`.
- `Struo.Infrastructure.Files.S3FileStorage` — `AWSSDK.S3`, `ForcePathStyle = true` (MinIO); `SupportsPresignedUrls = true`.
- `Struo.Infrastructure.Files.ImageSharpDimensionReader : IImageDimensionReader` — `SixLabors.ImageSharp` `Image.Identify`.
- `Struo.Infrastructure.Files.FileStorageServiceCollectionExtensions.AddStruoFiles(...)` — binds + validates
  `FileStorageOptions` (fail-fast), registers the selected `IFileStorage`, `IImageDimensionReader`, `FileService`.
- The `File` collection is picked up by the existing startup metadata scan (the framework assembly is already
  included in the scan since Phase 4).

**Api:**
- `Struo.Api.Controllers.FilesController` — `POST /api/files` (upload), `GET /api/files/{id}` (info),
  `GET /api/files/{id}/content` (download). Reads multipart, delegates to `FileService`.

New packages (CPM, latest via `dotnet add package`): `AWSSDK.S3`, `SixLabors.ImageSharp`.

## 4. PK-type generalization (Guid)

`FileAsset` uses a `Guid` PK while existing collections keep `long`. The id pipeline becomes **PK-type-aware**;
all changes are **additive** (the `long` path is unchanged). Verified touch points:

1. `SqlSugarItemRepository.ConvertId(string, EntityDescriptor)` (~`:588`): add a `Guid` → `Guid.Parse` branch.
2. M2M target-id parse in `ItemService` (`:384`, currently `e.GetInt64()`): branch on target PK type —
   `Guid` → `Guid.Parse(e.GetString())`.
3. Single-id `Convert.ChangeType(id, pkProp.PropertyType)` (`:426`) and M2M junction FK assignment
   (`SqlSugarItemRepository :333-334`): `Guid` is not `IConvertible` → add a `Guid` branch
   (`value is Guid` / `Guid.Parse(string)`), falling back to `Convert.ChangeType` for other types.
4. **Create flow**: a `Guid` PK is not DB-identity. On create, when the PK is `Guid` and unset, assign
   `Guid.NewGuid()` before insert and return it (the `long` flow keeps relying on DB identity).
5. Filter value coercion for an `_eq`/`_in` on a `Guid` field: JSON string → `Guid`.

> This is a generalization of the verified Phase 2/3 query core, so it carries regression risk and is
> covered by both the migrated existing suite (must stay green) and new Guid-specific tests, plus the live
> Postgres `uuid` verification gate (§13).

## 5. `FileAsset` entity & metadata

| Field | Type | `[CmsField]` | Notes |
|---|---|---|---|
| Id | `Guid` | — | PK; app-assigned `Guid.NewGuid()` on create |
| StorageKey | `string` | ✗ internal | path within the backend, e.g. `2026/06/{guid:N}.png` |
| FileName | `string` | ✓ (searchable) | original filename (may be multibyte) |
| ContentType | `string` | ✓ | MIME type |
| Size | `long` | ✓ | bytes |
| Width | `int?` | ✓ | images only |
| Height | `int?` | ✓ | images only |
| Alt | `string?` | ✓ editable | descriptive |
| Title | `string?` | ✓ editable | descriptive |
| Status | `string` | ✓ `Select` (`draft:Draft`,`published:Published`) | default `draft` |
| CreatedAt/By, UpdatedAt/By | | | `IAuditable` |

- **Immutable system fields**: filename, contentType, size, width, height, storageKey are written once at
  upload. A metadata update (`PUT /api/items/file/{id}`) only accepts `alt`, `title`, `status`; other
  incoming fields are ignored (enforced in the write path).
- **Storage key scheme**: `{yyyy}/{MM}/{guid:N}{ext}` — the `Guid` prevents collisions and path traversal;
  the original filename lives only in metadata. (The storage-key guid is independent of the row PK.)

## 6. Storage abstraction

`IFileStorage` is the single seam over backends; exactly one implementation is registered per the
configured backend.

- **`LocalFileStorage`**: writes under `FileStorageOptions.Local.RootPath`; `SaveAsync` creates directories
  as needed and writes the stream; `OpenReadAsync` opens a read stream; `DeleteAsync` removes the file
  (best-effort). `SupportsPresignedUrls = false`; `GetPresignedUrlAsync` returns `null`.
- **`S3FileStorage`**: `AWSSDK.S3` `AmazonS3Client` configured with endpoint, credentials, region, and
  `ForcePathStyle = true` for MinIO compatibility. `SaveAsync` → `PutObject`; `OpenReadAsync` → `GetObject`
  stream; `DeleteAsync` → `DeleteObject`. `SupportsPresignedUrls = true`; `GetPresignedUrlAsync` →
  `GetPreSignedURL` with the configured TTL.

The download endpoint asks the storage for a presigned URL; non-null → `302` redirect, null → stream the
content from `OpenReadAsync` with the row's `ContentType` and a `Content-Disposition` derived from `FileName`.

## 7. Configuration (`FileStorageOptions`)

Bound from `Struo:Files:*`; required values validated at startup (fail-fast). Secrets via
configuration/environment, never hardcoded.

```
Struo:Files:Backend            = "local" | "s3"        # required
Struo:Files:MaxUploadBytes     = 26214400              # 25 MB default
Struo:Files:AllowedContentTypes= []                    # empty = allow all; non-empty = allowlist
Struo:Files:Local:RootPath     = "App_Data/uploads"    # required when Backend=local
Struo:Files:S3:Endpoint        = "http://localhost:9000"# required when Backend=s3
Struo:Files:S3:Bucket          = "struo"               # required when Backend=s3
Struo:Files:S3:AccessKey       = "…"                   # required when Backend=s3
Struo:Files:S3:SecretKey       = "…"                   # required when Backend=s3
Struo:Files:S3:Region          = "us-east-1"
Struo:Files:S3:ForcePathStyle  = true
Struo:Files:S3:PresignTtlSeconds = 300
```

Validation: `Backend` ∈ {local, s3}; when `local`, `RootPath` non-empty; when `s3`, endpoint/bucket/
access/secret non-empty. Missing required values throw at startup.

## 8. Upload flow

`POST /api/files` (`multipart/form-data`, file part `file`):

1. Validate the request has a file part (else 400).
2. Validate `Size ≤ MaxUploadBytes` (else 400) and, if `AllowedContentTypes` non-empty, `ContentType` ∈
   allowlist (else 400).
3. Generate storage key; `IFileStorage.SaveAsync(key, stream, ct)`.
4. If the content type is an image, read dimensions via `IImageDimensionReader.TryRead` (from a seekable
   copy/buffer) → width/height.
5. Insert a `FileAsset` row: `Id = Guid.NewGuid()`, status `draft`, storageKey, filename (original,
   UTF-8 preserved), contentType, size, width?, height?.
6. Return `201` with the metadata DTO (including the `id`).

## 9. Download / serve flow & status gate

Single route group under `/api` (no admin/public split; auth deferred):

| Method / Route | Purpose | Gate |
|---|---|---|
| `POST /api/files` | upload (creates `draft`) | — |
| `GET /api/files/{id}` | file info (metadata DTO) | non-`published` → 404 |
| `GET /api/files/{id}/content` | download | non-`published` → 404 |
| `/api/items/file` (existing) | list / edit (`alt`/`title`/`status`) / delete | — (management surface; locked when auth lands) |

- `GET /api/files/{id}/content`: load the row; if `Status != published` → `404` (do not leak existence).
  Otherwise ask `IFileStorage.GetPresignedUrlAsync`; non-null → `302` to the presigned URL (S3/MinIO);
  null → stream `OpenReadAsync` with `ContentType` + `Content-Disposition`.
- `GET /api/files/{id}`: same gate; returns the metadata DTO.
- Publishing a file = `PUT /api/items/file/{id}` setting `status = published` (admin management surface).

## 10. Reference wiring (Phase-3a reuse)

Other collections link to files using the existing relation machinery — no new linking logic.

- **Single image/file** (one-to-one FK): parent gains `Guid? CoverImageId` plus a nav
  `FileAsset? CoverImage` with `[Navigate(NavigateType.OneToOne, nameof(CoverImageId))]` and
  `[CmsRelation(Interface = RelationInterface.ImagePicker, OnDelete = OnDelete.SetNull)]`. Read auto-expands
  the linked `FileAsset` metadata (same as `Author`).
- **Multiple files** (ordered M2M): a junction entity (e.g. `ArticleFile { Guid Id; long ArticleId; Guid FileId; int SortOrder }`)
  plus a nav `List<FileAsset> Gallery` with `[Navigate(typeof(ArticleFile), nameof(ArticleFile.ArticleId), nameof(ArticleFile.FileId))]`
  and `[CmsRelation(Interface = RelationInterface.FilesPicker, SortField = nameof(ArticleFile.SortOrder))]`
  (same as `Tags`).
- `RelationInterface` gains `FilePicker, ImagePicker, FilesPicker` — UI rendering hints only; the backend
  treats these as ordinary relations. The reserved `FieldInterface.File/Image/Files` members remain
  unused for now (file references are modeled as relations, not field interfaces).

**Sample demonstration** (`samples/Struo.Sample.Blog`, the acceptance-test carrier):
- Convert the placeholder `Article.SeoOgImageId` (currently a `long?` scalar mapped as `Number`) into a real
  single-image relation to `File` (`SeoOgImageId` → `Guid?`, nav `SeoOgImage`). This touches `ISeoMeta` and
  the scanner's SEO block (which maps `seoOgImageId` as `Number`).
- Add an ordered `Gallery` M2M (`ArticleFile`) to `Article`.

## 11. Image dimension extraction

`ImageSharpDimensionReader` uses `SixLabors.ImageSharp`'s `Image.Identify` (header-only, no full decode) to
read width/height cheaply. Non-image or unreadable content yields `null` (no width/height). The reader
operates on a seekable buffer so the upload stream can also be persisted.

## 12. Error handling

- **400**: missing file part; size > `MaxUploadBytes`; content type not in allowlist; malformed multipart.
- **404**: unknown id; `GET /api/files/{id}` or `/content` when `Status != published`.
- **500**: storage backend failure — log full detail server-side, return a generic message.
- **Startup fail-fast**: invalid/missing `FileStorageOptions` (e.g. `Backend=s3` with no bucket/credentials).
- **Delete**: deleting a `FileAsset` row best-effort deletes the bytes via `IFileStorage.DeleteAsync`
  (failure logged, not fatal). Deleting a referenced file is governed by the relation's `OnDelete`
  (`SetNull` for the single image; junction rows removed for the gallery).

## 13. Testing strategy

- **Unit**: `LocalFileStorage` (temp dir save/read/delete); `FileStorageOptions` validation; storage-key
  generator; `ImageSharpDimensionReader` (tiny PNG fixture); `FileService` validation (size/type);
  Guid id-conversion points.
- **Integration** (`[Collection("ApiIntegration")]` + `ApiFactory`, SQLite + `LocalFileStorage` to a temp dir):
  upload → 201 + metadata; download streams bytes; published download OK / draft download → 404;
  oversize → 400; disallowed type → 400; multibyte filename round-trip (UTF-8, per the live-PG encoding
  lesson); image upload populates width/height; reference expansion (article with cover image expands the
  linked `FileAsset`); delete removes bytes; Guid relation round-trip (single + gallery).
- **S3/MinIO**: a separate integration category gated on env-provided connection info (provided at test
  time); skipped when unset so the default SQLite suite stays fully green.
- **Migrated suite**: every existing test must stay green after the Guid PK-pipeline generalization and the
  `SeoOgImageId` type change.
- **Live verification gate** (mirrors Phase 4 §12): run the full flow against MinIO + PostgreSQL, including
  Guid `uuid` mapping, presigned download (302), and multibyte round-trip. SQLite green is necessary but
  not sufficient.

## 14. Self-review checklist (filled at plan time)

- Spec coverage: storage abstraction + local/S3 (§3,§6) ✓; framework `File` collection + Guid PK (§4,§5) ✓;
  upload (§8); download + status gate (§9); reference reuse (§10); dimensions (§11); errors (§12); testing +
  live gate (§13). All decisions from §2 mapped.
- Sequencing note for the plan: land the PK-type generalization (§4) and keep the existing suite green
  *before* introducing the Guid-keyed `File` collection and references.
