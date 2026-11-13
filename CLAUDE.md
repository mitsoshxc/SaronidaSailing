# Saronida Sailing

Public website for a sailing business in Saronida, Greece, with a custom built-in CMS. Page layouts and sections are fixed in code; the **content** of each section (texts, images) is data entered by the admin. An admin logs in, sees the site exactly as a visitor does, and edits each section in place (one or more admins, created with the `saronida-admin` CLI).

Stack: ASP.NET Core 10 Razor Pages · Entity Framework Core 10 · PostgreSQL · ASP.NET Core Identity · Output Caching · Hangfire (PostgreSQL storage) · SkiaSharp · NLog · HtmlSanitizer · Quill 2 · SortableJS · plain JS/CSS (no frameworks) · xUnit · Testcontainers · Playwright + axe-core · Docker · k3s · nginx · DigitalOcean · Cloudflare (Free). Full table with reasons: `docs/architecture.md` → Tech Stack.

**Infrastructure changed on 2026-11-11** (Phase 4.5): SQLite → PostgreSQL; Docker Compose → k3s on one $12 droplet with one app pod, built to run two (cross-pod cache eviction, shared logs with the pod name); backups pulled to the developer's PC instead of DO backups. Local development needs Docker Desktop running: `docker compose -f deploy/dev/docker-compose.yml up -d` starts the dev PostgreSQL, and the tests start their own via Testcontainers.

Languages: Greek (default, no URL prefix) and English (`/en/`). Pages and sections come from the "Saronida Sailing Redesign" design canvas: https://claude.ai/artifact/RcU5iMfQSTJ34rH5gFB7MC (read it with the Artifact tool; desktop artboards `Desktop*.dc.html` hold the full layouts, the canvas notes hold design decisions).

Project status: **Phase 5 (Media) is complete except the real-iPhone test (Phase 8); Phase 6 (Instagram Feed) is next**. Docs mark what is implemented and what is still agreed design; `PLAN.md` tracks the rest of the build.

## Before You Start (working agreements)

- **People:** the developer (senior .NET back-end dev, works with Claude as tech lead) and the business owner (answers in [`OWNER-QUESTIONS.md`](OWNER-QUESTIONS.md)).
- **Commit and push directly to `main`.** No feature branches, no PRs unless the developer asks.
- **Never decide on your own** what hasn't been agreed — propose, ask, then record the decision in `docs/` / `PLAN.md`.
- **Development and testing happen on the developer's PC.** Cloud sessions: the "SaronidaSailing" environment installs the .NET 10 SDK via its setup script; there is no Docker daemon in the cloud, so the `Dockerfile` and k3s manifests are only built and run locally, and the database tests (Testcontainers PostgreSQL) run only on the developer's PC.
- **Environments:** local → **preview** at `https://saronida.visualpivot.net` (password-protected, never crawlable, for showing the owner) → production at `https://www.saronidasailing.gr` on **the same droplet** (preview features removed, DNS switched). See `docs/deployment.md` → Preview.
- **Verify during implementation** (flagged in the docs): iPhone HEIC upload (Phase 5, tested on the Phase 8 preview), Meta app development mode (Phase 6, with the developer's own Instagram account through an ngrok tunnel).

## Emergency: take the site offline

On the droplet: `touch /var/saronida/maintenance.on` (ON) · `rm /var/saronida/maintenance.on` (OFF). nginx serves a 503 "Back soon" page immediately. More in [`docs/deployment.md`](docs/deployment.md) → Quick Actions.

## Team — READ FIRST

| Agent | Role | Color | Model |
|---|---|---|---|
| **Dev** | Full-stack developer. Implements task briefs from the tech lead across C#, Razor, EF Core, CSS and JS. Enforces conventions from `docs/`. Runs `dotnet build` to verify. **All code goes through Dev.** | Green | Sonnet (default) · Haiku for narrow mechanical briefs |
| **Doc** | Documentation specialist. Exclusive owner of `docs/*.md`. Updates or creates docs once per phase, after every task in the phase is approved. Never touches production code. **All doc updates go through Doc.** | Blue | Sonnet (default) · Haiku for fully-specified row-level briefs |

**Dev model selection.** Default Sonnet. Tech lead may override to Haiku for narrow, mechanical briefs — small refactors, view tweaks, single-method additions, rename-only changes. Stay on Sonnet for anything touching the domain model, caching, auth, the CMS editing flow, multi-file changes, or convention-sensitive work.

**Doc model selection.** Default Sonnet. Haiku only when the brief is fully specified at the row level (exact section, exact rewrite, what to keep, what to drop).

### Workflow — MANDATORY, NO EXCEPTIONS

**You = tech lead. You plan, brief, review, and approve. Dev executes. NEVER write production code directly.**

| Step | Action |
|---|---|
| New feature or entity | Tech lead designs the approach → writes a concrete brief → `/dev <brief>` → review Dev's report → `/verify <brief>` → if approved: flip `PLAN.md` task `[ ]` → `[x]` → commit |
| Bug fix | Tech lead identifies root cause → writes a scoped brief → `/dev <brief>` → `/verify <brief>` → if approved: flip `PLAN.md` task → commit |
| Rework | Tech lead writes a correction brief → `/dev <brief>` → `/verify <brief>` → if approved: flip `PLAN.md` task → commit |
| Phase complete | When every task in a phase is `[x]`: `/doc <phase summary>` — one Doc call covering the whole phase |

**NEVER skip Dev for code. NEVER skip Doc after a phase is fully approved. NEVER make architecture decisions inside Dev — bring them back to the tech lead.**

## Skills

| Skill | Invoked by | When to use |
|---|---|---|
| `/dev <brief>` | Tech lead | Delegate an implementation task to the Dev agent |
| `/verify <brief>` | Tech lead | Verify Dev's output against the brief, conventions, build, and tests |
| `/doc <phase summary>` | Tech lead | Delegate phase-level documentation updates to the Doc agent after every task in the phase is approved |

## Implementation Plan

**`PLAN.md` is the single source of truth for what to build and in what order.** The tech lead reads it to write `/dev` briefs; Dev reads it for context and file targets. After each approved task, flip its status `[ ]` → `[x]`.

## Docs

| Doc | Contents |
|---|---|
| [`docs/architecture.md`](docs/architecture.md) | Tech stack and why, pages, localization, caching (incl. cross-pod eviction), CMS editing flow, media pipeline, Instagram feed, background jobs, logging and diagnostics, security, accessibility, owner-provided open items |
| [`docs/domain-model.md`](docs/domain-model.md) | Entities, fields, keys, indexes, content key format |
| [`docs/conventions.md`](docs/conventions.md) | Layering, naming, EF Core / PostgreSQL portability rules, caching rules, views, CSS, JS |
| [`docs/deployment.md`](docs/deployment.md) | Production infrastructure: droplet, k3s, PostgreSQL, nginx, Let's Encrypt, Cloudflare, deploy script, backups, crash diagnostics, scaling to two pods |
| [`docs/workflow.md`](docs/workflow.md) | Team structure, mandatory step sequence, skills, branch strategy, doc cadence |
| [`OWNER-QUESTIONS.md`](OWNER-QUESTIONS.md) | Open questions for the business owner (domain, prices, photos, legal, Instagram) |
