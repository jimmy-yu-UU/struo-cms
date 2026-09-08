# 11. Files, Media & Image Transforms

`File` and `MediaFolder` are framework collections (`src/Struo.Infrastructure/Files/*`) that ship with
every StruoCMS install — the media library needs no downstream setup. Mutations to `File` go through a
dedicated pipeline (`FileService`, `IFileStorage`, `FilesController` under `api/files`) rather than the
generic `ItemService`/`ItemsController` surface every other collection uses, because a file upload has
concerns — byte storage, content-type sniffing, image dimensions, on-the-fly transforms — the generic
CRUD path has no reason to carry for collections that aren't files. Reads of the `file`/`mediaFolder`
*metadata* (list/filter/sort a media library, not the bytes themselves) still go through the ordinary
`api/items/file` surface chapters 8 and 9 document — this chapter covers the parts of the file
subsystem those two don't: upload, storage backends, serving, transforms, and licensing.

## The file model and its translations

`File` (`src/Struo.Infrastructure/Files/File.cs`) is `[CmsCollection("File", Group = "System",
DefaultDisplayField = nameof(FileName), Hidden = true)]` — `Hidden` here is the presentation flag from
chapter 4 (omitted from the admin sidebar because the media library is its own dedicated screen), not
the field-level `Hidden` chapter 5 covers. Its own fields:

| Field | Interface | Read-only | Notes |
|---|---|---|---|
| `fileName` | Text | yes | Original filename, set at upload. |
| `contentType` | Text | yes | The validated MIME type stored at upload. |
| `size` | Number | yes | Byte count of the stored blob. |
| `width` / `height` | Number | yes | Pixel dimensions, populated only when the upload is a raster format the dimension reader recognizes (see below) — `null` otherwise, including for every non-image upload. |
| `status` | Select (`draft`/`published`) | no | Governs anonymous/public visibility of the file's bytes and metadata (see Serving below). |

`StorageKey` and `FolderId` carry no `[CmsField]` — `StorageKey` is a pure storage-layer internal (never
projected or writable through any API), while `FolderId` is declared via `[CmsRelation]` instead (a
nullable many-to-one to `MediaFolder`, `OnDelete = OnDelete.Restrict` — chapter 7's relation-delete
guard, so a folder still containing files cannot be deleted).

The **translatable** fields — `title` and `alt` — live on a sidecar entity, `FileTranslation`
(`src/Struo.Infrastructure/Files/FileTranslation.cs`, table `file_translations`, unique on
`(fileid, locale)`), exactly the mechanism chapter 6 documents for any collection's `[CmsTranslations]`
sidecar. An upload seeds the **default-locale** `title` from the filename with its extension stripped
(`FileService.SaveAsync`, `src/Struo.Infrastructure/Files/FileService.cs`) — a dotfile whose
name strips to empty falls back to the full filename, and the result is clamped to 255 characters — so
every uploaded file is immediately human-readable in the media library without an extra edit step.
`alt` is never auto-populated; a locale beyond the default has no seeded row at all until someone writes
one, same as any other translatable collection (chapter 6).

## Upload

```
POST /api/files
Content-Type: multipart/form-data
  file: <binary>       (required)
  folderId: <uuid>     (optional)
```

Requires Cookie or Bearer plus `IFileAccessPolicy.CanWrite()` — the `file`-collection RBAC grant,
enforced here instead of inside `ItemService` because uploads bypass it entirely
(`src/Struo.Api/Auth/FileAccessPolicy.cs`). A successful upload is `201` with the row's public shape and
a `Location: /api/files/{id}` header — chapter 9's `EnvelopeResultFilter`/`CreatedResult` note applies
here too: `FilesController.Upload` calls `Created($"/api/files/{id}", ...)`, and the filter rebuilds
that `CreatedResult` (rather than flattening it to a plain `ObjectResult`) so the header survives
enveloping:

```
$ curl -s -X POST http://localhost:5221/api/files -H "X-Struo-CSRF: 1" -b cookies.txt \
    -F "file=@doc-sample.png;type=image/png"
{"success":true,"data":{"id":"4cac2852-e270-4091-94d1-01831452a9b9","fileName":"doc-sample.png","contentType":"image/png","size":6363,"width":64,"height":48,"status":"published","folderId":null}}
```

