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

A small .NET library that fills template documents with data inside the application that needs
them, with no per-document cost and no licence obligation that prevents embedding it in a
commercial product. An HTTP host ships alongside for callers that want it over the wire.

## Design Principles

1. **One job, done well.** Template and data in, PDF out. Not a document management system.
2. **Permissive all the way down.** MIT, on top of dependencies whose licences allow
   redistribution under MIT.
3. **Safe by default.** Templates are untrusted even in process. Bounded resources and no network
   reach from the renderer are requirements, not features.
4. **Few dependencies.** Every package is a licence obligation and a patching burden — and one
   the consumer inherits, which is a stronger reason for a library than for a service.
5. **Impose nothing on the consumer.** No DI container, no logging framework, no configuration
   system. Options are a plain object and failures are exceptions.

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

## Decision 9 — Versioning — **DECIDED: SemVer, driven by a tag**

The package follows [Semantic Versioning](https://semver.org). Releases are cut by pushing a
`vX.Y.Z` tag; the release workflow takes the version from the tag, so what shipped and what the
repository held at that tag cannot disagree. The `<Version>` in the project file is the working
version between releases and is overridden at pack time.

**From 1.0**, a breaking change to the public API requires a major bump. The approval test's
baseline is the definition of that surface, and changing it is the moment to ask whether a major
version is warranted — a break shows up as a diff in review rather than being discovered by a
consumer.

The 0.x series took the opposite freedom, and used it: `0.3.0` and `0.4.0` each broke something.
That is what 1.0 gives up, which is the whole point of declaring it — the surface stops being
something the project can revise at will and becomes something callers can build against.

1.0 is warranted by use rather than by elapsed time: labels rendered by this library now print on
real hardware, in place of the commercial component they were written to replace. That is the
evidence that matters, and no amount of further synthesised testing would have produced it.

What 1.0 does **not** claim is that every code path has been exercised that far. The newest of
them, filling placeholders addressed by position, is also the least worn: it shipped in `0.3.0`
and a defect in it was found and fixed within a day. The response to that is not to withhold the
version but to hold the API steady while the paths behind it settle, which is exactly what a
major version permits.

Prereleases use a suffix the tag carries — `v1.1.0-rc.1` — and publish like any other version.

## Decision 5 — Distribution — **DECIDED: a NuGet package, with a container for the host**

The library is the product and ships as a NuGet package targeting `net8.0` and `net10.0`. The
HTTP host ships as a container image for callers that want the capability over HTTP.

Publishing runs from CI: pushing a `vX.Y.Z` tag builds, tests, packs and pushes to nuget.org, then
opens a GitHub release. See Decision 9 for the versioning policy and Decision 10 for how the
workflow authenticates.

The package carries XML documentation for IntelliSense, a symbols package, and the third-party
notices that ZXing.Net's Apache-2.0 terms require.

## Decision 10 — Publishing credentials — **DECIDED: Trusted Publishing, no stored key**

The release workflow authenticates to nuget.org with [Trusted
Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing) rather than a
stored API key. GitHub issues a short-lived, signed OIDC token describing the repository and
workflow; nuget.org validates it against a policy and returns an API key valid for one hour.

The reason is simple: **there is no long-lived secret in this repository to leak.** A stored
publishing key is a standing credential — it sits in settings, it has to be rotated, and anyone
who obtains it can publish under this package id until someone notices. A token that exists for
the length of one job cannot be exfiltrated from a repository that never holds it. NuGet's own
guidance now prefers this, and it is the same direction PyPI and others have taken.

Three consequences worth recording:

* **The login step comes last**, immediately before the push. The key lasts an hour and each
  token buys exactly one key, so requesting it before a long build risks it expiring.
* **`NuGet/login` is pinned to a commit**, not the moving `v1` tag. It is the step that turns an
  identity token into a publishing credential, so a silently updated version of it is the worst
  supply-chain outcome available in this repository.
* **The job runs in a `release` environment**, which can carry required reviewers. Publishing
  should be a deliberate approval, not a side effect of pushing a tag.

The only stored value is `NUGET_USER`, the nuget.org profile name. That is an identifier, not a
credential.

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


## Decision 11 — Fonts the library can rely on — **DECIDED**

Two changes, both driven by the same problem: PDFsharp's cross-platform build cannot use a host's
installed fonts, and a slim Linux container has none, so text simply fails to draw there. The
project's own CI hit this before any consumer did.

**The library carries one font.** DejaVu Sans is embedded in the assembly and served by
`MonitovoPdf.UseBundledFonts()`. It is opt-in rather than automatic: for a label, silently drawing
in a font whose metrics differ from the designer's changes what fits, and wrong output is worse
than a loud failure. When nothing is configured and the host has no fonts, the first render throws
a `TemplateRenderException` naming both `UseBundledFonts` and `UseFontDirectory`, so the failure
carries its own fix. Only the regular face is bundled — the renderer never asks for a styled one —
which keeps it to roughly 750KB. DejaVu adds no new licence obligation, since the container image
already redistributes it.

**The template's font is honoured.** A field's default-appearance string names a resource and a
size; the resource resolves through the form's default resources to a base font. Previously only
the size was read and everything drew in the configured default. Now the family travels too, with
subset tags and style suffixes stripped, and the base-14 names mapped to what a host is likely to
have. A family the host lacks substitutes rather than failing.

Worth knowing for anything that touches this: **PDFsharp fixes its font resolver on first use.**
Fonts can only be configured once, at start-up, and a later call throws. That constraint is
translated into a message that says so, rather than surfacing the engine's own wording.

What is deliberately *not* done is extracting font programs embedded in the template. Those are
normally subset to the glyphs the static artwork already uses, so an empty field contributes none
and the very characters being drawn would be missing. There is also a question of whether a font's
embedding permission extends to authoring new text. Naming the family and letting the deployment
supply it is both more honest and more useful.

## Decision 12 — What the fill API owes a caller — **DECIDED**

Four additions in v0.2.0, each closing a case where the library was correct in the narrow sense
and unhelpful in practice.

**A value reaches every field of its name.** A template may show one value in several places, and
does so in two different shapes: one field with several widget annotations, or separate field
objects sharing a name. The first already worked; the second kept only one field and silently
dropped the rest — a document that looks plausible and is missing a value. Both now fill
everywhere. Related: a kid with no name of its own is a widget, not a child field, and treating it
as one invented fields the template never contained.

**A missing field can be tolerated, and is always reported.** Failing the whole render on an
unknown name is right by default, because it usually means the wrong template. But one set of
values may legitimately feed templates that do not all carry every field, so `OnMissingField`
relaxes it. What is *not* offered is quiet tolerance: `FillWithReport` returns the names that did
not land, because ignoring silently turns the wrong template into a plausible wrong document.

**Appearance can be overridden per field, but the template still wins by default.** `TextOptions`
sets size, family, alignment or wrapping for one value. The default remains what the field asks
for, because appearance living in the template is what lets whoever designs it change how a
document looks without a code change. The override is an escape hatch, not the recommended path.

**A template can be read without being filled.** `Inspect` reports page size, rotation, and each
field's name, kind, placements and requested appearance. It answers "why did nothing appear"
— nearly always a field named something other than assumed — and lets a caller reject a template
of the wrong size up front rather than stretching it to fit.

Multiline text was simply broken: a value containing line breaks was drawn as a single run, so the
extra lines were lost. It now wraps on word boundaries, honours the field's multiline flag, shrinks
to fit height as well as width, and clips at the bottom edge rather than drawing outside the field.

One discovery worth recording: **reading a text field constructs a font inside the PDF engine**, so
even `Inspect`, which draws nothing, fails on a host with no fonts. It configures fonts the same
way a fill does.

## Decision 13 — Placeholders that are images — **DECIDED: address them by position**

Not every template marks its placeholders with form fields. A large class uses ordinary image
XObjects instead: the page draws an image at a fixed spot, and filling means exchanging that image
for another. Templates of that shape are frequently customer-owned or contractually fixed, so
re-authoring them is not available to whoever has to fill them.

`SetImageAt` and `SetBarcodeAt` address a placeholder by page and position. Only the image object
is exchanged; the page's drawing instructions are untouched, so the replacement inherits the
placeholder's position and size exactly and is stretched to fill it whatever its own proportions.
Placeholders that were not addressed are left completely alone.

**Ordering is the whole risk.** A PDF resource dictionary has no key order, so an ordinal means
nothing unless the ordering rule is ours, stated, and invariant. Placeholders are numbered by
resource name with embedded numbers compared numerically, so `/Im2` precedes `/Im10`. Getting this
wrong does not fail: it swaps one placeholder's content with another's and the document still
renders, which is why `Inspect` reports the numbering rather than leaving callers to infer it.

A barcode becomes a **form XObject on the unit square** rather than a picture. A page draws an
image by mapping the unit square onto wherever it belongs, and a form with that same bounding box
is drawn by the same operator under the same transform — so the geometry is inherited while the
bars stay vector. Substituting a raster would fix the resolution at the moment of filling, and a
barcode resampled on its way to a printer is a barcode that stops scanning.

Two things this forced, both improvements in their own right: a template whose placeholders are all
images has no interactive form, so requiring one was wrong and now only applies to callers naming a
field; and the engine *throws* rather than returning null for a document with no form, despite its
type promising otherwise, so the absence has to be caught rather than tested for.

The alternative considered was addressing by resource name, which is stabler. It was rejected as
the primary form because the templates this exists for were authored against ordinals, and a
migration that requires re-authoring the templates defeats the purpose. Name-based addressing
remains available to add if a case for it appears.

## Decision 14 — The value under the bars — **DECIDED: draw it on the page, not into the symbol**

A barcode's value printed as readable text below it is the fallback when a scanner is not to hand
or the symbol has been damaged: somebody reads the number and keys it in. A label carrying its
number only as bars has no fallback at all. It is normal practice for printed barcodes and part of
the specification for some symbologies, so it belongs in the library rather than in each caller.

It is **off by default**. The text goes inside the space the barcode was already given rather than
beside it, so the bars give up the height it takes. That is a trade — bar height is what lets a
scanner read a symbol that is not squarely presented to it — and a trade should be asked for.

**Where the text is drawn was the real decision.** A barcode replacing an image placeholder is a
form XObject, and drawing the text into that form would inherit the placeholder's rotation for
free. It was rejected for two reasons. A form built by hand has no font to draw with, so the text
would have to use one of the fourteen standard faces that are never embedded — reintroducing the
dependency on what the consumer has installed that flattening the template exists to remove. And
the form is the unit square stretched onto the placeholder, so every glyph would be stretched by
the placeholder's proportions along with it.

So the text is drawn onto the page, in the engine's own text path with a real embedded font, under
a transform recovered from the placeholder. That transform is decomposed rather than used whole:
each of the placeholder's two axes is measured separately, which keeps its position and rotation
while discarding its stretch. A barcode a template stood on its end gets its value turned to match;
a placeholder five times wider than it is tall gets text at a true point size.

The cost is that this only works where the placement can be recovered — a placeholder the page
declares but never draws has no position to inherit, and that is refused rather than dropped
silently, since the value is the whole point of asking.

**The text is the value as supplied, not as encoded.** A check character added during encoding is
not shown. The number a person reads off the label is the number they were given to look up, and
printing a longer one underneath would send them looking for something that does not exist.
EAN and UPC prescribe a grouped layout and an outset digit; that is not attempted, and a caller
needing it should say so.

## Decision 15 — The other field types — **DECIDED: paint the template's own artwork**

A template arrives, its fields are filled from whatever the data system holds, and the result is
flat. Nothing about that changes with what the template depicts — a label and a claim form are
the same operation. Which made it awkward that the library implemented one of the four field
types PDF defines, and awkward in a way that only showed up on the forms: text is most of a
label and almost none of a business form, where tick boxes and dropdowns carry the answers.

Buttons and choices are now filled. Signature fields are not; see below.

**A tick box is drawn from the template's own artwork.** A widget carries a small form XObject
per state, and the tick, the box and the weight of both are the designer's. Painting the state
asked for is therefore both the faithful answer and the simpler one — the alternative is
choosing a glyph, a size and a stroke width that the template already decided. Mapping it onto
the widget's rectangle follows the mapping the specification prescribes for exactly this, which
is what makes the result identical to what a viewer would have shown.

That also repairs something flattening quietly broke. A box's outline usually lives in the
widget rather than in the page, so removing the form removed the box. An unticked box is
therefore painted rather than skipped, and "clear this box" is a different instruction from
"leave this box alone" — a template may ship one ticked.

**The options belong to the template.** A dropdown carries `/Opt`; a radio group is defined by
the states its widgets answer to. A caller chooses from what is there, and a value the field
does not offer is refused. A form recording an answer it never offered is worse than one that
fails, because it looks completed. `Inspect` reports the list so a caller need not guess it.

The choice itself is drawn as text where the control was, because flattening removes the
control that would have shown it.

**Verified against a real tool, which mattered.** LibreOffice writes `/Opt` as UTF-16 hex
strings rather than as literals — a synthesised fixture using literals would have passed while
the real thing failed. It also confirmed that a real authoring tool does write per-state
artwork, which the whole approach depends on. The end-to-end check now fills a LibreOffice form
and measures the rasterised page: the ticked document carries more ink than the cleared one,
which is the only way to assert a tick from the outside.

**Signature fields are out of scope for now, and it is not the dependency that stops it.**
PDFsharp already ships a signer, which is why `System.Security.Cryptography.Pkcs` is in the
graph. What stops it is that signing is asynchronous throughout while this API is synchronous
by design (Decision 2); that it takes a private key, making a library that currently handles no
secrets handle them; that timestamping reaches an external server, making a library that
currently opens no sockets open them; and that a signature is a form field, so signing and
flattening pull against each other. Each of those is a decision rather than an implementation,
and they should be taken deliberately rather than because the package was already referenced.
