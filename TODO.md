# MonitovoPDF — TODO

Immediate action items. Keep this list short and current: when something is done, delete it rather
than marking it done, and move anything that turns into a real body of work into [PLAN.md](PLAN.md).

Last reviewed: 2026-08-20

---

## Before the first release — needs Joe

These cannot be done from the repository, and the release workflow will fail without the first.

- [ ] **Set up Trusted Publishing on nuget.org.** Sign in, then username → Trusted Publishing →
      add a policy. No API key is involved: the workflow proves its identity with a short-lived
      OIDC token and gets a key valid for one hour, so there is no long-lived secret to leak or
      rotate. The fields:

      | Field | Value |
      |---|---|
      | Repository Owner | `jwvalentine` |
      | Repository | `MonitovoPDF` |
      | Workflow File | `release.yml` — file name only, no `.github/workflows/` path |
      | Environment | `release`, or blank if the environment is not created |
      | Glob Patterns and Packages | `MonitovoPDF` |

      The last field scopes which package ids the temporary key may push, the same way API key
      scoping always has, and it is **required**. The Microsoft documentation page does not
      mention it yet. Because the package does not exist on nuget.org, it cannot be picked from
      a list — type the id in as a pattern. Keep it to the exact id rather than `MonitovoPDF*`,
      which would also match ids like `MonitovoPDFSomethingElse`. If companion packages appear
      later, add `MonitovoPDF.*` on a second line; the field takes one entry per line.

      *If Trusted Publishing is not visible in the account, it has not rolled out there yet —
      the API-key version of the workflow is recoverable from git history as a stopgap.*
- [ ] **Add the `NUGET_USER` repository secret**, set to the nuget.org username (the profile
      name, not an email address). It is the one input the login step needs.
- [ ] **Create the `release` GitHub environment** and consider adding yourself as a required
      reviewer. That turns publishing into a deliberate approval rather than a side effect of
      pushing a tag.
- [ ] **Reserve the `MonitovoPDF` package id on nuget.org** before someone else does, and check
      whether the `Monitovo` prefix qualifies for id-prefix reservation, which earns the verified
      check mark on the package page. Tagging `v0.1.0-preview.1` claims the id and exercises the
      release path in one go.
- [ ] **Enable branch protection on `main`**: require a pull request and require the CI check to
      pass. The no-direct-commits rule is documented but not enforced.

## Next up

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

- [ ] Decide whether to add `CONTRIBUTING.md` and `CODE_OF_CONDUCT.md`. `SECURITY.md` is written,
      and it was the one that mattered: it gives people a private channel to report a vulnerability
      instead of opening a public issue.
- [ ] Add repository topics and confirm the description on GitHub for discoverability.
- [ ] Consider pinning the remaining GitHub Actions to commit SHAs. `NuGet/login` is already
      pinned, because it exchanges an identity token for a publishing credential and so is the
      most sensitive step in the repository. The first-party `actions/*` steps still float on
      major tags.

## Deferred / to revisit

- [ ] Reconsider whether `appsettings.Development.json` should stay tracked. It currently overrides
      nothing — its contents duplicate `appsettings.json` exactly. The risk is that it is an
      already-tracked file in a public repo, so a local secret added to it would commit silently.
      `appsettings.Local.json` and `.env` are gitignored and are the intended homes for local
      secrets.
