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
- [x] Repeating blocks: editable lists (add / delete / reorder / active-inactive); sections can be deactivated too
- [x] Analytics: Cloudflare Web Analytics (cookieless)
- [x] Google Map embed with a cookie consent banner (Necessary + External content)
- [x] Greek slugs transliterated and fixed in code; old site URLs mapped to 301 redirects
- [x] Instagram feed included at launch; fetch twice a day
- [x] Background jobs: Hangfire with SQLite storage

## Phase 1 — Foundation

- [ ] Solution `SaronidaSailing.slnx`: `Core`, `Infrastructure`, `Web`, `Tests` projects and references
- [ ] EF Core + SQLite: `AppDbContext`, `BaseEntity` timestamps, WAL mode, migrations applied at startup
- [ ] Localization: routing (`el` unprefixed, `en` under `/en/`), request culture, helper for the other-language URL
- [ ] Hangfire with `Hangfire.Storage.SQLite` (`hangfire.db`)
- [ ] `/health` endpoint: DB + Greek home content, Hangfire storage, media folder writable, home page GET (see `docs/architecture.md` → Health Check)
- [ ] `Dockerfile` (multi-stage, pinned base images, non-root)

## Phase 2 — Admin Authentication

- [ ] ASP.NET Core Identity (cookie auth): 12h sliding session, `Secure`/`HttpOnly`/`SameSite=Strict`, Data Protection keys persisted
- [ ] Admin CLI commands: `admin add` / `reset-password` / `list` / `remove` (password prompted, hidden)
- [ ] `/admin/login` + mandatory TOTP 2FA setup with recovery codes; logout
- [ ] Lockout (5 tries → 15 min) + per-IP login rate limiting; forwarded headers
- [ ] NLog: `audit-*.log` + `app-*.log` in `/var/saronida/logs`, 90-day retention
- [ ] Hangfire dashboard at `/admin/jobs`, admin-only

## Phase 3 — CMS Core

- [ ] Entities + configurations + repositories: `ContentItem`, `ContentListItem`, `ContentSection`, `ContentHistory`
- [ ] `IContentService`: read by key + locale (English falls back to Greek when missing), save field with history (kept forever), list operations (add / delete / reorder / activate), section activate/deactivate
- [ ] Tag helpers: `<cms>`, `<cms-link>`, `<cms-list>` (visitor rendering)
- [ ] Page SEO rendering: `<title>`, meta description, Open Graph tags from `{page}.seo.*`
- [ ] Output cache policy `CmsPage` (1h) + tag eviction on every write

## Phase 4 — Inline Editing

- [ ] Admin rendering: `data-cms-*` attributes, dimmed inactive items, admin toolbar (edit mode on/off, Save page, Discard, unsaved counter, logout)
- [ ] Pending-changes model in `cms-edit.js` + unsaved-changes warning
- [ ] Save-page API: all pending changes in one request, one transaction, one cache eviction; antiforgery, HtmlSanitizer, link URL validation
- [ ] Modals (`<dialog>`): rich text (Quill 2, self-hosted), link, history, Page settings
- [ ] List editing: add, delete, drag-to-reorder (SortableJS) + up/down buttons, active/inactive toggle
- [ ] Section active/inactive toggle (hides section and its menu links)
- [ ] Undo from `ContentHistory`
- [ ] "Missing EN" markers; Page settings modal for `{page}.seo.*`
- [ ] Phone layout for quick fixes (collapsed toolbar, full-screen modals)

## Phase 5 — Media

- [ ] `MediaAsset` entity, `IMediaService`, storage under `Media:RootPath`
- [ ] SkiaSharp pipeline: validate (JPEG/PNG/WebP, 25 MB, 50 MP), EXIF orientation, strip metadata, WebP 480/960/1600/2400 q~80, content-hash file names; original kept privately
- [ ] `<cms-image>` tag helper (`srcset`, `alt-source`)
- [ ] Image modal: upload new / choose existing (media library), required alt text pre-filled from the nearest title
- [ ] Test an upload from a real iPhone (HEIC → JPEG conversion)

## Phase 6 — Instagram Feed

- [ ] `InstagramPost`, `IntegrationSetting` entities; Instagram API client
- [ ] "Connect Instagram" OAuth flow (admin), long-lived token stored; app secret from droplet environment only
- [ ] Token storage + weekly refresh job
- [ ] Fetch job twice a day: store latest 12 posts (cover image for video/reels, first photo for carousels), download thumbnails through the media pipeline, evict `page:home`
- [ ] Feed section rendering: admin-set post count (1–12, default 6), last good posts on failure
- [ ] "Instagram disconnected — reconnect" warning in the admin toolbar
- [ ] Section switch: deactivating `home.instagram` removes the recurring jobs, activating re-registers them; startup registers them only when active

## Phase 7 — Public Pages

- [ ] Layout: header (logo, menu, language switch, Book button), footer — shared content
- [ ] Site CSS from the design canvas; self-hosted Commissioner font
- [ ] Pages: Home, Trips (tabs remember the chosen trip), Boat, Contact, Terms, Privacy, Accessibility
- [ ] Cookie consent banner (client-side, `localStorage`), footer "Cookie settings", Google Map loaded only after consent with static-image fallback
- [ ] Seed initial content from the design (Greek) + drafted English translations for owner review
- [ ] Routes with fixed slugs (`/ekdromes`, `/skafos`, `/epikoinonia`, `/oroi`, `/aporrito`, `/prosvasimotita`; `/en/trips`, `/en/boat`, `/en/contact`, `/en/terms`, `/en/privacy`, `/en/accessibility`)
- [ ] `sitemap.xml`, `robots.txt`, canonical + `hreflang`, 404 / 500 pages
- [ ] JSON-LD `LocalBusiness` from content
- [ ] Old-site 301 redirect table (see `docs/architecture.md` → SEO) + canonical-host redirect
- [ ] Accessibility: axe-core checks via Playwright on every page in both languages; manual keyboard pass

## Phase 8 — Production

- [ ] `deploy/docker-compose.yml`, `deploy/nginx/` site config (incl. `/health` denied externally)
- [ ] nginx maintenance mode: `/var/saronida/maintenance.on` → 503 + `deploy/nginx/maintenance.html`
- [ ] `deploy/deploy.ps1`
- [ ] Nightly `VACUUM INTO` backup job
- [ ] Droplet setup: hardening, swap, Docker, nginx, certbot, firewall, `saronida-admin` wrapper script
- [ ] Cloudflare: DNS (apex + www, proxied), Full (strict) SSL, Always Use HTTPS, Web Analytics
- [ ] Canonical redirects: http → https and no-www → `https://www.saronidasailing.gr` in one hop (nginx); certificate for both names
- [ ] Enable DO weekly backups
- [ ] Security headers (CSP incl. Google Maps frame, nosniff, Referrer-Policy, frame-ancestors); HSTS after launch, short max-age first
- [ ] `unattended-upgrades` with nightly auto-reboot; container `restart: unless-stopped`
- [ ] UptimeRobot monitor (alerts to the developer's email)
- [ ] Go-live: test via hosts file → copy all DNS records to Cloudflare → lower TTL → switch → verify → keep old site a few days → submit sitemap
