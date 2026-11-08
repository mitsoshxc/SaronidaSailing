# Architecture

Status: **agreed design, not yet implemented.** Update as phases land.

## Tech Stack

| Concern | Choice | Why |
|---|---|---|
| Web framework | ASP.NET Core 10, Razor Pages | Team's main stack; fixed page templates fit Razor Pages well |
| ORM | EF Core 10 | Keeps the app independent of the database engine |
| Database | SQLite (WAL mode) | One admin, rare writes, tiny data set; no DB server to run. Swap to Postgres/SQL Server later via EF Core if needed |
| Auth | ASP.NET Core Identity, cookie auth, mandatory TOTP 2FA | One or more admins, managed by CLI commands |
| Logging | NLog to files, 90-day retention | Audit log (logins, admin actions) + app log |
| Caching | ASP.NET Core Output Caching (in-memory, in the app process) | Visitors are served rendered pages from memory; DB is hit only after a cache miss |
| Background jobs | Hangfire, SQLite storage (`Hangfire.Storage.SQLite`, community package) in its own file | Recurring jobs: Instagram fetch, token refresh, nightly DB backup copy |
| Image processing | SkiaSharp (MIT) | Resize, fix orientation, strip metadata, encode WebP |
| Media | Files on the droplet disk, served by nginx, cached by Cloudflare | Zero extra cost |
| Analytics | Cloudflare Web Analytics | Free, cookieless — needs no consent category |
| Hosting | DigitalOcean Basic droplet (FRA1), Docker | See `docs/deployment.md` |
| Reverse proxy / TLS | nginx + Let's Encrypt (certbot) on the host | |
| DNS / CDN | Cloudflare Free | DNS, edge caching of static files, DDoS protection |

**Explicitly rejected**

| Option | Reason |
|---|---|
| Elasticsearch | Second datastore to sync; needs ≥1 GB RAM on its own. Output caching already avoids DB reads |
| SQL Server | SQL Server on Linux needs ≥2 GB RAM. Not needed for this data volume |
| Redis | Only useful with multiple app instances; we run one |
| DigitalOcean Spaces | Paid; local disk + Cloudflare is enough |
| Own booking system | Bookings happen on external platforms; the site links out |
| Contact form / email provider | Contact page has no form (phone, email link, Instagram DM) |
| Google Fonts CDN | Fonts are self-hosted (sending visitor IPs to Google was ruled a GDPR violation by a Munich court, 2022) |
| CI/CD pipeline | Deploys run from the developer's PC (see `docs/deployment.md`) |

## Solution Structure (planned)

```
SaronidaSailing.slnx
src/
  SaronidaSailing.Core/            entities, interfaces, services (no EF, no ASP.NET)
  SaronidaSailing.Infrastructure/  AppDbContext, EF configurations, migrations, repositories, media, Instagram client, jobs
  SaronidaSailing.Web/             Razor Pages, tag helpers, admin API endpoints, wwwroot, Program.cs
tests/
  SaronidaSailing.Tests/           xUnit, SQLite in-memory
deploy/
  deploy.ps1, docker-compose.yml, nginx/ (site config)
Dockerfile
```

`Core` references nothing. `Infrastructure` references `Core`. `Web` references both.

## Pages (from the "Saronida Sailing Redesign" design canvas)

| Page | Greek URL | English URL | Sections |
|---|---|---|---|
| Home | `/` | `/en/` | Hero · At a glance · Trips teaser · Boat teaser · Why with a skipper · Instagram feed · Departure point |
| Trips | `/ekdromes` | `/en/trips` | Intro · 3 trip tabs (day cruise / islands / weekly charter) · Useful info · Booking box (links to booking platforms) |
| Boat | `/skafos` | `/en/boat` | Gallery · Intro · Specs · Life on board · Included · Charter details |
| Contact | `/epikoinonia` | `/en/contact` | Contact tiles · Google Map (after consent; static image otherwise) + directions · Useful details · Call to action |
| Terms | `/oroi` | `/en/terms` | Rich-text sections |
| Privacy | `/aporrito` | `/en/privacy` | Rich-text sections |
| Accessibility | `/prosvasimotita` | `/en/accessibility` | Rich-text sections |
| Shared | — | — | Header (logo, menu, language switch, Book button) · Footer |

