# MonitovoPDF — Session Context

Use this file to orient Claude at the start of a new chat session on this project.
Tell Claude: "Read CONTEXT.md before we start."

---

## What This Project Is

**MonitovoPDF** is a self-hostable HTTP service for generating PDF documents, built on
ASP.NET Core. It is **free and open source under the MIT licence**, published publicly,
and intended to be usable inside commercial products without
per-document licensing costs.

The motivating problem: commercial PDF generation libraries and hosted PDF APIs are
either expensive per document, restrictively licensed for redistribution, or both. A
small permissively-licensed service that does one job well is more useful than another
dependency with a licence audit attached.

See [PLAN.md](PLAN.md) for the product plan and open decisions, and [TODO.md](TODO.md)
for immediate action items.

---

## Current State — read this before assuming anything exists

**The project is at the scaffolding stage.** As of 2026-08-19 it is the bare ASP.NET Core
minimal-API template: a six-line `Program.cs` serving `"Hello World!"` at `/`.

There is **no PDF functionality yet**. No rendering engine has been chosen, no API surface
has been designed, there are no tests and there is no CI. Nothing in this file or in
PLAN.md describes working code — it describes intent. Do not assume an endpoint, service
or abstraction exists because a document mentions it. Check the source.

---

## Repository Location

```
c:\dev\MonitovoPDF\
```

GitHub remote: `https://github.com/jwvalentine/MonitovoPDF` (**public**, MIT licensed)
Default branch: `main`

Because the repository is public, everything committed is world-readable immediately and
remains in history after deletion. See the public-repository section of CLAUDE.md.

---

## Project Structure

```
MonitovoPDF/
├── Program.cs                        # Minimal API entry point (currently Hello World)
├── MonitovoPDF.csproj                # net10.0, nullable + implicit usings enabled
├── appsettings.json                  # Defaults, non-secret structural config
├── appsettings.Development.json      # Local dev overrides, non-secret only
├── Properties/
│   └── launchSettings.json           # Local dev ports (http 5155, https 7255)
├── CLAUDE.md                         # Governing rules for AI agents
├── AGENTS.md                         # Pointer to CLAUDE.md
├── CONTEXT.md                        # This file
├── PLAN.md                           # Product plan and open decisions
├── TODO.md                           # Immediate action items
├── LICENSE                           # MIT, Copyright (c) 2026 Monitovo LLC
├── .gitattributes                    # Line-ending normalisation
└── .gitignore                        # Standard .NET ignores + project-specific section
```

No source directories exist yet. The layout for real code is an open decision — see PLAN.md.

---

## Tech Stack

| Concern | Choice |
|---|---|
| Runtime | .NET 10 (`net10.0`) |
| Web framework | ASP.NET Core minimal APIs |
| PDF engine | **Not yet chosen** — see PLAN.md |
| Tests | **Not yet set up** — framework undecided |
| CI | **Not yet set up** |
| Licence | MIT |

---

## Build & Run

```bash
cd c:\dev\MonitovoPDF
dotnet build          # currently 0 warnings, 0 errors
dotnet run            # listens on http://localhost:5155
curl http://localhost:5155/
```

`dotnet` commands are fine to run locally. **Do not run `npm`, `yarn` or `npx` on Joe's
machine** — use Docker if Node tooling is ever needed.

The `gh` CLI is installed but not on `PATH`:
```bash
export PATH="$PATH:/c/Program Files/GitHub CLI"
```

---

## Working Agreements

- Work on a `feature/` or `fix/` branch, open a PR against `main`, and let Joe merge.
  No direct commits to `main`, no stacked PRs, one at a time.
- Never commit without stating the branch, the staged files and the commit message first,
  then waiting for explicit confirmation.
- Never mention Claude, AI or any AI tooling in commits, PRs, issues or comments.
