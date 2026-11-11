# Architecture

Status: **Phases 0-5 are implemented (decisions, foundation, admin authentication, CMS core, inline editing, PostgreSQL and two-pod readiness: cross-pod cache eviction, shared logs, runtime diagnostics; media: upload pipeline, library, `<cms-image>`, image window), except the real-iPhone HEIC upload test, which stays open until the Phase 8 preview (it needs HTTPS). Everything else (Instagram, jobs, public pages, k3s deploy) is agreed design, not yet built.** Update as phases land.

## Tech Stack

| Concern | Choice | Why |
|---|---|---|
| Web framework | ASP.NET Core 10, Razor Pages | Team's main stack; fixed page templates fit Razor Pages well |
| ORM | EF Core 10 | Keeps the app independent of the database engine |
| Database | PostgreSQL (Npgsql EF Core provider; `postgres:18.6-alpine` locally and in tests, production version pinned in Phase 8) | One pod inside the cluster, data on the droplet disk. Lets two app pods share one database, and carries the cross-pod cache eviction (`LISTEN/NOTIFY`) and Hangfire storage |
| Auth | ASP.NET Core Identity, cookie auth, mandatory TOTP 2FA | One or more admins, managed by CLI commands |
| Logging | NLog to files (`NLog.Targets.AtomicFile`), 90-day retention | Audit log (logins, admin actions), app log and metrics log, shared by all pods, the pod name on every line |
| Caching | ASP.NET Core Output Caching (in-memory, per app process); evictions broadcast between pods with Postgres `LISTEN/NOTIFY` | Visitors are served rendered pages from memory; DB is hit only after a cache miss |
| Background jobs | Hangfire with `Hangfire.PostgreSql` storage, schema `hangfire` in the content database | Recurring jobs: Instagram fetch, token refresh. Every pod runs a server; Hangfire's distributed locks keep recurring jobs single |
| Image processing | SkiaSharp 4.155.0 (MIT) | Resize, fix orientation, strip metadata, encode WebP. Linux native library from `SkiaSharp.NativeAssets.Linux.NoDependencies` |
| Media | Files on the droplet disk, served by nginx, cached by Cloudflare | Zero extra cost |
| Analytics | Cloudflare Web Analytics | Free, cookieless — needs no consent category |
| Hosting | DigitalOcean Basic droplet (FRA1), k3s single node (agreed design, Phase 8) | One app pod at launch, built to run two. See `docs/deployment.md` |
| Reverse proxy / TLS | nginx + Let's Encrypt (certbot) on the host | |
| DNS / CDN | Cloudflare Free | DNS, edge caching of static files, DDoS protection |
| Rich-text editor | Quill 2 (BSD-3), self-hosted | Small, limited to our formats; TinyMCE rejected (GPLv2+) |
| List reordering | SortableJS (MIT), self-hosted | Drag-and-drop, plus up/down buttons |
| HTML sanitizing | HtmlSanitizer | Rich text cleaned server-side before storage |
| Frontend | Plain JS and custom CSS, no frameworks; self-hosted Commissioner font | Small, matches the design canvas |
| Tests | xUnit on real PostgreSQL via Testcontainers; Playwright + axe-core for accessibility | Database tests need Docker, so they run on the developer's PC, not in cloud sessions |
| Deploy | PowerShell 7 script (`deploy/deploy.ps1`): `docker save \| ssh` + `ctr images import` into k3s, `migrate` Job, rollout (agreed design, Phase 8) | No CI, no registry |

**Explicitly rejected**

| Option | Reason |
|---|---|
| Elasticsearch | Second datastore to sync; needs ≥1 GB RAM on its own. Output caching already avoids DB reads |
| SQL Server / SQL Server Express | Needs ≥2 GB RAM on its own. Not needed for this data volume |
| Redis | A second moving part. Cross-pod cache eviction uses Postgres `LISTEN/NOTIFY` on the database we already run |
| DigitalOcean Managed PostgreSQL | Cost; Postgres runs as one pod on the droplet disk instead |
| DigitalOcean Kubernetes (DOKS) | A one-node DOKS cluster costs about $25/mo against $12 for k3s on a plain droplet: the $12 worker node plus a $12 DigitalOcean Load Balancer to expose it (the control plane is free). DOKS also replaces nodes during upgrades, so data on the node's disk would be lost and the database, media and keys would need a Block Storage volume. k3s on one droplet keeps the agreed host-folder layout under `/var/saronida` |
| SQLite | Replaced on 2026-11-11: two app pods cannot share a SQLite file safely, and the cache bus and Hangfire storage need a database server |
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
  SaronidaSailing.Core/            Entities/ (BaseEntity, ApplicationUser, Content*, MediaAsset), Interfaces/ (repositories, IContentService, IMediaService, MediaImportResult, IRichTextSanitizer, IUnitOfWork, IPageCacheInvalidator, ICacheTagEvictor, ICacheEvictionBus, PageContent, PageImage), Services/ (ContentService, ContentBatch, ContentKeys, ContentLocales, ContentLink, MediaFileNames, MediaLimits, MediaDimensions, Slugs); no EF, no ASP.NET
  SaronidaSailing.Infrastructure/  Data/ (AppDbContext, DatabaseInitializer, DatabaseSettings, UnitOfWork, Configurations/, Migrations), Caching/ (PostgresCacheEvictionBus, PostgresCacheEvictionListener, CacheEvictionChannel), Repositories/, Content/ (RichTextSanitizer), Health/, Logging/ (AuditLog), Media/ (MediaService, ImageProcessor, MediaProcessingGate, MediaOptions, MediaFolder), DependencyInjection.cs
  SaronidaSailing.Web/             Program.cs, SiteOptions.cs, LogInstance.cs, DevelopmentMediaFiles.cs, Caching/ (CacheTags, OutputCachePageInvalidator, OutputCacheTagEvictor), Diagnostics/ (lifecycle, crash, metrics, request and health logging), Content/ (PageContentAccessor, CmsEditContext, CmsEditorConfig), Endpoints/ (ContentEndpoints, ContentApiModels, MediaEndpoints, MediaApiModels), TagHelpers/, Cli/ (admin and migrate commands), Identity/, Localization/, Health/, Pages/ (Pages/Admin/, Pages/Dev/), wwwroot (css/, js/cms/, lib/quill/, lib/sortablejs/), appsettings*.json
tests/
  SaronidaSailing.Tests/           xUnit; one PostgreSQL Testcontainer, one database per test class (see docs/conventions.md -> Tests)
Dockerfile, .dockerignore
deploy/dev/docker-compose.yml      local development PostgreSQL only, never used in production
deploy/                            planned (Phase 8): deploy.ps1, k8s/, nginx/, backup.ps1
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

`Web` has no in-app HTTPS redirect and no HSTS: TLS ends at nginx / Cloudflare and the container only speaks HTTP on 8080. `Web` also references `NLog.Targets.AtomicFile`; `Infrastructure` references `Npgsql.EntityFrameworkCore.PostgreSQL`, `Hangfire.PostgreSql` and `Newtonsoft.Json` 13.0.4. The Newtonsoft.Json reference exists only to pin a safe version: Hangfire.Core's minimum dependency (11.0.1) has a high-severity advisory (NU1903), and the solution builds with NuGet audit warnings as errors, never suppressed.

Data layer (`Infrastructure/Data/`):

