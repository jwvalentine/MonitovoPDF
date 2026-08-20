# MonitovoPDF — Session Context

Use this file to orient Claude at the start of a new chat session on this project.
Tell Claude: "Read CONTEXT.md before we start."

---

## What This Project Is

**MonitovoPDF** is a **.NET library** that fills template PDFs with text, images and barcodes, in
process. It is **free and open source under the MIT licence**, published publicly, and intended to
be embedded in commercial products without per-document licensing costs.

It exists to replace the commercial PDF components that do this job inside an application at
runtime — Aspose.PDF, IronPDF and similar. `MonitovoPDF.Server` is an optional ASP.NET Core host
for callers who want the capability over HTTP; it is not the product.

The motivating problem: those products are expensive per document, restrictively licensed for
redistribution, or both. The capability actually needed is narrow — take a template, replace
named placeholders with text, images and barcodes, return the finished document — and a small
permissively-licensed library that does exactly that is more useful than a large dependency with
a licence audit attached.

See [PLAN.md](PLAN.md) for decisions made and still open, and [TODO.md](TODO.md) for immediate
action items.

---

## Current State

**The library is built, tested and packs.** `MonitovoPdf.Fill(template, fill => ...)` draws text,
images and barcodes into the positions the template's form fields occupy, strips the fields, and
returns a flat PDF. It targets `net8.0` and `net10.0`, and `dotnet pack` produces a package that
has been verified by consuming it from a separate .NET 8 application. 137 tests cover the public
API (pinned by an approval baseline), the renderer, the request decoder and the HTTP surface.

Barcodes are generated in 15 symbologies — see [BarcodeType.cs](MonitovoPDF/BarcodeType.cs) —
drawn as vector rectangles rather than rasterised, so bar edges stay exact at print resolution.

A `Dockerfile` builds a runnable image with fonts installed, and `integration/` holds a
container-based end-to-end check: LibreOffice builds a real PDF form through its own API, the
service fills it, poppler reads the text back out, and three independent decoders read the
barcodes back.

**Not yet built:** authentication of any kind, and OpenAPI documentation. The service must not be
exposed to an untrusted network as it stands.

**Not yet published.** CI and a tag-driven release workflow are in place, but nothing is on
nuget.org. Publishing uses Trusted Publishing rather than a stored key, so what is missing is a
policy on nuget.org and a `NUGET_USER` secret — both of which only Joe can set up. See the top of
[TODO.md](TODO.md).

**Not yet proven:** nothing has been sent to an actual label printer. The chain is verified as far
as a correct PDF that an independent extractor can read; the final hop to hardware is untested.

---

## The One Thing To Know Before Changing The Renderer

Form fields are used **only as a coordinate source**. Values are drawn into the page content
stream and the fields are then removed.

This is not incidental. PDFsharp does not generate appearance streams for filled form fields
(upstream issue 64, closed as *wontfix*), so a document whose content lives in field values
renders **blank** in viewers that do not build appearances themselves — including print paths a
label is likely to take. Any change that moves back to setting field values reintroduces that bug.
[LabelRendererTests.cs](MonitovoPDF.Tests/LabelRendererTests.cs) pins this behaviour by
asserting the drawn text appears in the page content stream.

---

## Repository Location

```
c:\dev\MonitovoPDF\
```

GitHub remote: `https://github.com/jwvalentine/MonitovoPDF` (**public**, MIT licensed)
Default branch: `main`

Because the repository is public, everything committed is world-readable immediately and remains
in history after deletion. See the public-repository section of CLAUDE.md.

---

## Project Structure

```
MonitovoPDF/
├── MonitovoPDF/                      # THE PRODUCT — the library, packed to NuGet
│   ├── MonitovoPdf.cs                # Public entry point: Fill, font configuration
│   ├── fonts/DejaVuSans.ttf          # Embedded, served by UseBundledFonts()
│   ├── FillBuilder.cs                # Collects the values to draw
│   ├── BarcodeType.cs                # Public symbology enum
│   ├── BarcodeTypes.cs               # Name <-> type mapping for config-driven callers
│   ├── TemplateInfo.cs               # What Inspect reports back
│   ├── TextOptions.cs                # Per-field appearance overrides
│   ├── FillResult.cs                 # Fill output plus unmatched field names
│   ├── Rendering/                    # All internal
│   │   ├── LabelRenderer.cs          # The core: draws values in, strips the form
│   │   ├── RenderingOptions.cs       # Ceilings and defaults (public, plain object)
│   │   ├── BarcodeSymbology.cs       # Symbology to encoder mapping
│   │   ├── TemplateInspector.cs      # Reads pages and fields without filling
│   │   ├── ImageSlots.cs             # Finds and replaces image placeholders
│   │   ├── PlacedXObjects.cs         # Reads where a page draws each XObject
│   │   ├── BarcodeForm.cs            # A barcode as a form XObject on the unit square
│   │   ├── FileSystemFontResolver.cs # Loads .ttf files from a directory
│   │   ├── BundledFontResolver.cs    # Serves the embedded font from memory
│   │   └── TemplateRenderException.cs # Public; the exception to catch
│   └── MonitovoPDF.csproj            # net8.0;net10.0, packable
├── MonitovoPDF.Server/               # OPTIONAL HTTP host, references the library
│   ├── Program.cs                    # Minimal API endpoints and font wiring
│   ├── ServerOptions.cs              # Request size and response timeout
│   ├── Api/RenderLabelRequest.cs     # Wire DTO, decoding and boundary validation
│   └── appsettings.json              # Server and Rendering sections
├── MonitovoPDF.Tests/                # xUnit; synthetic PDF fixtures built in code
├── integration/                      # LibreOffice-driven end-to-end check, run via Docker
│   ├── make_template.py              # Builds AcroForm templates through the UNO API
│   ├── barcodes.py                   # All-symbologies sheet, and decoding it back
│   ├── run_tests.py                  # Drives the service and inspects the results
│   ├── Dockerfile                    # LibreOffice, poppler + PDFium, zbar, libdmtx, zxing-cpp
│   └── docker-compose.yml            # Runs the service and the check together
├── Dockerfile                        # Image for the server; installs DejaVu so text can draw
├── licenses/Apache-2.0.txt           # Ships in the package and the image, for ZXing.Net
├── THIRD-PARTY-NOTICES.md            # Redistributed works and the copyleft audit
├── .github/workflows/                # CI on pull requests, release on a v* tag
├── SECURITY.md                       # Private disclosure channel
├── MonitovoPDF.slnx                  # Solution tying the three projects together
├── CLAUDE.md                         # Governing rules for AI agents
├── AGENTS.md                         # Pointer to CLAUDE.md
├── PLAN.md                           # Decisions made and still open
├── TODO.md                           # Immediate action items
└── LICENSE                           # MIT, Copyright (c) 2026 Monitovo LLC
```

