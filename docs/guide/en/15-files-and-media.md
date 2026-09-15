# 15. Files, Media and Image Transforms

This chapter covers how a file gets uploaded, where it's stored, how it reaches a reader, and how
an image is transformed live at download time.

## The `file` collection and its translations

`File` and `MediaFolder` are framework-provided collections, present in every StruoCMS install.
The media library is therefore ready out of the box: both the configuration and the code come from
the framework.

Writing a `File` goes through a dedicated pipeline — `FilesController` (routed at `api/files`),
`FileService`, `IFileStorage` — rather than the `ItemService`/`ItemsController` path every other
collection shares. An upload involves byte storage, content-type checking, and image dimensions and
transforms, none of which the generic item CRUD path needs to know about.

Reading, updating and deleting `file` metadata still goes through the ordinary `api/items/file` and
`api/items/mediaFolder` routes: browsing, filtering and sorting the media library is just an
ordinary query. The grants and status codes for each endpoint are in
[Chapter 13: REST API Endpoint Reference](13-rest-endpoints.md).

Creating a `file` through the generic item API is always refused; the check lives in
`ItemService.CreateAsync`, so GraphQL's `createFile` mutation is blocked by the same message:
`Files cannot be created through the generic items API. Upload one with POST /api/files instead.`,
status `400`/`BAD_USER_INPUT`.

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
itself declares `Searchable`; `contentType` is the validated MIME type; `size` is the actual number
of bytes stored.

`width` and `height` are filled in only when the dimensions can be read from the file's header —
every other upload, image or not, leaves both blank. `status` is the only one of its own fields
that's writable, and it decides who can read this row and its bytes, covered in the next section.

`folderId` isn't a `[CmsField]` — it's a many-to-one relation declared with `[CmsRelation]`,
pointing at `mediaFolder`, using the `TreeSelect` interface, `OnDelete.Restrict`, which is also why
a folder that still holds files can't be deleted. It's deliberately not read-only: moving a file to
another folder is just an ordinary item update. `storageKey` carries no `[CmsField]` at all — it's
never projected out, and no write path can touch it.

The translatable `title` and `alt` live in the `FileTranslation` sidecar entity (table
`file_translations`, unique on `(fileid, locale)`), exactly the `[CmsTranslations]` mechanism
described in [Chapter 7: Multilingual Content](07-i18n.md).

On upload, the default locale's `title` is auto-filled from the filename with its extension
stripped; when stripping the extension leaves an empty string (a filename that's nothing but an
extension), it falls back to the full filename, and the result is always truncated to 255
characters.

`alt` is never auto-filled, and no other locale gets an automatically generated row either, unless
someone writes one by hand. `FileTranslation.Title` is also `Searchable`, so `search=` on `file`
matches both the filename and every locale's title.

Once an upload succeeds, `status` is always `"published"`, even though the field's own declared
default is `"draft"` — as soon as `file` carries a public read grant, a freshly uploaded file is
immediately public, with no separate publish step needed.

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

If the caller under-reports the size and then actually streams more bytes than declared, the
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

The storage key is generated entirely by the server — the caller can never choose it:
`{yyyy}/{MM}/{guid:N}{extension}`, using the current UTC year and month plus a fresh GUID; the
original filename never appears in the storage-layer path.

The `size` actually stored is the number of bytes the buffering stream genuinely read, not the
caller's declared `Content-Length` — the stream is read to completion first and only then handed to
storage, specifically so a caller that misreports its size can't cause the wrong size to be
recorded or a truncated object to be stored.

The file row and its auto-filled translation row are written together inside the same transaction,
so there's never an intermediate state with a file but no title.

## Storage backends: local and s3

`Struo:Files:Backend` selects exactly one of the registered `IFileStorage` implementations:
`"local"` (the default) or `"s3"`; any other value fails startup outright. Both backends' keys
and validation rules are in [Chapter 4](04-configuration.md); this section covers only the
behavioral differences.

`local` stores files as plain files under `Struo:Files:Local:RootPath`; every path resolution
checks that it lands inside that root, and a key that tries to escape it throws an exception
outright, before any I/O is attempted.

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

## Serving files: who can see what

`GET /api/files/{id}` and `GET /api/files/{id}/content` carry no `[Authorize]` at all — an
anonymous request reaches both: a published file's metadata and bytes can be embedded in a public
page with no session needed.

A file whose `status` isn't `published` needs both conditions at once: the caller is signed in, and
holds a write grant on `file`. There's no separate "read unpublished" grant flag — this rule is
derived from the same three grant flags every collection already has.

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
a super-admin from turning around and granting `public` a write grant, which would make drafts
visible to every caller, anonymous included.

When this check fails, the response is always a plain `404`, never `403` — whether a draft exists
at all is never leaked; a file already sitting in the trash also gets `404` from both read actions,
because the query already applies the same soft-delete filter.

A download with no transform applied is sent as an attachment: the response carries
`Content-Disposition` with the original filename; a successfully transformed response carries no
such header, only the transformed content type (a failed transform that falls back to the original
bytes is covered below).

When `Struo:Files:PresignedRedirect` is on and no transform was requested, the download becomes a
`302` redirect to the storage backend's presigned URL; it's off by default, so the admin UI's
thumbnails display with no extra setup, and so the storage endpoint itself can stay private. On the
`local` backend this setting has no effect at all — a presigned-URL query always returns null, and
the download falls back to streaming as usual.

Every response carries `X-Content-Type-Options: nosniff`, one more layer of defense on top of the
download endpoint's own attachment disposition.

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
transforms when three things are all true at once: the feature itself is on (key
`Struo:Files:ImageTransform:Enabled`, default `true`), at least one of `width`/`height`/`format`
was given, and this row's `contentType` starts with `image/`.

