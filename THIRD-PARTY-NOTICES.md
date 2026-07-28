# Third-Party Notices

StruoCMS is licensed under the **MIT License**. This file lists third-party components used by the
on-the-fly image-transform feature (`GET /api/files/{id}/content?width=&height=&format=&fit=&quality=`,
see `docs/guide/05-image-transforms.md`) and their license terms. Including these components does
**not** change StruoCMS's own license — StruoCMS remains MIT.

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
