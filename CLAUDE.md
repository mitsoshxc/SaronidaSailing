# Saronida Sailing

Public website for a sailing business in Saronida, Greece, with a custom built-in CMS. Page layouts and sections are fixed in code; the **content** of each section (texts, images) is data entered by the admin. An admin logs in, sees the site exactly as a visitor does, and edits each section in place (one or more admins, created with the `saronida-admin` CLI).

Stack: ASP.NET Core 10 Razor Pages · Entity Framework Core 10 · SQLite · ASP.NET Core Identity · Output Caching · Hangfire (SQLite storage) · SkiaSharp · Docker · nginx · DigitalOcean · Cloudflare (Free)

Languages: Greek (default, no URL prefix) and English (`/en/`). Pages and sections come from the "Saronida Sailing Redesign" design canvas.

Project status: **pre-code**. Docs describe the agreed design; `PLAN.md` tracks the build.

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
| [`docs/architecture.md`](docs/architecture.md) | Tech stack and why, pages, localization, caching, CMS editing flow, media pipeline, Instagram feed, background jobs, security, accessibility, owner-provided open items |
| [`docs/domain-model.md`](docs/domain-model.md) | Entities, fields, keys, indexes, content key format |
| [`docs/conventions.md`](docs/conventions.md) | Layering, naming, EF Core / SQLite portability rules, caching rules, views, CSS, JS |
| [`docs/deployment.md`](docs/deployment.md) | Production infrastructure: droplet, Docker, nginx, Let's Encrypt, Cloudflare, deploy script, backups |
| [`docs/workflow.md`](docs/workflow.md) | Team structure, mandatory step sequence, skills, branch strategy, doc cadence |
| [`OWNER-QUESTIONS.md`](OWNER-QUESTIONS.md) | Open questions for the business owner (domain, prices, photos, legal, Instagram) |