Every upload is validated against three independent limits before a single byte is written
(`FileService.UploadAsync`, `src/Struo.Infrastructure/Files/FileService.cs`; defaults from
`Struo:Files` in `src/Struo.Api/appsettings.json`, documented in full in chapter 3):

- **Size** — `Struo:Files:MaxUploadBytes`, default **25 MB** (`26214400` bytes). The client-declared
  `Content-Length` is checked up front (`QueryException`, `400`); the actual streamed bytes are
  independently capped by the same limit via `FileBufferingReadStream`'s buffer limit, so a client that
  under-declares its size and then streams more still fails — as `PAYLOAD_TOO_LARGE` / `413`
  (`DomainErrorMap`, chapter 9), not a silently-truncated file.
- **Content type allow-list** — `Struo:Files:AllowedContentTypes`, defaulting to this exact list shipped
  in `appsettings.json`: `image/jpeg`, `image/png`, `image/gif`, `image/webp`, `image/avif`,
  `image/svg+xml`, `application/pdf`, `text/plain`, `video/mp4`, `audio/mpeg`,
  `application/vnd.ms-powerpoint`, `application/vnd.openxmlformats-officedocument.presentationml.presentation`,
  `application/msword`, `application/vnd.openxmlformats-officedocument.wordprocessingml.document`,
  `application/vnd.ms-excel`, `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`,
  `application/zip`, `application/x-zip-compressed`. **An empty array allows every content type** —
  this is the documented meaning of `[]`, not a misconfiguration.
- **Signature consistency** — `FileSignatureValidator`
  (`src/Struo.Infrastructure/Files/FileSignatureValidator.cs`) sniffs the first bytes against the
  *declared* content type for the formats it knows a magic-byte signature for (PNG, JPEG, GIF, WebP,
  PDF); a mismatch is rejected (`QueryException`, `400`) — this stops a script stored as `image/png` from
  later being served as one. A declared type with no known signature (e.g. `text/plain`,
  `application/octet-stream`) has nothing to contradict and is allowed through unchanged.

Pixel dimensions are read from the file's **header only** — `ImageDimensionReader`
(`src/Struo.Infrastructure/Files/ImageDimensionReader.cs`) parses the PNG/GIF/WebP/JPEG container
structure directly (magic bytes plus a handful of fixed offsets) without ever decoding pixel data, which
is why it adds no attack surface for an untrusted upload. It recognizes exactly those four raster
formats regardless of the declared content type; anything else — including `image/svg+xml`, which
*is* in the default allow-list — leaves `width`/`height` as `null`.

The storage key itself is generated, never client-supplied: `StorageKey.Create`
(`src/Struo.Infrastructure/Files/StorageKey.cs`) builds `{yyyy}/{MM}/{guid:N}{ext}` from the current UTC
date and a fresh GUID, so keys never collide and the original filename never reaches the storage layer's
namespace.

## Storage backends: `local` and `s3`

`Struo:Files:Backend` selects exactly one registered `IFileStorage` (the `AddSingleton<IFileStorage>`
registration in `FileStorageServiceCollectionExtensions.AddStruoFiles`,
`src/Struo.Infrastructure/DependencyInjection/FileStorageServiceCollectionExtensions.cs`) — `"local"`
(the default) or `"s3"`; any other value fails startup validation (chapter 3).

**`local`** (`LocalFileStorage`, `src/Struo.Infrastructure/Files/LocalFileStorage.cs`) writes/reads plain
files under `Struo:Files:Local:RootPath` (default `App_Data/uploads`). Every resolved path is checked
against the root with a trailing-separator-safe prefix comparison before any I/O — a storage key that
would resolve outside the root throws `InvalidOperationException` rather than escaping it. This backend
never supports presigned URLs (`SupportsPresignedUrls => false`) and ignores the passed content type on
save — bytes are always streamed back through the API itself, with the persisted `File.ContentType` as
the `Content-Type` header (`FilesController.Download`), never a type recorded at the storage layer. **This
is the backend live-verified throughout this chapter** — the running host used for every example above
is configured with `Struo:Files:Backend = "local"`.

