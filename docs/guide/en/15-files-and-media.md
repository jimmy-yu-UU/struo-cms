# 15. Files, Media and Image Transforms

This chapter covers how a file gets uploaded, where it's stored, how it reaches a reader, and how
an image is transformed live at download time.

## The `file` collection and its translations

`File` and `MediaFolder` are framework-provided collections. The media library is therefore
present in every install with no extra setup: both the configuration and the code come from the
framework.

Writing a `File` goes through a dedicated pipeline — `FilesController` (routed at `api/files`),
`FileService`, `IFileStorage` — rather than the `ItemService`/`ItemsController` path every other
collection shares. An upload involves byte storage, content-type checking, and image dimensions and
transforms, none of which the generic item CRUD path needs to know about.

Reading, updating and deleting `file` metadata still goes through the ordinary `api/items/file` and
`api/items/mediaFolder` routes: browsing, filtering and sorting the media library is just an
ordinary query. The grants and status codes for each endpoint are in
[Chapter 13: REST API Endpoint Reference](13-rest-endpoints.md).

Creating a `file` through the generic item API is always refused, because `File` declares
`Creatable = false`. The check lives in `ItemService.CreateAsync`, so GraphQL's `createFile` mutation
is blocked by the same message: `Items cannot be created in collection 'file' through the generic items
API.`, status `400`/`BAD_USER_INPUT`. A file is created by uploading it to `POST /api/files`.

`File`'s collection name is `file`, grouped under `System`, and it declares `Hidden = true`. That
governs how the collection itself appears in the admin UI, a different thing from the field-level
`Hidden` covered in [Chapter 6: Field Types and Editors](06-field-types.md).

`file`'s own fields:

| Field | Interface | Writable |
|---|---|---|
| `fileName` | Text | No |
| `contentType` | Text | No |
| `size` | Number | No |
| `width` | Number (nullable) | No |
| `height` | Number (nullable) | No |
| `status` | Select `draft`/`published` | Yes |

`fileName` is the original filename recorded at upload time, and the only field the collection
itself declares `Searchable`. A filename longer than 255 UTF-16 units is shortened to the column
width, keeping its extension and never splitting a surrogate pair. `contentType` is the validated
MIME type; `size` is the actual number of bytes stored.

`width` and `height` are filled in only when the dimensions can be read from the header —
every other upload, image or not, leaves both blank. For a JPEG they follow the Exif orientation:
a photo tagged as turned by 90 degrees is stored with width and height swapped, matching how it is
displayed. WebP and AVIF keep their coded size. `status` is the only one of its own fields
that's writable, and it decides who can read this row and its bytes, covered in the next section.

`folderId` isn't a `[CmsField]` — it's a many-to-one relation declared with `[CmsRelation]`,
pointing at `mediaFolder`, using the `TreeSelect` interface, `OnDelete.Restrict`, which is why
a folder still holding files can't be deleted. It's deliberately not read-only: moving a file to
another folder is just an ordinary item update. `storageKey` carries no `[CmsField]` at all — it's
never projected out, and no write path can touch it.

The translatable `title` and `alt` live in the `FileTranslation` sidecar entity (table
`struo_file_translations`, unique on `(fileid, locale)`), exactly the `[CmsTranslations]` mechanism
described in [Chapter 7: Multilingual Content](07-i18n.md).

On upload, the default locale's `title` is auto-filled from the filename with its extension
stripped; when stripping the extension leaves an empty string (a filename that's nothing but an
extension), it falls back to the full filename, and the result is always truncated to 255
UTF-16 units, again without splitting a surrogate pair.

`alt` is never auto-filled, and no other locale gets an automatically generated row either, unless
someone writes one by hand. `FileTranslation.Title` is also `Searchable`, so `search=` on `file`
matches both the filename and every locale's title.

A new upload takes the status in `Struo:Files:DefaultStatus` (key in
[Chapter 4](04-configuration.md)): `published` by default, or `draft`. The field's own declared
default of `"draft"` is never what an upload gets. With the default `published`, a freshly uploaded
file is readable at once by anyone who has its id, with no separate publish step.

## Uploading

