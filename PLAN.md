# Saronida Sailing — Implementation Plan

Status legend: `[ ]` not started · `[x]` complete · `[~]` in progress

Reviewed phase by phase and agreed on 2026-11-08. Open owner-provided items: [`OWNER-QUESTIONS.md`](OWNER-QUESTIONS.md) — they do not block development.

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

- [x] Solution `SaronidaSailing.slnx`: `Core`, `Infrastructure`, `Web`, `Tests` projects and references
- [x] EF Core + SQLite: `AppDbContext`, `BaseEntity` timestamps, WAL mode, migrations applied at startup
- [x] Localization: routing (`el` unprefixed, `en` under `/en/`), request culture, helper for the other-language URL
- [x] Hangfire with `Hangfire.Storage.SQLite` (`hangfire.db`)
- [x] `/health` endpoint: DB reachable, Hangfire storage, media folder writable, home page GET (see `docs/architecture.md` → Health Check). The "Greek home content exists" check is added in Phase 7 with the seed data (decided 2026-11-08)
- [x] `Dockerfile` (multi-stage, pinned base images, non-root) + repo `nuget.config` with nuget.org as the only source (decided 2026-11-09; makes local and Docker restores independent of machine-level feeds)

## Phase 2 — Admin Authentication

Approved design (2026-11-09): Identity tables live in `saronida.db` (`AppDbContext` becomes an `IdentityDbContext<ApplicationUser>`; `ApplicationUser` in `Core/Entities`, so `Core` references `Microsoft.Extensions.Identity.Stores`). Security stamp re-validated every 1 minute. One `Admin` authorization policy = authenticated **and** 2FA enabled; a signed-in admin without 2FA can only reach the 2FA setup page. No "remember this browser" — the TOTP code is asked on every login. Logout is a POST from `/admin/logout`. Audit events go through `IAuditLog` (`Core/Interfaces`) to the NLog logger `Audit`, timestamps UTC. Forwarded headers: the app trusts the Docker compose network's fixed subnet `172.30.0.0/24` (`ForwardedHeaders:KnownNetworks`; the Phase 8 compose file must pin this subnet), **not** `127.0.0.1` — nginx on the host reaches the container through the published port, so requests arrive from the network gateway. The `saronida-admin` host wrapper ships with `deploy/` in Phase 8. Lost phone with no recovery codes left: `admin remove` + `admin add` (fresh 2FA setup at next login); there is no 2FA-reset command (decided 2026-11-09). Build order: NLog first, so later tasks can audit.

- [x] ASP.NET Core Identity (cookie auth): 12h sliding session, `Secure`/`HttpOnly`/`SameSite=Strict`, Data Protection keys persisted
- [x] Admin CLI commands: `admin add` / `reset-password` / `list` / `remove` (password prompted, hidden). Password policy: minimum 12 characters, no character-class rules (decided 2026-11-09)
- [x] `/admin/login` + mandatory TOTP 2FA setup with recovery codes; logout. Admin-only pages are English only; QR code rendered server-side as inline SVG with QRCoder (decided 2026-11-09)
- [x] Lockout (5 tries → 15 min) + per-IP login rate limiting (login and 2FA-code POSTs: 10 per minute per IP, fixed window, 429; decided 2026-11-09); forwarded headers
- [x] NLog: `audit-*.log` + `app-*.log` in `/var/saronida/logs`, 90-day retention
- [x] Hangfire dashboard at `/admin/jobs`, admin-only

## Phase 3 — CMS Core

Approved design (2026-11-09): `ContentItem` uniqueness uses two filtered unique indexes — (`PageKey`, `SectionKey`, `FieldKey`, `Locale`) where `ListItemId IS NULL`, and (`ListItemId`, `FieldKey`, `Locale`) where `ListItemId IS NOT NULL`; a list item's fields carry the list's `PageKey` and the `ListKey` as `SectionKey`. `ContentHistory` stores one row per saved version (`ContentItemId`, `Value`, `ChangedBy`), the first save included; `CreatedAt` is the change time (no `ChangedAt`, no `PreviousValue`). Deleting a list item cascades to its fields and their history. Repositories only stage changes; `IUnitOfWork.SaveChangesAsync` commits them in one transaction (Phase 4's Save page). Reads: one query per request loads a page's content plus `shared` for the locale and the Greek fallback into a scoped snapshot that the tag helpers read. List items have no locale (their fields do). Output-cache eviction is called from `IContentService` through a Core interface implemented in `Web`.

- [x] Entities + configurations + repositories: `ContentItem`, `ContentListItem`, `ContentSection`, `ContentHistory`. `ContentItem.UpdatedBy` and `ContentHistory.ChangedBy` store the admin's **email** as plain text, no FK to `ApplicationUser` — history survives `admin remove` and continues when an admin is re-added with the same email (decided 2026-11-09; supersedes the FK in `docs/domain-model.md`, which Doc updates at the end of Phase 3)
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
- [ ] Seed initial content from the design (Greek) + drafted English translations for owner review; add the `/health` check "at least one Greek `home` `ContentItem` exists"
- [ ] Routes with fixed slugs (`/ekdromes`, `/skafos`, `/epikoinonia`, `/oroi`, `/aporrito`, `/prosvasimotita`; `/en/trips`, `/en/boat`, `/en/contact`, `/en/terms`, `/en/privacy`, `/en/accessibility`) — add pages to `SitePages`; test that cross-language slugs (`/en/skafos`, `/boat`) return 404
- [ ] `sitemap.xml`, `robots.txt`, canonical + `hreflang`, 404 / 500 pages
- [ ] JSON-LD `LocalBusiness` from content
- [ ] Old-site 301 redirect table (see `docs/architecture.md` → SEO) + canonical-host redirect
- [ ] Accessibility: axe-core checks via Playwright on every page in both languages; manual keyboard pass

## Phase 8 — Preview & Production

- [ ] Preview on the future production droplet at `https://saronida.visualpivot.net`: Basic Auth (one owner login, htpasswd on the droplet), `X-Robots-Tag: noindex, nofollow`, `robots.txt` disallow-all, no sitemap, `Site:BaseUrl` = preview URL; no DO backups until go-live

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
- [ ] Go-live: remove preview features (Basic Auth, noindex, Preview mode) → test via hosts file → copy all DNS records to Cloudflare → lower TTL → switch → verify → keep old site a few days → submit sitemap
