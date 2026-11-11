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
- [x] Backups: ~~DO weekly backups (FRA1) + nightly `VACUUM INTO`~~ → nightly `pg_dump` on the droplet, pulled daily by a script on the developer's PC into a Proton Drive folder; no DO backups (changed 2026-11-11, see Phase 4.5)
- [x] Deploys: build on the developer's PC, ~~`docker save | ssh docker load`~~ → `docker save | ssh k3s ctr images import`, no registry (changed 2026-11-11)
- [x] Hosting: k3s (single node) on one DO droplet, 1 vCPU / 2 GiB ($12/mo); one app pod at launch, the app is built to run as two pods (decided 2026-11-11, see Phase 4.5)
- [x] Database: PostgreSQL instead of SQLite (decided 2026-11-11, see Phase 4.5)
- [x] Pages and sections: the "Saronida Sailing Redesign" design canvas
- [x] Repeating blocks: editable lists (add / delete / reorder / active-inactive); sections can be deactivated too
- [x] Analytics: Cloudflare Web Analytics (cookieless)
- [x] Google Map embed with a cookie consent banner (Necessary + External content)
- [x] Greek slugs transliterated and fixed in code; old site URLs mapped to 301 redirects
- [x] Instagram feed included at launch; fetch twice a day
- [x] Background jobs: Hangfire with ~~SQLite~~ PostgreSQL storage (changed 2026-11-11)

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

Write API (decided 2026-11-09): `IContentService` has one batch write method — an ordered list of change records (set field, add / delete / reorder list items, activate list item, activate section) is validated, applied, committed once, then evicted once per touched page (`shared` → `pages:all`) and audited with one line. Any invalid change → nothing is written and the errors are returned. A new list item's Id is generated by the client (`cms-edit.js`), so later changes in the same save can refer to it; the server rejects an empty or already-used Id and appends new items at the end of the list.

Tag helpers (decided 2026-11-09): attributes on real elements instead of `<cms>` wrapper elements — `<h1 cms="home.hero.title">`, `<a cms-link="home.hero.cta">`, a repeating element `<li cms-list="home.highlights">` whose children use `cms-field="title"` / `cms-link-field="link"`, and `<section cms-section="home.instagram">` (added to this task; suppressed when inactive). A field with no value in either language omits its element for visitors (Phase 4 gives the admin a placeholder). Supersedes the element syntax in `docs/architecture.md` / `docs/conventions.md`, which Doc updates at the end of Phase 3.

- [x] Entities + configurations + repositories: `ContentItem`, `ContentListItem`, `ContentSection`, `ContentHistory`. `ContentItem.UpdatedBy` and `ContentHistory.ChangedBy` store the admin's **email** as plain text, no FK to `ApplicationUser` — history survives `admin remove` and continues when an admin is re-added with the same email (decided 2026-11-09; supersedes the FK in `docs/domain-model.md`, which Doc updates at the end of Phase 3)
- [x] `IContentService`: read by key + locale (English falls back to Greek when missing), save field with history (kept forever), list operations (add / delete / reorder / activate), section activate/deactivate
- [x] Tag helpers: `cms`, `cms-link`, `cms-list` (+ `cms-field` / `cms-link-field`), `cms-section` (visitor rendering)
- [x] Page SEO rendering: `<title>`, meta description, Open Graph tags from `{page}.seo.*`
- [x] Output cache policy `CmsPage` (1h) + tag eviction on every write

## Phase 4 — Inline Editing