Uploading is `POST /api/files`, `Content-Type: multipart/form-data`, with one required part named
`file` plus an optional `folderId` part carrying a GUID. Uploading also requires the `file` write
grant; without it the response is `Write not permitted.`.

Success is `201`, with `Location` pointing at the file just created, and a body that's the
projected row:

```text
$ POST /api/files  (multipart: file=@swatch.png; type=image/png)
HTTP/1.1 201 Created
Content-Type: application/json; charset=utf-8
Date: Tue, 15 Sep 2026 03:01:43 GMT
Server: Kestrel
Location: /api/files/c4440800-43d3-4ff0-9fe7-b5d7671b30df
Transfer-Encoding: chunked
X-Content-Type-Options: nosniff

{"success":true,"data":{"id":"c4440800-43d3-4ff0-9fe7-b5d7671b30df","fileName":"swatch.png","contentType":"image/png","size":125,"width":64,"height":48,"status":"published","folderId":null}}
HTTP_STATUS:201
```

The caller never sent `width`/`height`, yet the response already has them — detection happens at
upload time.

A malformed request shape is rejected outright, always `400`/`BAD_USER_INPUT`:

- Not multipart: `Expected multipart/form-data.`
- No `file` part: `Missing 'file' part.`
- `folderId` isn't a valid GUID: `Invalid 'folderId'.`
- `folderId` points at a folder that doesn't exist: `Folder '{id}' does not exist.` (an
  application-layer lookup, not a database foreign key)
- Empty file: `Empty file.`

Size gets two independent checks. The caller's own declared `Content-Length` is compared first
against `Struo:Files:MaxUploadBytes` (the key itself is in
[Chapter 4: Configuration Reference](04-configuration.md)); over the limit is `400`,
`File exceeds the maximum size of {n} bytes.`.

If the caller under-reports the size and then streams more bytes than declared, the
buffering stream's own copy of that same cap catches it, this time as `413`/`PAYLOAD_TOO_LARGE`,
with the same message — never a file that gets silently truncated instead. This buffer has a 64 KB
in-memory threshold; past that it spills to a temp file, so several large uploads running at once
don't each hold a full cap's worth in memory.

Content type first passes through the `Struo:Files:AllowedContentTypes` allowlist: left blank means
unrestricted, which is also the compiled-in default; the accompanying `appsettings.json` lists a
separate set (the full list is in [Chapter 4](04-configuration.md)) — a fork that deletes this key
entirely reverts to unrestricted. A type not on the list is `400`,
`Content type '{x}' is not allowed.`.

Past the allowlist, an upload declared as PNG, JPEG (`image/jpg` counts too), GIF, WebP or PDF also
gets its file header bytes checked against the declared type; a mismatch is `400`,
`File contents do not match the declared content type '{x}'.`; a declared type with no known
signature (`text/plain`, for example) has nothing to compare against, so it passes straight
through.

Reading raster dimensions only looks at the bytes in the container header, never decoding any
pixels: PNG, GIF, WebP and JPEG are recognized by their header bytes, independent of the declared
content type; everything else is always left blank, `image/svg+xml` included.

The storage key is generated by the server — the caller can never choose it:
`{yyyy}/{MM}/{guid:N}{extension}`, using the current UTC year and month plus a fresh GUID; the
original filename never appears in the storage-layer path.

The `size` actually stored is the number of bytes the buffering stream genuinely read, not the
caller's declared `Content-Length` — the stream is read to completion first and only then handed to
storage, specifically so a caller that misreports its size can't cause the wrong size to be
recorded or a truncated object to be stored.

The file row and its auto-filled translation row are written together inside the same transaction,
so there's never an intermediate state with a file but no title.

## Storage backends: local, s3 and custom

`Struo:Files:Backend` selects exactly one of the registered `IFileStorage` implementations:
`"local"` (the default), `"s3"` or `"custom"`; any other value fails startup outright. Both backends' keys
and validation rules are in [Chapter 4](04-configuration.md); this section covers only the
behavioral differences.

`local` stores files as plain files under `Struo:Files:Local:RootPath`; every path resolution
checks that it lands inside that root, and a key that tries to escape it throws an exception
before any I/O is attempted.

