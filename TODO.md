# MonitovoPDF — TODO

Immediate action items. Keep this list short and current: when something is done, delete it rather
than marking it done, and move anything that turns into a real body of work into [PLAN.md](PLAN.md).

Last reviewed: 2026-08-20

---

## Next up

- [ ] **Publish the package.** It builds, carries XML docs and notices, and has been verified by
      consuming it from a separate .NET 8 application, but nothing is pushed to nuget.org. Needs a
      versioning policy and a release workflow first.
- [ ] **Add a GitHub Actions workflow**: restore, build, test and pack on pull requests to `main`.
      Consider running the `integration/` container there too, though it is slow to build.
- [ ] **Add an API-surface test.** The public surface is now a shipped contract; a public-API
      approval test would make an accidental breaking change visible in review.
- [ ] **Enable branch protection on `main`** so the no-direct-commits rule is enforced rather than
      documented.
- [ ] **Print a rendered label on real hardware.** A LibreOffice-authored template is now filled
      end to end in Docker and read back with poppler, but nothing has been sent to an actual label
      printer. That is the last unverified link in the chain.
- [ ] Enable Dependabot for NuGet and GitHub Actions.
- [ ] **Confirm MSI and Plessey against real scanners** if anyone needs them. They render, but no
      decoder in the integration image can read them, so they are the only symbologies shipped
      without scan verification. Consider dropping them if nobody does.
- [ ] Decide whether the library should compute check digits. It currently encodes a value as
      given, so a GS1 or ITF-14 caller must supply a correct one or get a valid-looking barcode
      carrying a wrong number.

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

- [ ] **Add an endpoint that lists the fields a template defines.** Template authors currently have
      to guess field names, and a typo only surfaces as a rejected render.
- [ ] Add OpenAPI documentation for the HTTP surface.
- [ ] Consider a `netstandard2.0` target so .NET Framework applications can use the library. Both
      dependencies support it; the blocker is that the code uses .NET 7+ APIs that would need
      polyfilling. Worth doing if anyone replacing Aspose is still on Framework.

## Repository housekeeping

- [ ] Decide whether to add `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md` and `SECURITY.md`. A public
      repo inviting contributions usually wants all three; `SECURITY.md` matters most, because it
      gives people a private channel to report vulnerabilities instead of opening a public issue.
- [ ] Add repository topics and confirm the description on GitHub for discoverability.
- [ ] Decide on a versioning policy before the first release. The package is at 0.1.0.

## Deferred / to revisit

- [ ] Reconsider whether `appsettings.Development.json` should stay tracked. It currently overrides
      nothing — its contents duplicate `appsettings.json` exactly. The risk is that it is an
      already-tracked file in a public repo, so a local secret added to it would commit silently.
      `appsettings.Local.json` and `.env` are gitignored and are the intended homes for local
      secrets.
