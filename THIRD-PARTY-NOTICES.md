# Third-Party Notices

MonitovoPDF is released under the [MIT License](LICENSE). It redistributes the third-party
works listed below, each under its own terms, which are reproduced here in full.

This file travels with both things a consumer receives: it is packed into the NuGet package
alongside `licenses/Apache-2.0.txt`, and copied into the container image. Do not strip it, and
do not remove `/usr/share/doc` from the image — several of these licences require their notice
to travel with the copies.

---

## Redistributed at runtime

These are shipped in the NuGet package and the container image, and are part of what a consumer
receives.

### PDFsharp 6.2.4 — MIT License

The PDF engine. Referenced as a NuGet package; its source is not incorporated into this
repository. <https://docs.pdfsharp.net>

```
Copyright (c) 2001-2026 empira Software GmbH, Troisdorf (Cologne Area), Germany

http://docs.pdfsharp.net

MIT License

Permission is hereby granted, free of charge, to any person obtaining a
copy of this software and associated documentation files (the "Software"),
to deal in the Software without restriction, including without limitation
the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the
Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included
in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
DEALINGS IN THE SOFTWARE.
```

### ZXing.Net 0.16.11 — Apache License 2.0

The barcode encoder. Referenced as a NuGet package; its source is not incorporated into this
repository. Only the core package is used, which has no dependencies of its own and no
imaging layer — it returns a bit matrix that this project draws as vector rectangles.
<https://github.com/micjahn/ZXing.Net>

Apache 2.0 is permissive and imposes no copyleft. It is not the same licence as this
project's, and it does not need to be: the package is a separate work, referenced rather
than incorporated, so MonitovoPDF remains MIT. Its obligations here are to include a copy of
the licence and to retain notices. The full licence text is at
[licenses/Apache-2.0.txt](licenses/Apache-2.0.txt), which is copied into the container image.

The package ships no `NOTICE` file, so clause 4(d) does not apply. This project does not
modify the package, so clause 4(b) does not apply either.

One consequence worth recording: Apache 2.0 is compatible with GPLv3 but **not** with
GPLv2. A downstream project under GPLv2 could not incorporate MonitovoPDF while it carries
this dependency. That is the only respect in which this addition narrows who can consume the
project, and it was accepted deliberately.

### DejaVu fonts — Bitstream Vera Fonts License

Redistributed twice over, because PDFsharp's cross-platform build loads no fonts of its own
and text would otherwise fail to draw on a host that has none:

* **Embedded in the library assembly** (`MonitovoPDF/fonts/DejaVuSans.ttf`), served by
  `MonitovoPdf.UseBundledFonts()`. Only the regular face is carried.
* **Installed in the container image** (Debian package `fonts-dejavu-core`).

<https://dejavu-fonts.github.io/>

Note the clause below permitting sale only as part of a larger package. Both routes satisfy it:
the font is a resource compiled into the library assembly, and a component of the container
image. It is never distributed as a font on its own.

