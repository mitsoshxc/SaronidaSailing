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

## Cloud Sessions

Claude Code cloud sessions for this repo use the personal environment **"SaronidaSailing"**: Default network rules plus `saronidasailing.gr` / `www.saronidasailing.gr` (the old site, used for the redirect map). Its setup script installs the .NET 10 SDK (Ubuntu package `dotnet-sdk-10.0`). No Docker daemon is available in cloud sessions.

## Branch Strategy

Work is committed and pushed directly to `main`. No feature branches, no PRs unless the developer asks. Deployment is manual via `deploy/deploy.ps1` (see `docs/deployment.md`).