This relative path resolves against the running process's working directory, unlike the
image-variant cache path covered later, which resolves against the application's content root. The
two settings look similar but behave differently: starting the API from a different directory sends
uploaded files to a different place.

`local` ignores content type when storing a file — the API always determines the response header
from the `contentType` recorded in the database — and it doesn't support presigned URLs at all.
`s3` is the opposite: it records the validated content type as the object's metadata at storage
time, so even a direct or presigned `GET` that bypasses the API gets the correct `Content-Type`,
and it does support presigned URLs.

`s3`'s presigned `GET` also overrides via response headers, forcing
`Content-Disposition: attachment` even for a type the API's own download-disposition logic can't
otherwise reach (SVG, for example), so after following the redirect the browser still downloads it
rather than rendering it directly.

`UseHttp` is decided by whether the configured endpoint starts with `http://`, because the SDK
generates presigned URLs using `https` by default, and an http-only MinIO would reject them after
the redirect.

Both backends throw the same kind of exception when a row exists but its bytes don't: `local` is a
missing file or missing year/month directory, and `s3` recognizes only the `NoSuchKey` error
code — a missing bucket entirely is a configuration error and stays a `500`. Either way, a blob
that can't be found is a clean `404` to the caller, never a `500`.

### Your own storage: `custom`

With `Struo:Files:Backend` set to `custom`, the core builds no storage of its own. Your fork implements
`IFileStorage` and registers it in `Program.cs` after `AddStruoCms()`; a host with `custom` and no
registered `IFileStorage` fails at startup with a message naming the key. The Local and S3 keys are not
validated in this mode. A fork that keeps `local` or `s3` can still register its own `IFileStorage`: the core's
default is registered with `TryAdd`, so yours wins either way.

`IFileStorage.Describe()` returns the one line `files:sweep` prints after `storage:`. It has a default
(the type name); override it to name the container or root path, and never put a key or connection string in
it. A minimal Azure Blob sketch is in [Chapter 18: Extension Points](18-extension-points.md).

A custom storage that returns presigned URLs gets their lifetime from `Struo:Files:S3:PresignTtlSeconds`, the
same key the S3 backend uses.

### S3-compatible services and checksums

The S3 client can add a checksum to every request and check one on every response. Some S3-compatible
services reject such requests, and uploads then fail. `Struo:Files:S3:ChecksumMode` defaults to
`WhenRequired`, which sends a checksum only where the operation demands one. Set `WhenSupported` for a
service that expects them on every operation. See [Chapter 4](04-configuration.md).

## Serving files: who can see what

`GET /api/files/{id}` and `GET /api/files/{id}/content` carry no `[Authorize]` at all — an
anonymous request reaches both: a published file's metadata and bytes can be embedded in a public
page with no session needed.

Visibility follows an unlisted-link model: a `published` file is readable by anyone who has its id,
and the id is the only secret. A file whose `status` isn't `published` needs both conditions at
once: the caller is signed in, and holds a write grant on `file`. There's no separate "read
unpublished" grant flag — this rule is derived from the same three grant flags every collection
already has. Listing the library (`GET /api/items/file`) is a separate matter and needs the
ordinary `file` read grant.

```text
$ GET /api/files/27327d5a-6cb0-40e3-b288-6d1186db6ab9
{"success":false,"error":{"code":"NOT_FOUND","message":"Resource not found."}}
HTTP_STATUS:404
```

```text
$ GET /api/files/27327d5a-6cb0-40e3-b288-6d1186db6ab9
{"success":true,"data":{"id":"27327d5a-6cb0-40e3-b288-6d1186db6ab9","fileName":"note.txt","contentType":"text/plain","size":19,"width":null,"height":null,"status":"draft","folderId":null}}
HTTP_STATUS:200
```

Same id, the only difference is who's calling. The bar is deliberately set at write, not read:
`public` is every caller's grant floor, and once `file` carries a public read grant,
`CanRead("file")` is true for both anonymous and signed-in callers alike, which would make it
useless as a bar. A draft is an editing state, and a caller who can edit files is the caller who
should be able to see drafts.

The accompanying seed data grants `public` only read access, but the framework itself doesn't stop
a super-admin from granting `public` a write grant, which would make drafts visible to every
signed-in caller — an anonymous request is still a `404`.