```
Copyright (c) 2003 by Bitstream, Inc. All Rights Reserved.
Bitstream Vera is a trademark of Bitstream, Inc.
DejaVu changes are in public domain.

Permission is hereby granted, free of charge, to any person obtaining a copy
of the fonts accompanying this license ("Fonts") and associated
documentation files (the "Font Software"), to reproduce and distribute the
Font Software, including without limitation the rights to use, copy, merge,
publish, distribute, and/or sell copies of the Font Software, and to permit
persons to whom the Font Software is furnished to do so, subject to the
following conditions:

The above copyright and trademark notices and this permission notice shall
be included in all copies of one or more of the Font Software typefaces.

The Font Software may be modified, altered, or added to, and in particular
the designs of glyphs or characters in the Fonts may be modified and
additional glyphs or characters may be added to the Fonts, only if the fonts
are renamed to names not containing either the words "Bitstream" or the word
"Vera".

This License becomes null and void to the extent applicable to Fonts or Font
Software that has been modified and is distributed under the "Bitstream
Vera" names.

The Font Software may be sold as part of a larger software package but no
copy of one or more of the Font Software typefaces may be sold by itself.

THE FONT SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
OR IMPLIED, INCLUDING BUT NOT LIMITED TO ANY WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT OF COPYRIGHT, PATENT,
TRADEMARK, OR OTHER RIGHT. IN NO EVENT SHALL BITSTREAM OR THE GNOME
FOUNDATION BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, INCLUDING
ANY GENERAL, SPECIAL, INDIRECT, INCIDENTAL, OR CONSEQUENTIAL DAMAGES,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF
THE USE OR INABILITY TO USE THE FONT SOFTWARE OR FROM OTHER DEALINGS IN THE
FONT SOFTWARE.

Except as contained in this notice, the names of Gnome, the Gnome
Foundation, and Bitstream Inc., shall not be used in advertising or
otherwise to promote the sale, use or other dealings in this Font Software
without prior written authorization from the Gnome Foundation or Bitstream
Inc., respectively. For further information, contact: fonts at gnome dot
org.
```

The `.NET` runtime and ASP.NET Core libraries in the base image are covered by their own
notices, which Microsoft ships inside the `mcr.microsoft.com/dotnet` images.

---

## Build and test only — not redistributed

Nothing below is shipped to consumers. It is listed so the distinction is on the record.

| Component | Licence | Where |
|---|---|---|
| xUnit | Apache-2.0 | test project |
| xunit.runner.visualstudio | Apache-2.0 | test project |
| Microsoft.NET.Test.Sdk | MIT | test project |
| Microsoft.AspNetCore.Mvc.Testing | MIT | test project |
| coverlet.collector | MIT | test project |
| LibreOffice Writer | MPL-2.0 | `integration/` image |
| python3-uno | MPL-2.0 | `integration/` image |
| python3 | PSF-2.0 | `integration/` image |
| ca-certificates | GPL-2+ and MPL-2.0 | `integration/` image |
| poppler-utils | **GPL-2 or GPL-3** | `integration/` image |
| zbar-tools | **LGPL-2.1-or-later** | `integration/` image |
| dmtx-utils | **LGPL-2+** (`libdmtx0b` itself is BSD-2-Clause) | `integration/` image |
| zxing-cpp | Apache-2.0 | `integration/` image |
| pypdfium2 | BSD-3-Clause / Apache-2.0 | `integration/` image |
| pillow | MIT-CMU | `integration/` image |
| reportlab | BSD-3-Clause | `integration/` image |

The licences of the Debian packages were read from each package's own
`/usr/share/doc/<package>/copyright` inside the built image, and those of the Python packages
from their installed distribution metadata, rather than from a summary elsewhere.

### On the copyleft tools in the integration image

The `integration/` image contains **copyleft software**: poppler-utils is GPL-2 or GPL-3, and
both zbar-tools and dmtx-utils are LGPL. This does not affect MonitovoPDF or the runtime image.
Every one of them is invoked as a separate process by a test script — `pdftotext` to read text
back, `zbarimg` and `dmtxread` to decode barcodes — and nothing in any of them is linked into,
derived from, or distributed with the library or the service. Neither the GPL nor the LGPL
reaches across that boundary.

That these are the decoders is the point of them. A barcode checked by the same library that
drew it proves only that the code agrees with itself, so the verification is worth having only
if it comes from somewhere else entirely — which in practice means the established free
implementations, and those carry the licences they carry.

That holds only while the image stays a local test harness. **Publishing the `integration/`
image would be distributing GPL software** and would bring the GPL's own distribution
obligations with it. If publishing it ever becomes desirable, that decision needs taking
deliberately rather than by habit.

---

## Copyleft position