**`s3`** (`S3FileStorage`, `src/Struo.Infrastructure/Files/S3FileStorage.cs`) is an S3-compatible client
(`AmazonS3Client`) configured from `Struo:Files:S3`:

| Key | Default | Notes |
|---|---|---|
| `Endpoint` | `REPLACE_ME` (required) | S3-compatible endpoint URL, e.g. `http://localhost:9000` for local MinIO. |
| `Bucket` | `REPLACE_ME` (required) | Target bucket name. |
| `AccessKey` / `SecretKey` | `REPLACE_ME` (required) | Credentials for the endpoint above. |
| `Region` | `"us-east-1"` | Passed to the AWS S3 SDK client. |
| `ForcePathStyle` | `true` | Path-style addressing — needed by MinIO and most self-hosted S3-compatible servers, which don't support virtual-hosted-style bucket URLs. |
| `PresignTtlSeconds` | `300` | Lifetime of a generated presigned URL, in seconds. |

`Endpoint`/`Bucket`/`AccessKey`/`SecretKey` are all required (startup-validated) once `Backend` is `s3`
(chapter 3). The backend records the validated content type as S3 object metadata on save (so a
direct/presigned `GET` serves the right `Content-Type` even without the API in the loop) and does support
presigned URLs — `GetPresignedUrlAsync` mints a time-limited `GET` URL with
`ResponseHeaderOverrides.ContentDisposition = "attachment"` forced on, so a browser following the redirect
downloads rather than renders — defence in depth for a type (e.g. SVG) the API's own `Content-Disposition`
handling wouldn't otherwise cover on that path. `AmazonS3Config.UseHttp` is derived from whether
`Endpoint` starts with `http://`, because the SDK's presigned-URL default scheme is `https`, which an
http-only MinIO endpoint would then refuse after the redirect.

**MinIO for local development:** `docker-compose.yml`'s `minio` service sits behind the `s3` Compose
profile (not started by a plain `docker compose up -d`, chapter 2) — `docker compose --profile s3 up -d`
starts it plus a one-shot `createbuckets` container that creates the bucket and exits 0. Its shipped
defaults: API port **9000**, console port **9001**, credentials `struoadmin`/`struoadmin` (development
only — never reused anywhere else), bucket **`struo-media`**. Every port is overridable without editing
the tracked compose file — `STRUO_MINIO_PORT` / `STRUO_MINIO_CONSOLE_PORT` env vars (or the gitignored
`.env`, copied from `.env.example`), the same convention chapter 2 documents for `STRUO_PG_PORT`/
`STRUO_REDIS_PORT`. A `Struo:Files:S3` block pointed at that container's defaults:

```json
"Struo": {
  "Files": {
    "Backend": "s3",
    "S3": {
      "Endpoint": "http://localhost:9000",
      "Bucket": "struo-media",
      "AccessKey": "struoadmin",
      "SecretKey": "struoadmin",
      "ForcePathStyle": true
    }
  }
}
```

**This chapter verifies the `local` backend live and documents `s3` from `S3FileStorage`/
`FileStorageOptions.S3Options` and `docker-compose.yml` directly** — the `s3` backend itself is not
exercised live here.

## Serving files: authenticated vs. anonymous, presigned redirects

