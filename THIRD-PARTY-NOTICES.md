# Third-Party Notices

StruoCMS is licensed under the **MIT License**. This file lists third-party components used by the
on-the-fly image-transform feature (`GET /api/files/{id}/content?width=&height=&format=&fit=&quality=`,
see `docs/guide/en/11-files-and-media.md`) and by the developer documentation site (`docs/index.html`,
rendered client-side by vendored assets under `docs/vendor/`), and their license terms. Including
these components does **not** change StruoCMS's own license — StruoCMS remains MIT.

## libvips

- **What it is**: the native image-processing library that decodes, resizes, and re-encodes images
  for the transform endpoint. StruoCMS never links against it directly — it is consumed through the
  managed **NetVips** wrapper (see below) and its NuGet-distributed native binaries.
- **Copyright**: © the libvips project (John Cupitt and contributors). See the upstream `COPYING`
  file for the authoritative list of copyright holders:
  <https://github.com/libvips/libvips/blob/master/COPYING>
- **License**: **LGPL-2.1-or-later**. Full license text:
  <https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html>
- **Upstream source**: <https://github.com/libvips/libvips>
- **Version bundled**: **8.18.4**. Determined from the `versions.json` manifest shipped inside the
  `NetVips.Native.win-x64` NuGet package (version 8.18.4, restored to
  `%USERPROFILE%\.nuget\packages\netvips.native.win-x64\8.18.4\versions.json`), which records
  `"vips": "8.18.4"` — i.e. the native-package version and the bundled libvips release coincide for
  this build. `Directory.Packages.props` pins `NetVips.Native` to `8.18.4`.
- **Linking**: libvips is used via **dynamic linking** through the NetVips managed wrapper (the
  native `libvips-42.dll` / `libvips.so` / `libvips.dylib` binaries are loaded at runtime by the
  NetVips P/Invoke layer, not compiled or statically linked into StruoCMS). StruoCMS itself is
  licensed **MIT**, and that license is **unaffected** by libvips's LGPL — LGPL-2.1's dynamic-linking
  provisions only obligate StruoCMS to: (a) reproduce this notice, (b) not restrict a user's ability
  to relink against a modified libvips, and (c) make libvips's own source available (it already is,
  upstream). StruoCMS imposes no additional restriction on libvips and does not embed libvips source.

The `NetVips.Native.*` NuGet packages additionally bundle several of libvips's own optional
dependencies (e.g. `mozjpeg`, `libpng`, `libwebp`, `cairo`, `pango`, `librsvg`), each under its own
license (a mix of MIT, BSD, and LGPLv3). The authoritative list for the exact set of bundled
dependencies and their licenses ships inside each `NetVips.Native.*` package as its own
`THIRD-PARTY-NOTICES.md` (restored alongside the package under
`%USERPROFILE%\.nuget\packages\netvips.native.<rid>\<version>\THIRD-PARTY-NOTICES.md`) and is
maintained upstream at <https://github.com/kleisauke/libvips-packaging>.

## NetVips

- **What it is**: the managed .NET binding for libvips used by StruoCMS
  (`Struo.Infrastructure.Files.NetVipsImageTransformer`) to invoke image-transform operations.
- **Copyright**: © Kleis Auke Wolthuizen.
- **License**: **MIT**.
- **Upstream source**: <https://github.com/kleisauke/net-vips>
- **Version used**: **3.2.0** (see `Directory.Packages.props`).

## docsify

- **What it is**: the client-side renderer for the developer documentation site. It turns the
  markdown chapters under `docs/guide/{en,zh-TW}/` into a navigable, searchable page at runtime, with
  no build step. StruoCMS loads it, together with its search plugin, from the vendored copies at
  `docs/vendor/docsify.min.js` and `docs/vendor/plugins/search.min.js` (see `docs/index.html`).
- **Copyright**: © 2016 - present Docsify Contributors
  (<https://github.com/docsifyjs/docsify/graphs/contributors>).
- **License**: **MIT**.
- **Upstream source**: <https://github.com/docsifyjs/docsify>
- **Version bundled**: **5.0.0**. Read from `frontend/pnpm-lock.yaml` (`docsify@5.0.0`).

## docsify-cli

- **What it is**: the command-line tool used to preview the documentation site locally during
  development (the `docs:serve` script in `frontend/package.json` runs `docsify serve ../docs`). It
  is a devDependency of `frontend/` only and ships nothing into the site itself.
- **Copyright**: © 2016 cinwell.li.
- **License**: **MIT**.
- **Upstream source**: <https://github.com/docsifyjs/docsify-cli>
- **Version used**: **5.0.0**. Read from `frontend/pnpm-lock.yaml` (`docsify-cli@5.0.0`).

## prismjs

- **What it is**: the syntax highlighter docsify uses to colorize the C#, TypeScript, Bash, JSON,
  YAML, and SQL code blocks in the documentation site. StruoCMS loads the vendored language
  components from `docs/vendor/prism/*.min.js` (see `docs/index.html`).
- **Copyright**: © 2012 Lea Verou.
- **License**: **MIT**.
- **Upstream source**: <https://github.com/PrismJS/prism>
- **Version bundled**: **1.30.0**. Read from `frontend/pnpm-lock.yaml` (`prismjs@1.30.0`).