MonitovoPDF is MIT and intended to be embedded in commercial products, so nothing may place a
copyleft obligation on it or on a consumer. The position as audited on 2026-08-21, against the
resolved dependency graph and the copyright files inside the built images rather than against
package summaries:

**No GPL, LGPL or AGPL code is compiled into, linked into, or derived from this project.**

* **Managed dependencies.** A consumer of the library receives five NuGet packages in total,
  on both `net8.0` and `net10.0`. Two are referenced directly and three arrive through
  PDFsharp:

  | Package | Licence | How it arrives |
  |---|---|---|
  | PDFsharp 6.2.4 | MIT | referenced directly |
  | ZXing.Net 0.16.11 | Apache-2.0 | referenced directly |
  | System.Security.Cryptography.Pkcs 8.0.1 | MIT | via PDFsharp |
  | Microsoft.Extensions.DependencyInjection.Abstractions 8.0.2 | MIT | via PDFsharp |
  | Microsoft.Extensions.Logging.Abstractions 8.0.3 | MIT | via PDFsharp |

  That is the whole graph. ZXing.Net's core package brings no dependencies of its own, which
  is why it was chosen over barcode libraries that pull in an imaging stack. Apache-2.0 is
  permissive, not copyleft, and the three Microsoft packages are MIT.
* **Native linkage.** In the runtime image, the process links only against glibc (`libc`, `libdl`,
  `libm`, `libpthread`, `librt`, `ld-linux`) and the GCC runtime (`libstdc++`, `libgcc_s`).
  glibc is LGPL-2.1, which permits dynamic linking without any reciprocal obligation. The
  GCC runtime libraries are GPL-3 **with the GCC Runtime Library Exception**, which exists
  precisely to permit linking them into software under any licence. No GPL library without
  an exception is linked.
* **No AGPL.** An automated scan flags `ca-certificates` for the string "Affero". This is a
  false positive: the word appears inside the text of MPL-2.0 itself, at clause 1.12, which
  defines "Secondary License" by naming GPL-2.0, LGPL-2.1 and AGPL-3.0. It is a definition
  in boilerplate, not a grant. That package is GPL-2+ and MPL-2.0.

### GPL utilities in the container image

The image is Debian-based, and around three quarters of its ~99 OS packages carry a GPL
licence somewhere — `bash`, `coreutils`, `sed`, `tar`, `perl-base` and so on. This is
normal for any Linux container and is **not** a copyleft obligation on this project.

Those are separate programs that happen to share a filesystem. The service neither links to
them, derives from them, nor combines with them into one work. Both licences address this
directly: GPLv2 clause 2 and GPLv3 clause 5 permit "mere aggregation" of independent works
on a distribution medium without the GPL extending to them. Shipping the image obliges
compliance for those packages as Debian already provides — their sources are published by
Debian and their notices remain under `/usr/share/doc` — and nothing more.

If reducing that surface is ever wanted for its own sake, `mcr.microsoft.com/dotnet/aspnet`
publishes `10.0-noble-chiseled` and `10.0-azurelinux3.0-distroless` variants that omit the
shell and package manager. Both were confirmed available. That would shrink the image and
its attack surface; it would not change the legal position, which is already sound.

### Where copyleft does appear

Only in `integration/`, and only in the barcode and text decoders: poppler-utils under
GPL-2-or-GPL-3, zbar-tools and dmtx-utils under the LGPL. See the note above — each is a
build-time test tool run as a separate process, is linked into nothing, and is not published.

Nothing a consumer receives carries a copyleft licence. The NuGet package resolves to MIT and
Apache-2.0 only, and the runtime image adds the Bitstream Vera fonts and the base image
Microsoft publishes.

---

## Adding a dependency

Every addition to this file is a licence obligation and a thing to keep patched. Before
adding one, verify its licence against the upstream source rather than a summary or a badge
— a permissively licensed package can sit on top of a restrictively licensed one, and the
badge shows only the top layer.
