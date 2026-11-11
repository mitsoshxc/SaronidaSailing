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
- **Logs:** ~~NLog file names include the pod name~~ → all pods write the **same daily files** (`app-YYYY-MM-DD.log`, `audit-YYYY-MM-DD.log`) and **every line names the pod** it came from (changed 2026-11-11 by the developer). Multi-process appends use the official `NLog.Targets.AtomicFile` target (NLog 6 removed `concurrentWrites` from the standard file target). The instance name is `Logs:Instance`, defaulting to the machine name (in k3s the container's hostname is the pod name).
- **Diagnostics (agreed 2026-11-11):** log everything that helps find out later what went wrong. A pod killed by the out-of-memory killer or `SIGKILL` cannot log its own death, so the app logs what it can see (items below) and Kubernetes / the droplet record the kill from outside (Phase 8). A `Started` line with no `Stopped` before it means the previous run was killed.
- **Login rate limiter:** stays in memory per pod (the limit is per pod with two pods — accepted; Identity lockout is in the database).
- **Tests:** database tests run against real PostgreSQL via Testcontainers. They need Docker, so they run on the developer's PC, not in cloud sessions.
- **Local development:** PostgreSQL in Docker on the developer's PC.
- **Backups:** a nightly `pg_dump` CronJob writes to `/var/saronida/backup` (last 7 kept on the droplet). A script on the developer's PC (Windows Task Scheduler, runs when missed) pulls the dumps, new files in `originals/` and the Data Protection keys over SSH with a dedicated read-only backup user into the Proton Drive sync folder; keeps 30 daily + 12 monthly; warns when the newest backup is more than 3 days old. No DO backups. A restore runbook is written and tested once.

- [x] PostgreSQL provider: Npgsql EF Core, fresh `InitialCreate` (SQLite migrations, WAL pragma and SQLite packages removed), UTC `DateTime` mapping, local dev PostgreSQL in Docker
- [x] Hangfire on `Hangfire.PostgreSql` (schema `hangfire`); `/health` storage check updated. The schema installer takes no lock; its scripts are idempotent (`IF NOT EXISTS` + version guards), so concurrent installs are only theoretically racy (60 concurrent installs in a test: no error) — the `migrate` task decides whether the Job installs it first (2026-11-11)
- [x] Tests on Testcontainers PostgreSQL (replaces the temp SQLite databases)
- [x] `migrate` CLI command; startup migration only in Development. Flag `Database:MigrateAtStartup` (true only in `appsettings.Development.json`). `migrate` applies EF migrations **and** installs/upgrades the Hangfire schema (decided 2026-11-11: pods never install it; Hangfire `PrepareSchemaIfNecessary` follows the flag). With the flag off, startup fails if migrations are pending, naming them and the `migrate` command
- [x] Cross-pod cache eviction via `LISTEN/NOTIFY` behind `IPageCacheInvalidator`, full eviction on listener (re)connect. Channel `saronida_cache_evict`, payload = JSON array of tags; the saving pod evicts locally first, then publishes (a failed publish is logged, the save still succeeds); an unreadable payload evicts everything; reconnect backoff 1 s → 30 s
- [x] Shared daily NLog files written safely by several processes, with the pod name on every line (`NLog.Targets.AtomicFile` 6.2.2, type `AtomFile`, `concurrentWrites` + `keepFileOpen`; pod name is the 2nd field in app, audit and console lines; invalid `Logs:Instance` fails startup)
- [x] Lifecycle lines: `Started` (version incl. commit when available, pod name, environment, memory limit seen by the GC, CPU count), `Stopping` (with the shutdown reason), `Stopped`. Logger `Lifecycle` (Info kept by its own NLog rule); reason = `SIGTERM` / `SIGINT` / `SIGQUIT` / `requested` (waits up to 250 ms for the signal handler); web app only, not the CLI commands
- [x] Crashes: unhandled exceptions (incl. `OutOfMemoryException` escaping a thread) logged as `FATAL` and flushed before the process dies; unobserved task exceptions logged as `ERROR` (the process keeps running, so not `FATAL` — changed 2026-11-11); startup failures (bad config, pending migrations, database down) logged as `FATAL Startup failed`, after start `FATAL Host terminated unexpectedly`
- [x] Resource snapshot every minute in its own daily file `metrics-YYYY-MM-DD.log` (pod name on every line, 30-day retention): working set, GC heap, % of the memory limit, GC counts, thread-pool threads and queue length, requests in progress, requests per minute. Warning in the app log when memory exceeds 80% of the limit or the thread-pool queue keeps growing. The memory limit is the **container's** (cgroup `memory.max`, v1 fallback, machine memory when unlimited), not the GC heap cap (.NET defaults that to 75% of the container limit); both are logged (`memoryLimit`, `gcHeapLimit`), also in the `Started` line. Also CPU %, 5xx and 429 counts per minute. Thresholds are `Diagnostics:*` settings (defaults in code): snapshot 60 s, slow request 2000 ms, memory 80%, thread-pool queue 100, alert repeat 10 min, 429 lines 20/min, health period 30 s. `requestsPerMin` includes the health check's own request to `/`
- [x] Requests: slow requests (over 2 s) logged as a Warning with method, path, status and duration; rate-limited (429) responses logged with IP and path (500s are already logged). Path without query string, cleaned against log injection; 429 lines capped per minute
- [x] Health: when `/health` turns Unhealthy, log which check failed (and when it recovers). An `IHealthCheckPublisher` runs the checks every 30 s and logs changes only (per check and `overall`)
- [x] Two-instance check on the PC: two app processes on one database — a save on one evicts the other's cache, recurring jobs run once, an admin session works on both, both pods' lines land in the same log files, and the diagnostics lines above appear. Done 2026-11-11 with `.claude/launch.json` configs `web-a` / `web-b` (ports 5010/5011 and 5020/5021, `Logs:Instance` a / b): after a silent DB edit both served the cached home page; a save on A showed on B within ~200 ms; a session created on A worked on B (shared Data Protection keys); `Started`, metrics and audit lines from both landed in the same daily files; health stayed green. Killing both processes left `Started` with no `Stopped` — the crash signature. **Not covered:** recurring jobs (none exist yet — the Phase 8 rehearsal checks them; two Hangfire servers registered on the shared storage) and a graceful stop's `reason=` (covered by tests; `SIGTERM` is checked on Linux in Phase 8)

## Phase 5 — Media

Agreed (2026-11-11), on top of `docs/architecture.md` → Media Pipeline:

- **Originals folder:** new setting `Media:OriginalsPath` (production `/var/saronida/data/originals`, dev `data/originals`). Same rules as `Media:RootPath`: missing setting fails startup, created by the app only in Development, checked by `/health`, never served.
- **Duplicates:** an upload whose content hash already exists reuses that `MediaAsset` (no second row, no new files). Unique index on `ContentHash`; when two pods insert the same hash at once, the losing insert returns the existing row. Files are written to a temp name and renamed, so a half-written file is never visible.
- **Slug:** from the original file name — Greek transliterated, lowercase ASCII `a-z0-9-`, max 50 chars, `image` when nothing is left.
- **Memory:** each pod processes **one upload at a time** (a waiter gets 503 "busy, try again" after ~30 s); the upload request body is capped at 25 MB.
- **Language:** the image field is stored in Greek only (English falls back to it); alt text is the localized `Text` field `{key}-alt`. The photo can be changed from **both** language versions of a page (decided 2026-11-11): on an English page the image window shows the English alt and the Greek alt, and the Greek alt is required whenever an image is set.
- **Save checks:** `ContentBatch` rejects an `Image` value that is not the id of an existing `MediaAsset`, and an image without Greek alt text (in the same batch or already saved). The client blocks it too.
- **`<cms-image>`:** `<cms-image key="…" sizes="…" alt-source="title" loading="lazy|eager" class="…">`, or `field="image"` inside a `cms-list`; renders `<img>` with `srcset`, `width`/`height`, `decoding="async"`. Visitors: nothing when empty; admin: a clickable placeholder.
- **`og:image`:** the largest generated width ≤ 1600, absolute URL, plus `og:image:width`/`height` and `og:image:alt` from `{page}.seo.image-alt`.
- **Media library:** grid of uploads, newest first; no delete at launch (cleanup job stays deferred).
- **HEIC:** the file input uses `accept="image/jpeg,image/png,image/webp"` (iOS then converts HEIC to JPEG); the server rejects HEIC with a clear message. The real-iPhone test needs HTTPS, so it runs on the preview in Phase 8; that task stays open until then.

- [x] `MediaAsset` entity, ~~`IMediaService`~~ (moved to the pipeline task), storage under `Media:RootPath`. Done: `MediaAsset` (original stored as `{ContentHash}{OriginalFileExtension}`, no absolute path), `MediaFileNames`, `IMediaAssetRepository`, migration `AddMediaAssets`, `Media:OriginalsPath`, both paths resolved against the content root, the `media` health check probes both folders, `/media` served by the app in Development only (`.webp` only, immutable). Startup also fails when `Media:OriginalsPath` is inside `Media:RootPath` (originals must never be served)
- [x] SkiaSharp pipeline: validate (JPEG/PNG/WebP, 25 MB, 50 MP), EXIF orientation, strip metadata, WebP 480/960/1600/2400 q~80, content-hash file names; original kept privately. Done: `IMediaService.ImportAsync` → `MediaImportResult` (errors `TooLarge`, `NotAnImage`, `UnsupportedFormat`, `Heic`, `TooManyPixels`, `Busy` are results, not exceptions); SkiaSharp 4.155.0; decoded to sRGB; an image narrower than 480 gets one file at its own width; `Media:ProcessingWaitSeconds` (default 30, code only). A 3000×2000 JPEG imports in ~0.6 s on the developer's PC. The Linux native library ships under `runtimes/linux-x64/native/` (publish is not RID-specific — first real run on Linux is the Phase 8 preview)
- [x] `<cms-image>` tag helper (`srcset`, `alt-source`) + `og:image` from `{page}.seo.image` in `<cms-seo>` (deferred from Phase 3, 2026-11-09). Done: the page snapshot loads all its images in one query (`PageImage`); `<cms-image key|field sizes alt-source loading>` → `<img>` (src = 960 or largest), admin data attributes `data-cms-type="image"`, `data-cms-value`, `data-cms-alt-key`, `data-cms-alt` (current locale), `data-cms-alt-el` (only when the Greek alt is known: Greek page, or English alt empty), `data-cms-alt-proposal`; admin "Add image" placeholder button; `og:image` + width/height/alt; sandbox has `shared.sandbox.image` and an image per `shared.sandbox-items` item
- [x] Image modal: upload new / choose existing (media library), required alt text pre-filled from the nearest title. Client done 2026-11-11 (`image-window.js`, `image-data.js`): opens from a click on an image / "Add image" and the field bar's "Edit image"; upload with per-code messages, library grid with "Load more", Remove image, alt proposal marked "Suggested from the title — please refine"; English pages show English + Greek alt; Page settings has a "Link preview image" row; History shows thumbnails and restores images. `ContentValue.DefaultLocaleValue` gives the admin the Greek alt on English pages; admin images also carry `data-cms-sizes` / `data-cms-loading`. Verified in the Claude browser except the phone layout and keyboard-only paths (pane not drawing). Server side: `POST /admin/api/media` (multipart `file`; error codes `invalidAntiforgery`, `tooLarge` 413, `noFile`, `notAnImage`, `unsupportedFormat`, `heic`, `tooManyPixels`, `busy` 503 + `Retry-After: 5`; audit `media-uploaded` / `media-rejected`, never the file name), `GET /admin/api/media?page=N` (48 per page + `hasMore`), `GET /admin/api/media/{id}`; save checks in `ContentBatch` (Greek only, existing media id via one `ExistAsync`, Greek `{field}-alt` required, also when the alt is cleared). Phase 8: nginx `client_max_body_size` must allow 25 MB + multipart overhead on `/admin/api/media`
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
- [ ] Kubernetes event recorder: a small service on the droplet writes cluster events (`OOMKilled`, failed liveness checks, restarts, evictions) to `/var/saronida/logs/k8s-events-YYYY-MM-DD.log` (Kubernetes forgets events after about an hour)
- [ ] Memory requests/limits on the app pod and PostgreSQL, so .NET's GC works within the limit and a kill is recorded as `OOMKilled`
- [ ] nginx access and error logs kept 30 days (logrotate)
- [ ] Persistent systemd journal with a size cap, so the kernel's out-of-memory messages for the whole droplet are kept
- [ ] Two-pod rehearsal on the preview: `replicas: 2` → cache eviction across pods, jobs run once, admin login, both pods in the shared log files, a forced pod kill shows up in `k8s-events` → back to 1; write the scale-up runbook (resize droplet → `replicas: 2`)
- [ ] Cloudflare: DNS (apex + www, proxied), Full (strict) SSL, Always Use HTTPS, Web Analytics
- [ ] Canonical redirects: http → https and no-www → `https://www.saronidasailing.gr` in one hop (nginx); certificate for both names
- [ ] Security headers (CSP incl. Google Maps frame, nosniff, Referrer-Policy, frame-ancestors); HSTS after launch, short max-age first
- [ ] `unattended-upgrades` with nightly auto-reboot; k3s and the pods come back on boot
- [ ] UptimeRobot monitor (alerts to the developer's email)
- [ ] Go-live: remove preview features (Basic Auth, noindex, Preview mode) → test via hosts file → copy all DNS records to Cloudflare → lower TTL → switch → verify → keep old site a few days → submit sitemap