Greek slugs are Latin transliterations (Greek letters get percent-encoded when URLs are shared). Slugs are **fixed in code**, not admin-editable — changing a URL later hurts search rankings.

The cookie-consent banner and the footer "Cookie settings" link from the design **are built** (needed for the Google Map — see Cookies & Privacy). The banner's "Statistics" category is dropped.

## Localization

- Languages: Greek (`el`, default) and English (`en`).
- URLs: Greek has no prefix (`/`, `/skafos`); English lives under `/en/...`. Adding a language later = a new prefix.
- Language switcher in the header links to the same page in the other language.
- Every page emits `<html lang>`, `hreflang` alternates and a canonical URL.
- **Missing translation fallback:** if a field has no English value, English visitors see the Greek value. The admin sees a "missing EN" marker on that field in edit mode.
- `sitemap.xml` lists every page in both languages with `hreflang` alternates; `robots.txt` points to it and disallows `/admin`.

## SEO

- `<title>`, meta description, Open Graph tags per page and language from `{page}.seo.*`; canonical URL and `hreflang` alternates on every page.
- `sitemap.xml` (both languages, `hreflang` alternates) and `robots.txt`.
- **Structured data:** JSON-LD `LocalBusiness` on every page, built from existing content (business name, phones, email, address, season, Instagram link) — the admin keeps it current by editing the normal content.
- **Old-site redirects (301).** The old site (Joomla, crawled 2026-11-08) serves the same pages on `saronidasailing.gr` and `www.saronidasailing.gr` under `/index.php/{el|en}/...`. Since the domain moves to the new server, the app maps old paths (any host, any query string) with a redirect table in middleware, early in the pipeline:

| Old path | New |
|---|---|
| `/index.php/el/`, `/index.php` | `/` |
| `/index.php/en/` | `/en/` |
| `/index.php/el/yacht-charter-gr`, `/index.php/el/daily-excursions-gr` | `/ekdromes` |
| `/index.php/en/yacht-charter`, `/index.php/en/daily-excursions` | `/en/trips` |
| `/index.php/el/sailing-yacht-2` | `/skafos` |
| `/index.php/en/sailing-yacht` | `/en/boat` |
| `/index.php/el/contacts-gr` (incl. `?format=vcf`) | `/epikoinonia` |
| `/index.php/en/contacts` (incl. `?format=vcf`) | `/en/contact` |
| `/index.php/el/privacy-policy` (contains the charter terms) | `/oroi` |
| `/index.php/en/privacy-policy` | `/en/terms` |
| `/index.php/el/friends-gr` | `/` |
| `/index.php/en/friends-en` | `/en/` |
| `/index.php/{el,en}/component/users/...` (Joomla login/registration) | **410 Gone** |
| any other `/index.php/el/...` / `/index.php/en/...` | `/` / `/en/` |

- **Canonical address: `https://www.saronidasailing.gr`.** Every `http://` request and every request to `saronidasailing.gr` (no www) gets a 301 straight to `https://www.saronidasailing.gr` + same path, in one hop (see `docs/deployment.md` → nginx). Canonical tags, `sitemap.xml` and `hreflang` always use the www HTTPS address.

## Request Flow

```
Visitor ─► Cloudflare ─► nginx (host) ─┬─► /media/*  → files on disk (immutable cache headers)
                                       └─► everything else → app container (127.0.0.1:5000)
                                                              │
                                                  Output cache hit? ─ yes ─► cached HTML
                                                              │ no
                                                        Razor Page → ContentService → SQLite
```

Cloudflare by default caches static file types (images, CSS, JS), not HTML, so HTML freshness is controlled by the app's output cache only.

## Output Caching

- Named policy `CmsPage`: expiration **1 hour**, tag `pages:all`.
- Each page adds its own tag `page:{pageKey}` (e.g. `page:home`).
- Any content write (field, list item add/remove/reorder/activate) on page X evicts `page:X`.
- Writes to shared content (page key `shared`: header, footer) evict `pages:all`.
- A successful Instagram fetch evicts `page:home`.
- Authenticated requests (the admin) are neither served from nor stored in the cache — built-in default policy behaviour. Requires `UseOutputCache()` **after** `UseAuthentication()`/`UseAuthorization()`.
- Cache is lost on restart/redeploy; pages rebuild on first request.

## CMS Editing Flow

