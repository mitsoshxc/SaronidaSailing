# Architecture

Status: **Phases 0-1 (decisions, foundation) are implemented; everything else is agreed design, not yet built.** Update as phases land.

## Tech Stack

| Concern | Choice | Why |
|---|---|---|
| Web framework | ASP.NET Core 10, Razor Pages | Team's main stack; fixed page templates fit Razor Pages well |
| ORM | EF Core 10 | Keeps the app independent of the database engine |
| Database | SQLite (WAL mode) | Few admins, rare writes, tiny data set; no DB server to run. Swap to Postgres/SQL Server later via EF Core if needed |
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
| Rich-text editor | Quill 2 (BSD-3), self-hosted | Small, limited to our formats; TinyMCE rejected (GPLv2+) |
| List reordering | SortableJS (MIT), self-hosted | Drag-and-drop, plus up/down buttons |
| HTML sanitizing | HtmlSanitizer | Rich text cleaned server-side before storage |
| Frontend | Plain JS and custom CSS, no frameworks; self-hosted Commissioner font | Small, matches the design canvas |
| Tests | xUnit (SQLite in-memory); Playwright + axe-core for accessibility | |
| Deploy | PowerShell 7 script (`deploy/deploy.ps1`), `docker save \| ssh` | No CI, no registry |

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

## Solution Structure

```
SaronidaSailing.slnx
global.json, Directory.Build.props, Directory.Packages.props, nuget.config
.config/dotnet-tools.json          dotnet-ef local tool
src/
  SaronidaSailing.Core/            entities (BaseEntity, ApplicationUser), interfaces, services (no EF, no ASP.NET)
  SaronidaSailing.Infrastructure/  Data/ (AppDbContext, DatabaseInitializer, Migrations), Health/, Logging/ (AuditLog), Media/, DependencyInjection.cs
  SaronidaSailing.Web/             Program.cs, Cli/, Identity/, Localization/, Health/, Pages/ (Pages/Admin/), wwwroot, appsettings*.json
tests/
  SaronidaSailing.Tests/           xUnit; temp-file databases (see docs/conventions.md -> Tests)
Dockerfile, .dockerignore
deploy/                            planned (Phase 8): deploy.ps1, docker-compose.yml, nginx/
```

`Core` references nothing. `Infrastructure` references `Core`. `Web` references both.

Root build files:

| File | Purpose |
|---|---|
| `global.json` | SDK 10.0.100, `rollForward: latestFeature` |
| `Directory.Build.props` | `net10.0`, nullable, implicit usings, `TreatWarningsAsErrors` for every project |
| `Directory.Packages.props` | Central package management: every package version lives here |
| `nuget.config` | `nuget.org` is the only source (machine-level feeds are cleared), so local and Docker restores behave the same |
| `.config/dotnet-tools.json` | `dotnet-ef` 10.0.14 (restore with `dotnet tool restore`) |

`Web` has no in-app HTTPS redirect and no HSTS: TLS ends at nginx / Cloudflare and the container only speaks HTTP on 8080.

Data layer (`Infrastructure/Data/`):

- `AppDbContext` stamps `CreatedAt` / `UpdatedAt` on every `SaveChanges*` from the injected `TimeProvider` (see `docs/domain-model.md` -> Entities). A convention converter makes every `DateTime` come back from SQLite as UTC.
- `DatabaseInitializer.InitializeDatabaseAsync` runs in `Web/Program.cs` before the app accepts requests: creates the database folder, applies migrations, then sets `PRAGMA journal_mode=WAL`. Migrations so far: an empty `InitialCreate` and `AddIdentity` (the Identity tables).
- Connection string `ConnectionStrings:Content`; a missing value fails startup.

## Pages (from the "Saronida Sailing Redesign" design canvas)

Canvas: https://claude.ai/artifact/RcU5iMfQSTJ34rH5gFB7MC

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
- **Routing:** `Web/Localization/SitePages.cs` is the single table of the 7 pages (key, Razor page path, Greek slug, English slug). `LocalizedPageRouteConvention` replaces each listed page's routes with exactly two: the Greek slug and `/en/{english slug}`. The default `/Index`, `/Trips` ... routes are not routable. Adding a page = a row in `SitePages` plus the Razor page.
- **Culture:** `UrlPrefixRequestCultureProvider` is the only request-culture provider (the default cookie and `Accept-Language` providers are removed): a path starting with `/en` is English, anything else Greek. Supported cultures and the prefix are constants in `SiteCultures.cs`. The layout sets `<html lang>` from the request UI culture.
- **URL helper:** `ILocalizedUrlService` (`LocalizedUrlService`, scoped) builds the path of a page key in a given culture (`GetPath`), finds the current page from the route (`GetCurrentPage`) and returns the current page's path in the other language (`GetAlternatePath`, for the language switcher).
- Every page emits `<html lang>`, `hreflang` alternates and a canonical URL.
- **Missing translation fallback:** if a field has no English value, English visitors see the Greek value. The admin sees a "missing EN" marker on that field in edit mode.
- `sitemap.xml` lists every page in both languages with `hreflang` alternates; `robots.txt` points to it and disallows `/admin`.