`GET /api/files/{id}` and `GET /api/files/{id}/content` carry no `[Authorize]` — anonymous requests are
allowed through so a **published** file's metadata/bytes can be embedded in public pages without a
session. A **non-published** (`status != "published"`) file additionally requires
`IFileAccessPolicy.CanReadUnpublished` — an **authenticated identity** and a `file`-collection
**`CanWrite`** grant (`FileAccessPolicy.CanReadUnpublished`, `FileAccessPolicy.cs`), not a
`CanRead` grant and not merely being logged in. The gate is write, deliberately: `public` is a
permission floor for every caller (chapter 12), so once `file` carries
a public *read* grant — the documented setup for serving images anonymously — `CanRead("file")` is true
for anonymous and authenticated callers alike and would gate nothing at all; a draft is an editorial
state, so the caller who may *edit* files is the caller who may see them. The **shipped** seed only
ever gives `public` a read grant — `RbacSeeder.SeedAsync`
(`src/Struo.Infrastructure/Identity/RbacSeeder.cs`) inserts `CanRead = true` and nothing else for every
`Rbac:PublicReadCollections` entry, `CanWrite`/`CanDelete` are never touched — but nothing in the RBAC model
*forbids* a super-admin from granting `public` write on `file` through the Role permission matrix (chapter 12
demonstrates exactly this grant flow, live, against the `role` collection); doing so on `file` would widen
draft access to every signed-in caller, since the write grant this section gates on would then be public too.
This check returns a plain `404` rather than `403` when it fails, so a draft's existence isn't leaked to a
caller without the grant (`FilesController.Get`/`Download`,
`src/Struo.Api/Controllers/FilesController.cs`). This also means a **trashed** file
(soft-deleted — see below) 404s from both actions too, since `FileService.GetAsync` reads through the
same `ISoftDeletable` query filter every other read does — live-verified:

```
$ curl -s -i -X DELETE http://localhost:5221/api/files/4cac2852-e270-4091-94d1-01831452a9b9 -H "X-Struo-CSRF: 1" -b cookies.txt
HTTP/1.1 204 No Content

$ curl -s -i -b cookies.txt http://localhost:5221/api/files/4cac2852-e270-4091-94d1-01831452a9b9
HTTP/1.1 404 Not Found
{"success":false,"error":{"code":"NOT_FOUND","message":"Resource not found."}}
```

When no transform is requested (see below) and `Struo:Files:PresignedRedirect` is `true`, `Download`
`302`-redirects to a storage-presigned URL instead of streaming the bytes itself — an explicit
deployment opt-in for setups where the browser can reach storage/a CDN directly, off (`false`) by
default so the admin SPA's thumbnails/previews work unconditionally through the API and the storage
endpoint itself can stay private. On the `local` backend this setting has no effect
(`GetPresignedUrlAsync` always returns `null` there), so `Download` falls through to streaming
regardless.

## Media folders

`MediaFolder` (`src/Struo.Infrastructure/Files/MediaFolder.cs`) is a second framework collection — a
pure organisational tree (self-referencing `ParentId`, `TreeSelect` relation interface), decoupled from
physical storage keys entirely. It is `Hidden` the same way `File` is (its only UI is the media library);
CRUD goes through the ordinary `api/items/mediaFolder` surface, not `FilesController`. Deleting a folder
that still contains files or subfolders is rejected the same way any `OnDelete.Restrict` relation is
(chapter 7) — no bespoke guard code, just the declared relations on `File.Folder` and
`MediaFolder.Parent`:

```
$ curl -s -i -X DELETE http://localhost:5221/api/items/mediaFolder/<id> -H "X-Struo-CSRF: 1" -b cookies.txt
{"success":false,"error":{"code":"CONFLICT","message":"Cannot delete 'mediaFolder/<id>': referenced by 'file'."}}
```

## On-the-fly image transforms

```
GET /api/files/{id}/content?width=&height=&format=&fit=&quality=
```

A transform is attempted only when the feature is enabled (`Struo:Files:ImageTransform:Enabled`, default
`true`), at least one of `width`/`height`/`format` is present, and the file's `contentType` starts with
`image/` — otherwise `Download` falls straight through to the plain passthrough/redirect behavior above,
unchanged (`FilesController.Download`, `FilesController.cs`, the `wantsTransform` check). Parameters,
from the live code, not just the docs:

| Parameter | Meaning | Clamping / default |
|---|---|---|
| `width` | Target width in pixels | `Math.Clamp(width, 1, Struo:Files:ImageTransform:MaxWidth)` — default `MaxWidth` is **4096**. |
| `height` | Target height in pixels | `Math.Clamp(height, 1, Struo:Files:ImageTransform:MaxHeight)` — default `MaxHeight` is **4096**. |
| `format` | Output format | Must be one of `Struo:Files:ImageTransform:AllowedFormats` — default `["webp", "jpeg", "png", "avif"]` — otherwise `400 BAD_USER_INPUT` ("Unsupported format '…'."); omitted keeps the source bytes' own encoding logic (see below). |
| `fit` | Resize strategy | Any value other than `"cover"` (or `"cover"` with only one of width/height given) is treated as "fit within the box, never upscale". Omitted defaults to `"inside"`, which resolves the same way. |
| `quality` | Encode quality/compression | `Math.Clamp(quality ?? Struo:Files:ImageTransform:DefaultQuality, 1, 100)` — default `DefaultQuality` is **82**. |

Only `fit=cover` with **both** `width` and `height` given actually crops
(`NetVipsImageTransformer.Transform`, `src/Struo.Infrastructure/Files/NetVipsImageTransformer.cs`)
— centre-cropped to exactly that box (libvips `Enums.Size.Both` + `Interesting.Centre`). Every other
combination — `inside`/`contain`/an unrecognized value, or `cover` with only one dimension — resizes to
fit within the given bound(s) without ever upscaling past the source's native resolution (`Size.Down`).
Requesting neither `width` nor `height` (format/quality conversion only) skips the thumbnail path
entirely and re-encodes the full decoded image via `Image.NewFromBuffer` — there's no box to shrink into.

Live-verified against the 64×48 PNG uploaded above (`doc-sample.png`), decoding each response's own
container header to confirm actual output dimensions — not merely a 200 status:

```
$ curl -s -i -b cookies.txt "http://localhost:5221/api/files/<id>/content?width=32&format=webp" -o t1.webp
HTTP/1.1 200 OK
Content-Type: image/webp
# decoded VP8X header: width=32, height=24 (aspect preserved: 64:48 == 32:24)

$ curl -s -i -b cookies.txt "http://localhost:5221/api/files/<id>/content?width=20&height=20&fit=cover&format=png" -o t2.png
HTTP/1.1 200 OK
Content-Type: image/png
# decoded IHDR: width=20, height=20 (fit=cover + both dims -> centre-cropped square)

$ curl -s -b cookies.txt "http://localhost:5221/api/files/<id>/content?format=bogus"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Unsupported format 'bogus'."}}

$ curl -s -i -b cookies.txt "http://localhost:5221/api/files/<id>/content?width=10&quality=500&format=jpeg" -o t3.jpg
HTTP/1.1 200 OK
Content-Type: image/jpeg
# decoded SOF0: width=10, height=8 (quality=500 accepted -> clamped to 100 server-side, no error)

$ curl -s -i -b cookies.txt "http://localhost:5221/api/files/<id>/content?width=5000&format=png" -o t4.png
HTTP/1.1 200 OK
Content-Type: image/png
# decoded IHDR: width=64, height=48 (width=5000 clamped to MaxWidth=4096, but the source is only
# 64px wide and ThumbnailBuffer's Size.Down never upscales past it -> output stays at the source size)
```

Resizing goes through libvips `ThumbnailBuffer` (`Image.ThumbnailBuffer`), not `NewFromBuffer` +
`ThumbnailImage` — the doc comment on `NetVipsImageTransformer` explains why: `ThumbnailImage` operates
on an already-fully-decoded image (no shrink-on-load), so a huge source would be decoded at full
resolution before being shrunk regardless of the requested output size. `ThumbnailBuffer` shrinks on
load for codecs that support it (JPEG/WebP/etc.), bounding decode memory to roughly the requested output
size instead of the source's full resolution — PNG has no cheaper decode path in libvips and still
decodes fully, but the common large-photo case is bounded. A height-only request first peeks the
source's width from its header alone (no pixel decode) so the unconstrained dimension can still be
passed to `ThumbnailBuffer` correctly.

The response's `Content-Type` is derived from `ImageTransformRequest.Format` via `ImageContentTypes`
(`src/Struo.Application/Files/ImageContentTypes.cs`) — `webp`/`jpeg`/`jpg`/`png`/`avif` map to their
obvious MIME types, and anything else (including the omitted case) falls back to `image/webp`. This
mapping is required to stay byte-for-byte identical to the format→content-type switch baked into
`NetVipsImageTransformer.Transform` itself, because a cache **hit** (below) never re-runs the
transformer — the controller has only the cached bytes plus the request's `Format` to derive the
response header from.