When this check fails, the response is always a plain `404`, never `403` — whether a draft exists
is never leaked; a file already sitting in the trash also gets `404` from both read actions,
because the query already applies the same soft-delete filter.

A download with no transform is sent as an attachment: the response carries `Content-Disposition`
with the original filename; a successfully transformed response carries no such header, only the
transformed content type (a failed transform that falls back to the original bytes is covered
below).

When `Struo:Files:PresignedRedirect` is on and no transform was requested, the download becomes a
`302` redirect to the storage backend's presigned URL; it's off by default, so the admin UI's
thumbnails display with no extra setup, and so the storage endpoint itself can stay private. On the
`local` backend this setting has no effect at all — a presigned-URL query always returns null, and
the download falls back to streaming as usual.

Every response carries `X-Content-Type-Options: nosniff`, one more layer of defense on top of the
download endpoint's own attachment disposition.

### Making a file private

Set the file's `status` to `draft`. In the admin UI, open the file in the media library and turn
off the Published switch, then save; over the API, send `status` on an ordinary item update:

```text
PUT /api/items/file/{id}
{"status":"draft","version":3}
```

This needs the `file` write grant, and `version` is the usual optimistic-concurrency token. Any
value other than `published` or `draft` is `400`. Switching back to `published` makes the file
readable by anyone with its id again. To make every new upload private from the start, set
`Struo:Files:DefaultStatus` to `draft`.

A brand logo must be `published`. Settings refuse to save a logo that isn't, and a saved logo that is
later switched to `draft` stops resolving: the public settings endpoint falls back to the default
logo. With `DefaultStatus` set to `draft`, a logo uploaded from Settings therefore has to be published
in the media library before the save, or the save returns `400`. A draft also gives anonymous pages and
read-only library users a `404`, so their thumbnails and images are broken until it is published.

## Media folders

`MediaFolder` is the second framework-provided collection: a tree that exists purely to categorize,
self-referencing its parent, entirely decoupled from storage keys, `Hidden` for the same reason as
`File`. A folder's CRUD goes through the ordinary `api/items/mediaFolder`, not through
`FilesController`.

Deleting a folder that still holds files or child folders is blocked by the ordinary
`OnDelete.Restrict`, not by any special-cased logic, returning `409`/`CONFLICT` with the message
`Cannot delete 'mediaFolder/{id}': referenced by 'file'.`.

## Live image transforms

The transform runs through the download endpoint itself, driven by a set of query parameters:
`GET /api/files/{id}/content?width=&height=&format=&fit=&quality=`. A request only actually
transforms when three things are true at once: the feature itself is on (key
`Struo:Files:ImageTransform:Enabled`, default `true`), at least one of `width`/`height`/`format`
was given, and this row's `contentType` is one of `TransformableContentTypes` (JPEG, PNG, WebP,
AVIF and GIF by default).

If any one condition is missing, the request falls straight back to the original stream or
redirect, unaffected. An SVG is the common case: `?width=50` on an SVG returns the SVG itself.

### Parameters

| Parameter | Value | Out of range |
|---|---|---|
| `width` | Integer | Clamped 1–`MaxWidth`, then snapped up to a size step |
| `height` | Integer | Clamped 1–`MaxHeight`, then snapped up to a size step |
| `format` | One of `AllowedFormats` | Other values `400`; missing gives WebP, not source format |
| `fit` | `inside` or `cover` | Other values `400`; missing is `inside` |
| `quality` | Integer | Rounded to a multiple of 10, 10–100; missing: `DefaultQuality` |

`MaxWidth`, `MaxHeight`, `AllowedFormats`, `DefaultQuality`, `SizeSteps` and the other transform
limits are all in [Chapter 4](04-configuration.md). `format` is only checked against `AllowedFormats` when the caller
explicitly gives a value, matched case-insensitively — `?format=WEBP` counts just the same; a value
not on the list is `400`, `Unsupported format '{x}'.`.

Omitting `format` always means WebP, and `Content-Type` changes to `image/webp` right along with
it — it never carries over the source file's own encoding.

