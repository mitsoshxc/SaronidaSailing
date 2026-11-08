---
name: doc
description: Delegate phase-level documentation updates to the isolated Doc agent once every task in a PLAN.md phase is approved. Spawns a fresh subagent on Sonnet with the agent instructions and current docs embedded.
---

# Delegate to Doc Agent

The **tech lead** invokes `/doc <phase summary>` once **every task in the phase is `[x]`** in `PLAN.md`. One call per phase, not per brief.

## Before spawning

1. **Confirm every task in the phase is `[x]` in `PLAN.md`.** If not, stop.
2. **Read these files in full** to embed them:
   - `.claude/agents/doc.md`
   - `CLAUDE.md`
   - `docs/architecture.md`
   - `docs/domain-model.md`
   - `docs/conventions.md`
   - `docs/deployment.md`
   - `docs/workflow.md`
3. **Note the phase summary** (`$ARGUMENTS`) and the phase's commit range (`git log --oneline`), so Doc can diff it.

## Spawning

Use the Agent tool with:
- `subagent_type`: `"claude"`
- `model`: `"sonnet"`
- `description`: e.g. "update docs after Phase 3"
- **No `isolation` parameter** — Doc edits the working tree so the tech lead can review with `git diff docs/`.
- `prompt`:

```
[Full contents of .claude/agents/doc.md]

---

## Current Documentation (injected by /doc skill)

### CLAUDE.md
[Full contents]

### docs/architecture.md
[Full contents]

### docs/domain-model.md
[Full contents]

### docs/conventions.md
[Full contents]

### docs/deployment.md
[Full contents]

### docs/workflow.md
[Full contents]

---

## Phase Summary

[$ARGUMENTS verbatim]

Commit range: <first-commit>^..HEAD
```

## After Doc completes

Present Doc's `DOCUMENTATION REPORT` to the tech lead, who reviews `git diff docs/` and commits.

## Hard bans

- **NEVER** invoke Doc before the whole phase is approved.
- **NEVER** paraphrase the docs — embed them verbatim.
- **NEVER** pass `isolation: "worktree"`.
