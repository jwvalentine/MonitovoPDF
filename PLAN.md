# MonitovoPDF — Product Plan

> **Status: the founding decisions are made and the first path is built.** The rendering approach,
> the API shape and the first use case are settled and implemented. What remains open is recorded
> below as open decisions.
>
> Every licence claim in this file **must be verified against the library's current licence before
> adoption.** Several .NET PDF libraries have changed licence, and the project cannot ship under
> MIT on top of a copyleft or commercially-licensed core.

---

## Vision

A small, self-hostable HTTP service that fills template documents with data, with no per-document
cost and no licence obligation that prevents embedding it in a commercial product.

## Design Principles

1. **One job, done well.** Template and data in, PDF out. Not a document management system.
2. **Permissive all the way down.** MIT, on top of dependencies whose licences allow
   redistribution under MIT.
3. **Safe by default.** The service accepts untrusted input. Bounded resources and no network
   reach from the renderer are requirements, not features.
4. **Few dependencies.** Every package is a licence obligation and a patching burden.
5. **Runs anywhere.** A single container, no external services required.

---

## Decision 1 — Rendering approach — **DECIDED: fill an existing template**

The first use case is label printing: a template is held as base64 in the consumer's database,
rehydrated per print, populated with text and images, and sent to a label printer.

That is a *fill* problem, not a *generate* problem, and it is much smaller than either of the
options originally considered here (a document-model library, or HTML through headless Chromium).
Both of those were dropped. Notably, because nothing is ever fetched by URL, the renderer needs no
network access at all — the SSRF surface that made the Chromium option expensive to secure does
not exist in this design.

**Library: PDFsharp**, verified MIT at the time of adoption from the upstream repository. QuestPDF
was rejected on two counts: its licence moved to a dual Community/Professional model, and it
generates documents rather than filling existing ones.

**Templates mark placeholders with named AcroForm fields**, which keeps templates self-describing
and lets a designer author them visually in any PDF tool.

**The output is flat, drawn into the page content stream.** This is the load-bearing detail.
PDFsharp does not generate appearance streams for filled form fields — upstream issue 64, closed
as *wontfix* — so a document whose content lives in field values renders blank in viewers that do
not build appearances themselves, which includes several print paths. The form fields are
therefore read only for their names, positions and sizes; the values are drawn onto the page and
the fields are then stripped.

## Decision 2 — API surface — **DECIDED: synchronous, stateless, self-contained**

`POST /v1/labels` takes the template and the values in one request and returns the finished PDF
in the response. The service stores nothing: no templates, no generated documents, no job state.
Consumers keep templates wherever they already keep them.

Printing is explicitly out of scope. The service returns bytes; the caller sends them to the
printer. This keeps the service platform-neutral and stops it from needing to reach into a local
network, which the security rules forbid.

## Decision 3 — Authentication — **OPEN**

Still undecided. Options range from none (assume it runs on a trusted network behind a proxy)
through a static API key to full OIDC. Whatever is chosen must be **optional and
configuration-driven**, because self-hosters' deployment models differ. Nothing in the current
build authenticates anything.

## Decision 4 — Project layout — **PARTLY DECIDED**

The application project stays at the repository root and a sibling `MonitovoPDF.Tests` project
holds the tests, with a solution file tying them together. The test framework is **xUnit**.

This leaves one wart: the test project sits inside the application project's directory, so the
application's `.csproj` has to exclude it from its source globs explicitly. Moving to a
`src/` + `tests/` layout would remove that, and is worth revisiting before the repository grows.

## Decision 5 — Distribution — **PARTLY DECIDED**

A `Dockerfile` now builds a runnable image, so a container is the working answer for deployment.
What is still undecided is *publishing*: whether the project pushes a tagged image to a registry,
and whether it also offers a NuGet package or a release binary. That decision needs a versioning
policy and a release workflow to go with it.

## Decision 6 — Fonts in a container — **DECIDED: ship DejaVu in the image**

PDFsharp's cross-platform build loads no fonts, and a Linux container has none installed, so a
deployment must supply them. The service reads `.ttf` files from a configured directory.

The container image installs `fonts-dejavu-core` and points `Rendering__FontDirectory` at it, so
the image works out of the box rather than failing on the first render. DejaVu is under the
Bitstream Vera licence, which permits redistribution provided the notice travels with the copies;
the Debian package's copyright file remains in the image, which satisfies that. A deployment
needing different typefaces overrides the directory and mounts its own.

Note that DejaVu's file names — `DejaVuSans.ttf`, `DejaVuSans-Bold.ttf` — happen to match the
face-name convention the resolver expects, which is why no mapping configuration is needed.

---

## Rough Phasing

* **Phase 0 — Foundations.** *Partly done.* Test project, health endpoint, configuration ceilings
  and a `Dockerfile` are in place. CI is not.
* **Phase 1 — First render.** *Done.* Template in, populated flat PDF out, with tests pinning the
  drawn output and explicit limits on size, page count and field count.
* **Phase 2 — Hardening.** An abuse test pass against malformed and hostile templates, a documented
  security model, and a decision on authentication.
* **Phase 3 — Usability.** Richer placement control, OpenAPI documentation, and better diagnostics
  for template authors — most usefully an endpoint that lists the fields a template defines.
* **Phase 4 — Release.** Versioning policy, published artefacts, contribution guide, security
  disclosure policy.