Agreed (2026-11-09): HtmlSanitizer and link-URL validation run **inside** `ContentService.ApplyChangesAsync`, so every writer (Save endpoint, Phase 7 seed, jobs) is covered. History is **per field, grouped by save**: every `ApplyChangesAsync` call gets one save id stored on its `ContentHistory` rows; a field gets at most one history row per save (the client merges pending edits per field, and a batch with two `SetField` for the same key/locale/list item keeps only the last). The History modal offers both "restore this field's version" and "undo this whole save" (field values only; list structure and section toggles are not in history). A template declares rich text with the `cms-rich` attribute (`<div cms="about.story.body" cms-rich>`); no attribute = plain text. No Playwright in Phase 4: the editing UI is verified by driving the Claude browser, on a Development-only sandbox page `/dev/cms-sandbox` (+ `/en/dev/cms-sandbox`, keys under `shared.sandbox*`, 404 outside Development). Edit mode starts **on** for the admin and is remembered per browser tab (decided 2026-11-09). Editing windows (decided 2026-11-09): in edit mode, focusing or clicking a field shows a small **field bar** next to it ("Edit" for rich text and links, "History"); clicking a rich-text block or a link also opens its editor directly. **Undo this whole save** puts every field of that save back to its earlier value as pending changes and flags the fields a later save changed again (the undo overwrites them). The **link window** has a type picker (Web page / Email / Phone) that adds `https://`, `mailto:` or `tel:` itself, plus a label and "Remove link". Lists and sections (decided 2026-11-09): a list item's tools sit in a small **item bar** at the item's top corner, shown while the pointer or keyboard focus is inside the item: drag handle, Move up, Move down, Hide/Show, Delete. Each list ends with an **Add item** button, which adds an empty item at the end and focuses its first field. **Delete** asks first ("Delete", "Hide instead", "Cancel"; a saved item has no history, so a saved delete can't be undone). The item disappears at once as a pending change, and Discard brings it back. Each section gets an always-visible **section bar** in edit mode with Hide section / Show section. Menu links to a section are marked `cms-section-link="{sectionKey}"` and hide with it; this covers sections of the current page and `shared` only, and any other page key throws at render time. Phone layout (decided 2026-11-11), under 720 px: the toolbar stays at the top in one line (Save page, the unsaved counter, a **More** button), and More opens a panel underneath with the other controls. Editing windows fill the screen. The drag handle is hidden, so items move with the up/down buttons.

- [x] Admin rendering: `data-cms-*` attributes, dimmed inactive items, admin toolbar (edit mode on/off, Save page, Discard, unsaved counter, logout) — Save/Discard/counter are wired by the next task
- [x] Pending-changes model in `cms-edit.js` + unsaved-changes warning (plus inline text editing and Save/Discard wiring)
- [x] Save-page API: all pending changes in one request, one transaction, one cache eviction; antiforgery, HtmlSanitizer, link URL validation (plus history save ids and the history/save read endpoints)
- [x] Modals (`<dialog>`): rich text (Quill 2, self-hosted), link, history, Page settings (opened from a field bar)
- [x] List editing: add, delete, drag-to-reorder (SortableJS) + up/down buttons, active/inactive toggle
- [x] Section active/inactive toggle (hides section and its menu links)
- [x] Undo from `ContentHistory` (restore a field version or undo a whole save, as pending changes)
- [x] "Missing EN" markers; Page settings modal for `{page}.seo.*` (title + description; image is Phase 5)
- [x] Phone layout for quick fixes (collapsed toolbar, full-screen modals)

## Phase 4.5 — PostgreSQL & Two-Pod Readiness

Agreed infrastructure (2026-11-11), supersedes the single-container Docker Compose + SQLite design in `docs/`; Doc updates `architecture.md`, `conventions.md`, `domain-model.md` and `deployment.md` (as agreed design) at the end of this phase.

- **Hosting:** k3s, single node, on one DO Basic droplet, 1 vCPU / 2 GiB ($12/mo + VAT), FRA1, with swap. k3s's bundled Traefik is disabled: **nginx stays on the host** (certbot, `maintenance.on`, `/health` blocked externally) and proxies to the app Service's NodePort; NodePorts are blocked by the firewall.
- **Scale-up path:** resize the droplet (2 vCPU / 4 GiB, $24/mo) and set the app Deployment to `replicas: 2`. The app must be correct with two pods from day one; launch runs one.
- **Database:** PostgreSQL (exact version pinned at implementation), one pod (StatefulSet, 1 replica) inside the cluster, data on the droplet disk under `/var/saronida/pgdata`, never exposed outside the cluster. Not DO Managed Postgres (cost). SQL Server Express was rejected: it needs ≥2 GB RAM on its own.
- **Hangfire:** `Hangfire.PostgreSql` in the same database, own schema `hangfire`. Every pod runs a Hangfire server; its distributed locks keep recurring jobs single. Resolves the "is `Hangfire.Storage.SQLite` maintained" check.
- **Output cache across pods:** each pod keeps its in-memory output cache. `IPageCacheInvalidator` evicts locally and broadcasts the evicted tags with Postgres `NOTIFY`; every pod `LISTEN`s and evicts them. When a pod's listener connects or reconnects it evicts everything (`pages:all`), so a missed message can't leave stale pages. No Redis, no Elasticsearch.
- **Migrations:** no longer applied by every pod at startup. A `migrate` CLI command runs as a Kubernetes Job before each rollout; startup still migrates in Development.
- **Shared folders:** media, originals and Data Protection keys stay on host folders under `/var/saronida`, mounted into every pod (single node).
- **Logs:** NLog file names include the pod name, so two pods never write the same file.
- **Login rate limiter:** stays in memory per pod (the limit is per pod with two pods — accepted; Identity lockout is in the database).
- **Tests:** database tests run against real PostgreSQL via Testcontainers. They need Docker, so they run on the developer's PC, not in cloud sessions.
- **Local development:** PostgreSQL in Docker on the developer's PC.
- **Backups:** a nightly `pg_dump` CronJob writes to `/var/saronida/backup` (last 7 kept on the droplet). A script on the developer's PC (Windows Task Scheduler, runs when missed) pulls the dumps, new files in `originals/` and the Data Protection keys over SSH with a dedicated read-only backup user into the Proton Drive sync folder; keeps 30 daily + 12 monthly; warns when the newest backup is more than 3 days old. No DO backups. A restore runbook is written and tested once.

- [x] PostgreSQL provider: Npgsql EF Core, fresh `InitialCreate` (SQLite migrations, WAL pragma and SQLite packages removed), UTC `DateTime` mapping, local dev PostgreSQL in Docker
- [x] Hangfire on `Hangfire.PostgreSql` (schema `hangfire`); `/health` storage check updated. The schema installer takes no lock; its scripts are idempotent (`IF NOT EXISTS` + version guards), so concurrent installs are only theoretically racy (60 concurrent installs in a test: no error) — the `migrate` task decides whether the Job installs it first (2026-11-11)
- [x] Tests on Testcontainers PostgreSQL (replaces the temp SQLite databases)
- [ ] `migrate` CLI command; startup migration only in Development
- [ ] Cross-pod cache eviction via `LISTEN/NOTIFY` behind `IPageCacheInvalidator`, full eviction on listener (re)connect
- [ ] NLog file names per pod
- [ ] Two-instance check on the PC: two app processes on one database — a save on one evicts the other's cache, recurring jobs run once, an admin session works on both

## Phase 5 — Media

- [ ] `MediaAsset` entity, `IMediaService`, storage under `Media:RootPath`
- [ ] SkiaSharp pipeline: validate (JPEG/PNG/WebP, 25 MB, 50 MP), EXIF orientation, strip metadata, WebP 480/960/1600/2400 q~80, content-hash file names; original kept privately
- [ ] `<cms-image>` tag helper (`srcset`, `alt-source`) + `og:image` from `{page}.seo.image` in `<cms-seo>` (deferred from Phase 3, 2026-11-09)
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

- [ ] Preview on the future production droplet at `https://saronida.visualpivot.net`: Basic Auth (one owner login, htpasswd on the droplet), `X-Robots-Tag: noindex, nofollow`, `robots.txt` disallow-all, no sitemap, `Site:BaseUrl` = preview URL; PC backups run from the start (changed 2026-11-11: no DO backups)

Infrastructure as agreed on 2026-11-11 (see Phase 4.5); replaces the Docker Compose tasks.

- [ ] `deploy/k8s/` manifests: namespace, app Deployment (`replicas: 1`, readiness/liveness probes) + NodePort Service, PostgreSQL StatefulSet, `migrate` Job, `pg_dump` CronJob, Secrets (DB password, Instagram app secret), host-folder mounts under `/var/saronida`
- [ ] `deploy/nginx/` site config proxying to the NodePort (incl. `/health` denied externally); `ForwardedHeaders:KnownNetworks` set to the k3s network
- [ ] nginx maintenance mode: `/var/saronida/maintenance.on` → 503 + `deploy/nginx/maintenance.html`
- [ ] `deploy/deploy.ps1`: build on the PC → `docker save | ssh k3s ctr images import` → `migrate` Job → rollout → `/health` check
- [ ] Droplet setup: hardening, swap, k3s (Traefik disabled), nginx, certbot, firewall (NodePorts closed), read-only backup user, `saronida-admin` wrapper script (`kubectl exec`)
- [ ] PC backup script (`deploy/backup.ps1`) + Task Scheduler entry + restore runbook, restore tested once
- [ ] Two-pod rehearsal on the preview: `replicas: 2` → cache eviction across pods, jobs run once, admin login → back to 1; write the scale-up runbook (resize droplet → `replicas: 2`)
- [ ] Cloudflare: DNS (apex + www, proxied), Full (strict) SSL, Always Use HTTPS, Web Analytics
- [ ] Canonical redirects: http → https and no-www → `https://www.saronidasailing.gr` in one hop (nginx); certificate for both names
- [ ] Security headers (CSP incl. Google Maps frame, nosniff, Referrer-Policy, frame-ancestors); HSTS after launch, short max-age first
- [ ] `unattended-upgrades` with nightly auto-reboot; k3s and the pods come back on boot
- [ ] UptimeRobot monitor (alerts to the developer's email)
- [ ] Go-live: remove preview features (Basic Auth, noindex, Preview mode) → test via hosts file → copy all DNS records to Cloudflare → lower TTL → switch → verify → keep old site a few days → submit sitemap