- `AppDbContext` stamps `CreatedAt` / `UpdatedAt` on every `SaveChanges*` from the injected `TimeProvider` (see `docs/domain-model.md` -> Entities). A convention converter (`UtcDateTimeConverter`) marks every `DateTime` read back as `DateTimeKind.Utc`; the columns are `timestamp with time zone`.
- **Migrations:** `InitialCreate` (`20261111081638_InitialCreate`) holds the Identity tables and the four content tables; `AddMediaAssets` (`20261111133422_AddMediaAssets`) adds `MediaAssets` (see `docs/domain-model.md`). The earlier SQLite migrations were deleted when the provider changed.
- **Who applies migrations** (`Infrastructure/Data/DatabaseInitializer.cs`, flag `Database:MigrateAtStartup`, `DatabaseSettings`): `InitializeDatabaseAsync` runs in `Web/Program.cs` before the app accepts requests. With the flag `true` (set only in `appsettings.Development.json`) it calls `MigrateAsync`: applies pending EF migrations **and** installs or upgrades the Hangfire schema (`PostgreSqlObjectsInstaller.Install`, schema `hangfire`). With the flag `false` (the default in `appsettings.json`, so preview and production) it calls `EnsureUpToDateAsync`, which throws "The database is behind: N pending migration(s): ... Run the 'migrate' command first." Pods never migrate or install the Hangfire schema themselves (Hangfire's `PrepareSchemaIfNecessary` follows the flag). In production the `migrate` command runs as a Kubernetes Job before each rollout (agreed design, Phase 8).
- Entity configurations are in `Infrastructure/Data/Configurations/` (one per content entity). Repositories (`Infrastructure/Repositories/`: `ContentItemRepository`, `ContentListItemRepository`, `ContentSectionRepository`, `ContentHistoryRepository`) only **stage** changes; `IUnitOfWork` (`Infrastructure/Data/UnitOfWork.cs`) has `SaveChangesAsync` (one commit, one transaction) and `DiscardChanges`. All are scoped (`AddInfrastructure`).
- Connection string `ConnectionStrings:Content` is an Npgsql string; a missing value fails startup. The same string serves EF Core, Hangfire storage and the cache-eviction bus and listener. `appsettings.json` has no password (`Host=postgres;Port=5432;Database=saronida;Username=saronida`); `appsettings.Development.json` points at the local container (`Host=localhost`).
- **Local PostgreSQL:** `docker compose -f deploy/dev/docker-compose.yml up -d` starts `postgres:18.6-alpine` on `127.0.0.1:5432` (database and user `saronida`, named volume `saronida-dev-pgdata`).
- **`migrate` command** (`Web/Cli/MigrateCommand.cs`): `dotnet SaronidaSailing.Web.dll migrate`. When the first argument is `migrate`, `Program.cs` builds the host without the command-line args, runs `MigrateAsync` and exits without starting the web server. Prints `Applied N migration(s): ... Hangfire schema up to date.` (or `No pending migrations.`). Exit codes: `0` success, `1` failure (message and exception on stderr), `2` wrong usage (any extra argument).

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

- **Implemented (Phase 3):** `<cms-seo fallback-title="...">` (`Web/TagHelpers/CmsSeoTagHelper.cs`) in the `<head>` of `Pages/Shared/_Layout.cshtml`. On a site page (one in `SitePages`) it emits `<title>` (`{page}.seo.title`, else the fallback title), `<meta name="description">` (`{page}.seo.description`, omitted when empty), `og:title`, `og:description`, `og:type` (`website`), `og:site_name`, `og:url`, `og:locale` and `og:locale:alternate` (`el_GR` / `en_GB`). On other pages (admin, errors) it emits only `<title>`. Values are read as `Text` fields in the visitor's language (English falls back to Greek). `og:image` (Phase 5): when `{page}.seo.image` holds an existing image, the tag helper also emits `og:image` (absolute URL: `Site:BaseUrl` + the largest generated width of 1600 or less, else the smallest), `og:image:width`, `og:image:height` and, when `{page}.seo.image-alt` has text in the visitor's language (English falls back to Greek), `og:image:alt`. A stored value of the wrong type, or an id with no `MediaAsset`, logs a warning and emits no image tags.
- **Planned:** canonical URL and `hreflang` alternates on every page, `sitemap.xml`, `robots.txt`, JSON-LD.
- **`Site:BaseUrl`** (`Web/SiteOptions.cs`) is a required option: an absolute http(s) URL with no path, query or fragment (a trailing `/` is trimmed). It is validated on start, so a missing or invalid value fails startup. `appsettings.json` has no value; Development sets `https://localhost:5001`; preview and production must set `Site__BaseUrl` (Phase 8). It prefixes `og:url` today and all absolute URLs (canonical, sitemap, `hreflang`) later.
- **Greek is not escaped in HTML output:** `WebEncoderOptions` in `Program.cs` allows `BasicLatin`, `Latin1Supplement`, `GreekandCoptic` and `GeneralPunctuation`, so Greek text is emitted as characters, not `&#x...;` entities.
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
                                       └─► everything else → app pod (Service NodePort; Phase 8)
                                                              │
                                                  Output cache hit? ─ yes ─► cached HTML
                                                              │ no
                                                        Razor Page → ContentService → PostgreSQL
```

Cloudflare by default caches static file types (images, CSS, JS), not HTML, so HTML freshness is controlled by the app's output cache only.

## Output Caching

Implemented in Phase 3 (`Web/Program.cs`, `Web/Caching/`).

- Named policy `CmsPage` (`CacheTags.PolicyName`): expiration **1 hour**, tag `pages:all`, built on the default policy, with the query string ignored (`SetVaryByQuery([])`), so `?x=1` variants share one entry.
- Each page declares the policy and its own tag on its page model: `[OutputCache(PolicyName = CacheTags.PolicyName, Tags = [CacheTags.HomePage])]` (tag `page:{pageKey}`, e.g. `page:home`). Only `IndexModel` is cached so far; a guard test (`OutputCacheTests`) requires every built site page to carry the policy and its page tag, so a new page without it fails the tests.
- `IContentService.ApplyChangesAsync` calls `IPageCacheInvalidator` once per save with the touched page keys. `OutputCachePageInvalidator` (singleton) turns them into tags: `page:X` for each touched page, or `pages:all` only if `shared` is touched. It evicts them locally through `ICacheTagEvictor`, then publishes them with `ICacheEvictionBus`.
- A successful Instagram fetch evicts `page:home` (planned, Phase 6).
- Authenticated requests (the admin) are neither served from nor stored in the cache — built-in default policy behaviour (and admin-rendered pages are sent with `Cache-Control: no-store`). `app.UseOutputCache()` is placed **after** `UseAuthentication()`/`UseAuthorization()`.
- Cache is lost on restart/redeploy; pages rebuild on first request.

### Cache eviction across pods

Each pod keeps its own in-memory output cache. A save on one pod must evict the other pods' copies too, so evictions travel through PostgreSQL `LISTEN/NOTIFY` (no Redis).

```
Save on pod A ─► ICacheTagEvictor.EvictTagsAsync (local, first)
              └► ICacheEvictionBus.PublishAsync ─► pg_notify('saronida_cache_evict', ["page:home"])
                                                        │
                       every pod's PostgresCacheEvictionListener (LISTEN) ◄┘ ─► EvictTagsAsync
```

| Part | File | Behaviour |
|---|---|---|
| `ICacheTagEvictor` | `Core/Interfaces/`; implemented by `Web/Caching/OutputCacheTagEvictor` (singleton, `IOutputCacheStore.EvictByTagAsync`) | `EvictTagsAsync(tags)`; `EvictAllAsync()` = evict tag `pages:all` |
| `ICacheEvictionBus` | `Core/Interfaces/`; implemented by `Infrastructure/Caching/PostgresCacheEvictionBus` | `PublishAsync(tags)` opens a connection and runs `SELECT pg_notify(channel, payload)`; empty tag list publishes nothing |
| Channel | `Infrastructure/Caching/CacheEvictionChannel` | Name `saronida_cache_evict`; payload = JSON array of tags |
| Listener | `Infrastructure/Caching/PostgresCacheEvictionListener` (hosted service) | Own non-pooled connection (`ApplicationName` `saronida-cache-listener`, `KeepAlive` 30 s). Runs `LISTEN`, then evicts everything, then waits for notifications |

Rules:

- **Full eviction on (re)connect:** every time the listener connects or reconnects it calls `EvictAllAsync`, so a notification missed while disconnected cannot leave stale pages.
- **Unreadable payload** (not a JSON string array) logs a warning and evicts everything.
- **Reconnect backoff** 1 s, doubling to a 30 s maximum; reset after a successful connect. A lost connection logs a warning in the app log.
- **A failed publish never fails the save:** the local eviction has already happened; the failure is logged as a warning ("other instances will catch up on expiry or reconnect"). The worst case is the other pod serving a page for up to the 1-hour expiration.
- The login rate limiter stays in memory per pod, so with two pods the limit is per pod (accepted; Identity lockout is in the database).

## CMS Editing Flow

```
Template (Razor, tag helper attributes)
   |  admin signed in? -- no --> visitor HTML (cached)
   v yes
Admin HTML: data-cms-* attributes + #cms-config JSON + cms-edit.css + cms-edit.js (Cache-Control: no-store)
   |  admin edits in place; every change stays in the browser as pending
   v
Save page --> POST /admin/api/content (one request)
   |            ContentService.ApplyChangesAsync: validate + sanitize -> one transaction -> one cache eviction -> one audit line
   v
200 {saveId} --> page reloads ("Saved")      400 {errors} --> nothing written, errors shown in the toolbar
```

### Template markup

Razor templates mark editable spots with tag helper **attributes on real elements** (`Web/TagHelpers/`), not wrapper elements:

| Markup | Does |
|---|---|
| `<h1 cms="home.hero.title">` | Fills the element with a `Text` value (encoded). Add `cms-rich` (`<div cms="about.story.body" cms-rich>`) to declare a `RichText` field, emitted as HTML. If the stored type differs from what the template declares, a warning is logged and the element is omitted |
| `<a cms-link="home.hero.cta">` | Link field: sets `href` and the label. An `https:` link also gets `target="_blank"`, `rel="noopener"` and a visually hidden "(opens in a new tab)" notice in the page language (`Localization/UiText.cs`). Invalid link JSON or a disallowed scheme logs a warning and omits the element for visitors; the admin sees it flagged `data-cms-invalid` |
| `<li cms-list="home.highlights">` | Repeats the element once per **active** entry (the admin also sees inactive ones), in `SortOrder`. Inside it, `cms-field="title"` (with optional `cms-rich`) and `cms-link-field="link"` read that entry's fields. Lists cannot be nested; no active entries omits the element for visitors |
| `<section cms-section="home.instagram">` | Omits the whole element when the section is inactive (a section with no row is active) |
| `<a cms-section-link="home.instagram">` | A menu link to a section (`CmsSectionLinkTagHelper`): omitted for visitors when that section is inactive. Accepts only a section of the current page or of `shared`; any other page key throws at render time |
| `<cms-image key="home.hero.image" sizes="..." />` | An element, not an attribute (`CmsImageTagHelper`, no end tag). Renders an `<img>` for an `Image` field (see Media Pipeline → `<cms-image>`) |
| `<cms-seo fallback-title="...">` | Page `<title>`, description and Open Graph tags (see SEO) |

A field with no value in either language omits its element for visitors; the admin gets an empty element flagged `data-cms-empty` to click into. An invalid key, or `cms-field` outside a `cms-list`, throws. `Web/TagHelpers/CmsFieldResolver.cs` holds the shared key checks.

### Content snapshot

Tag helpers read a per-request snapshot, `IPageContentAccessor` (`Web/Content/PageContentAccessor.cs`, scoped): the first call asks `IContentService.GetPageContentAsync(page, locale)` for the current page (from `ILocalizedUrlService`; `shared` when the route is not a site page) and the UI-culture locale, and every later call reuses it. The snapshot (`Core/Interfaces/PageContent.cs`) holds the page's content plus `shared` content, loaded in one pass (items, list items, sections, then one query for every image the items refer to: `ContentService.LoadImagesAsync` builds a `PageImage` per `MediaAsset`, `PageContent.GetImage(id)`). English values that are missing or blank fall back to the Greek value, flagged `IsFallback`; `ContentValue.DefaultLocaleValue` also carries the Greek value (set on every value read in English, and on Greek values), so the admin can see the Greek alt text on an English page. For visitors, inactive list items and sections are not rendered.

### Admin rendering

`ICmsEditContext.IsEditorAsync()` (`Web/Content/CmsEditContext.cs`, scoped) evaluates the `Admin` policy once per request. For an admin it also sets `Cache-Control: no-store` on the response (in `OnStarting`, after antiforgery's own header). Only then do the tag helpers and `Pages/Shared/_Layout.cshtml` emit admin markup:

| Emitted | Where |
|---|---|
| `data-cms-key`, `data-cms-type` (`text`, `richText`, `link`, `image`) | Every field element |
| `data-cms-value` | Rich-text and link fields: the stored HTML / link JSON, so the editors start from the saved value. Image: the media id (empty when none) |
| `data-cms-alt-key`, `data-cms-alt`, `data-cms-alt-el`, `data-cms-alt-proposal`, `data-cms-sizes`, `data-cms-loading` | `<cms-image>` only: see Media Pipeline → `<cms-image>` |
| `data-cms-fallback`, `data-cms-empty`, `data-cms-invalid` | Flags: English shows the Greek fallback, no value, invalid link |
| `data-cms-list`, `data-cms-item` | Every list item copy (`data-cms-item` is the item's Id) |
| `<template data-cms-list-template="{listKey}">` | After each list: one empty copy of the item markup, cloned by "Add item" |
| `data-cms-section`, `data-cms-section-link` | Sections and their menu links |
| `data-cms-active="false"` | Inactive list items, sections and section links; shown dimmed |
| `<script type="application/json" id="cms-config">` | `CmsEditorConfig` (`Web/Content/CmsEditorConfig.cs`): `pageKey`, `locale`, `saveUrl`, `historyUrl`, `savesUrl`, `antiforgeryToken`, `opensInNewTab` and `seo` (the page's SEO title and description, each with its Greek value when English falls back; the link-preview `image` (`{value, previewUrl}`, the 960 px file or the largest) and `imageAlt` (`{value, greek}`); null on non-site pages) |
| `_AdminToolbar` partial, `~/css/cms-edit.css`, `~/js/cms-edit.js` (module) | Layout |

### Save API

`Web/Endpoints/ContentEndpoints.cs` (`MapContentEndpoints`, called in `Program.cs`) maps the group `/admin/api/content`, which requires the `Admin` policy and sets `Cache-Control: no-store` through an endpoint filter. Request and response records are in `Web/Endpoints/ContentApiModels.cs`; endpoints only translate and delegate to `IContentService`.

| Endpoint | Behaviour |
|---|---|
| `POST /admin/api/content` | Body `{ "changes": [ ... ] }`, each change a polymorphic record with an `op` discriminator: `setField`, `addListItem`, `deleteListItem`, `reorderList`, `setListItemActive`, `setSectionActive` (see Write service). Field `type` is a camel-case string (`text`, `richText`, `link`, `image`). The antiforgery token must be sent in the `RequestVerificationToken` header (`HeaderName` set in `Program.cs`). Success: `200 {"saveId": "..."}` (`saveId` is null when the batch changed nothing). Invalid changes: `400 {"errors": [...]}`, nothing written |
| `GET /admin/api/content/history?key=&locale=&listItemId=` | The saved versions of one field, newest first: `[{saveId, value, changedBy, createdAt}]`. `400` for an invalid key, locale or `listItemId` |
| `GET /admin/api/content/saves/{saveId}` | One save: `{saveId, changedBy, createdAt, fields: [{key, locale, type, listItemId, value, previousValue, currentValue}]}`; `404` when the save has no history rows. `previousValue` is the field's value in the version before that save (empty for the first version), `currentValue` is its value now |

Other status codes of `POST`:

| Status | Cause |
|---|---|
| 400 `{errors: ["Invalid antiforgery token."]}` | Missing or wrong token |
| 400 `{errors: ["Malformed request body..."]}` | Not valid JSON for the request shape (the JSON path is named), unknown `op`, a `null` entry in `changes` |
| 413 | Body over **256 KB** (`MaxBodyBytes`): checked on `Content-Length` and while reading the body |
| 401 / 403 | Not signed in / signed in without `amr=mfa` |

**401/403 instead of a login redirect:** `Web/Identity/AdminApiCookieEvents.cs` replaces the cookie `OnRedirectToLogin` / `OnRedirectToAccessDenied` events. For any path under `/admin/api` they set status 401 / 403; for every other path they redirect as before.

### Write service

The write API is one method, `IContentService.ApplyChangesAsync(changes, ContentEditor)` (`Core/Services/ContentService.cs`, `ContentBatch.cs`), taking an ordered list of change records (`Core/Interfaces/ContentChanges.cs`):

| Change | Effect |
|---|---|
| `SetField(key, locale, type, value, listItemId?)` | Creates or updates a field; every changed value appends a `ContentHistory` row (the first save included). A different `Type` than the stored one is rejected. Setting the same value is a no-op (no history row). Two `SetField` for the same key, locale and list item in one batch keep the last value (one history row) |
| `AddListItem(listKey, id)` | Appends a list item at the end. The `Id` is generated by the client (`crypto.randomUUID()`) so later changes in the same save can refer to it; empty or already-used Ids are rejected |
| `DeleteListItem(listKey, id)` | Deletes the item (its fields and their history cascade) |
| `ReorderList(listKey, orderedIds)` | Must list each current item exactly once |
| `SetListItemActive(listKey, id, isActive)` | Shows or hides a list item |
| `SetSectionActive(sectionKey, isActive)` | Shows or hides a section |

Changes are validated and applied in order (keys, locale, type, value present, list item belongs to the list, then the sanitizing and link checks below); then **one** `IUnitOfWork.SaveChangesAsync` commit, **one** `IPageCacheInvalidator.EvictPagesAsync` call with the touched pages, and **one** `content-saved` audit line. Any invalid change means nothing is written: the pending staging is discarded, errors are returned in `ContentSaveResult`, and `content-save-rejected` is audited. Changes that alter nothing do not count as touching a page, and a batch that touches no page returns success without a `SaveId`. The editor is identified by email (`ContentEditor(Email, IpAddress)`).

### Sanitizing and validation

Both run inside `ContentService.ApplyChangesAsync` (`ContentBatch.SetFieldAsync`), so every writer is covered (the save endpoint, the Phase 7 seed, jobs), not only the HTTP endpoint.

| Value type | Rule |
|---|---|
| `RichText` | Passed through `IRichTextSanitizer` (`Core/Interfaces/`), implemented by `RichTextSanitizer` (`Infrastructure/Content/`, HtmlSanitizer 11.0.16, registered as a singleton). Allowed tags: `p`, `br`, `strong`, `em`, `a`, `ul`, `ol`, `li`, `h3`. The only allowed attribute is `href`; no classes, CSS or `data-` attributes. Allowed URL schemes: `https`, `mailto`, `tel`, and only on `a`. Disallowed tags are unwrapped (children kept), except `script`, `style`, `iframe`, `frame`, `frameset`, `object`, `embed`, `applet`, `noscript`, `template`, `textarea`, `title`, `svg` and `math`, which are dropped with their content. A result that is only empty paragraphs becomes `""` |
| `Link` | An empty value clears the link. Otherwise the JSON must parse with `ContentLink.TryParse`: `https:`, `mailto:` or `tel:` URL and a non-blank label; if not, the save is rejected with an error naming the field |
| `Text` | Stored as sent |
| `Image` | Checked in `ContentBatch` (`SetFieldAsync`, then `ValidateImagesAsync` once all changes are applied): see Media Pipeline → Save checks |

### History

- `ContentHistory.SaveId` (`docs/domain-model.md`): one id per `ApplyChangesAsync` call, shared by all history rows of that save; a field gets at most one row per save.
- `GetFieldHistoryAsync(key, locale, listItemId)` returns a field's versions newest first; `GetSaveAsync(saveId)` returns the fields of a save with their `PreviousValue` and `CurrentValue` (computed in `ContentService` from the field's other history rows).
- History holds **field values only**. List structure (add, delete, reorder, hide) and section toggles are not in history; a deleted list item takes its history with it.
- **Edit history is kept forever.** The History window restores a version or undoes a whole save, both as pending changes saved with Save page.

### Admin toolbar

`Pages/Shared/_AdminToolbar.cshtml`, at the top of the page (`role="region"`):

| Control | Notes |
|---|---|
| Save page | Disabled until there are pending changes; Ctrl/Cmd+S does the same while edit mode is on |
| Unsaved counter | "No unsaved changes" / "N unsaved changes" (fields plus list and section operations) |
| More | Opens the panel with the rest; hidden from 720 px up, where the panel's controls sit inline |
| Edit mode | Toggle, `aria-pressed`. Off = the page looks exactly as for a visitor |
| Discard | Asks for confirmation, then reloads the page |
| Page settings | Only on a site page |
| "N missing in English" | English pages only; hidden when the count is 0 |
| Language link, Jobs, Log out | Log out is a POST form with an antiforgery token |

A status line and an error list (`role="alert"`) sit below the controls.

### Editing UI (client)

`wwwroot/js/cms-edit.js` is the entry module: it reads `#cms-config` and wires the modules in `wwwroot/js/cms/`. It is loaded only for the admin. Plain ES modules, no framework.

| Module | Role |
|---|---|
| `pending.js` | The pending model. Field changes are keyed by key + locale + list item id and dropped when the value returns to the original; list and section operations live in named **op slots** (`list:{listKey}`, `section:{sectionKey}`) that are replaced as a whole. `toBatch()` puts the ops first, then the `setField` changes |
| `fields.js` | Reads and writes one field element: original value, preview of the new value, `data-cms-dirty` / `-empty` / `-invalid` / `-fallback` flags, hand-off to `pending` |
| `inline.js` | Inline editing of `text` fields (`contenteditable="plaintext-only"`). Enter finishes, Escape reverts. Clicks on editable links are blocked in edit mode |
| `toolbar.js` | Save (one `fetch` POST with the `RequestVerificationToken` header; on an antiforgery 400 it reloads the page HTML once for a fresh token and retries), Discard, counter, More panel, `beforeunload` warning, the `--cms-toolbar-height` CSS variable, the "Saved" message after the reload, messages for 400 / 401 / 403 / 413 |
| `edit-mode.js` | Edit mode on/off. Starts **on**; remembered per tab in `sessionStorage` (`cms-edit-mode`). Off adds the class `cms-view` to `<html>` |
| `fieldbar.js` | The small bar next to a focused or clicked field: "History" for every field, plus "Edit text" / "Edit link" / "Edit image". Clicking a rich-text block, a link or an image opens its window directly |
| `windows.js`, `dialog.js` | `dialog.js` builds the native `<dialog>` windows (focus return, Escape asks before discarding changes); `windows.js` opens the window types (rich text, link, image, history, page settings) |
| `rich-window.js`, `quill-loader.js`, `rich-html.js` | Rich-text window: Quill 2 limited to heading (h3), bold, italic, numbered and bulleted lists, link and clear formatting. `quill-loader.js` loads Quill lazily. `rich-html.js` is a client-side mirror of the sanitizer's allowed tags, for preview only; the server's sanitizer is the authority. Links not starting with `https://`, `mailto:` or `tel:` are refused |
| `link-window.js`, `values.js` | Link window: type picker (Web page / Email / Phone, which adds `https://`, `mailto:` or `tel:`), label, "Remove link". `values.js` holds shared helpers (text normalize, link parse, allowed schemes) |
| `history-window.js` | Loads `historyUrl`; image versions show thumbnails (`imagePreview`) and restore as an image change, and an image that no longer exists cannot be restored; "Restore this version" sets the field as a pending change; "Undo this whole save" loads `savesUrl{saveId}`, lists the fields that differ from before the save, flags those changed again by a later save (undo overwrites them), and sets them back as pending changes |
| `image-window.js`, `image-data.js` | Image window and its shared helpers (see Media Pipeline → Image window). `image-data.js` holds the media API client (`loadAsset` with a per-id cache), `imagePreview`, `recordImage` (puts the image id and the alt texts into `pending`), and builders for the placeholder button and the `<img>` element, so the page shows the new image before Save |
| `settings-window.js` | Page settings: `{page}.seo.title` and `{page}.seo.description` in the current language, with the Greek value shown as guidance when English is empty, plus a "Link preview image" row (`{page}.seo.image` and its alt `{page}.seo.image-alt`) that opens the image window |
| `missing.js` | Counts elements flagged `data-cms-fallback` and jumps to the next one |
| `lists.js` | List editing (below) |
| `sections.js` | Section bar (below) |

**Lists** (`lists.js`): each list item gets an **item bar** (drag handle, Move up, Move down, Hide/Show, Delete), shown while the pointer or keyboard focus is inside the item. Each list ends with **Add item**: it clones the `data-cms-list-template`, gives the copy a new GUID and focuses its first field. Delete asks first (Delete, "Hide instead", Cancel); a saved item has no history, so its deletion cannot be undone after Save. The list operations are **computed from the DOM** against the state at page load, not recorded as clicks: `addListItem` for new ids, `deleteListItem` for missing ids, `setListItemActive` for changed visibility, and one `reorderList` when the order differs from the expected one. The result goes into the slot `list:{listKey}`. Pending edits of a deleted item's fields are dropped. Drag-and-drop uses SortableJS, loaded lazily only when the page has a list; if it fails to load, the buttons still work.

**Sections** (`sections.js`): every `data-cms-section` gets an always-visible **section bar** with Hide section / Show section. The toggle also dims or restores the section's menu links (`data-cms-section-link`) and sets the slot `section:{sectionKey}` with a `setSectionActive` op (empty when the state returns to the original). The same visibility applies to both languages.

**Phone layout** (under 720 px; `wwwroot/css/cms-edit.css` is mobile-first with `min-width: 720px` and `min-width: 960px` breakpoints; the media library grid has 2 columns on phones, 3 from 720 px and 4 from 960 px): the toolbar stays in one line (Save page, counter, More) and More opens a panel below; windows are full-screen (`100vw` x `100dvh`) and become centred dialogs from 720 px; the item drag handle is hidden, so items move with the up/down buttons; buttons in the item and section bars and the toolbar are 44 px (2.75rem) touch targets, shrinking from 720 px; `scroll-padding-top` follows `--cms-toolbar-height` so focused fields are not hidden under the toolbar.

### Vendored libraries

Self-hosted in `wwwroot/lib/`, each folder with a `README.md` (version, source, files, date) and the license. Both load lazily and only for the admin.

| Library | Version | Files | Loaded by |
|---|---|---|---|
| Quill (BSD-3-Clause) | 2.0.3 | `lib/quill/quill.js` (UMD, sets `window.Quill`), `quill.snow.css` | `quill-loader.js`, the first time a rich-text window opens |
| SortableJS (MIT) | 1.15.6 | `lib/sortablejs/sortable.core.esm.js` (core ES module, no plugins) | `lists.js`, `import()` when the page has a list. No auto-scroll while dragging; the up/down buttons cover long lists |

The CSP is own-origin only (`docs/deployment.md`), so admin JS never adds `style=""` attributes or `<style>` elements; setting `element.style` through the CSSOM is allowed.

### Dev sandbox

`/dev/cms-sandbox` and `/en/dev/cms-sandbox` (`Pages/Dev/CmsSandbox.cshtml`; the English route is added in `Program.cs` only in Development). `CmsSandboxModel` returns 404 outside Development. It uses keys under `shared.sandbox*` (a text, a rich-text block, a link, a list with text / rich text / link fields, a plain list, two sections and a `cms-section-link`), so the editing UI can be exercised before the real pages exist. In Phase 4 the UI is verified by driving the Claude browser on this page, not with Playwright.

### Other rules

- **One save per page:** every change (fields, list operations, section toggles) is kept in the browser as pending until the admin clicks **Save page**. Save sends all pending changes in **one request**, written in **one database transaction**, followed by **one** cache eviction. The page warns before navigating away with unsaved changes. Save publishes immediately (no draft/publish workflow). Only changed fields are sent, so two admins editing different fields of a page don't overwrite each other; the same field: last save wins.
- Image files are uploaded and processed when chosen (the file must be processed anyway, `POST /admin/api/media`); assigning the image to the field is a pending change like any other, saved with Save page. A file uploaded but never assigned stays in the library (no cleanup at launch).
- **Page settings window:** per page and language, the SEO fields `{page}.seo.title` (browser tab / search result title) and `{page}.seo.description` (search result snippet), and "Link preview image" `{page}.seo.image` (shown by Facebook, WhatsApp, etc. through the `og:image` tag) with its alt text `{page}.seo.image-alt`.

## Media Pipeline (SkiaSharp)

Implemented in Phase 5 (`Core/Interfaces/IMediaService.cs`, `Infrastructure/Media/`, `Web/Endpoints/MediaEndpoints.cs`, `Web/TagHelpers/CmsImageTagHelper.cs`). Not yet verified on a real iPhone (HEIC) and not yet run on Linux (SkiaSharp native library); both happen on the Phase 8 preview.

```
POST /admin/api/media (multipart "file")
   | antiforgery, body cap 25 MB + 64 KB
   v
MediaService.ImportAsync
   read up to 25 MB -> SHA-256 -> hash already in MediaAssets? -- yes --> return that asset (existed=true)
                                          | no
                                 per-pod gate (1 at a time, waits up to 30 s, else Busy)
                                          v
        ImageProcessor: decode -> sRGB, apply EXIF orientation -> resize -> WebP q80
                                          v
        write public files + original (temp name, then rename) -> insert MediaAsset
                                          | unique violation on the hash (another pod won)? -> return the winner's row
                                          v
                       200 { id, width, height, previewUrl, thumbUrl, sources, ... }
```

### Import (`MediaService`, `ImageProcessor`)

| Step | Rule |
|---|---|
| Size | `MediaLimits.MaxUploadBytes` = 25 MB. The stream is read into memory with the limit enforced while reading; over it = `TooLarge` |
| Duplicates | The SHA-256 of the bytes (lowercase hex) is the `ContentHash`. An existing `MediaAsset` with that hash is returned as is: no second row, no new files (`MediaImportResult.AlreadyExisted`). Two uploads of the same file at once (two pods): the second insert hits the unique index `UX_MediaAssets_ContentHash`; `MediaService` catches the PostgreSQL error `23505` for that constraint only, discards its staged row and returns the winner's row |
| Concurrency | `MediaProcessingGate` (singleton, one slot) allows **one upload at a time per pod**. A waiter gives up after `Media:ProcessingWaitSeconds` (default 30) with `Busy` |
| Format check | The file is decoded with `SKCodec` (never trusted by extension). JPEG, PNG and WebP are accepted. HEIC/HEIF (the Heif codec, or a file that does not decode but has a HEIC `ftyp` brand) = `Heic`; another decodable format = `UnsupportedFormat`; not decodable = `NotAnImage`; over `MediaLimits.MaxPixels` (50 million) = `TooManyPixels` |
| Colour and orientation | Decoded into an sRGB RGBA bitmap, then drawn through the matrix of the EXIF orientation (all 8 origins), so portrait photos come out upright. Re-encoding drops all metadata, including GPS |
| Widths | `MediaLimits.Widths` = 480, 960, 1600, 2400, **only those not wider than the oriented image** (never upscaled). An image narrower than 480 gets **one** file at its own width. Height = `MediaDimensions.ScaledHeight` (rounded, at least 1) |
| Encoding | Lossy WebP, quality `MediaLimits.WebpQuality` = 80, whatever the upload format |
| Files | Public: `{slug}-{first 16 hex chars of the hash}-{width}.webp` under `Media:RootPath` (`MediaFileNames.PublicFileName`, URL prefix `/media/`). Private original: `{full hash}{original extension}` (`.jpg`, `.png`, `.webp`) under `Media:OriginalsPath`, never served (it keeps its metadata, so every size can be regenerated). Every file is written to `{name}.{guid}.tmp` and renamed, so a half-written file is never visible; a failed write deletes the temp file |
| Slug | `Slugs.FromFileName`: the file name without extension, lowercased, accents removed, Greek transliterated (`ου` = `ou`, `θ` = `th`, `χ` = `ch`, `ψ` = `ps`, ...), everything else outside `a-z0-9` becomes `-` (no doubles, none at the ends), at most 50 characters; `image` when nothing is left |
| Row | `MediaAsset` (`docs/domain-model.md`); the stored original file name is cut to 255 characters |

Errors are results (`MediaImportError`: `TooLarge`, `NotAnImage`, `UnsupportedFormat`, `Heic`, `TooManyPixels`, `Busy`), not exceptions. The two folders must exist when a file is written: the app creates them only in Development (see Health Check).

### Admin media API (`Web/Endpoints/MediaEndpoints.cs`)

Group `/admin/api/media`: `Admin` policy, `Cache-Control: no-store`. Records are in `MediaApiModels.cs`. Every error body is `{ "code": "...", "errors": ["message"] }`.

| Endpoint | Behaviour |
|---|---|
| `POST /admin/api/media` | Multipart form with exactly one file in the field `file`; antiforgery token in the `RequestVerificationToken` header. The request body cap is raised to 25 MB + 64 KB (`MaxBodyBytes`) and counted while reading. Success: `200` with the asset (below) plus `alreadyExisted` |
| `GET /admin/api/media?page=N` | The library: 48 per page (`PageSize`), newest first (`CreatedAt`, then `Id`, descending), `{ items, hasMore }`. `page` defaults to 1; anything other than a whole number from 1 up is `400 invalidPage` |
| `GET /admin/api/media/{id}` | One asset, or `404 notFound` |

Asset response: `id`, `width`, `height`, `originalFileName`, `createdAt`, `previewUrl` (the 960 file, or the widest), `thumbUrl` (the narrowest file), `sources` (`[{width, url}]`).

| Status and `code` | Cause |
|---|---|
| 400 `invalidAntiforgery` | Missing or wrong token |
| 400 `noFile` | Not multipart, malformed multipart, no file, more than one file, an empty file, or the file in another field |
| 413 `tooLarge` | Declared or actual body over the cap, or a file over 25 MB |
| 400 `notAnImage`, `unsupportedFormat`, `heic`, `tooManyPixels` | `MediaImportError` of the same name; the `heic` message tells the admin to pick JPEG ("Most Compatible" on iPhone) |
| 503 `busy` + `Retry-After: 5` | The pod's one processing slot stayed taken for the whole wait |
| 401 / 403 | Not signed in / signed in without `amr=mfa` (`AdminApiCookieEvents`) |

Audit events: `media-uploaded` (details `id= existed= size=WxH`) and `media-rejected` (details `code=`); the file name is never logged.

### Image values and save checks

- An `Image` field (`ContentType.Image`) stores the **`MediaAsset.Id`** as its value, **in Greek only** (`el`); English visitors get the Greek value. The photo can be changed from either language's page.
- The alt text is the localized `Text` field with the same key plus `-alt` (`home.hero.image-alt`; for a list item field `image`, the item field `image-alt`; for the page image, `{page}.seo.image-alt`).
- Checks in `ContentBatch` (`SetFieldAsync` per change, then `ValidateImagesAsync` once all changes are applied), so every writer is covered. A violation rejects the whole save (nothing written, `content-save-rejected`):

| Rule | Error |
|---|---|
| An `Image` value for locale `en` | "Images are stored in Greek only" |
| A non-empty value that is not a GUID | "image value must be a media id" |
| A media id with no `MediaAsset` (one `IMediaAssetRepository.ExistAsync` query for all ids in the batch) | "Image '...' does not exist." |
| An image set (in the batch or already saved) while the Greek `{field}-alt` is empty, including when the batch clears the alt of a saved image | "Greek alt text is required." |

Images of a list item deleted in the same batch are skipped. Clearing an image (empty value) is allowed; its alt text may stay.

### `<cms-image>`

`<cms-image key="home.hero.image" sizes="(min-width: 960px) 50vw, 100vw" alt-source="title" loading="lazy|eager" class="..." />`, or `field="image"` instead of `key` inside a `cms-list` (`CmsImageTagHelper`; the alt text is read from `{key}-alt` / `{field}-alt`).

| Attribute | Rule |
|---|---|
| `key` / `field` | One of them, resolved as for the other tag helpers (`CmsFieldResolver`) |
| `sizes` | Required: the `sizes` attribute of the `<img>`; a missing value throws |
| `loading` | `lazy` (default) or `eager` (above-the-fold images); anything else throws |
| `alt-source` | Admin only: the field whose text proposes the alt text. With `key`, a full content key (`shared.sandbox.title`); inside a list, a field of the same item (`title`). Rich text is turned into plain text. Written to `data-cms-alt-proposal` |
| other attributes (`class`, ...) | Kept on the `<img>` |

- **Visitors:** an `<img>` with `src` (the 960 px file, else the widest), `srcset` of every generated width (`url 480w, ...`), `sizes`, `width` and `height` (the source dimensions, so the layout does not jump), `alt`, `loading`, `decoding="async"`. Nothing is rendered when the field is empty, holds a value of another type (warning logged) or refers to a missing asset (warning logged).
- **Admin:** the same `<img>` plus `data-cms-key`, `data-cms-type="image"`, `data-cms-value` (media id), `data-cms-alt-key`, `data-cms-alt` (alt in the current language, empty when English falls back), `data-cms-alt-el` (the Greek alt), `data-cms-sizes`, `data-cms-loading`, `data-cms-alt-proposal` (when `alt-source` is set) and the flags `data-cms-fallback` (English alt falls back to Greek) and `data-cms-empty`. With no image the element is `<button type="button" class="... cms-image-placeholder">Add image</button>`; the layout `class` is kept.
- One query per page loads every image the page refers to (see CMS Editing Flow → Content snapshot).
- **`og:image`** for the page image: see SEO.

### Image window (client)

`wwwroot/js/cms/image-window.js` (`openImageWindow`) opens from a click on an image or "Add image", from the field bar's "Edit image", and from Page settings. Helpers shared with Page settings and History are in `image-data.js`.

- **Upload new:** a file input with `accept="image/jpeg,image/png,image/webp"` (so iOS converts a HEIC photo to JPEG when it is picked). The upload is an `XMLHttpRequest` (for the "Uploading..." then "Processing..." status) with the antiforgery header. Each error code has its own message; `busy` and network errors show "Try again". The uploaded photo is selected at once.
- **Choose existing:** the library grid (2, 3 or 4 columns by width), "Load more" 48 at a time, from `GET /admin/api/media`.
- **Remove image:** clears the choice.
- **Alt text:** Greek is required whenever an image is set (the window refuses to apply and focuses the field). It is pre-filled from `data-cms-alt-proposal` when empty and marked "Suggested from the title - please refine" until edited. On an English page the window shows an optional English alt (visitors see Greek when it is empty) and the Greek alt, and says the photo is shared by both languages. The preview's alt follows what is typed.
- **Apply:** `fields.writeImage` updates the element in place (a new `<img>` with `srcset`, or the placeholder again) and `recordImage` puts up to three pending changes into `pending`: the image id (locale `el`), the alt in the page language and, on an English page, the Greek alt. Nothing is written until Save page; the server checks above are the authority.
- **History** (`history-window.js`) shows thumbnails for image versions and restores them; **Page settings** (`settings-window.js`) has the "Link preview image" row.

### Serving and configuration

- **Production:** nginx serves `/media/` from `Media:RootPath` with `Cache-Control: public, max-age=31536000, immutable` (see `docs/deployment.md`). File names contain the content hash, so a file never changes.
- **Development:** the app serves `Media:RootPath` itself at `/media` (`Web/DevelopmentMediaFiles.cs`, registered only in Development, before the other middleware): `.webp` files only (`ServeUnknownFileTypes` off), immutable cache header. Originals are never served.
- `Media:RootPath` (`/var/saronida/media`; Development `data/media`) and `Media:OriginalsPath` (`/var/saronida/data/originals`; Development `data/originals`) are both required and resolved against the content root. Startup fails when either is missing, or when `OriginalsPath` is inside `RootPath` (originals must never end up under the served folder). `Media:ProcessingWaitSeconds` (default 30, not in `appsettings.json`) must not be negative.
- Health: the `media` check covers both folders (see Health Check).

Linux native assets: `SkiaSharp.NativeAssets.Linux.NoDependencies` (nothing to install in the container). Publish is not RID-specific; the native library lands under `runtimes/linux-x64/native/` in the output. The first real run on Linux is the Phase 8 preview.

**Video: not at launch.** The hero uses a photo. Self-hosted video would need pre-compressed uploads (no ffmpeg on the small droplet) and a check of Cloudflare's CDN terms on serving video. Revisit with the owner later.

**Not built:** deleting media (the library has no delete at launch) and the cleanup job for files nothing refers to (see Background Jobs).

**Open: real-iPhone HEIC test.** The browser should hand a JPEG to the file input; a HEIC file that reaches the server is rejected with the `heic` error. Whether an iPhone really does this can only be tested over HTTPS, so it is done on the Phase 8 preview (the PLAN.md task stays open until then).

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

The database backup is not a Hangfire job: a nightly `pg_dump` Kubernetes CronJob does it (agreed design, see `docs/deployment.md` → Backups).

- Storage: `Hangfire.PostgreSql` 1.21.1 (`Infrastructure/DependencyInjection.cs`), schema `hangfire` in the content database, same connection string (`ConnectionStrings:Hangfire` no longer exists). The schema is installed by `migrate` (or at startup in Development), never by a pod in production: `PrepareSchemaIfNecessary` equals `Database:MigrateAtStartup`.
- Server: Hangfire 1.8.25, **2 workers**, registered in `AddInfrastructure`. **Every pod runs a server**; Hangfire's distributed locks keep each recurring job running once. **Implemented so far: storage, server and the dashboard** - no jobs yet, so the single-run behaviour is checked in the Phase 8 two-pod rehearsal.
- Dashboard at `/admin/jobs`, mapped in `Web/Program.cs` with `MapHangfireDashboardWithAuthorizationPolicy(AdminPolicy.Name, ...)`: only requests that satisfy the `Admin` policy (authenticated + `amr=mfa`) get in; the dashboard's own authorization filter list is empty. Hangfire's antiforgery works with our `Secure` / `SameSite=Strict` cookies (`HangfireDashboardTests`).
- Every job must be safe to run twice.
- Possible later job: delete media files never referenced by any content or history (uploads discarded before Save). Not in scope until needed.

## Health Check — `/health`

Returns **200 "Healthy"** only if every check passes, otherwise **503 "Unhealthy"** (ASP.NET Core defaults). Body is the status word only — no details. Mapped in `Web/Program.cs`. The startup migration check runs before the app accepts requests, so a database that is down or behind means no answer at all (and a `FATAL Startup failed` line, see Logging).

| Check (name) | Where | How |
|---|---|---|
| `database` | `Infrastructure/Health/DatabaseHealthCheck.cs` | `CanConnectAsync` on the content database |
| `hangfire` | `Infrastructure/Health/HangfireStorageHealthCheck.cs` | `GetMonitoringApi().GetStatistics()` against the Hangfire storage (schema `hangfire`) |
| `media` | `Infrastructure/Health/MediaFolderHealthCheck.cs` | Write and delete a temp file under `Media:RootPath`, then under `Media:OriginalsPath`; unhealthy ("Media folder is not writable." / "Originals folder is not writable.") when either fails |
| `home-page` | `Web/Health/HomePageHealthCheck.cs` | GET of `Health:HomePageUrl` through a named `HttpClient` (5 s timeout, redirects not followed); must return exactly 200 |

**Planned, Phase 7:** a fifth check, "at least one Greek `ContentItem` for page `home` exists (e.g. `home.hero.title`)". It needs the seed data, so it is deferred (recorded in `PLAN.md`).

Configuration keys:

| Key | Production (`appsettings.json`) | Development |
|---|---|---|
| `Health:HomePageUrl` | `http://127.0.0.1:8080/` (the container's own port) | `http://localhost:5000/` |
| `Media:RootPath` | `/var/saronida/media` | `data/media` |
| `Media:OriginalsPath` | `/var/saronida/data/originals` | `data/originals` |

The media and originals folders are created at startup **only in Development** (`MediaFolder.EnsureExists` in `Web/Program.cs`). Elsewhere they are bind mounts, and a missing or unwritable folder must fail `/health` rather than be silently created inside the container.

**Not checked:** Instagram (an outage there must not fail health and roll back a good deploy) and maintenance mode (handled by nginx, see `docs/deployment.md`).

Reachable only from the droplet itself — nginx denies `/health` from the internet (nginx config planned, Phase 8). Used by `deploy.ps1` after every deploy and, planned, by the pod's readiness and liveness probes.

A health-check publisher also runs the checks every 30 s and logs state changes (see Logging → Diagnostics).

## Admin Accounts

- No registration page. Admins are managed only with CLI commands built into the web app, run on the droplet (they go through Identity, so password hashes and stamps are always valid — never insert users with SQL).

**CLI** (`Web/Cli/AdminCommandRunner.cs`): `dotnet SaronidaSailing.Web.dll admin <command>`. When the first argument is `admin`, `Program.cs` builds the host without the command-line args, runs `InitializeDatabaseAsync` (migrates in Development; elsewhere it only fails if migrations are pending), runs the command and exits without starting the web server.

| Command | Does |
|---|---|
| `admin add <email>` | Prompts for the password, creates the admin (2FA is set up at first login) |
| `admin reset-password <email>` | Prompts for a new password; clears lockout and the failed-attempt count, and changes the security stamp (existing sessions end within a minute). 2FA is left as it is |
| `admin list` | Lists admins: email, 2FA on/off, and "locked until" when locked out |
| `admin remove <email>` | Asks `[y/N]`, then deletes the admin |

- Exit codes: `0` success, `1` failure (password mismatch, policy error, unknown admin, remove not confirmed, no TTY), `2` wrong usage.
- Passwords are never arguments (shell history). They are typed twice with hidden input (`ConsolePasswordPrompt`), which needs an interactive terminal: with redirected input the command fails with a message. Run on the droplet through the host script `saronida-admin`, which wraps `kubectl exec` into the app pod (no `-T`/non-TTY mode; agreed design, Phase 8).
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
| `app-YYYY-MM-DD.log` | Warnings and errors from all loggers, plus the `Lifecycle` and `Diagnostics` loggers from Info up |
| `metrics-YYYY-MM-DD.log` | The per-minute resource snapshot (see Diagnostics) |

- **Shared by all pods.** Every pod writes the **same daily files**; the targets are `NLog.Targets.AtomicFile` (type `AtomFile`, `concurrentWrites` and `keepFileOpen` on; NLog 6's standard file target has no `concurrentWrites`), so appends from several processes neither lose nor interleave lines (`AtomicFileConcurrencyTests`).
- **Pod name on every line:** the second field of every app, audit, metrics and console line. It comes from `Logs:Instance`, defaulting to the machine name (in Kubernetes the container's hostname is the pod name). `Web/LogInstance.cs` rejects a value that is empty or contains `|` or a control character, which fails startup.
- Lines start with a UTC ISO 8601 timestamp. Never in the database.
- Line layouts (fields separated by `|`):

| Target | Layout |
|---|---|
| Audit | `time|pod|event|user=…|ip=…[|details]` |
| App file and console | `time|pod|LEVEL|logger|message[|exception]` |
| Metrics | `time|pod|key=value key=value …` |

- Routing rules in `appsettings.json`, first match wins: `Audit` → audit file only; `Lifecycle` Info and up → app file and console; `Metrics` Info and up → metrics file only; `Diagnostics` Info and up → app file and console; `Microsoft.Hosting.Lifetime` Info → console; `Microsoft.*`, `System.*`, `Hangfire.*` up to Info → dropped; everything else from Warn → app file and console. The console feeds the container log (`kubectl logs`).
- **Retention:** `maxArchiveDays: 90` on the audit and app files, **30** on the metrics file. Because the file names carry the date, NLog deletes older files when a new day's file is opened — no Hangfire cleanup job needed. Verified by `LogRetentionTests`.
- IP addresses in logs are personal data: the Privacy page must mention the logs and the 90-day retention.

**Audit line format** (`Infrastructure/Logging/AuditLog.cs`): the event part is `event|user=…|ip=…[|details]` (the pod name comes right before it, see above); an empty user or IP is written as `-`. Fields can carry attacker input (the email typed on the login form), so each is sanitized: control and line/paragraph-separator characters become a space, `|` becomes `/`, and it is cut at 256 characters.

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
| `content-saved` | A content batch was committed (details: `pages=` touched page keys, counts of `fields`, `list-added`, `list-deleted`, `reordered`, `list-activated`, `sections`, and `save=` the save id) |
| `content-save-rejected` | A content batch had an invalid change and nothing was written (details: error count and the first error) |

The user field of the content events is the editor's email.

### Diagnostics

Code in `Web/Diagnostics/`. Goal: find out afterwards what went wrong. A pod killed by the out-of-memory killer or `SIGKILL` cannot log its own death, so **a `Started` line with no `Stopped` before it means the previous run was killed**; Kubernetes and the droplet record the kill from outside (event recorder, memory limits, journal: Phase 8, `docs/deployment.md`).

**Lifecycle** (logger `Lifecycle`, level Info; web app only, not the `admin` / `migrate` commands; `LifecycleLogger`):

| Line | Written when |
|---|---|
| `Started version=… environment=… dotnet=… cpus=N memoryLimit=NMiB gcHeapLimit=NMiB pid=N` | `ApplicationStarted`. `version` is the assembly informational version (includes the commit when the build provides it, else `unknown`) |
| `Stopping reason=… uptime=HH:mm:ss` | `ApplicationStopping`. `reason` is `SIGTERM`, `SIGINT`, `SIGQUIT` or `requested` (no signal seen; `ShutdownSignalRecorder` waits up to 250 ms for the signal handler) |
| `Stopped uptime=HH:mm:ss` | `ApplicationStopped`; flushes the log targets |

**Crashes** (`CrashLogging`, logger `Lifecycle`, flushed for up to 5 s before the process dies):

| Line | Level | When |
|---|---|---|
| `Unhandled exception terminating=True/False` + exception | FATAL | `AppDomain.UnhandledException`, including an `OutOfMemoryException` escaping a thread |
| `Unobserved task exception` + exception | ERROR | `TaskScheduler.UnobservedTaskException`; the process keeps running, so it is not FATAL |
| `Startup failed` + exception | FATAL | An exception escaped `Program.cs` before the host started (bad config, pending migrations, database down) |
| `Host terminated unexpectedly` + exception | FATAL | The same, after the host had started |

`OperationCanceledException` and `HostAbortedException` are not logged as a crash.

**Resource snapshot** (`ResourceSnapshotService`, logger `Metrics`, file `metrics-YYYY-MM-DD.log`): one line per `Diagnostics:SnapshotIntervalSeconds`, the counters reset at each snapshot:

`workingSetMiB= gcHeapMiB= memoryLimitMiB= gcHeapLimitMiB= memoryPct= cpuPct= gc0= gc1= gc2= threads= queue= inFlight= requestsPerMin= errors5xxPerMin= rateLimitedPerMin=`

| Field | Source |
|---|---|
| `memoryLimitMiB` | The **container's** limit (`ContainerMemoryLimit`): cgroup v2 `/sys/fs/cgroup/memory.max`, else cgroup v1 `/sys/fs/cgroup/memory/memory.limit_in_bytes`, else the machine's memory (`max`, a non-number or a value of 2^60 and more counts as unlimited) |
| `gcHeapLimitMiB` | `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes`: the GC's own cap, which .NET defaults to 75% of the container limit; logged next to the container limit on purpose |
| `memoryPct` | `workingSet / memoryLimit` (0 when no limit is known) |
| `cpuPct` | Process CPU time since the previous snapshot, divided by elapsed time and CPU count |
| `threads`, `queue` | Thread-pool thread count and pending work items |
| `inFlight`, `requestsPerMin`, `errors5xxPerMin`, `rateLimitedPerMin` | `RequestCounters`, fed by `RequestDiagnosticsMiddleware`; it counts every request the app handles, so `requestsPerMin` includes `/health` and the home-page health check's own request to `/` |

**Threshold warnings** (logger `Diagnostics`, app file and console; `ThresholdAlert` logs on crossing, repeats every `WarningRepeatMinutes` while still breached, and logs once when back to normal):

| Warning (Info when cleared) | Condition |
|---|---|
| `Memory high memoryPct= workingSetMiB= memoryLimitMiB=` / `Memory back to normal …` | `memoryPct` above `MemoryWarningPercent` |
| `Thread pool queue high queue= threads=` / `Thread pool queue back to normal …` | `queue` at or above `ThreadPoolQueueWarning` |
| `Resource snapshot failed` | Reading the process values threw |

**Requests** (`RequestDiagnosticsMiddleware`, logger `Diagnostics`, Warning; placed right after `UseForwardedHeaders`, so it sees every request and the client IP is the forwarded one). A request that throws through the middleware is counted as 500.

| Line | When |
|---|---|
| `Slow request method= path= status= duration=Nms` | Duration above `SlowRequestMilliseconds` |
| `Rate limited method= path= ip=` | Status 429; at most `RateLimitedLinesPerMinute` lines per UTC minute (the counter in `rateLimitedPerMin` is not capped) |

`path` is the path without the query string, cut at 256 characters; `|` becomes `/` and control or whitespace characters become a space (log injection).

**Health transitions** (`HealthTransitionPublisher`, an `IHealthCheckPublisher`, first run 5 s after start, then every `HealthCheckPeriodSeconds`): logs changes only, per check (`database`, `hangfire`, `media`, `home-page`) and for `overall`.

| Line | Level |
|---|---|
| `Health check {name} {previous} -> {status}: {description}` (`overall` lists `failing: a, b`) | Warning |
| `Health check {name} recovered: {previous} -> Healthy` | Info |
| `Health check {name} {status} at startup: {description}` | Warning; first run only, per check (not for `overall`) |

**Settings** (`Diagnostics:*`, `Web/Diagnostics/DiagnosticsOptions.cs`; defaults are in code, `appsettings.json` has no `Diagnostics` section; every value must be greater than 0 or startup fails):

| Key | Default | Meaning |
|---|---|---|
| `SnapshotIntervalSeconds` | 60 | Metrics line interval |
| `SlowRequestMilliseconds` | 2000 | Slow-request threshold |
| `MemoryWarningPercent` | 80 | Memory warning threshold (% of container limit) |
| `ThreadPoolQueueWarning` | 100 | Thread-pool queue warning threshold |
| `WarningRepeatMinutes` | 10 | How often a still-active warning repeats |
| `RateLimitedLinesPerMinute` | 20 | Cap on `Rate limited` lines per minute |
| `HealthCheckPeriodSeconds` | 30 | Health publisher period |

## Security

| Concern | Measure |
|---|---|
| Admin login | Identity cookie auth + mandatory TOTP 2FA; lockout; per-IP rate limiter on login (see Admin Accounts) |
| Real client IP | nginx `realip` module sets the client IP from `CF-Connecting-IP` (Cloudflare ranges only) and forwards it in `X-Forwarded-For`. The app (`UseForwardedHeaders`, `Web/ForwardedHeadersSetup.cs`) reads `X-Forwarded-For` and `X-Forwarded-Proto` with `ForwardLimit = 1`, and trusts them **only** from the networks in `ForwardedHeaders:KnownNetworks`. It is not `127.0.0.1`: host nginx reaches the pod through the Service NodePort, so the app sees the request coming from the cluster network. `appsettings.json` still carries the compose-era value `172.30.0.0/24`; Phase 8 sets it to the k3s network (see `docs/deployment.md`) |
| Rate limiting | `UseRateLimiter` with one global partitioned limiter (`Web/Identity/AdminLoginRateLimit.cs`): only **POSTs** to `/admin/login`, `/admin/login/code`, `/admin/login/recovery` and `/admin/two-factor/setup` are limited, 10 per minute per client IP, fixed window, no queue. Rejection: 429, `Retry-After: 60`, plain-text message, audit event `login-rate-limited`. GETs and all other paths are not limited |
| Lockout | 5 failed attempts → 15 minutes, per account; TOTP and recovery failures count |
| CSRF | Antiforgery tokens on all admin write endpoints (the save API reads the `RequestVerificationToken` header, token from `#cms-config`); antiforgery cookie `Secure`, `HttpOnly`, `SameSite=Strict` |
| Admin API | `/admin/api/*` requires the `Admin` policy, answers 401 / 403 instead of a login redirect, sends `Cache-Control: no-store`, and rejects bodies over 256 KB with 413 |
| Admin pages in caches | Pages rendered for an admin carry `Cache-Control: no-store` and are never stored by the output cache |
| XSS | Razor encoding by default; rich text sanitized with HtmlSanitizer on save, inside `ContentService` (see CMS Editing Flow); link URLs restricted to `https:`, `tel:`, `mailto:` and validated on save |
| Transport | Cloudflare SSL mode **Full (strict)**; Let's Encrypt cert on the origin |
| Uploads | `POST /admin/api/media` is admin-only with antiforgery; the file is validated by decoding (not by extension or content type), limited to 25 MB and 50 megapixels, one at a time per pod; re-encoding strips all metadata; originals are kept outside the served folder, and startup fails if they would be inside it. Only `.webp` is served from the media folder in Development; file names are generated from a slug and the content hash, never from the raw upload name |

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

Edit mode is designed for **laptop/desktop**. On phones (under 720 px) it works for quick fixes: the toolbar collapses behind a More button, windows go full-screen, and up/down buttons replace dragging (see CMS Editing Flow → Editing UI).

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