If any one condition is missing, the request falls straight back to the original stream or
redirect, unaffected.

### Parameters

| Parameter | Value | Out of range |
|---|---|---|
| `width` | Integer | Clamped 1–`MaxWidth`; missing derives proportionally |
| `height` | Integer | Clamped 1–`MaxHeight`; missing derives proportionally |
| `format` | One of `AllowedFormats` | Other values `400`; missing gives WebP, not source format |
| `fit` | Any string | Missing defaults `inside`; `cover` crops only with both sides |
| `quality` | Integer | Clamped 1–100; missing uses `DefaultQuality` |

`MaxWidth`, `MaxHeight`, `AllowedFormats` and `DefaultQuality` are all in
[Chapter 4](04-configuration.md). `format` is only checked against `AllowedFormats` when the caller
explicitly gives a value, matched case-insensitively — `?format=WEBP` counts just the same; a value
not on the list is `400`, `Unsupported format '{x}'.`.

Omitting `format` always means WebP, and `Content-Type` changes to `image/webp` right along with
it — it never carries over the source file's own encoding.

`fit=cover` only actually crops to that frame when both dimensions are given; a `cover` with just
one dimension, or any other spelling, scales proportionally to fit inside the frame without
enlarging. With neither dimension given, and only format or quality adjusted, the transform skips
the resizing step entirely and re-encodes at the original resolution, since there's no frame to
fit.

A request giving only `height` reads the source width from the header first (again without
decoding pixels), so the unconstrained side scales correctly too.

Resizing is handled by libvips: for encodings that support shrink-on-load (JPEG, WebP), it shrinks
while loading, so the decoding memory scales roughly with the output size rather than the source's
full resolution; PNG has no cheaper decode path and is still decoded in full.

If the transform itself throws — a corrupted source, an unsupported encoding, or libvips failing
outright all count — the exception is logged at Warning level along with the file id, and the
original bytes are sent instead, with the request still succeeding.

The returned original bytes go out the same way an ordinary download does, so they carry this
row's own content type and attachment `Content-Disposition` — a failed transform's response is
distinguishable from a successful one just by its headers.

### Examples

```text
$ GET /api/files/c4440800-43d3-4ff0-9fe7-b5d7671b30df/content?width=32&format=webp
HTTP/1.1 200 OK
Content-Length: 292
Content-Type: image/webp
# decoded WebP VP8X: width=32, height=24
# body: 292 bytes (binary, not shown)
HTTP_STATUS:200
```

The source is a 64×48 PNG; `width=32` scales proportionally to 32×24, with both the format and
`Content-Type` changed to WebP as requested.

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
`version`, plus `width`, `height`, `format`, `fit` and `quality`, one per line. Changing any one of
them produces a different key. The version stamp used is the optimistic-concurrency `version`
counter, not `UpdatedAt`. Any update to this row advances `version`, which naturally invalidates
old variants with no active invalidation mechanism needed.

Each cache entry is bucketed into a subdirectory by the key's first two hex characters; a write
goes to a temp file in the same directory first and is then moved into place, so a concurrent read
never sees a half-written variant. A read that loses a race against a concurrent write is treated
as a cache miss, not an error.

## NetVips and LGPL

The transform pipeline is built on NetVips, an MIT-licensed managed wrapper. StruoCMS loads
libvips through NetVips dynamically at runtime — never statically linked, and never linked
directly.

libvips itself is LGPL-2.1-or-later; because the link is dynamic, LGPL-2.1 requires only three
things: reproducing the license notice, not preventing a user from swapping in a modified libvips,
and making libvips's own source available — something upstream already does. StruoCMS adds no
further restriction on top of that and embeds none of libvips's source, so its own MIT license is
unaffected.

The actual pinned version numbers are recorded in `Directory.Packages.props` and
`THIRD-PARTY-NOTICES.md`, not repeated here. The `NetVips.Native` package family also bundles
several of libvips's optional dependencies (mozjpeg, libpng, libwebp, cairo, pango, librsvg, and
others), under a mix of MIT, BSD and LGPLv3 licenses, each recorded in its own package's notice
file.

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
response is `Delete not permitted.`. Both return `204` on success, and `404` when the target
doesn't exist.

`DELETE /api/files/{id}?purge=true` is the actual permanent delete: the file row, the translation
row, and the stored blob are all removed together. Cleaning up the blob is best-effort and happens
outside the transaction — the row data is already gone, so at worst a storage-layer failure leaves
an orphaned blob behind.

Purge re-queries the row with the soft-delete filter cleared, so the most common flow — trash
first, then purge (the default delete already trashes first) — works. Purge clears the brand-logo
reference the same way trashing does.

`purge` only accepts `true`, case-insensitively; a spelling like `?purge=1` doesn't work:

```text
$ DELETE /api/files/27327d5a-6cb0-40e3-b288-6d1186db6ab9?purge=1
{"success":false,"error":{"code":"VALIDATION","message":"One or more validation errors occurred.","details":[{"field":"purge","message":"The value '1' is not valid."}]}}
HTTP_STATUS:400
```

`purge` binds as a boolean, and `1` makes the binding itself fail, with `details` naming that
field. The correct form is `?purge=true`.

Upload, trash, restore and purge — the four write paths specific to files — each emit their own
change notification (see
[Chapter 18: Extension Points: Search Providers and Change Listeners](18-extension-points.md)),
because `FileService` bypasses `ItemService` entirely and has to add this itself.

Clearing the brand-logo reference doesn't separately count as a site-settings change; only the
file itself gets reported.

## What's next

With files and media covered, the next step is how authentication works — see
[Chapter 16: Authentication and SSO](16-authentication.md).