If the transform itself throws (a corrupt upload, an unsupported source encoding, a libvips failure),
`Download` logs the exception with the file id and falls back to serving the **original** bytes rather
than failing the request outright (`FilesController.Download`, `FilesController.cs`, the transform
`try`/`catch` fallback) — a broken thumbnail is considered worse than an un-transformed original, but
the error is never silently swallowed.

### Cache location and versioning

Transformed variants are cached on disk — `DiskImageVariantCache`
(`src/Struo.Infrastructure/Files/DiskImageVariantCache.cs`) — under
`Struo:Files:ImageTransform:CachePath` (default `App_Data/image-cache`), resolved against the app's
**content root**, never the process's current working directory (the `IImageVariantCache` registration's
`CachePath` resolution in `FileStorageServiceCollectionExtensions.cs` — the same CWD-vs-content-root
footgun chapter 3 flags for this exact setting). The cache key is the lowercase-hex SHA-256 digest of
the file's id, its current `Version` (the `AuditableEntity` optimistic-concurrency counter — not
`UpdatedAt`, which this entity treats as non-nullable so there's no natural "unset" sentinel to reason
about), and every transform parameter, each on its own tagged line before hashing
(`DiskImageVariantCache.DeriveKey`/`FilesController.Download`, `FilesController.cs`) — so a
re-upload (which bumps `Version`) naturally misses instead of ever serving a stale variant, with no
explicit cache invalidation required. Keys are sharded into a subdirectory
named after their first two hex characters and written atomically (temp file
+ rename) so a concurrent reader never observes a partially-written variant.
Live-verified: after the four transforms above, the cache directory holds one file per distinct key,
each under a two-character shard directory:

```
$ find src/Struo.Api/App_Data/image-cache -type f
src/Struo.Api/App_Data/image-cache/8b/8b6791b1b88b7ad046f6d69219f24cbdb1aec32a811b9a9454fb0528634a0968
src/Struo.Api/App_Data/image-cache/a8/a8906004f2b920caf99b71f67e80424f2755b5d63b6d584d98e2109425455405
src/Struo.Api/App_Data/image-cache/ad/ad7814d39400620fc7cc152244584778e7f1ed86e29c80ad8617e4069fa32833
src/Struo.Api/App_Data/image-cache/d0/d03c56a833ffb5c93816e6a8271de100b96f57dd3c1d4e44bae1f364e67e4964
```

## The libvips/NetVips LGPL note

The transform pipeline is implemented against **NetVips** (MIT-licensed managed wrapper), which loads
the native **libvips** library dynamically at runtime — StruoCMS never links against libvips directly,
and never statically links it either. This matters because libvips itself is **LGPL-2.1-or-later**,
while StruoCMS is MIT. Per `THIRD-PARTY-NOTICES.md`: dynamic linking means LGPL-2.1's terms only oblige
StruoCMS to (a) reproduce the license notice, (b) not restrict a user's ability to relink against a
modified libvips, and (c) make libvips's own source available (it already is, upstream) — StruoCMS
imposes no additional restriction on libvips and embeds no libvips source, so **StruoCMS's own MIT
license is unaffected**. The bundled native version is libvips **8.18.4** (via the
`NetVips.Native` package — not RID-suffixed; it carries native assets for every supported runtime —
pinned in `Directory.Packages.props`); NetVips itself is **3.2.0**. The `NetVips.Native.*` packages
additionally bundle several of libvips's own optional dependencies (mozjpeg,
libpng, libwebp, cairo, pango, librsvg, …) under their own mixed MIT/BSD/LGPLv3 licenses, documented in
each package's own `THIRD-PARTY-NOTICES.md`.

## Trash, restore and purge for files

`File` is the **only** framework collection that implements `ISoftDeletable` (chapter 13 covers the
interface and its global query filter in full; this section is the file-specific walkthrough).
`FileService` exposes the three operations `FilesController`'s `DELETE`/`restore` actions call:

- **`TrashAsync`** (default `DELETE`) — soft-deletes via the same atomic repository primitive every
  other soft-deletable collection uses (`WHERE deletedat IS NULL`, chapter 13); the blob and its
  `FileTranslation` rows are left in place for a later restore. If the trashed file was the current
  brand logo (`site_settings.logofileid`), that reference is cleared in the same transaction so
  `ConfigController` stops resolving it into a dead `/content` URL.
- **`RestoreAsync`** (`POST /{id}/restore`) — clears `DeletedAt`, same as any other soft-deletable
  collection's restore.
- **`DeleteAsync`** (`DELETE ?purge=true`) — a genuine hard delete: the `File` row, its
  `FileTranslation` rows, and the stored blob (best-effort — a storage-layer failure here is swallowed
  since the row is already gone and the bytes are simply orphaned) are all removed. Same
  `site_settings.logofileid` clearing as trash, for the same reason.

Live-verified end to end (upload → trash → restore → trash again → purge):

```
$ curl -s -X POST http://localhost:5221/api/files -H "X-Struo-CSRF: 1" -b cookies.txt -F "file=@purge-test.txt;type=text/plain"
{"success":true,"data":{"id":"a1f1112e-e8c7-4bfa-8aff-460303d8546d","fileName":"purge-test.txt", ...}}

$ curl -s -i -X DELETE http://localhost:5221/api/files/a1f1112e-e8c7-4bfa-8aff-460303d8546d -H "X-Struo-CSRF: 1" -b cookies.txt
HTTP/1.1 204 No Content

$ curl -s -i -X DELETE "http://localhost:5221/api/files/a1f1112e-e8c7-4bfa-8aff-460303d8546d?purge=true" -H "X-Struo-CSRF: 1" -b cookies.txt
HTTP/1.1 204 No Content

$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?deleted=with&sort=fileName"
{"success":true,"data":[{"fileName":"alpha-report.txt", ...},{"fileName":"beta-notes.txt", ...},{"fileName":"doc-sample.png", ...},{"fileName":"gamma-draft.txt", ...}],"meta":{"total":4,"limit":25,"offset":0}}
```

(`purge-test.txt` is absent even here — `?deleted=with` includes trashed rows, but a purge removes the
row entirely, so there is nothing left for any `deleted=` mode to find.)

The purged file's blob was confirmed removed from `App_Data/uploads` on disk (no orphaned file under
its storage key remained).

Upload, trash, restore and purge — `FileService`'s four write paths (upload is [above](#upload)) —
each also raise a post-commit `IItemChangeListener` notification (`Created`/`Trashed`/`Restored`/
`Purged` respectively) through the same `FileService`, since `FileService` bypasses `ItemService`
entirely and so raises its own. Trashing or purging the file that is the current brand logo clears
`site_settings.logofileid` in the same transaction as the trash/purge (see above) but raises nothing
for site settings itself — only the `Trashed`/`Purged` change for the file row. See chapter 13's
[Reacting to writes: `IItemChangeListener`](13-revisions-and-soft-delete.md#reacting-to-writes-iitemchangelistener)
for the full contract.

## Next steps

- Chapter 3, [Configuration Reference](03-configuration-reference.md), for every `Struo:Files:*` key's
  default and validation behavior in isolation.
- Chapter 7, [Relations](07-relations.md), for `OnDelete.Restrict` — the mechanism behind media folders
  rejecting a non-empty delete.
- Chapter 8, [Query DSL](08-query-dsl.md), and chapter 9, [REST API](09-rest-api.md), for the ordinary
  `filter`/`sort`/`deleted=` surface over the `file`/`mediaFolder` collections' own metadata.
- Chapter 12, [Authentication, SSO & RBAC](12-auth-and-rbac.md), for the `file`-collection RBAC grant
  behind `IFileAccessPolicy` and `CanReadUnpublished`.
- Chapter 13, [Revisions & Soft Delete](13-revisions-and-soft-delete.md), for the general
  `ISoftDeletable`/global-query-filter/`deleted=` mechanism `File` is the one live example of.
