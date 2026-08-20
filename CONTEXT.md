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
text and image values, draws them into the page at the positions the template's form fields
occupy, strips the fields, and returns a flat PDF. There is a `/health` endpoint. 27 tests cover
the renderer, the request decoder and the HTTP surface.

**Not yet built:** authentication of any kind, a `Dockerfile`, CI, and OpenAPI documentation. The
service must not be exposed to an untrusted network as it stands.

**Not yet proven:** everything is tested against synthetic templates built in code. No template
authored in a real PDF tool has been filled and sent to a real label printer. That is the test
that matters and it has not been run.

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
│   ├── FileSystemFontResolver.cs     # Loads .ttf files from a configured directory
│   └── TemplateRenderException.cs    # Caller-input failures, mapped to 4xx
├── MonitovoPDF.Tests/                # xUnit; synthetic PDF fixtures built in code
├── MonitovoPDF.csproj                # net10.0, nullable + implicit usings, PDFsharp 6.2.4
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
| Tests | xUnit, with `Microsoft.AspNetCore.Mvc.Testing` for the HTTP surface |
| CI | **Not yet set up** |
| Licence | MIT |

Dependencies are deliberately few: PDFsharp is the only runtime package.

---

## Build & Run

```bash
cd c:\dev\MonitovoPDF
dotnet build          # currently 0 warnings, 0 errors
dotnet test           # currently 27 passing
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
without configuration — which means a font problem will first appear in a container, not here.

---

## Working Agreements

- Work on a `feature/` or `fix/` branch, open a PR against `main`, and let Joe merge.
  No direct commits to `main`, no stacked PRs, one at a time.
- Never commit without stating the branch, the staged files and the commit message first,
  then waiting for explicit confirmation.
- Never mention Claude, AI or any AI tooling in commits, PRs, issues or comments.
