# Image Transforms

StruoCMS can resize and re-encode images on the fly when they are downloaded, so a frontend can
request exactly the variant it needs (e.g. a 320px WebP thumbnail) without a separate image pipeline
or an external CDN. No client changes are required beyond adding query parameters to the existing
download URL.

## Endpoint

```
GET /api/files/{id}/content?width=&height=&format=&fit=&quality=
```

This is the **same** endpoint already used to download a file's raw bytes
(`GET /api/files/{id}/content`) — the query parameters are additive and optional.

### Query parameters

| Parameter | Type | Meaning | Limits |
|---|---|---|---|
| `width` | int | Target width in pixels. | Clamped to `1..MaxWidth` (default max `4096`). |
| `height` | int | Target height in pixels. | Clamped to `1..MaxHeight` (default max `4096`). |
| `format` | string | Output format to re-encode to. | Must be one of `AllowedFormats` (default `webp`, `jpeg`, `png`, `avif`); otherwise the request fails with `400 BAD_USER_INPUT`. |
| `fit` | string | How `width`/`height` interact when both are given. | `inside` (default) and `contain` fit the image within the box without upscaling; `cover` fills the box, cropping as needed. Unrecognized values fall back to `inside`. |
| `quality` | int | Encoder quality for lossy formats (e.g. JPEG/WebP/AVIF). | Clamped to `1..100`, default `82` (`DefaultQuality`). |

You can pass any subset of these — e.g. `?width=320` alone, or `?format=webp&quality=70`.

## Behavior

- **No transform requested** (none of `width`/`height`/`format` present), **or the file is not an
  image** (`contentType` doesn't start with `image/`): the endpoint streams the original file bytes
  unchanged — identical to today's plain download behavior.
- **Unsupported `format`**: the request fails with `400 BAD_USER_INPUT` rather than silently
  falling back to a different format.
- **Authorization parity with plain downloads**: a **published** file's content — transformed or
  not — is downloadable **anonymously**. A file that is not published (draft/archived) returns
  **404** unless the caller has a genuine read grant for the `file` collection; existence is not
  leaked to unauthorized callers.
- **Caching**: the first request for a given `(file, version, width, height, format, fit, quality)`
  combination transforms and writes the result to an on-disk variant cache; subsequent requests for
  the same combination are served straight from that cache. The cache key includes the file's
  version counter, so a re-uploaded/replaced file's stale variants are never served — they simply
  miss the cache and are regenerated once.

## Configuration

Settings live under `Struo:Files:ImageTransform` in `appsettings.json`:

```json
"Struo": {
  "Files": {
    "ImageTransform": {
      "Enabled": true,
      "MaxWidth": 4096,
      "MaxHeight": 4096,
      "AllowedFormats": ["webp", "jpeg", "png", "avif"],
      "DefaultQuality": 82,
      "CachePath": "App_Data/image-cache"
    }
  }
}
```

| Key | Meaning |
|---|---|
| `Enabled` | Master switch for the feature. `false` makes every request fall through to plain passthrough download, regardless of query parameters. |
| `MaxWidth` / `MaxHeight` | Upper clamp for `width`/`height`, in pixels. |
| `AllowedFormats` | Allowlist of output formats a caller may request via `format`. |
| `DefaultQuality` | Encoder quality used when `quality` is omitted. |
| `CachePath` | Directory for cached transformed-image variants. A relative path is resolved against the application's **content root** (not the process's current working directory), so it stays correct regardless of how/where the process is launched (e.g. as a systemd unit or from a different working directory). |

Any key can be overridden per-environment via the standard double-underscore env-var form, e.g.:

```
Struo__Files__ImageTransform__Enabled=false
Struo__Files__ImageTransform__CachePath=/var/lib/struo/image-cache
```

Disabling the feature this way (or setting `Enabled=false` in `appsettings.Production.json`) is a
safe way to turn it off in an environment without a rebuild.

## Deployment note

Image transforms are powered by **[libvips](https://github.com/libvips/libvips)** via the managed
**NetVips** wrapper. The native libvips binaries are shipped by the `NetVips.Native.*` NuGet
packages (referenced by `Struo.Infrastructure`) and are restored automatically for the target
runtime identifier — no separate system package install is required in the common case. If you
build/publish for a runtime identifier that isn't covered by the referenced `NetVips.Native.*`
packages, the transform feature will fail at runtime for that platform; `Enabled=false` is the
supported fallback until a matching native package is added. See `THIRD-PARTY-NOTICES.md` (repo
root) for licensing details on libvips (LGPL-2.1-or-later, dynamically linked) and NetVips (MIT) —
using them does not change StruoCMS's own MIT license.

## Security note

The transform pipeline only ever decodes files that were already accepted and stored through
StruoCMS's normal upload validation (`AllowedContentTypes`, size limits) — it does not decode
arbitrary caller-supplied bytes. Output is restricted to the server-configured `AllowedFormats`
allowlist, and requested dimensions are always clamped to `MaxWidth`/`MaxHeight` before the image
library ever runs, bounding both the decode/encode surface and the memory an individual request can
consume.
