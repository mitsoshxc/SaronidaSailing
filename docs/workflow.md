# Workflow

## Team

| Agent | Role | Owner of |
|---|---|---|
| **Tech lead** (main session) | Plans, writes briefs, reviews, approves | Architecture decisions, `PLAN.md` status, commits |
| **Dev** | Implements briefs | All production code (`src/`, `tests/`, `deploy/`, `Dockerfile`) |
| **Doc** | Writes documentation after a phase is approved | All files in `docs/` |

**Never write production code directly as tech lead. Never skip Doc after a fully approved phase.**

---

## Mandatory Step Sequence

```
1. Tech lead writes brief
2. /dev <brief>          — Dev implements and runs dotnet build
3. /verify <brief>       — returns APPROVED or NEEDS REWORK
4. If APPROVED:
     a. Update PLAN.md task status [ ] → [x]
     b. Commit
     c. (After all tasks in the phase are done) /doc <phase summary>
5. If NEEDS REWORK:
     Tech lead writes correction brief → back to step 2
```

`/doc` runs once per phase, not after every brief.

---

## Skills

| Skill | Used by | Effect |
|---|---|---|
| `/dev <brief>` | Tech lead | Delegates implementation to the Dev agent. Dev runs with no session history; the skill embeds the agent instructions and docs in its prompt. |
| `/verify <brief>` | Tech lead | Checks Dev's output against the brief, conventions, build, and tests. Returns APPROVED or NEEDS REWORK. |
| `/doc <phase summary>` | Tech lead | Delegates documentation updates to the Doc agent after a phase is approved. |

---

## PLAN.md

Single source of truth for what to build. The tech lead maintains task status. Doc edits `PLAN.md` only on explicit row-level instructions.

Status legend: `[ ]` not started · `[x]` complete · `[~]` in progress.

---

## Where Work Happens

Development and testing run on the developer's PC (`dotnet run`, local Docker). Then: preview droplet for the owner → production. See `docs/deployment.md`.

Local development:

1. `dotnet tool restore` once — installs `dotnet-ef` from `.config/dotnet-tools.json`.
2. `dotnet restore` / `dotnet build` work independently of machine-level NuGet feeds: the repo `nuget.config` clears them and uses nuget.org only.
3. `dotnet run --project src/SaronidaSailing.Web` serves http://localhost:5000 (Development environment, from `launchSettings.json`).
4. Development data (content DB, `hangfire.db`, media) goes to `src/SaronidaSailing.Web/data/`, which is gitignored; delete the folder to start fresh. The media folder is created automatically in Development only.
5. `dotnet test` runs the xUnit tests (see `docs/conventions.md` -> Tests).
6. To try the editing UI before real pages exist, sign in at `https://localhost:5001/admin/login` and open `/dev/cms-sandbox` (or `/en/dev/cms-sandbox`), a Development-only page (404 elsewhere). The editing UI is verified through the Claude browser there, not with Playwright (see `docs/architecture.md` -> CMS Editing Flow).

## Cloud Sessions

Claude Code cloud sessions for this repo use the personal environment **"SaronidaSailing"**: Default network rules plus `saronidasailing.gr` / `www.saronidasailing.gr` (the old site, used for the redirect map). Its setup script installs the .NET 10 SDK (Ubuntu package `dotnet-sdk-10.0`). No Docker daemon is available in cloud sessions.

## Branch Strategy

Work is committed and pushed directly to `main`. No feature branches, no PRs unless the developer asks. Deployment is manual via `deploy/deploy.ps1` (see `docs/deployment.md`).
