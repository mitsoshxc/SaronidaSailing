# Saronida Sailing — Implementation Plan

Status legend: `[ ]` not started · `[x]` complete · `[~]` in progress

**DRAFT — under review with the owner, phase by phase.**

Phases are the build order; the whole site goes live once, after all phases are done. Every `/dev` brief is followed by `/verify`. A task is flipped to `[x]` only after `/verify` returns APPROVED. `/doc` runs once per phase, after all its tasks are `[x]`. The tech lead breaks each task into concrete briefs before starting the phase.

---

## Phase 0 — Decisions

- [x] Bookings: external platforms only — the site links out
- [x] Languages: Greek (default, no URL prefix) + English (`/en/`)
- [x] SEO: `sitemap.xml` + `robots.txt`
- [x] Image processing: SkiaSharp; images served from the droplet
- [x] No contact form, no email provider
- [x] Backups: DO weekly backups (FRA1) + nightly `VACUUM INTO`
- [x] Deploys: build on the developer's PC, `docker save | ssh docker load`
- [x] Pages and sections: the "Saronida Sailing Redesign" design canvas
- [x] Repeating blocks: editable lists (add / delete / reorder / active-inactive)
- [x] Analytics: Cloudflare Web Analytics; no cookie banner
- [x] Instagram feed included at launch; fetch twice a day
- [x] Background jobs: Hangfire with SQLite storage

## Phase 1 — Foundation

- [ ] Solution `SaronidaSailing.slnx`: `Core`, `Infrastructure`, `Web`, `Tests` projects and references
- [ ] EF Core + SQLite: `AppDbContext`, `BaseEntity` timestamps, WAL mode, migrations applied at startup
- [ ] Localization: routing (`el` unprefixed, `en` under `/en/`), request culture, helper for the other-language URL
- [ ] Hangfire with `Hangfire.Storage.SQLite` (`hangfire.db`)
- [ ] `/health` endpoint
- [ ] `Dockerfile` (multi-stage, pinned base images, non-root)

## Phase 2 — Admin Authentication

- [ ] ASP.NET Core Identity (cookie auth), admin seeded from configuration
- [ ] Login page + TOTP 2FA
- [ ] Login rate limiting; forwarded headers
- [ ] Hangfire dashboard at `/admin/jobs`, admin-only

## Phase 3 — CMS Core

- [ ] Entities + configurations + repositories: `ContentItem`, `ContentListItem`, `ContentHistory`
- [ ] `IContentService`: read by key + locale, save field with history, list operations (add / delete / reorder / activate)
- [ ] Tag helpers: `<cms>`, `<cms-link>`, `<cms-list>` (visitor rendering)
- [ ] Output cache policy `CmsPage` (1h) + tag eviction on every write

## Phase 4 — Inline Editing

- [ ] Admin rendering: `data-cms-*` attributes, dimmed inactive items, `cms-edit.js`
- [ ] Admin content API: fields (text / rich text / link) with antiforgery, HtmlSanitizer, link URL validation
- [ ] List editing: add, delete, drag-to-reorder, active/inactive toggle
- [ ] Undo from `ContentHistory`

## Phase 5 — Media

- [ ] `MediaAsset` entity, `IMediaService`, storage under `Media:RootPath`
- [ ] SkiaSharp pipeline: validate, EXIF orientation, strip metadata, widths, WebP, content-hash file names
- [ ] `<cms-image>` tag helper (`srcset`) + editor image upload/picker

## Phase 6 — Instagram Feed

- [ ] `InstagramPost`, `IntegrationSetting` entities; Instagram API client
- [ ] Token storage + weekly refresh job
- [ ] Fetch job twice a day: store posts, download thumbnails through the media pipeline, evict `page:home`
- [ ] Feed section rendering (last good posts on failure)

## Phase 7 — Public Pages

- [ ] Layout: header (logo, menu, language switch, Book button), footer — shared content
- [ ] Site CSS from the design canvas; self-hosted Commissioner font
- [ ] Pages: Home, Trips (tabs remember the chosen trip), Boat, Contact, Terms, Privacy, Accessibility
- [ ] Seed initial content from the design (Greek)
- [ ] `sitemap.xml`, `robots.txt`, canonical + `hreflang`, 404 / 500 pages
- [ ] Accessibility pass (WCAG 2.2 AA)

## Phase 8 — Production

- [ ] `deploy/docker-compose.yml`, `deploy/nginx/` site config
- [ ] `deploy/deploy.ps1`
- [ ] Nightly `VACUUM INTO` backup job
- [ ] Droplet setup: hardening, swap, Docker, nginx, certbot, firewall
- [ ] Cloudflare: DNS, Full (strict) SSL, Web Analytics
- [ ] Enable DO weekly backups