## SEO

- `<title>`, meta description, Open Graph tags per page and language from `{page}.seo.*`; canonical URL and `hreflang` alternates on every page.
- `sitemap.xml` (both languages, `hreflang` alternates) and `robots.txt`. All absolute URLs come from `Site:BaseUrl` (local / preview / production).
- **Preview environment** (`ASPNETCORE_ENVIRONMENT=Preview`): `robots.txt` disallows everything, no `sitemap.xml`, `X-Robots-Tag: noindex, nofollow` on every response (see `docs/deployment.md` → Preview).
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

- Storage: `Hangfire.Storage.SQLite` 0.4.3 in a separate file (`hangfire.db`; `ConnectionStrings:Hangfire` is a plain file path, not a connection string) so job polling never locks the content database.
- Server: Hangfire 1.8.25, **2 workers**, registered in `Infrastructure/DependencyInjection.cs` (`AddInfrastructure`). **Implemented so far: storage, server and the dashboard** - no jobs yet.
- **Known defect:** `SQLiteStorage.Dispose()` in 0.4.3 can hang or crash. Production disposes it only at process exit; the test factory therefore retries deleting its temp folder instead of relying on a clean dispose.
- Dashboard at `/admin/jobs`, mapped in `Web/Program.cs` with `MapHangfireDashboardWithAuthorizationPolicy(AdminPolicy.Name, ...)`: only requests that satisfy the `Admin` policy (authenticated + `amr=mfa`) get in; the dashboard's own authorization filter list is empty. Hangfire's antiforgery works with our `Secure` / `SameSite=Strict` cookies (`HangfireDashboardTests`).
- Every job must be safe to run twice.
- Possible later job: delete media files never referenced by any content or history (uploads discarded before Save). Not in scope until needed.

## Health Check — `/health`

Returns **200 "Healthy"** only if every check passes, otherwise **503 "Unhealthy"** (ASP.NET Core defaults). Body is the status word only — no details. Mapped in `Web/Program.cs`. Migrations run before the app accepts requests, so a failed migration means no answer at all.

| Check (name) | Where | How |
|---|---|---|
| `database` | `Infrastructure/Health/DatabaseHealthCheck.cs` | `CanConnectAsync` on the content database |
| `hangfire` | `Infrastructure/Health/HangfireStorageHealthCheck.cs` | `GetMonitoringApi().GetStatistics()` against `hangfire.db` |
| `media` | `Infrastructure/Health/MediaFolderHealthCheck.cs` | Write and delete a temp file under `Media:RootPath` |
| `home-page` | `Web/Health/HomePageHealthCheck.cs` | GET of `Health:HomePageUrl` through a named `HttpClient` (5 s timeout, redirects not followed); must return exactly 200 |

**Planned, Phase 7:** a fifth check, "at least one Greek `ContentItem` for page `home` exists (e.g. `home.hero.title`)". It needs the seed data, so it is deferred (recorded in `PLAN.md`).

Configuration keys:

| Key | Production (`appsettings.json`) | Development |
|---|---|---|
| `Health:HomePageUrl` | `http://127.0.0.1:8080/` (the container's own port) | `http://localhost:5000/` |
| `Media:RootPath` | `/var/saronida/media` | `data/media` |

The media folder is created at startup **only in Development** (`MediaFolder.EnsureExists` in `Web/Program.cs`). Elsewhere it is a bind mount, and a missing or unwritable folder must fail `/health` rather than be silently created inside the container.

**Not checked:** Instagram (an outage there must not fail health and roll back a good deploy) and maintenance mode (handled by nginx, see `docs/deployment.md`).

Reachable only from the droplet itself — nginx denies `/health` from the internet (nginx config planned, Phase 8). Used by `deploy.ps1` after every deploy.

## Admin Accounts

- No registration page. Admins are managed only with CLI commands built into the web app, run on the droplet (they go through Identity, so password hashes and stamps are always valid — never insert users with SQL).

**CLI** (`Web/Cli/AdminCommandRunner.cs`): `dotnet SaronidaSailing.Web.dll admin <command>`. When the first argument is `admin`, `Program.cs` builds the host without the command-line args, runs migrations, runs the command and exits without starting the web server.

| Command | Does |
|---|---|
| `admin add <email>` | Prompts for the password, creates the admin (2FA is set up at first login) |
| `admin reset-password <email>` | Prompts for a new password; clears lockout and the failed-attempt count, and changes the security stamp (existing sessions end within a minute). 2FA is left as it is |
| `admin list` | Lists admins: email, 2FA on/off, and "locked until" when locked out |
| `admin remove <email>` | Asks `[y/N]`, then deletes the admin |

- Exit codes: `0` success, `1` failure (password mismatch, policy error, unknown admin, remove not confirmed, no TTY), `2` wrong usage.
- Passwords are never arguments (shell history). They are typed twice with hidden input (`ConsolePasswordPrompt`), which needs an interactive terminal: with redirected input the command fails with a message. Run on the droplet as `docker compose exec web dotnet SaronidaSailing.Web.dll admin <command>` (no `-T`), wrapped by the host script `saronida-admin` (still Phase 8).
- Every command writes an audit line (user `cli`).
- The first admin is created with `admin add` after the first deploy — no passwords in config or environment variables.
- **Password policy** (`AddInfrastructureIdentity` in `Infrastructure/DependencyInjection.cs`): minimum 12 characters, no character-class rules. Email is the user name and must be unique.

**Pages** (all under `Web/Pages/Admin/`, layout `Pages/Shared/_AdminLayout.cshtml`): English only and not localized, `noindex, nofollow` meta, and `[ResponseCache(NoStore = true)]`. Not linked from the public site.

| Path | Purpose |
|---|---|
| `/admin/login` | Email + password |
| `/admin/login/code` | TOTP code (second step) |
| `/admin/login/recovery` | Recovery code instead of a TOTP code |
| `/admin/two-factor/setup` | Mandatory authenticator setup |
| `/admin/logout` | The GET page only shows a confirm form; signing out is a POST |

```
password ──► 2FA on?  yes ─► /admin/login/code ─► signed in (amr=mfa) ─► returnUrl or /
                      no  ─► signed in WITHOUT amr ─► /admin/two-factor/setup ─► code ─► signed in (amr=mfa)
```

1. `/admin/login` checks the password with lockout counting. Failures show one generic message ("Invalid email or password, or the account is temporarily locked.") whether the user is unknown, the password is wrong or the account is locked.
2. With 2FA on, the user goes to `/admin/login/code`; until the code is accepted the user is held only in the Identity two-factor cookie (`Secure`, `HttpOnly`, `SameSite=Strict`, `Web/Identity/ConfigureTwoFactorUserIdCookie.cs`). A wrong TOTP or recovery code shows a generic message too and counts toward lockout.
3. With 2FA off, the password login signs the user in without the `amr` claim, so the `Admin` policy still fails, and the user lands on `/admin/two-factor/setup` (which is also the `AccessDeniedPath`).
4. The setup page shows a QR code (QRCoder, inline SVG, `Web/Identity/AuthenticatorQr.cs`) and the same key in groups of 4. A valid code enables 2FA, signs the user in with `amr=mfa`, and the POST response shows **10 recovery codes once**; the page cannot show them again and the app never logs them.
5. No "remember this browser" and no "remember me": the sign-in is never persistent. `returnUrl` is honoured only when it is a local URL, otherwise `/`.
6. A lost phone is recovered with a recovery code; a forgotten password with `admin reset-password`.

**Authorization:** the `Admin` policy (`Web/Identity/AdminPolicy.cs`) = authenticated user + claim `amr=mfa`. Only a completed 2FA login carries that claim.

**Session:** cookie `saronida.auth`, `Secure` (always), `HttpOnly`, `SameSite=Strict`, 12 hours, sliding. The antiforgery cookie uses the same flags. The security stamp is re-validated every **1 minute**, so a password reset or removal takes effect quickly. A refresh builds a new principal from the user store, which would drop the `amr` claim and lock the admin out of the `Admin` policy; `MfaClaimPreserver` (`OnRefreshingPrincipal`) copies `amr` from the old principal.

**Lockout:** 5 failed attempts → 15 minutes (also for new users). Wrong TOTP and recovery codes count like wrong passwords.

**Data Protection keys** are persisted to `DataProtection:KeysPath` (`/var/saronida/data/keys`; Development `data/keys`), application name `SaronidaSailing` — otherwise every deploy logs admins out and invalidates antiforgery tokens. The keys are **not encrypted at rest** (file permissions only). When a key is created, `XmlKeyManager` logs a warning that reaches the app log.

- Several admins may edit at once; concurrent saves to the same field: last save wins. `ContentHistory` records who changed what.
- Hangfire dashboard `/admin/jobs`: see Background Jobs.

## Logging (NLog)

Implemented. Configuration is the `NLog` section of `appsettings.json`, loaded in `Web/Program.cs` (the default Microsoft logging providers are cleared). The folder comes from `Logs:Path` (`/var/saronida/logs`; Development `data/logs`); startup fails if it is missing.

| File | Contents |
|---|---|
| `audit-YYYY-MM-DD.log` | Everything written by the `Audit` logger (see below) |
| `app-YYYY-MM-DD.log` | Warnings and errors from all other loggers |

- Lines start with a UTC ISO 8601 timestamp. Warnings and errors also go to the console (Docker log). Never in the database.
- Routing rules, first match wins: `Audit` → audit file only; `Microsoft.Hosting.Lifetime` Info → console; `Microsoft.*`, `System.*`, `Hangfire.*` up to Info → dropped; everything else from Warn → app file and console.
- **Retention 90 days**: `maxArchiveDays: 90` on both file targets. Because the file names carry the date, NLog deletes older files when a new day's file is opened — no Hangfire cleanup job needed. Verified by `LogRetentionTests`.
- Docker's console log for the container is size-capped (json-file `max-size` / `max-file`).
- IP addresses in logs are personal data: the Privacy page must mention the logs and the 90-day retention.
- Until Phase 3 adds entity configurations, EF logs a startup warning "No instantiatable types implementing IEntityTypeConfiguration".

**Audit line format** (`Infrastructure/Logging/AuditLog.cs`): `time|event|user=…|ip=…[|details]`; an empty user or IP is written as `-`. Fields can carry attacker input (the email typed on the login form), so each is sanitized: control and line/paragraph-separator characters become a space, `|` becomes `/`, and it is cut at 256 characters.

| Event | Written when |
|---|---|
| `login-2fa-required` | Password correct, 2FA on |
| `login-2fa-setup-required` | Password correct, 2FA not set up yet |
| `login-failed` | Wrong password or unknown user |
| `login-locked-out` | A password, TOTP or recovery attempt hit lockout |
| `login-2fa-failed` | Wrong TOTP or recovery code |
| `login-succeeded` | Login complete (details `totp` or `recovery`) |
| `recovery-code-used` | Recovery code accepted (details `remaining=N`) |
| `2fa-enabled`, `2fa-setup-failed` | Setup page: valid or invalid code |
| `logout` | Sign out |
| `login-rate-limited` | Rate limiter rejection (details: path) |
| `admin-added`, `admin-password-reset`, `admin-removed`, `admin-listed`, `admin-remove-cancelled` | CLI commands (user `cli`) |
| `admin-add-failed`, `admin-password-reset-failed`, `admin-remove-failed` | CLI command failed (details: email and errors) |

Content-change events are added with the CMS.

## Security

| Concern | Measure |
|---|---|
| Admin login | Identity cookie auth + mandatory TOTP 2FA; lockout; per-IP rate limiter on login (see Admin Accounts) |
| Real client IP | nginx `realip` module sets the client IP from `CF-Connecting-IP` (Cloudflare ranges only) and forwards it in `X-Forwarded-For`. The app (`UseForwardedHeaders`, `Web/ForwardedHeadersSetup.cs`) reads `X-Forwarded-For` and `X-Forwarded-Proto` with `ForwardLimit = 1`, and trusts them **only** from the networks in `ForwardedHeaders:KnownNetworks` = `172.30.0.0/24`, the docker compose network subnet. It is not `127.0.0.1`: host nginx reaches the container through the published port, so the app sees the request coming from the compose network gateway. The compose file must pin that subnet (see `docs/deployment.md`) |
| Rate limiting | `UseRateLimiter` with one global partitioned limiter (`Web/Identity/AdminLoginRateLimit.cs`): only **POSTs** to `/admin/login`, `/admin/login/code`, `/admin/login/recovery` and `/admin/two-factor/setup` are limited, 10 per minute per client IP, fixed window, no queue. Rejection: 429, `Retry-After: 60`, plain-text message, audit event `login-rate-limited`. GETs and all other paths are not limited |
| Lockout | 5 failed attempts → 15 minutes, per account; TOTP and recovery failures count |
| CSRF | Antiforgery tokens on all admin write endpoints; antiforgery cookie `Secure`, `HttpOnly`, `SameSite=Strict` |
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

Full list to send the owner: [`OWNER-QUESTIONS.md`](../OWNER-QUESTIONS.md).


- Booking platform names and URLs
- Prices, max guests, cabins, email, contact hours, season
- Photos / video
- English texts
- Legal texts reviewed (ideally by a lawyer) before go-live
- English texts: Claude drafts translations of the seed content; owner reviews
