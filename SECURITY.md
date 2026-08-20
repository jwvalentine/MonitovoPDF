# Security Policy

## Reporting a vulnerability

**Please do not open a public issue for a security problem.** A public report tells everyone
running the code about the weakness before there is a fix available.

Report privately through GitHub's [security advisory
form](https://github.com/jwvalentine/MonitovoPDF/security/advisories/new), which creates a
discussion visible only to the maintainers.

Please include what you can: the version, a description of the problem, and the smallest input
that reproduces it. If a proof of concept involves a PDF, describe how to build it rather than
attaching a document that might carry real data.

You should get an acknowledgement within a few days. Once there is a fix, the release notes will
credit you unless you would rather stay anonymous.

## What is in scope

This library turns untrusted input into documents, which is an unusually sharp surface. The
following are all worth reporting:

* A template that causes the renderer to read or write outside the document — a path traversal,
  an unbounded allocation, a crash that escapes as something other than `TemplateRenderException`.
* Any input that consumes resources far beyond the configured ceilings, or bypasses them.
* A barcode or text value that escapes into the PDF structure rather than being drawn as content.
* Anything in `MonitovoPDF.Server` that reaches the network or the filesystem on behalf of a
  caller.

## What is not

* **The server has no authentication.** That is a known and documented gap, not a vulnerability
  report. It is not meant to be exposed to an untrusted network as it stands.
* **The `integration/` container is a test harness**, not something that ships. Findings against
  the tools inside it belong upstream.
* Denial of service achieved by configuring the ceilings higher than the host can bear. The
  defaults are conservative for a reason.

## Supported versions

The project is pre-1.0, so only the most recent release receives fixes. Once 1.0 ships, this
section will name a support window.