1. Razor templates mark editable spots with tag helpers: `<cms key="home.hero.title" />` (text / rich text), `<cms-link key="..." />` (URL + label), `<cms-image key="..." />`, and `<cms-list key="home.highlights">` for repeating blocks.
2. For visitors, tag helpers render stored values only; inactive list items and inactive sections are not rendered.
3. When the admin is signed in, they also render `data-cms-*` attributes, inactive list items are shown dimmed, and the layout loads `wwwroot/js/cms-edit.js`.
4. **Admin toolbar** (top of the page): Edit mode on/off (off = see the page exactly as a visitor), **Save page**, Discard, unsaved-changes counter, Page settings, language switch, jobs dashboard link, logout.
5. `cms-edit.js` provides: inline text editing; **modals** (native `<dialog>`) for rich text, links, images, history and Page settings; for lists: add, delete (with confirmation), drag-to-reorder (SortableJS, self-hosted) **plus up/down buttons** (keyboard and phone), active/inactive toggle; an active/inactive toggle per section. Menu links to an inactive section are hidden too.
6. **One save per page:** every change (fields, list operations, section toggles) is kept in the browser as pending until the admin clicks **Save page**. Save sends all pending changes in **one request**, written in **one database transaction**, followed by **one** cache eviction. The page warns before navigating away with unsaved changes. Save publishes immediately (no draft/publish workflow) — history makes every change undoable. Only changed fields are sent, so two admins editing different fields of a page don't overwrite each other; the same field: last save wins.
7. Image files are uploaded and processed when chosen (the file must be processed anyway); assigning the image to the field is a pending change like any other.
8. Save → write content + append history → evict the page's output-cache tag (`pages:all` if shared content changed).
9. Rich-text HTML is sanitized server-side (HtmlSanitizer library) before storage. Allowed formatting: bold, italic, links, bullet and numbered lists, subheadings. Editor library: **Quill 2** (BSD-3), self-hosted, toolbar limited to exactly these formats. (TinyMCE rejected: GPLv2+ since v7.)
10. **Edit history is kept forever** (every previous value in `ContentHistory`); a field's History modal restores any earlier version (as a pending change, saved with Save page).
11. **Page settings modal**: per page and language, the SEO fields `{page}.seo.title` (browser tab / search result title), `{page}.seo.description` (search result snippet) and `{page}.seo.image` (link-preview image for Facebook, WhatsApp, etc. via Open Graph tags).

## Media Pipeline (SkiaSharp)

1. Admin uploads an image from the image modal ("Upload new"), or picks a past upload ("Choose existing" — media library grid).
2. Validation: accepted formats **JPEG, PNG, WebP**; max **25 MB** and **50 megapixels**; the file is decoded to confirm it is an image (not by extension). Rejections show a clear error. (HEIC from iPhones: expected to arrive as JPEG via the browser — to be tested on a real iPhone.)
3. Apply the EXIF orientation (SkiaSharp does not do this automatically), then re-encode — which drops all metadata, including GPS location.
4. Generate widths **480 / 960 / 1600 / 2400** (never upscaled) as **WebP, quality ~80**. Whatever the upload format, the public files are always WebP.
5. Public files: `{slug}-{contentHash}-{width}.webp` under `Media:RootPath` (production `/var/saronida/media`).
6. **The original upload is kept privately** in `/var/saronida/data/originals/` (never served by nginx — it still has GPS metadata), so all sizes can be regenerated if the design changes. Included in backups.
7. A `MediaAsset` row records the file set; image content references it.
8. **Alt text is required**: an image can't be saved without Greek alt text (English falls back to Greek). The modal pre-fills a **proposed alt text from the nearest title** — the template names it, e.g. `<cms-image key="home.trips.image" alt-source="title" />` uses the list item's title. The admin should refine it to describe the photo (an alt text identical to the visible title adds little for screen-reader users).
9. nginx serves `/media/` with `Cache-Control: public, max-age=31536000, immutable`. In development the app serves the folder via static files.

Linux native assets: `SkiaSharp.NativeAssets.Linux.NoDependencies` (nothing to install in the container).

**Video: not at launch.** The hero uses a photo. Self-hosted video would need pre-compressed uploads (no ffmpeg on the small droplet) and a check of Cloudflare's CDN terms on serving video. Revisit with the owner later.

## Instagram Feed