Sizes snap so that the number of distinct variants a caller can create stays small: `width=100`
returns a 128-wide image with the default steps, and `width=65` and `width=128` share one cached
variant. A request above the last step gets the last step. `fit=cover` snaps the width and derives
the height from the ratio of the requested `width` and `height`, so the crop keeps the proportions
that were asked for. The derived height is rounded to a multiple of 8 pixels, so a width step has
a limited set of cover heights; `width=300&height=200&fit=cover` returns 320×216.

`fit=cover` only actually crops to that frame when both dimensions are given; a `cover` with just
one dimension scales proportionally to fit inside the frame without enlarging. `fit=inside` snaps
each given side on its own. With neither dimension given, and only format or quality adjusted, the transform skips
the resizing step entirely and re-encodes at the original resolution, since there's no frame to fit.

A request giving only `height` reads the source width from the header first (again without
decoding pixels), so the unconstrained side scales correctly too.

Resizing is handled by libvips: for encodings that support shrink-on-load (JPEG, WebP), it shrinks
while loading, so the decoding memory scales roughly with the output size rather than the source's
full resolution; PNG has no cheaper decode path and is still decoded in full.

If the transform itself throws — a corrupted source, an unsupported encoding, or libvips failing
outright all count — the exception is logged at Warning level along with the file id, and the
original bytes are sent instead, with the request still succeeding.

Those original bytes go out the same way an ordinary download does, so they carry this row's own
content type and attachment `Content-Disposition` — a failed transform's response is
distinguishable from a successful one just by its headers.

Before decoding, the source's width and height are read from its header. A source over
`MaxSourcePixels`, or one whose header cannot be read, is served as its original bytes, so a small
file whose header declares a huge image is not decoded. For a GIF the size is the canvas its frames
need, and for an AVIF it is the largest image size the header declares. A derived AVIF image (a grid
or an overlay) is served as its original bytes, without transformation. When libvips opens the
source, the size it reads is checked against `MaxSourcePixels` again before any pixel is decoded. A client-supplied `quality`
is rounded to a multiple of 10; the configured `DefaultQuality` is used exactly as written. At most `MaxConcurrentTransforms` transforms
run at once, and anonymous requests may hold at most one slot fewer, so an authenticated request
(session cookie or bearer token) can always use the remaining slot. A request that waits longer than `QueueTimeoutSeconds` for a slot it
may use gets `503` with the `IMAGE_TRANSFORM_BUSY` code and a `Retry-After` header.

### Examples

```text
$ GET /api/files/c4440800-43d3-4ff0-9fe7-b5d7671b30df/content?width=5000&format=png
HTTP/1.1 200 OK
Content-Length: 394
Content-Type: image/png
# decoded PNG IHDR: width=64, height=48
# body: 394 bytes (binary, not shown)
HTTP_STATUS:200
```

`width=5000` exceeds `MaxWidth`, but the request isn't rejected — it's clamped to the limit; even
that clamped limit is still larger than the 64×48 source, and since resizing never enlarges, the
output ends up at the source's own dimensions.

### Variant caching

Transformed variants are stored on disk, at the path set by
`Struo:Files:ImageTransform:CachePath` (key in [Chapter 4](04-configuration.md)); a relative path
here resolves against the application's content root, unlike `local`'s own root.

The cache key is a SHA-256 of a labeled text block, lowercased to hex: the file id, this row's
`version`, plus `width`, `height`, `format`, `fit` and `quality`, one per line, taken from the request after
snapping and rounding. Changing any one of them produces a different key. The version stamp used is the optimistic-concurrency `version`
counter, not `UpdatedAt`. Any update to this row advances `version`, which naturally invalidates
old variants with no active invalidation mechanism needed.

Each cache entry is bucketed into a subdirectory by the key's first two hex characters; a write
goes to a temp file in the same directory first and is then moved into place, so a concurrent read
never sees a half-written variant. A read that loses a race against a concurrent write is treated
as a cache miss, not an error.

The cache is size-capped by `CacheMaxBytes` (2 GB by default). Every hit stamps the variant as
recently used; when a write leaves the cache over the cap, the variants that were read least
recently are deleted until the cache is down to 90% of the cap, so the next writes have room. A variant evicted while another request is reading it is read
as a miss and transformed again.

