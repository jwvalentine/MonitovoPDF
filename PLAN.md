# MonitovoPDF — Product Plan

> **Status: draft. The decisions below are OPEN, not made.** This file records the
> intent of the project and lays out the choices that need making, with the trade-offs
> as they are currently understood. Nothing here has been agreed or implemented. Joe
> decides; do not treat any option as chosen because it appears first.
>
> Every licence claim in this file **must be verified against the library's current
> licence before adoption.** Several .NET PDF libraries have changed licence, and the
> project cannot ship under MIT on top of a copyleft or commercially-licensed core.

---

## Vision

A small, self-hostable HTTP service that turns source content into PDF documents, with
no per-document cost and no licence obligation that prevents embedding it in a
commercial product.

## Design Principles

1. **One job, done well.** Content in, PDF out. Not a document management system.
2. **Permissive all the way down.** MIT, on top of dependencies whose licences allow
   redistribution under MIT.
3. **Safe by default.** The service accepts untrusted input. Sandboxing, bounded
   resources and no network reach from the renderer are requirements, not features.
4. **Few dependencies.** Every package is a licence obligation and a patching burden.
5. **Runs anywhere.** A single container, no external services required.

---

## Open Decision 1 — Rendering approach

The most consequential choice, and everything else follows from it.

**Option A — Document-model library (draw the PDF directly in C#).**
Define documents in code or from a data model, and a library emits the PDF.
* Upside: no browser, small container, fast, low memory, fully deterministic output,
  no SSRF surface.
* Downside: layout is code, not markup. Rich or design-heavy documents are laborious.
* Candidates to evaluate: **PDFsharp / MigraDoc** (believed MIT — verify),
  **QuestPDF** (*believed to have moved from MIT to a dual Community/Professional
  licence — verify carefully, this may disqualify it*).

**Option B — HTML to PDF via headless Chromium.**
Accept HTML/CSS, render in a headless browser, print to PDF.
* Upside: authors use HTML/CSS, which almost everyone already knows. Excellent
  fidelity for complex layouts. Templates are easy to write and preview.
* Downside: large container image, high memory per render, slower cold start, and a
  genuine security surface — a renderer that follows user-supplied URLs is an SSRF
  primitive. Requires strict sandboxing and network egress blocking.
* Candidates to evaluate: **PuppeteerSharp**, **Playwright for .NET**.

**Option C — Both, behind one API.**
A document-model path for structured reports, an HTML path for arbitrary templates.
* Upside: covers both use cases.
* Downside: two rendering paths to build, test, secure and document. Realistically a
  later phase, not a starting point.

**Recommendation:** decide the *first* target use case before picking. If the first
consumer needs structured, data-driven reports with a consistent layout,
Option A is the smaller and safer starting point. If the first consumer needs
designer-authored templates, Option B is hard to avoid.

## Open Decision 2 — API surface

Undecided. Questions to answer:
* Synchronous render-and-return, asynchronous job + poll, or both?
* What is the request body — HTML, a JSON document model, Markdown, a template name
  plus data?
* Where do generated PDFs go — streamed back in the response only, or stored?
* Is there a template concept, or is every request self-contained?

## Open Decision 3 — Authentication

Undecided. Options range from none (assume it runs on a trusted network behind a
proxy) through a static API key to full OIDC. Whatever is chosen must be **optional
and configuration-driven**, because self-hosters' deployment models differ.

## Open Decision 4 — Project layout

Currently a single flat project. Needs a decision once real code exists: stay flat,
or split into `src/` + `tests/` with a solution file. A test project is needed either
way, and the test framework is undecided.

## Open Decision 5 — Distribution

How consumers are expected to get it: a published Docker image, a NuGet package, a
GitHub release binary, or source only. This affects whether the repository needs a
`Dockerfile`, a release workflow and a versioning policy.

---

## Rough Phasing

These phases assume the decisions above are made first.

* **Phase 0 — Foundations.** Test project, CI on pull requests, `Dockerfile`, health
  endpoint, structured logging. No PDF code yet.
* **Phase 1 — First render.** The narrowest useful path end to end: one input format,
  one output, with tests pinning the output and explicit limits on size and time.
* **Phase 2 — Hardening.** Resource ceilings, sandboxing, input validation, an abuse
  test pass, and documentation of the security model.
* **Phase 3 — Usability.** Templates, additional input formats, richer options
  (page size, margins, headers/footers), OpenAPI documentation.
* **Phase 4 — Release.** Versioning policy, published artefacts, contribution guide,
  security disclosure policy.
