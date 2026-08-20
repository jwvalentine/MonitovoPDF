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

## Decision 4 — Project layout — **DECIDED: three sibling projects**

`MonitovoPDF` is the library and the product. `MonitovoPDF.Server` is an optional ASP.NET Core
host that references it. `MonitovoPDF.Tests` covers both. All three are sibling directories at
the repository root, tied together by a solution file. The test framework is **xUnit**.

This replaced a flat layout in which the application project sat at the root and the test project
inside its directory, which forced explicit source-glob exclusions. Splitting the library out
removed that wart as a side effect.

## Decision 5 — Distribution — **DECIDED: a NuGet package, with a container for the host**

The library is the product and ships as a NuGet package targeting `net8.0` and `net10.0`. The
HTTP host ships as a container image for callers that want the capability over HTTP.

What is still open is *publishing*: nothing is pushed to nuget.org or a registry yet, and that
needs a versioning policy and a release workflow first. The package builds correctly today with
`dotnet pack`, carries XML documentation for IntelliSense, and includes the third-party notices
that ZXing.Net's Apache-2.0 terms require.

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

## Decision 7 — Barcode generation — **DECIDED: generate them in the service**

Callers originally had to supply a barcode as an image. The service now generates them from a
`barcodes` map instead, using **ZXing.Net** (Apache-2.0, verified upstream). The `images` path
remains for anything not covered.

Two reasons. A caller-supplied bitmap is rasterised and then scaled into the field, which can
blur at label-printer resolution and cost a scan; drawing the symbol as vector rectangles keeps
the edges exact at any resolution. And ZXing includes the quiet zone in the symbol, so a template
author no longer has to leave room for it around the field.

ZXing.Net's core package was chosen specifically because it has **no transitive dependencies**
and no imaging layer — it returns a bit matrix that this project draws itself. The alternatives
were rejected: NetBarcode is MIT but sits on SixLabors.ImageSharp, which is revenue-gated and now
enforces with licence keys; BarcodeLib drags in SkiaSharp's native binaries.

Apache-2.0 is permissive and imposes nothing on this project, which stays MIT. The one accepted
cost is that Apache-2.0 is incompatible with GPLv2, so a GPLv2 project could not incorporate
MonitovoPDF. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Fifteen symbologies are exposed. Thirteen are verified end to end by decoding the rendered PDF
with independent decoders that must agree on both symbology and value. MSI and Plessey encode and
render but no freely available decoder can read them, so they are documented as unverified rather
than quietly presented as tested.

The service does **not** compute check digits. A value is encoded as given, so GS1 and ITF-14
callers must supply a correct one. Worth revisiting if it trips people up.

## Decision 8 — The library is the product — **DECIDED**

MonitovoPDF is a **library first**. It exists to replace the commercial PDF components that run
inside an application at runtime — Aspose.PDF, IronPDF and similar — for the narrow job of
populating a template. The HTTP service is now an optional host on top, not the deliverable.

That reframing drove several things:

* **The public surface is `MonitovoPdf`**, a static entry point taking a callback that sets
  values. `LabelRenderer` and the symbology table are internal; consumers see one way in.
* **No `Microsoft.Extensions` dependency.** The library takes no `IOptions` and no `ILogger`:
  options are a plain object, and everything a caller needs to know arrives as an exception or,
  for the font resolver, an optional callback. A library that forces a DI container on its
  consumer is a worse library, and this also keeps the dependency count at two.
* **`net8.0` and `net10.0`.** Targeting only the newest runtime would exclude most of the
  applications that currently pay for Aspose. Both dependencies also support `netstandard2.0`, so
  reaching .NET Framework later is possible if anyone asks; it would need polyfills for the .NET 7+
  APIs the code uses.
* **Exceptions carry their cause.** `TemplateRenderException` keeps the underlying failure as
  `InnerException`, because an in-process caller has no log to consult.

The one sharp edge is fonts. PDFsharp resolves them through a single process-wide hook, so a
resolver this library installs affects everything else in the process that uses PDFsharp. There is
no way around that from inside a library, so `UseFontDirectory` refuses to displace a resolver it
did not install unless told to force it, and the behaviour is documented rather than hidden.

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