## NetVips and LGPL

The transform pipeline is built on NetVips, an MIT-licensed managed wrapper. StruoCMS loads libvips
through NetVips dynamically at runtime — never statically linked, and never linked directly.

libvips itself is LGPL-2.1-or-later; because the link is dynamic, LGPL-2.1 requires only three
things: reproducing the license notice, not preventing a user from swapping in a modified libvips,
and making libvips's own source available — something upstream already does. StruoCMS adds no
further restriction on top of that and embeds none of libvips's source, so its own MIT license is
unaffected.

The actual pinned version numbers are recorded in `Directory.Packages.props` and
`THIRD-PARTY-NOTICES.md`, not repeated here. The `NetVips.Native` package family also bundles
several of libvips's optional dependencies (mozjpeg, libpng, libwebp, cairo, pango, librsvg, and
others), under a mix of MIT, BSD and LGPLv3 licenses, each recorded in the `NetVips.Native.*`
package's own notice file.

The host sets `NetVips.BlockUntrusted` while it registers the file services, which loads native libvips
at startup even when transforms are disabled. A host without a usable libvips fails to start instead of
failing on the first transform.

## Trash for files

The general soft-delete mechanism — the underlying query filter, the `deleted=` filter, and how it
interacts with revisions — is covered in
[Chapter 9: Revisions and Soft Delete](09-revisions-and-trash.md); this section covers only what's
specific to files.

The default `DELETE /api/files/{id}` moves the file to the trash: the same atomic store operation
any other soft-deletable collection uses, leaving the blob and the translation row in place, ready
to be restored later. If the file being trashed happens to be the current brand logo, the same
transaction also clears that reference, so the public settings endpoint never resolves a dead URL.

`POST /api/files/{id}/restore` clears the deletion stamp, exactly like restoring from any other
soft-deletable collection.

Both `DELETE` and `restore` require the `file` delete grant, not the write grant; without it the
response is `Delete not permitted.`. Both return `204` on success and `404` when the target doesn't
exist. Only `DELETE` (trash and purge) can return `409` `CONFLICT`, when another item still
references the file through an `OnDelete.Restrict` relation; `restore` never checks references.

`DELETE /api/files/{id}?purge=true` is the actual permanent delete: the file row, the translation
row and the stored blob are removed together. Purge follows the same relation rules as any other
collection: a `Restrict` reference blocks it, references with `OnDelete.SetNull` are set to null, and
many-to-many junction rows pointing at the file are removed. Cleaning up the blob is best-effort and
happens outside the transaction — the row data is already gone, so at worst a storage-layer failure
leaves an orphaned blob behind; [`files:sweep`](20-deployment.md#removing-orphaned-blobs) removes
such blobs.

Purge re-queries the row with the soft-delete filter cleared, so the most common flow — trash
first, then purge (the default delete already trashes first) — works. Purge clears the brand-logo
reference the same way trashing does.

Only `true` triggers the permanent delete, case-insensitively; a spelling like `?purge=1` doesn't
work:

```text
$ DELETE /api/files/27327d5a-6cb0-40e3-b288-6d1186db6ab9?purge=1
{"success":false,"error":{"code":"VALIDATION","message":"One or more validation errors occurred.","details":[{"field":"purge","message":"The value '1' is not valid."}]}}
HTTP_STATUS:400
```

`purge` binds as a boolean, and `1` makes the binding itself fail, with `details` naming that
field. The correct form is `?purge=true`.

Upload, trash, restore and purge — the four write paths specific to files — each emit their own
change notification (see
[Chapter 18: Extension Points](18-extension-points.md)),
because `FileService` handles file writes itself. A purge reports one batch: `Purged` for the file,
and `Updated` for every live item whose reference was set to null and every parent that lost a
junction row.

The items API (`DELETE /api/items/file/{id}`) and the GraphQL file delete hand off to the same file
lifecycle as the media library, so they behave identically, including the `409`.

Clearing the brand-logo reference doesn't separately count as a site-settings change; only the file
itself gets reported.

## What's next

With files and media covered, the next step is how authentication works — see
[Chapter 16: Authentication and SSO](16-authentication.md).
