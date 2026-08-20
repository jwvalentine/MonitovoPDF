# MonitovoPDF — TODO

Immediate action items. Keep this list short and current: when something is done, delete it rather
than marking it done, and move anything that turns into a real body of work into [PLAN.md](PLAN.md).

Last reviewed: 2026-08-20

---

## Needs Joe

These cannot be done from the repository.

- [ ] **Confirm image placeholder ordering against a real template.** Placeholders are numbered
      by resource name with embedded numbers compared numerically, so `/Im2` precedes `/Im10`.
      That rule is ours, stated and tested, but whether it matches the order the existing
      templates were authored against has not been checked against one of them. Getting it wrong
      does not fail loudly: it swaps one placeholder's content for another's and the document
      still renders. `Inspect` reports the numbering alongside each placeholder's resource name,
      pixel size and drawn position, which is enough to check a template against expectations.
- [ ] **Enable branch protection on `main`**: require a pull request and require the CI check to
      pass. The no-direct-commits rule is documented but not enforced.
- [ ] **Unlist every `0.x` prerelease on nuget.org once `1.0.0` is published.** `--prerelease`
      resolves to the newest prerelease, so leaving them listed means a caller who keeps the flag
      out of habit lands on one instead of on 1.0. `0.3.0-preview.1` is the one that matters:
      filling a form field with an image and a placeholder by position in the same call fills the
      wrong placeholder there, silently, because the drawn image joins the page's resources and
      renumbers them.
- [ ] **Check whether the `Monitovo` prefix qualifies for id-prefix reservation** on nuget.org,
      which earns the verified check mark on the package page.

## Next up

- [ ] **Scan a printed label with a real scanner.** Labels rendered by this library now print on
      real hardware, so the chain from template to paper is proven. What is not is the last step
      of it: every barcode here has been decoded from a software rasterisation, never off a
      printed label through a scanner's optics. Thermal printing is where a symbol picks up the
      defects a rasteriser has no way to introduce — ink spread widening the bars, a worn head
      thinning them, the media moving under the print line.
- [ ] **Enable Dependabot for NuGet and GitHub Actions.** This matters more now that every action
      is pinned to a commit: a pin never moves, so without something proposing updates the
      workflows quietly fall behind on security fixes. Dependabot understands the pinned
      `sha # version` form and updates both parts together.
- [ ] **Confirm MSI and Plessey against real scanners** if anyone needs them. They render, but no
      decoder in the integration image can read them, so they are the only symbologies shipped
      without scan verification. Consider dropping them if nobody does.
- [ ] Decide whether the library should compute **data** check digits. Symbology check characters
      are already generated as part of the encoding; what is not is the digit belonging to the data
      — the last digit of an EAN, UPC or GS1 number, or of an ITF-14. A caller who gets it wrong
      gets a barcode that scans cleanly and carries the wrong number. Related: the readable value
      printed under a barcode is the value as supplied, so an EAN or UPC gets a single plain line
      rather than the grouped layout with an outset check digit that the specification prescribes.

## Hardening

- [ ] **Abuse pass over template parsing.** The renderer opens untrusted PDFs. Feed it malformed,
      truncated, deeply nested and decompression-bomb documents and confirm each is refused within
      its ceilings.
- [ ] **Decide authentication** (Decision 3 in PLAN.md). Nothing authenticates anything today, so
      the service must not be exposed to an untrusted network as it stands.
- [ ] Confirm the render timeout behaves acceptably. It bounds how long a caller waits, but the
      underlying render is synchronous and cannot be aborted, so the work continues after a
      timeout. The input ceilings are the real defence.

## Usability

- [ ] **Expose template inspection over HTTP.** The library reports a template's pages and fields
      through `Inspect`, which is what a template author needs when a field name turns out not to
      be what they assumed. The server does not surface it yet.
- [ ] Add OpenAPI documentation for the HTTP surface.
- [ ] Consider a `netstandard2.0` target so .NET Framework applications can use the library. Both
      dependencies support it; the blocker is that the code uses .NET 7+ APIs that would need
      polyfilling. Worth doing if anyone replacing Aspose is still on Framework.

## Repository housekeeping

- [ ] Decide whether to add `CONTRIBUTING.md` and `CODE_OF_CONDUCT.md`. `SECURITY.md` is written,
      and it was the one that mattered: it gives people a private channel to report a vulnerability
      instead of opening a public issue.
- [ ] Add repository topics and confirm the description on GitHub for discoverability.

## Deferred / to revisit

- [ ] Reconsider whether `appsettings.Development.json` should stay tracked. It currently overrides
      nothing — its contents duplicate `appsettings.json` exactly. The risk is that it is an
      already-tracked file in a public repo, so a local secret added to it would commit silently.
      `appsettings.Local.json` and `.env` are gitignored and are the intended homes for local
      secrets.
