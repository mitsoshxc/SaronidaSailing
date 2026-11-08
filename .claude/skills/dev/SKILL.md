---
name: dev
description: Delegate an implementation task to the isolated Dev agent. Reads the project docs, spawns a fresh subagent on Sonnet with the agent instructions and docs embedded, and passes the tech lead's brief verbatim. The Dev agent has no session history.
---

# Delegate to Dev Agent

The **tech lead** invokes `/dev <brief>` after deciding the approach. The brief must be concrete — architecture decisions belong to the tech lead.

## Before spawning

1. **Read the brief** (`$ARGUMENTS`). If it is ambiguous about file placement, architecture, or scope, **stop and ask the tech lead**.
2. **Confirm the brief is a single, well-scoped step.** If it spans several logical changes, ask the tech lead to split it.
3. **Read these files in full** to embed them:
   - `.claude/agents/dev.md`
   - `docs/conventions.md`
   - `docs/architecture.md`
   - `docs/domain-model.md`
   - `docs/deployment.md` — only if the brief touches `deploy/`, `Dockerfile`, or hosting config

## Spawning

Use the Agent tool with:
- `subagent_type`: `"claude"`
- `model`: `"sonnet"` (the tech lead may say `"haiku"` for narrow mechanical briefs)
- `description`: 5–10 word summary of the brief
- **No `isolation` parameter** — Dev edits the working tree directly so the tech lead can review with `git status` / `git diff`.
- `prompt`:

```
[Full contents of .claude/agents/dev.md]

---

## Project Reference (injected by /dev skill)

### Conventions
[Full contents of docs/conventions.md]

### Architecture
[Full contents of docs/architecture.md]

### Domain Model
[Full contents of docs/domain-model.md]

[### Deployment — only when relevant
 Full contents of docs/deployment.md]

---

## Tech Lead Brief

[$ARGUMENTS — verbatim]
```

## After Dev completes

Present Dev's `IMPLEMENTATION REPORT` to the tech lead, then:

```
Dev has finished. Run `/verify <original brief>` to validate the implementation.
```

## Hard bans

- **NEVER** spawn Dev with an ambiguous brief.
- **NEVER** paraphrase the docs or the brief — embed them verbatim.
- **NEVER** split one brief across multiple Dev invocations.
- **NEVER** pass `isolation: "worktree"`.
