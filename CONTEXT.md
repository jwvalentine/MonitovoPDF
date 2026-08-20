# MonitovoPDF — Session Context

Use this file to orient Claude at the start of a new chat session on this project.
Tell Claude: "Read CONTEXT.md before we start."

---

## What This Project Is

**MonitovoPDF** is a self-hostable HTTP service that fills template PDFs with data, built on
ASP.NET Core. It is **free and open source under the MIT licence**, published publicly, and
intended to be usable inside commercial products without per-document licensing costs.

The motivating problem: commercial products that do this (Aspose.PDF, IronPDF and similar) are
expensive per document or restrictively licensed for redistribution. The specific capability
needed is narrow — take a template, replace named placeholders with text and images, return the
finished document — and a small permissively-licensed service that does exactly that is more
useful than a large dependency with a licence audit attached.

See [PLAN.md](PLAN.md) for decisions made and still open, and [TODO.md](TODO.md) for immediate
action items.

---

## Current State

**The first rendering path is built and tested.** `POST /v1/labels` takes a base64 template plus
text, image and barcode values, draws them into the page at the positions the template's form
fields occupy, strips the fields, and returns a flat PDF. There is a `/health` endpoint. 57 tests
cover the renderer, the request decoder and the HTTP surface.

The service generates barcodes itself in 15 symbologies — see
[BarcodeSymbology.cs](Rendering/BarcodeSymbology.cs) — drawn as vector rectangles rather than
rasterised, so bar edges stay exact at print resolution.

A `Dockerfile` builds a runnable image with fonts installed, and `integration/` holds a
container-based end-to-end check: LibreOffice builds a real PDF form through its own API, the
service fills it, poppler reads the text back out, and three independent decoders read the
barcodes back.

**Not yet built:** authentication of any kind, CI, and OpenAPI documentation. The service must not
be exposed to an untrusted network as it stands.

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
[LabelRendererTests.cs](MonitovoPDF.Tests/LabelRendererTests.cs) pins the current behaviour by
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
├── Program.cs                        # Minimal API: endpoints, options, font resolver wiring
├── Api/
│   └── RenderLabelRequest.cs         # Wire DTO, plus decoding and boundary validation
├── Rendering/
│   ├── LabelRenderer.cs              # The core: draws values into a template, strips the form
│   ├── RenderingOptions.cs           # Configured ceilings and defaults
│   ├── BarcodeSymbology.cs           # The symbologies callers may ask for
│   ├── FileSystemFontResolver.cs     # Loads .ttf files from a configured directory
│   └── TemplateRenderException.cs    # Caller-input failures, mapped to 4xx
├── MonitovoPDF.Tests/                # xUnit; synthetic PDF fixtures built in code
├── integration/                      # LibreOffice-driven end-to-end check, run via Docker
│   ├── make_template.py              # Builds an AcroForm template through the UNO API
│   ├── barcodes.py                   # All-symbologies sheet, and decoding it back
│   ├── run_tests.py                  # Fills it against the running service and inspects the result
│   ├── Dockerfile                    # LibreOffice + poppler + zbar, libdmtx, zxing-cpp
│   └── docker-compose.yml            # Runs the service and the check together
├── Dockerfile                        # Runtime image; installs DejaVu so text can draw
├── licenses/Apache-2.0.txt           # Shipped with the image for ZXing.Net
├── THIRD-PARTY-NOTICES.md            # Redistributed works and the copyleft audit
├── MonitovoPDF.csproj                # net10.0, nullable + implicit usings
├── MonitovoPDF.slnx                  # Solution tying the app and test projects together
├── appsettings.json                  # Defaults, including the Rendering ceilings
├── Properties/launchSettings.json    # Local dev ports (http 5155, https 7255)
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
| Runtime | .NET 10 (`net10.0`) |
| Web framework | ASP.NET Core minimal APIs |
| PDF engine | PDFsharp 6.2.4 (MIT, verified upstream) |
| Barcodes | ZXing.Net 0.16.11 (Apache-2.0, no transitive dependencies) |
| Tests | xUnit, with `Microsoft.AspNetCore.Mvc.Testing` for the HTTP surface |
| CI | **Not yet set up** |
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
dotnet test           # currently 57 passing
dotnet run            # listens on http://localhost:5155
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

One consequence worth knowing: the two font paths encode text differently. Windows' platform
resolver produces literal text in the content stream, while a font loaded from a directory is
embedded as a subset and the text becomes glyph indices. Assertions that grep the raw bytes for a
drawn value will pass on Windows and fail in a container — use a text extractor instead.

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
