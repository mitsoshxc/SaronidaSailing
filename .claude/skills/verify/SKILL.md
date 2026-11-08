---
name: verify
description: Tech lead verification of Dev agent output. Checks the implementation against the original brief and project conventions, runs build and tests, and returns an APPROVED / NEEDS REWORK verdict.
---

# Verify Dev Agent Output

The **tech lead** invokes `/verify <original brief>` right after Dev reports completion, with the same brief that was passed to `/dev`.

## Steps — do all, in order

### 1. Read the brief
`$ARGUMENTS` is the ground truth.

### 2. Inspect all changes
```bash
git status
git diff HEAD
```
Read every created, modified, or deleted file in full (`git status` shows untracked new files that `git diff` doesn't).

### 3. Spec compliance
Flag missing pieces, pieces done differently than specified, and additions beyond the brief (scope creep).

### 4. Convention compliance
Read `docs/conventions.md` and check, where applicable:

- [ ] Files are in the layer/folder `docs/conventions.md` assigns them
- [ ] No entities passed to pages/views — ViewModels (`record`) only
- [ ] No business logic in page models or endpoints
- [ ] No `AppDbContext` access outside `Infrastructure`
- [ ] EF Core portability: LINQ only, UTC `DateTime`, no `decimal` comparisons in queries
- [ ] New/changed entities have a migration
- [ ] Every content write (fields and list operations) evicts `page:{pageKey}` (or `pages:all` for `shared`)
- [ ] Repeating blocks use `<cms-list>`; inactive items hidden from visitors
- [ ] Hangfire jobs idempotent; failures keep the last good state
- [ ] Pages set `<html lang>`, canonical, `hreflang`; Greek unprefixed, English `/en/`
- [ ] Link URLs limited to `https:`, `tel:`, `mailto:`
- [ ] Accessibility (WCAG 2.2 AA): real buttons/links, focus outline, contrast, alt text, `aria-label` on icon buttons
- [ ] Fonts self-hosted
- [ ] `UseOutputCache()` still after `UseAuthentication()` / `UseAuthorization()`
- [ ] Rich text sanitized before storage
- [ ] Editable content rendered via the `<cms*>` tag helpers, not hardcoded
- [ ] Admin-only markup/scripts gated on authentication; antiforgery on write endpoints
- [ ] CSS mobile-first (no `max-width` queries, breakpoints 720/960); JS has no `var`
- [ ] No "what" comments, debug logs, or commented-out code

### 5. Build
```bash
dotnet build SaronidaSailing.slnx
```
Must be **0 errors, 0 warnings**.

### 6. Tests
```bash
dotnet test SaronidaSailing.slnx
```
All tests must pass. (Before the test project exists, report "no tests yet".)

## Report

```
VERIFICATION REPORT
===================
Brief:              <one line>
Spec match:         Pass | Fail — <missing/incorrect items, or "complete">
Convention checks:  Pass | Fail — <violations, or "none">
Build:              Pass | Fail — <summary>
Tests:              Pass | Fail — <N passed, N failed>

Verdict:            APPROVED | NEEDS REWORK

<If NEEDS REWORK: exactly what to fix — file path, what is wrong, what is expected.>
```

If **APPROVED**, end with:

```
Next: flip the task in PLAN.md to [x] and commit. If this completes the phase, run `/doc <phase summary>`.
```

If **NEEDS REWORK**, the tech lead writes a correction brief and re-runs `/dev`, then `/verify`.

## Hard bans

- **NEVER** approve with a failing build, failing tests, convention violations, an incomplete spec, or scope creep.
