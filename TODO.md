# MonitovoPDF — TODO

Immediate action items. Keep this list short and current: when something is done,
delete it rather than marking it done, and move anything that turns into a real
body of work into [PLAN.md](PLAN.md).

Last reviewed: 2026-08-19

---

## Blocking — decisions needed before code

These gate everything else. See [PLAN.md](PLAN.md) for the trade-offs.

- [ ] **Decide the rendering approach** (document-model library vs headless Chromium).
      Everything else follows from this.
- [ ] **Verify the licence of the chosen PDF library** before adding the package.
      A copyleft or commercially-licensed core cannot ship under MIT. Specifically
      confirm QuestPDF's current licence terms if it is a candidate.
- [ ] **Name the first real use case.** What is the first thing that needs a PDF?
      The answer decides the rendering approach and the API shape.

## Foundations — safe to do now, independent of the decisions above

- [ ] Add a test project and a first passing test, so there is somewhere for tests to go.
- [ ] Add a GitHub Actions workflow: restore, build, test on pull requests to `main`.
- [ ] Add a `Dockerfile` so the service can be run as a container.
- [ ] Add a `/health` endpoint and replace the placeholder `"Hello World!"` route.
- [ ] Enable Dependabot for NuGet and GitHub Actions.

## Repository housekeeping

- [ ] Decide whether to add `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md` and
      `SECURITY.md`. A public repo inviting contributions usually wants all three;
      `SECURITY.md` matters most, because it gives people a private channel to report
      vulnerabilities instead of opening a public issue.
- [ ] Add repository topics and confirm the description on GitHub for discoverability.
- [ ] Decide on a versioning policy before the first release.

## Deferred / to revisit

- [ ] Reconsider whether `appsettings.Development.json` should stay tracked. It is
      currently committed and contains only log levels, which is standard for the
      .NET template. The risk is that it is an already-tracked file in a public repo,
      so a local secret added to it would commit silently. `appsettings.Local.json`
      and `.env` are gitignored and are the intended homes for local secrets.