- Uses the official Instagram API for Business/Creator accounts (the old Basic Display API was shut down in December 2024). The owner's account must be Business or Creator.
- **Connect Instagram** button in the admin: the owner logs in to Instagram once (OAuth); the app exchanges the code for a long-lived token (expires after ~60 days) and stores it in `IntegrationSetting`. Nobody copies tokens by hand.
- Meta app ID in configuration; **app secret only in the environment file on the droplet**, never in the repo. Whether the app can stay in development mode (owner's account as tester, no app review) is confirmed when creating the app.
- **Number of posts shown: set by the admin** (field `home.instagram.post-count`, 1–12, default 6 — the design shows 6). The fetch job always stores the latest 12, so changing the number needs no refetch.
- Video posts and reels show their cover image; carousels show the first photo. Every tile links to the post on Instagram.
- **Disconnected warning:** if the token refresh fails or the token has expired, the admin toolbar shows "Instagram disconnected — reconnect" (links to Connect Instagram). Visitors keep seeing the last good posts.
- Hangfire recurring jobs: **fetch latest posts twice a day**; **refresh the token weekly**.
- Thumbnails are downloaded and processed through the media pipeline, so the page never depends on Instagram's image URLs.
- If a fetch fails, the last good posts stay on the page.
- **Section switch:** deactivating the `home.instagram` section removes both recurring jobs (`RecurringJob.RemoveIfExists`); activating it registers them again. At startup the jobs are registered **only if the section is active**, so a restart never brings them back while the section is off.

## Background Jobs (Hangfire)

| Job | Schedule |
|---|---|
| Instagram fetch | Twice a day |
| Instagram token refresh | Weekly |
| Database backup copy (`VACUUM INTO`) | Nightly |

- Storage: `Hangfire.Storage.SQLite` in a separate file (`hangfire.db`) so job polling never locks the content database.
- Dashboard at `/admin/jobs`, admin-only.
- Every job must be safe to run twice.
- Possible later job: delete media files never referenced by any content or history (uploads discarded before Save). Not in scope until needed.

## Health Check — `/health`

Returns **200 "Healthy"** only if every check passes, otherwise **503 "Unhealthy"** (ASP.NET Core defaults). Body is the status word only — no details.

| Check | How |
|---|---|
| App started | Endpoint answers (migrations run before the app accepts requests, so a failed migration means no answer) |
| Content DB + seed data | At least one Greek `ContentItem` for page `home` exists (e.g. `home.hero.title`) |
| Hangfire storage | Query against `hangfire.db` via Hangfire's storage API |
| Media folder writable | Write and delete a temp file under `Media:RootPath` |
| Home page renders | In-process HTTP GET `http://127.0.0.1:8080/` returns 200 |

**Not checked:** Instagram (an outage there must not fail health and roll back a good deploy) and maintenance mode (handled by nginx, see `docs/deployment.md`).

Reachable only from the droplet itself — nginx denies `/health` from the internet. Used by `deploy.ps1` after every deploy.

## Admin Accounts

- No registration page. Admins are managed only with CLI commands built into the web app, run on the droplet (they go through Identity, so password hashes and stamps are always valid — never insert users with SQL):

| Command | Does |
|---|---|
| `admin add <email>` | Prompts for the password (hidden input, typed twice, checked against the password policy) and creates the admin |
| `admin reset-password <email>` | Prompts for a new password the same way; also clears lockout |
| `admin list` | Lists admins |
| `admin remove <email>` | Deletes an admin |

  Run as `docker compose exec web dotnet SaronidaSailing.Web.dll admin <command>`, wrapped by a host script `saronida-admin`. Passwords are never passed as arguments (they would end up in shell history).
- The first admin is created with `admin add` after the first deploy — no passwords in config or environment variables.
- Login at `/admin/login`; not linked from the public site.
- **2FA mandatory**: first login forces authenticator-app (TOTP) setup and shows recovery codes. A lost phone is recovered with a recovery code; a forgotten password with `admin reset-password`.
- Session: 12 hours, sliding; no "remember me". Cookie `Secure`, `HttpOnly`, `SameSite=Strict`.
- Lockout: 5 failed attempts → 15 minutes. Plus a per-IP rate limit on the login endpoint.
- **Data Protection keys persisted** to `/var/saronida/data/keys` — otherwise every deploy logs admins out and invalidates antiforgery tokens.
- Several admins may edit at once; concurrent saves to the same field: last save wins. `ContentHistory` records who changed what.
- Hangfire dashboard `/admin/jobs`: admin-only authorization filter.

## Logging (NLog)

| File | Contents |
|---|---|
| `audit-YYYY-MM-DD.log` | Logins (success/failure), lockouts, 2FA setup, admin CLI commands, content changes — time, user, IP |
| `app-YYYY-MM-DD.log` | Warnings and errors |

- Location `/var/saronida/logs` (bind mount, survives deploys). Never in the database.
- **Retention 90 days**, deleted by NLog's own archive cleanup when files roll over (no Hangfire job needed). Exact NLog settings verified at implementation; if NLog's cleanup doesn't fit the file naming, fall back to a daily Hangfire cleanup job.
- Docker's console log for the container is size-capped (json-file `max-size` / `max-file`).
- IP addresses in logs are personal data: the Privacy page must mention the logs and the 90-day retention.

## Security

| Concern | Measure |
|---|---|
| Admin login | Identity cookie auth + mandatory TOTP 2FA; lockout; per-IP rate limiter on login (see Admin Accounts) |
| Real client IP | nginx `realip` module sets the client IP from `CF-Connecting-IP` (Cloudflare ranges only); app uses `UseForwardedHeaders` trusting nginx (127.0.0.1) |
| CSRF | Antiforgery tokens on all admin write endpoints |
| XSS | Razor encoding by default; rich text sanitized with HtmlSanitizer before storage; link URLs restricted to `https:`, `tel:`, `mailto:` |
| Transport | Cloudflare SSL mode **Full (strict)**; Let's Encrypt cert on the origin |
| Uploads | Admin-only; validated by decoding; size limits |

## Cookies & Privacy

The site embeds a **Google Map** (boat location), which sends visitor data to Google and may set Google cookies. Under EU ePrivacy rules (Greek Law 3471/2006) that needs **prior consent**, so the site has a **cookie consent banner** (design canvas: "Συναίνεση cookies" artboards).

| Item | Consent? |
|---|---|
| Language | None needed — chosen by URL (`/` vs `/en/`), no cookie |
| Admin login + antiforgery cookies | Strictly necessary; only the admin gets them |
| Consent choice itself (`localStorage`) | Strictly necessary |
| Cloudflare Web Analytics | Cookieless — no consent category |
| Cloudflare security cookies (e.g. `__cf_bm`, when set) | Classified by Cloudflare as strictly necessary |
| Google Maps embed | **"External content" category — loaded only after consent** |
| Instagram feed, fonts, hero video | Served from our server; no third-party requests |

**Banner rules**
- Categories: **Necessary** (always on, informational) and **External content** (Google Maps). No "Statistics" category — we set no analytics cookies.
- "Reject" is as easy as "Accept" (same size and style), nothing pre-ticked, "Choose by category" view available.
- The choice is changeable at any time from "Cookie settings" in every footer.
- Without consent, the map spot shows a static map image (served from our server) and a button that opens the banner settings.
- **Fully client-side**: the banner and the map are handled by JS reading `localStorage`, so every visitor gets the same cached HTML and the output cache is unaffected.
- Banner texts are CMS content (`shared.cookies.*`), in both languages.
- Map location is admin-editable: an embed URL, accepted only if it starts with `https://www.google.com/maps/embed`. The basic embed is free and needs no API key.

The Privacy page must cover Cloudflare (hosting/CDN, analytics), Google Maps, and the server logs. Owner/lawyer review before go-live. Any new third-party embed or tracker needs its own consent category.

## Admin Devices

Edit mode is designed for **laptop/desktop**. On phones it must work for quick text fixes: toolbar collapses, modals go full-screen, up/down buttons instead of dragging.

## Accessibility

Target **WCAG 2.2 AA** (per the design notes): real buttons/links, keyboard focus outline (3 px), text contrast ≥ 4.5:1, touch targets ≥ 44 px, alt text on every content image (alt text is editable content), accessibility statement page.

## Open Items (owner-provided, not blocking development)

- Booking platform names and URLs
- Prices, max guests, cabins, email, contact hours, season
- Photos / video
- English texts
- Legal texts reviewed (ideally by a lawyer) before go-live
- English texts: Claude drafts translations of the seed content; owner reviews
