# Saronida Sailing — Implementation Plan

Status legend: `[ ]` not started · `[x]` complete · `[~]` in progress

Every `/dev` brief is followed by `/verify`. A task is flipped to `[x]` only after `/verify` returns APPROVED. `/doc` runs once per phase, after all its tasks are `[x]`.

Phases below are a draft outline. The tech lead breaks each task into concrete briefs (file targets, acceptance criteria) before starting the phase.

---

## Phase 0 — Open Decisions

- [ ] Online bookings / payments: yes or no
- [ ] Languages (Greek + English?)
- [ ] Site map: which pages and which sections per page
- [ ] Image processing library
- [ ] Contact form email provider
- [ ] Backup approach
- [ ] Deploy image transfer: registry or `docker save | ssh`

## Phase 1 — Foundation

- [ ] Solution `SaronidaSailing.slnx` with `Core`, `Infrastructure`, `Web`, `Tests` projects and references
- [ ] EF Core + SQLite, `AppDbContext`, `BaseEntity` timestamps, WAL mode at startup, migrations applied at startup
- [ ] `/health` endpoint
- [ ] `Dockerfile` (multi-stage, runs as non-root `app` user)

## Phase 2 — Admin Authentication

- [ ] ASP.NET Core Identity (cookie auth), admin seeded from configuration
- [ ] Login page + TOTP 2FA
- [ ] Rate limiting on login; forwarded headers configured

## Phase 3 — CMS Core

- [ ] `ContentItem`, `ContentHistory` entities, configurations, repositories
- [ ] `IContentService` (read by key + locale, save with history)
- [ ] `<cms>` tag helper (visitor rendering)
- [ ] Output cache policy `CmsPage` (1h) + tag eviction on save

## Phase 4 — Inline Editing

- [ ] Admin rendering of `data-cms-*` attributes + `cms-edit.js`
- [ ] Admin content API (save, antiforgery, HtmlSanitizer for rich text)
- [ ] Undo from `ContentHistory`

## Phase 5 — Media

- [ ] `MediaAsset` entity, `IMediaService`, storage under `Media:RootPath`
- [ ] Upload endpoint: validate, resize, WebP, content-hash file names
- [ ] `<cms-image>` tag helper (`srcset` from generated widths) + editor image picker

## Phase 6 — Public Pages

- [ ] Layout, header, footer (shared content), site CSS
- [ ] Page templates per the site map from Phase 0

## Phase 7 — Contact Form

- [ ] Form + email sending (provider from Phase 0), caching workaround for antiforgery

## Phase 8 — Production

- [ ] `deploy/docker-compose.yml`, `deploy/nginx/` site config
- [ ] `deploy/deploy.ps1`
- [ ] Droplet setup (swap, Docker, nginx, certbot), Cloudflare DNS, Full (strict) SSL
- [ ] Backups per Phase 0 decision