---

## Tech Stack

| Concern | Choice |
|---|---|
| Package | Library targeting `net8.0` and `net10.0`, with XML docs and symbols |
| Optional host | ASP.NET Core minimal APIs on `net10.0` |
| PDF engine | PDFsharp 6.2.4 (MIT, verified upstream) |
| Barcodes | ZXing.Net 0.16.11 (Apache-2.0, no transitive dependencies) |
| Tests | xUnit, with `Microsoft.AspNetCore.Mvc.Testing` for the HTTP surface |
| CI | GitHub Actions: build, test and pack on PRs; release on a `v*` tag |
| Licence | MIT |

Dependencies are deliberately few: PDFsharp and ZXing.Net are the only runtime packages, and
neither brings transitive dependencies. Before adding a third, read the copyleft section of
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) — a permissive package can sit on a
restrictively licensed one, and the badge shows only the top layer.

---

## Build & Run

```bash
cd c:\dev\MonitovoPDF
dotnet build          # currently 0 warnings, 0 errors
dotnet test           # currently 137 passing
dotnet pack MonitovoPDF/MonitovoPDF.csproj -c Release -o artifacts   # 1.0.0
dotnet run --project MonitovoPDF.Server   # optional host, http://localhost:5155
curl http://localhost:5155/health
```

`dotnet` commands are fine to run locally. **Do not run `npm`, `yarn` or `npx` on Joe's machine**
— use Docker if Node tooling is ever needed.

The `gh` CLI is installed but not on `PATH`:
```bash
export PATH="$PATH:/c/Program Files/GitHub CLI"
```

### Fonts

Text will not draw on Linux unless `Rendering__FontDirectory` points at a directory of `.ttf`
files. On Windows the host's installed fonts are used automatically, so local development works
without configuration — which means a font problem will first appear in a container, not here. The
shipped `Dockerfile` installs DejaVu and sets the directory, so the image is already correct.

**The tests need a font too, and CI runners have none.** A bare `dotnet/sdk` image ships zero
`.ttf` files, so before [TestFonts.cs](MonitovoPDF.Tests/TestFonts.cs) existed the suite passed on
Windows and 37 of 80 tests failed on Linux with "No appropriate font found". That module
initializer copies one font into a temporary directory and installs a resolver, so the suite runs
anywhere; CI installs `fonts-dejavu-core` to guarantee there is one to find.

It copies a single font rather than pointing at a system directory on purpose: the resolver reads
every `.ttf` it finds, so aiming it at `C:\Windows\Fonts` would load hundreds of megabytes.

A related note, because an earlier version of this file got it wrong: **both font paths write
literal text into the content stream**, verified with `qpdf --stream-data=uncompress`. Raw-byte
searches of a PDF fail because the content stream is *compressed*, not because the text is stored
as glyph indices. Decompress it, or use a text extractor.

### Running the end-to-end check

```bash
docker compose -f integration/docker-compose.yml up --build --abort-on-container-exit
```

Artefacts land in `integration/out/` (gitignored). The run exits non-zero if any check fails.

---

## Working Agreements

- Work on a `feature/` or `fix/` branch, open a PR against `main`, and let Joe merge.
  No direct commits to `main`, no stacked PRs, one at a time.
- Never commit without stating the branch, the staged files and the commit message first,
  then waiting for explicit confirmation.
- Never mention Claude, AI or any AI tooling in commits, PRs, issues or comments.

---

## The Other Thing To Know: fonts are process-wide

PDFsharp resolves fonts through a single global hook, so a resolver installed by this library
applies to everything in the process that uses PDFsharp. That is unavoidable from inside a
library. `MonitovoPdf.UseFontDirectory` therefore refuses to displace a resolver it did not
install unless `force: true` is passed, and a first render with nothing configured falls back to
the host's installed fonts rather than failing. Do not make this implicit — a library that quietly
changes how its host renders text is a bug waiting to be filed against the wrong project.

---

## The public API is a pinned contract

[PublicApi.approved.txt](MonitovoPDF.Tests/PublicApi.approved.txt) is a rendering of every public
type and member in the library, and [PublicApiTests.cs](MonitovoPDF.Tests/PublicApiTests.cs) fails
if the real surface drifts from it. Regenerating the baseline is easy and that is the point: the
diff is what gets reviewed. Do not regenerate it to make a test pass without reading what changed
— anything removed or retyped breaks every consumer who upgrades.

It has already earned this. It caught `RenderingOptions` and `TemplateRenderException` sitting in
`MonitovoPDF.Rendering` while everything else was in `MonitovoPDF`, which would have forced every
consumer to write a second `using` just to catch the library's own exception.
