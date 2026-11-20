# Architecture

Status: **Phases 0-7.5 are implemented (editing fixes in 7.5: map window, editable header and footer menus, per-page Index/Follow, Site settings page with robots.txt, Meta App ID and secret on the admin Instagram page; decisions, foundation, admin authentication, CMS core, inline editing, PostgreSQL and two-pod readiness: cross-pod cache eviction, shared logs, runtime diagnostics; media: upload pipeline, library, `<cms-image>`, image window; Instagram feed: Connect flow, encrypted token, fetch and renewal jobs, section switch, toolbar warning; public pages: layout, all seven pages, startup content seed, cookie consent, SEO endpoints, error pages, old-site redirects, accessibility pass). Open until the Phase 8 preview: the real-iPhone HEIC upload test (needs HTTPS), every check against the real Meta app (Phase 6 is verified against stub Instagram answers only, see Instagram Feed → Not yet verified), and a repeat of the axe-core run (see Accessibility). The k3s deploy is agreed design, not yet built.** Update as phases land.

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
| Tests | xUnit on real PostgreSQL via Testcontainers | Database tests need Docker, so they run on the developer's PC, not in cloud sessions. No Playwright: the UI is checked in the built-in Claude browser, with axe-core 4.10.2 injected into each page (see Accessibility) |
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
| Contact form / email provider | Contact page has no form (phone, email link, Instagram profile link) |
| Google Fonts CDN | Fonts are self-hosted (sending visitor IPs to Google was ruled a GDPR violation by a Munich court, 2022) |
| CI/CD pipeline | Deploys run from the developer's PC (see `docs/deployment.md`) |

## Solution Structure

```
SaronidaSailing.slnx
global.json, Directory.Build.props, Directory.Packages.props, nuget.config
.config/dotnet-tools.json          dotnet-ef local tool
src/
  SaronidaSailing.Core/            Entities/ (BaseEntity, ApplicationUser, Content*, MediaAsset, MediaSource, InstagramPost, IntegrationSetting), Interfaces/ (repositories, IContentService, IContentSeeder, IContentSeedSource, ContentSeed, IDistributedLock, IMediaService, MediaImportResult, IRichTextSanitizer, IUnitOfWork, IPageCacheInvalidator, ICacheTagEvictor, ICacheEvictionBus, PageContent, PageImage, ISectionActivationHandler, ISecretProtector, IInstagram* services, InstagramApiException, InstagramApiModels), Services/ (ContentService, ContentBatch, ContentSeeder, ContentKeys, ContentLocales, ContentLink, ContactMap, MediaFileNames, MediaLimits, MediaDimensions, Slugs, Instagram* services and constants); no EF, no ASP.NET
  SaronidaSailing.Infrastructure/  Data/ (AppDbContext, DatabaseInitializer, DatabaseSettings, UnitOfWork, Configurations/, Migrations), Caching/ (PostgresCacheEvictionBus, PostgresCacheEvictionListener, CacheEvictionChannel), Repositories/, Content/ (RichTextSanitizer), Health/ (incl. HomeContentHealthCheck), Logging/ (AuditLog), Media/ (MediaService, ImageProcessor, MediaProcessingGate, MediaOptions, MediaFolder), Instagram/ (InstagramApiClient, InstagramDtos, InstagramOptions, DataProtectionSecretProtector, InstagramServiceCollectionExtensions), Jobs/ (InstagramFetchJob, InstagramTokenRefreshJob, InstagramJobQueue, InstagramJobScheduler, HangfireDistributedLock), DependencyInjection.cs
  SaronidaSailing.Web/             Program.cs, SiteOptions.cs, LogInstance.cs, DevelopmentMediaFiles.cs, Caching/ (CacheTags, OutputCachePageInvalidator, OutputCacheTagEvictor), Diagnostics/ (lifecycle, crash, metrics, request and health logging), Content/ (PageContentAccessor, CmsEditContext, CmsEditorConfig), Endpoints/ (ContentEndpoints, ContentApiModels, MediaEndpoints, MediaApiModels, AdminEndpoints, SeoEndpoints), PreviewEnvironment.cs, ErrorPageStatus.cs, Redirects/ (OldSiteRedirects, OldSiteRedirectMiddleware), Seed/ (embedded `{group}.json` seed files) + Content/EmbeddedContentSeedSource, TagHelpers/, ViewComponents/ (InstagramFeedViewComponent), Models/ (InstagramFeedViewModel, TripTabs, BoatGallery, LegalPageViewModel, SiteLogoViewModel), Instagram/ (InstagramOAuthState, InstagramPaths, AthensTime), Cli/ (admin and migrate commands), Identity/, Localization/ (SitePages, SiteCultures, UiText, ...), Health/, Pages/ (the seven site pages, Pages/Admin/, Pages/Dev/, Pages/Errors/, Pages/Shared/ with the layout partials, Pages/Shared/Components/InstagramFeed/), wwwroot (css/ (`site.css` is the public stylesheet, see docs/conventions.md -> CSS and fonts), fonts/commissioner/, js/ (public modules, see Public Pages), js/cms/, lib/quill/, lib/sortablejs/), appsettings*.json
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
- **Migrations:** `InitialCreate` (`20261111081638_InitialCreate`) holds the Identity tables and the four content tables; `AddMediaAssets` (`20261111133422_AddMediaAssets`) adds `MediaAssets`; `AddInstagram` (`20261113073900_AddInstagram`) adds `InstagramPosts`, `IntegrationSettings` and `MediaAssets.Source` (see `docs/domain-model.md`). The earlier SQLite migrations were deleted when the provider changed.
- **Who applies migrations** (`Infrastructure/Data/DatabaseInitializer.cs`, flag `Database:MigrateAtStartup`, `DatabaseSettings`): `InitializeDatabaseAsync` runs in `Web/Program.cs` before the app accepts requests. With the flag `true` (set only in `appsettings.Development.json`) it calls `MigrateAsync`: applies pending EF migrations **and** installs or upgrades the Hangfire schema (`PostgreSqlObjectsInstaller.Install`, schema `hangfire`). With the flag `false` (the default in `appsettings.json`, so preview and production) it calls `EnsureUpToDateAsync`, which throws "The database is behind: N pending migration(s): ... Run the 'migrate' command first." Pods never migrate or install the Hangfire schema themselves (Hangfire's `PrepareSchemaIfNecessary` follows the flag). In production the `migrate` command runs as a Kubernetes Job before each rollout (agreed design, Phase 8).
- Entity configurations are in `Infrastructure/Data/Configurations/` (one per content entity). Repositories (`Infrastructure/Repositories/`: `ContentItemRepository`, `ContentListItemRepository`, `ContentSectionRepository`, `ContentHistoryRepository`) only **stage** changes; `IUnitOfWork` (`Infrastructure/Data/UnitOfWork.cs`) has `SaveChangesAsync` (one commit, one transaction) and `DiscardChanges`. All are scoped (`AddInfrastructure`).
- Connection string `ConnectionStrings:Content` is an Npgsql string; a missing value fails startup. The same string serves EF Core, Hangfire storage and the cache-eviction bus and listener. `appsettings.json` has no password (`Host=postgres;Port=5432;Database=saronida;Username=saronida`); `appsettings.Development.json` points at the local container (`Host=localhost`).
- **Local PostgreSQL:** `docker compose -f deploy/dev/docker-compose.yml up -d` starts `postgres:18.6-alpine` on `127.0.0.1:5432` (database and user `saronida`, named volume `saronida-dev-pgdata`).
- **`migrate` command** (`Web/Cli/MigrateCommand.cs`): `dotnet SaronidaSailing.Web.dll migrate`. When the first argument is `migrate`, `Program.cs` builds the host without the command-line args, runs `MigrateAsync` and exits without starting the web server. Prints `Applied N migration(s): ... Hangfire schema up to date.` (or `No pending migrations.`). Exit codes: `0` success, `1` failure (message and exception on stderr), `2` wrong usage (any extra argument).

## Pages (from the "Saronida Sailing Redesign" design canvas)

Canvas: https://claude.ai/artifact/RcU5iMfQSTJ34rH5gFB7MC

All seven pages are built (Phase 7). Section keys are `{page}.{section}`; the full key list is in `docs/domain-model.md` → Public page content keys.

| Page | Greek URL | English URL | Sections (`cms-section` keys) |
|---|---|---|---|
| Home | `/` | `/en/` | `home.hero` · `home.glance` · `home.trips` with the cards `home.trip-day` / `-islands` / `-week` (each its own section) · `home.boat` · `home.why` · `home.instagram` (view component) · `home.departure` · `home.sticky` (mobile "Book" bar) |
| Trips | `/ekdromes` | `/en/trips` | `trips.intro` · `trips.day` / `trips.islands` / `trips.week` (the three tab panels) · `trips.booking` (`id="booking"`) · `trips.info` (accordion) · `trips.sticky` |
| Boat | `/skafos` | `/en/boat` | `boat.gallery` (cover + list, "All photos" lightbox) · `boat.intro` · `boat.specs` · `boat.life` · `boat.included` · `boat.charter` · `boat.booking` (`id="booking"`) · `boat.sticky` |
| Contact | `/epikoinonia` | `/en/contact` | `contact.intro` (four tile links) · `contact.where` (map spot + directions) · `contact.details` · `contact.cta`. The map image, notice and embed URL are `contact.map.*` fields inside `contact.where`; the embed URL is edited in the map window (see CMS Editing Flow → Map window) |
| Terms | `/oroi` | `/en/terms` | `terms.intro` · `terms.summary` · `terms.body` (list `terms.sections`) · `terms.contact` |
| Privacy | `/aporrito` | `/en/privacy` | the same four sections with the prefix `privacy` |
| Accessibility | `/prosvasimotita` | `/en/accessibility` | the same four sections with the prefix `accessibility` |
| Shared | — | — | Header (logo, editable menu, language switch, Book button) · Footer (tagline, editable pages menu, contact, social, copyright, "Cookie settings") · cookie banner, in `Pages/Shared/_Header`, `_Footer`, `_Logo`, `_CookieConsent`. The logo is the inline SVG in `_Logo.cshtml` and is not editable: a different logo is a code change |
| Errors | `/error/404`, `/error/410`, `/error/500` | same, language from the original path | Own layout `_ErrorLayout` (see Error pages) |

Greek slugs are Latin transliterations (Greek letters get percent-encoded when URLs are shared). Slugs are **fixed in code**, not admin-editable — changing a URL later hurts search rankings. A slug of the other language is a 404 (`/en/skafos`, `/boat`).

The cookie-consent banner and the footer "Cookie settings" button from the design **are built** (needed for the Google Map — see Cookies & Privacy). The banner's "Statistics" category is dropped.

### Public Pages

How the public pages are built (`Web/Pages/`, `Web/Pages/Shared/`):

| Topic | Rule |
|---|---|
| Legal pages | Terms, Privacy and Accessibility share one partial `_LegalPage` (`Models/LegalPageViewModel`); their page models derive from `Pages/LegalPageModel` and each keeps its own output-cache tag. Per page: `{page}.intro` (eyebrow, title = the `<h1>`, `updated`), `{page}.summary` (title, list `summary-items`), `{page}.body` with the list `{page}.sections` (`title` Text → `<h2>`, `body` RichText), `{page}.contact` (lead, label; the whole card links to Contact). Numbering is a CSS counter, so titles carry no numbers and reordering cannot break it. The "On this page" table of contents is built on the server from the same list (visitors: active items, none when the body section is hidden; admin: all) and links to `#section-{itemId}`, set by `cms-item-id-prefix` on the `cms-list`. Terms and Privacy link to each other under "See also" |
| Header | One `#site-menu` element (nav, language link, Book) laid out inline from 960 px and as a JS-toggled panel below; without JS the panel is in flow and the menu button is hidden. Mobile: Home = logo + language link + menu button; inner pages = back link + page title + menu button. The menu is the editable list `shared.menu-header` (see CMS Editing Flow → Menus); the language link and the Book button stay fixed, and the Book button sits in the section `shared.book` |
| Footer | Dark; tagline, Pages nav (the editable list `shared.menu-footer`; the "Cookie settings" button stays the last item, fixed), Contact (`shared.contact.phone`, `phone-2` as `tel:` links, `shared.contact.address`), Follow us (icon-only links `shared.social.instagram` / `facebook`), `shared.footer.copyright`, "Cookie settings" button (hidden without JS) |
| Fixed UI text | Page names (mobile header title, the page list in the link window), tab labels, button labels, cookie-banner buttons and status messages, map title and similar are `UiText` (`Web/Localization/UiText.cs`): `UiString(Greek, English)` properties with `.For(locale)` and `.Current`; a test checks both languages are filled. Everything the owner may want to reword is content instead |
| Trip tabs | The tabs are links `?trip=day\|islands\|week` that work without JS: the server renders the selected tab (`TripTabs.Normalize` / `Resolve`: an unknown value becomes `day`, a hidden trip becomes the first active one; the admin always sees all three). `trip-tabs.js` upgrades them to ARIA tabs (`role="tab"`, automatic activation, roving tabindex) and updates the URL with `history.replaceState`. The canonical URL stays `/ekdromes`. Home's trip cards link to `/ekdromes?trip=…` |
| Boat gallery | Cover (`boat.gallery.cover`, eager) plus list `boat.gallery-photos`; with JS only the cover and four photos show, without JS or in edit mode all. "All photos (N)" (`BoatGallery.CountPhotos`: cover + active items that really have an image) opens a native `<dialog>` lightbox; the button and dialog exist only when N > 0 |
| Sticky bars | `home.sticky`, `trips.sticky`, `boat.sticky` are mobile-only bottom bars ("Book" / "Dates" jump to `#booking` or link to Trips); from 960 px they are static for the admin so they stay editable. The sticky booking card is offset by `--cms-toolbar-height` for the admin |
| Back to top | `site.js`: shown after one viewport of scroll, focuses the visible header home or back link |

Public JS modules (`wwwroot/js/`, plain ES modules loaded by `site.js` from `_Layout` for every visitor; the admin modules in `js/cms/` are separate):

| Module | Role |
|---|---|
| `site.js` | Entry: header menu (Escape returns focus, click outside closes), back-to-top, then initialises the modules below |
| `trip-tabs.js` | Upgrades the trip tab links to ARIA tabs |
| `gallery.js` | The boat lightbox: slides are the grid's rendered `<img>`s (so no image key renders twice), `showModal`, previous / next wrap, arrow keys, swipe (`touch-action: pan-y`), Escape / Close, focus returns to "All photos", scroll lock with `html:has(dialog.lightbox[open])`. Tile clicks open it for pointer users but not in edit mode (`#cms-config` present without `html.cms-view`) |
| `consent.js` | The cookie banner and settings (see Cookies & Privacy) |
| `contact-map.js` | Adds and removes the Google Map `<iframe>` according to consent |

Page content rules (tech lead decisions that every page follows):

- **Internal links** in page templates (Trips, Boat, Contact, Terms ...) have fixed targets in code and an editable `Text` label. A Link field can also point to a site page (`page:{key}`, see CMS Editing Flow → Site page links); the menus use that. Trip links carry `?trip=day|islands|week`.
- **Prices live once**: `shared.prices.day|islands|week`, rendered on Home, Trips and Boat. The **charter costs** (package, deposit, fuel) are one shared list `shared.charter-costs`, rendered on Trips (weekly panel) and Boat (charter details). Trip types are fixed in code (`day`, `islands`, `week`).
- **An image key renders at most once per page**: the editor's image preview replaces its element, so a second copy would not update. (A non-image field rendered twice, such as the header and footer Instagram link, is previewed in every copy.)
- Image spots keep their aspect box with a striped placeholder behind; the Home hero shows the canvas illustration until a photo exists. The hero is one `<cms-image>` (no video).
- Unknown business values (prices, guest numbers, email, hours, season, booking platforms) are seeded as visible bracketed placeholders (`[ΤΙΜΗ]` / `[PRICE]`, ...) so gaps show on the preview; the booking links seed a placeholder `https://` URL.

### Error pages

`Pages/Errors/NotFound` (`/error/404` and `/error/410`) and `ServerError` (`/error/500`) use their own layout `_ErrorLayout`: logo, fixed `UiText` in both languages, `<meta name="robots" content="noindex">`, `Cache-Control: no-store`, **no content read** (a test with a throwing content service proves it), and no header, footer, consent banner or JSON-LD. The language comes from the original path (`SiteCultures.ForPath`, base class `ErrorPageModel`), so `/en/nothing` answers an English 404 with `/en/...` links. The old `Pages/Error` is gone.

The pipeline (see Request Flow) re-executes `/error/{0}` through `UseStatusCodePagesWithReExecute`, but `Web/ErrorPageStatus.cs` disables that per request unless the status is 404 or 410 **and** the path is not `/admin/api`, `/health`, `/media`, `/sitemap.xml` or `/robots.txt`, so API and file answers stay empty. `UseExceptionHandler("/error/500")` runs outside Development.

## Localization

- Languages: Greek (`el`, default) and English (`en`).
- URLs: Greek has no prefix (`/`, `/skafos`); English lives under `/en/...`. Adding a language later = a new prefix.
- Language switcher in the header links to the same page in the other language.
- **Routing:** `Web/Localization/SitePages.cs` is the single table of the 7 pages (key, Razor page path, Greek slug, English slug). `LocalizedPageRouteConvention` replaces each listed page's routes with exactly two: the Greek slug and `/en/{english slug}`. The default `/Index`, `/Trips` ... routes are not routable. Adding a page = a row in `SitePages` plus the Razor page.
- **Culture:** `UrlPrefixRequestCultureProvider` is the only request-culture provider (the default cookie and `Accept-Language` providers are removed): a path starting with `/en` is English, anything else Greek. Supported cultures and the prefix are constants in `SiteCultures.cs`. The layout sets `<html lang>` from the request UI culture.
- **URL helper:** `ILocalizedUrlService` (`LocalizedUrlService`, scoped) builds the path of a page key in a given culture (`GetPath`), finds the current page from the route (`GetCurrentPage`) and returns the current page's path in the other language (`GetAlternatePath`, for the language switcher).
- Every site page emits `<html lang>`, `hreflang` alternates and a canonical URL (see SEO).
- **Missing translation fallback:** if a field has no English value, English visitors see the Greek value. The admin sees a "missing EN" marker on that field in edit mode.
- `sitemap.xml` lists every page that is not marked no-index, in both languages, with `hreflang` alternates; the default `robots.txt` points to it and disallows `/admin` (see SEO).

## SEO

- **Implemented (Phase 3):** `<cms-seo fallback-title="...">` (`Web/TagHelpers/CmsSeoTagHelper.cs`) in the `<head>` of `Pages/Shared/_Layout.cshtml`. On a site page (one in `SitePages`) it emits `<title>` (`{page}.seo.title`, else the fallback title), `<meta name="description">` (`{page}.seo.description`, omitted when empty), `og:title`, `og:description`, `og:type` (`website`), `og:site_name`, `og:url`, `og:locale` and `og:locale:alternate` (`el_GR` / `en_GB`). On other pages (admin, errors) it emits only `<title>`. Values are read as `Text` fields in the visitor's language (English falls back to Greek). `og:image` (Phase 5): when `{page}.seo.image` holds an existing image, the tag helper also emits `og:image` (absolute URL: `Site:BaseUrl` + the largest generated width of 1600 or less, else the smallest), `og:image:width`, `og:image:height` and, when `{page}.seo.image-alt` has text in the visitor's language (English falls back to Greek), `og:image:alt`. A stored value of the wrong type, or an id with no `MediaAsset`, logs a warning and emits no image tags.
- **Canonical and `hreflang` (Phase 7):** `CmsSeoTagHelper` also emits `<link rel="canonical">` (the page in the visitor's language) and `hreflang` alternates `el`, `en` and `x-default` (the Greek page), all absolute and never carrying the query string, so `/ekdromes?trip=islands` has the canonical `/ekdromes`.
- **Per-page Index and Follow (Phase 7.5):** the content fields `{page}.seo.index` and `{page}.seo.follow` (`Text`, Greek only, `true` / `false`; no row means on; `Core/Services/PageRobots.cs`) are set with two checkboxes in the Page settings window ("Search engines" group, "Applies to both languages."). `ContentBatch` rejects an English value, any other value, the `shared` page and unknown pages. When a switch is off, `CmsSeoTagHelper` adds `<meta name="robots" content="noindex">`, `nofollow`, or `noindex, nofollow`, on site pages in both languages; nothing is added when both are on.
- **`robots.txt` and `sitemap.xml`** (`Web/Endpoints/SeoEndpoints.cs`, `Cache-Control: public, max-age=3600`, GET and HEAD): `robots.txt` is the text saved on the Site settings page (`/admin/settings`, `IntegrationSettings` key `site.robots-txt`), else the generated default (`RobotsTxt.Default`: `User-agent: *`, `Disallow: /admin` plus a `Sitemap:` line). `sitemap.xml` lists the 7 pages in both languages (14 `<loc>`, absolute), each with `xhtml:link` alternates `el`, `en`, `x-default`, **except pages whose `seo.index` is `false`** (both language URLs are skipped; one query, `IContentService.GetNoIndexPagesAsync`). The browser may keep the sitemap for its hour.
- **`Site:BaseUrl`** (`Web/SiteOptions.cs`) is a required option: an absolute http(s) URL with no path, query or fragment (a trailing `/` is trimmed). It is validated on start, so a missing or invalid value fails startup. `appsettings.json` has no value; Development sets `https://localhost:5001`; preview and production must set `Site__BaseUrl` (Phase 8). It prefixes `og:url`, canonical, `hreflang`, the sitemap, the JSON-LD URL and the old-site redirect `Location`.
- **Greek is not escaped in HTML output:** `WebEncoderOptions` in `Program.cs` allows `BasicLatin`, `Latin1Supplement`, `GreekandCoptic` and `GeneralPunctuation`, so Greek text is emitted as characters, not `&#x...;` entities.
- **Preview environment** (`ASPNETCORE_ENVIRONMENT=Preview`, `Web/PreviewEnvironment.cs`): enforced by the app itself, not only by nginx. `robots.txt` = `User-agent: *` / `Disallow: /` whatever is saved on the Site settings page (`RobotsTxt.Served` does not read the setting in Preview), `sitemap.xml` answers 404, and `UsePreviewNoIndex` adds `X-Robots-Tag: noindex, nofollow` to every response (see `docs/deployment.md` → Preview).
- **Structured data (JSON-LD):** `<cms-json-ld />` (`Web/TagHelpers/CmsJsonLdTagHelper.cs`, in `_Layout`, site pages only; error and admin pages emit nothing) writes one `LocalBusiness` script: `name` (fixed "Saronida Sailing"), `url` (Home in the visitor's language), `description` (`shared.footer.tagline`), `telephone` (`shared.contact.phone` and `phone-2`, one string or an array), `email` (`shared.contact.email`), `PostalAddress` (`shared.contact.address`, `addressCountry` `GR`) and `sameAs` (`shared.social.instagram`, `facebook`). **Placeholder rule:** a value that is empty, contains a bracketed `[...]` part or contains `example.com` is left out, so the seeded `[EMAIL]` / `email@example.com` is omitted until the owner's address replaces it. The default `System.Text.Json` encoder escapes `<`, `>` and `&`, so content cannot close the script. Season and opening hours are not emitted (the season is free text on the Contact page, not shared content). The admin keeps it current by editing the normal shared content.
- **Old-site redirects (301)** (`Web/Redirects/OldSiteRedirects.cs`, `OldSiteRedirectMiddleware`, `UseOldSiteRedirects()`). The old site (Joomla, crawled 2026-11-08) serves the same pages on `saronidasailing.gr` and `www.saronidasailing.gr` under `/index.php/{el|en}/...`. Since the domain moves to the new server, the app maps old paths with a redirect table in middleware. The table is data (path → page key + culture) resolved through `ILocalizedUrlService`, so no slug is repeated. Rules: only paths equal to `/index.php` or below it (segment match, case-insensitive, trailing `/` ignored), any host, the query string dropped, GET and HEAD only (a POST is untouched); the answer is `301` with an absolute `Location` = `Site:BaseUrl` + the new path, or an empty `410` that the not-found page renders. The middleware is registered **outside Preview only** (Preview answers 404). Its place in the pipeline: after the status-code-pages block, before request localization (see Request Flow). The 410 page is Greek for old `/index.php/en/...` paths, because the language comes from the `/en` URL prefix.

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

Middleware order in `Web/Program.cs` (after the startup work: migration check, `Content:SeedAtStartup` seed, Instagram schedule sync):

| Order | Middleware | Note |
|---|---|---|
| 1 | `UseSecurityHeaders` (`Web/Security/SecurityHeaders.cs`) | First, so every response carries the headers (see Security → Security headers) |
| 2 | `UseHealthDirectOnly` (`Web/Health/HealthEndpointAccess.cs`) | Before `UseForwardedHeaders` on purpose: it must see the raw `X-Forwarded-For` header (see Health Check) |
| 3 | `UseForwardedHeaders`, `UseRequestDiagnostics` | Real client IP first |
| 4 | `UsePreviewNoIndex` | Preview only: `X-Robots-Tag` on every response |
| 5 | Development media files | Development only |
| 6 | `UseExceptionHandler("/error/500")` | Not in Development |
| 7 | `UseStatusCodePagesWithReExecute("/error/{0}")` + the per-path rule (`ErrorPageStatus`) | Error page only for 404 / 410 outside the plain paths |
| 8 | `UseOldSiteRedirects` | Not in Preview |
| 9 | `UseRequestLocalization` | Culture from the URL prefix |
| 10 | `UseRouting`, `UseRateLimiter`, `UseAuthentication`, `UseAuthorization`, `UseOutputCache` | Output cache after authentication |
| 11 | Endpoints | Hangfire dashboard, `/health`, static assets, content / media / admin / SEO endpoints, Razor Pages |

The redirects sit before localization and routing so an old URL is answered without any content or routing work, and after the status-code block so a 410 gets the not-found page.

## Output Caching

Implemented in Phase 3 (`Web/Program.cs`, `Web/Caching/`).

- Named policy `CmsPage` (`CacheTags.PolicyName`): expiration **1 hour**, tag `pages:all`, built on the default policy, with the query string ignored (`SetVaryByQuery([])`), so `?x=1` variants share one entry.
- Each page declares the policy and its own tag on its page model: `[OutputCache(PolicyName = CacheTags.PolicyName, Tags = [CacheTags.HomePage])]` (tag `page:{pageKey}`, e.g. `page:home`; constants `HomePage`, `TripsPage`, `BoatPage`, `ContactPage`, `TermsPage`, `PrivacyPage`, `AccessibilityPage`). All seven site pages are cached; a guard test (`OutputCacheTests`) requires every site page to carry a policy and its page tag, so a new page without it fails the tests.
- **Trips page policy `CmsPageTrips`** (`CacheTags.TripsPolicyName`, `Web/Caching/TripsVaryByPolicy.cs`): the same as `CmsPage` plus a vary-by value of the **normalised** `?trip=` (`TripTabs.Normalize`), so there are at most three entries (`day`, `islands`, `week`) and junk values cannot fill the cache. Only the Trips page uses it (the guard test expects it for `trips` only); every other query key stays ignored.
- `IContentService.ApplyChangesAsync` calls `IPageCacheInvalidator` once per save with the touched page keys. `OutputCachePageInvalidator` (singleton) turns them into tags: `page:X` for each touched page, or `pages:all` only if `shared` is touched. Every `shared.*` content (footer, contact, social, prices, charter costs, cookie texts) is on every page, so saving any `shared.*` field or list evicts all pages. It evicts them locally through `ICacheTagEvictor`, then publishes them with `ICacheEvictionBus`.
- A successful Instagram fetch that added, updated or removed a post evicts `page:home` (`InstagramFeedService` calls `IPageCacheInvalidator.EvictPagesAsync(["home"])`, so the other pods are told too); a fetch that changes nothing does not.
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
| `<a cms-link="home.hero.cta">` | Link field: sets `href` and the label. An `https:` link also gets `target="_blank"`, `rel="noopener"` and a visually hidden "(opens in a new tab)" notice in the page language (`Localization/UiText.cs`). A `page:{key}` link (see Site page links) gets the visitor's-language path of that page, the same tab, no notice, and `aria-current="page"` when it is the current page. Invalid link JSON or a disallowed scheme logs a warning and omits the element for visitors; the admin sees it flagged `data-cms-invalid` |
| `<li cms-list="home.highlights" cms-add-label="Add highlight">` | Repeats the element once per **active** entry (the admin also sees inactive ones), in `SortOrder`. Inside it, `cms-field="title"` (with optional `cms-rich`) and `cms-link-field="link"` read that entry's fields. Lists cannot be nested; no active entries omits the element for visitors. `cms-add-label` is **required**: it is the text of the list's Add button in edit mode. Optional `cms-list-nav` marks a list of navigating links (the menus) and `cms-list-label="Header menu"` names the list in the item-bar button names; both are removed from the output for visitors (see Menus) |
| `<section cms-section="home.instagram" cms-section-label="Instagram feed">` | Omits the whole element when the section is inactive (a section with no row is active). `cms-section-label` is **required**: the section's name in the admin's section bar and status messages |
| `<a cms-section-link="home.instagram">` | A menu link to a section (`CmsSectionLinkTagHelper`): omitted for visitors when that section is inactive. Accepts only a section of the current page or of `shared`; any other page key throws at render time |
| `<section cms-section="home.instagram" cms-section-label="Instagram feed" cms-instagram-count="6" cms-instagram-empty="false">` | `CmsInstagramSectionTagHelper` (targets any element with `cms-instagram-count`; used by the Instagram feed view). Nothing for visitors. For the admin it writes `data-cms-instagram-count` (the stored post count), `data-cms-instagram-count-key` (`home.instagram.post-count`) and, when `cms-instagram-empty` is true, `data-cms-empty`. `instagram-count.js` reads these |
| `<cms-image key="home.hero.image" label="Hero photo" sizes="..." />` | An element, not an attribute (`CmsImageTagHelper`, no end tag). Renders an `<img>` for an `Image` field; `label` is required (see Media Pipeline → `<cms-image>`) |
| `<cms-seo fallback-title="...">` | Page `<title>`, description, canonical, `hreflang` and Open Graph tags (see SEO) |
| `<cms-json-ld />` | The `LocalBusiness` JSON-LD script (see SEO → Structured data); an element without an end tag, in `_Layout` |
| `<a cms-link="..." cms-keep-content>` (also `cms-link-field`) | Keep-content mode (`CmsLinkTagHelper`): the template's markup (icons, captions) stays and the label is written only into `<span cms-link-label>` slots (`CmsLinkLabelTagHelper`, zero or more per link), so icon-only social links, contact tiles and buttons with icons stay content-driven. Visitors: a blank or invalid link is suppressed; admin: the template is kept, slots empty, flagged. A `cms-link-label` outside a keep-content link throws, except inside a plain link, where it is silently dropped. Admin-only `data-cms-keep-content`, `data-cms-link-label`, `data-cms-new-tab-notice`; `fields.js` `previewKeptLink` fills the slots in the editor preview |
| `<ul cms-list="..." cms-item-id-prefix="section-">` | Optional on `cms-list`: sets `id="{prefix}{itemId}"` on every saved item copy, not on the empty `<template>` copy (used for the legal pages' table of contents anchors) |

A field with no value in either language omits its element for visitors; the admin gets an empty element flagged `data-cms-empty` to click into. An invalid key, or `cms-field` outside a `cms-list`, throws. A missing or blank `cms-add-label`, `cms-section-label` or `<cms-image label>` also throws at render time (`CmsFieldResolver.RequireLabel`), so every admin control has a distinct name (see Editing UI → Distinct controls). `Web/TagHelpers/CmsFieldResolver.cs` holds the shared key checks.

### Content snapshot

Tag helpers read a per-request snapshot, `IPageContentAccessor` (`Web/Content/PageContentAccessor.cs`, scoped): the first call asks `IContentService.GetPageContentAsync(page, locale)` for the current page (from `ILocalizedUrlService`; `shared` when the route is not a site page) and the UI-culture locale, and every later call reuses it. The snapshot (`Core/Interfaces/PageContent.cs`) holds the page's content plus `shared` content, loaded in one pass (items, list items, sections, then one query for every image the items refer to: `ContentService.LoadImagesAsync` builds a `PageImage` per `MediaAsset`, `PageContent.GetImage(id)`). English values that are missing or blank fall back to the Greek value, flagged `IsFallback`; `ContentValue.DefaultLocaleValue` also carries the Greek value (set on every value read in English, and on Greek values), so the admin can see the Greek alt text on an English page. For visitors, inactive list items and sections are not rendered.

### Admin rendering

`ICmsEditContext.IsEditorAsync()` (`Web/Content/CmsEditContext.cs`, scoped) evaluates the `Admin` policy once per request. For an admin it also sets `Cache-Control: no-store` on the response (in `OnStarting`, after antiforgery's own header). Only then do the tag helpers and `Pages/Shared/_Layout.cshtml` emit admin markup:

| Emitted | Where |
|---|---|
| `data-cms-key`, `data-cms-type` (`text`, `richText`, `link`, `image`) | Every field element |
| `data-cms-value` | Rich-text and link fields: the stored HTML / link JSON, so the editors start from the saved value. Image: the media id (empty when none) |
| `data-cms-alt-key`, `data-cms-alt`, `data-cms-alt-el`, `data-cms-alt-proposal`, `data-cms-sizes`, `data-cms-loading`, `data-cms-label` | `<cms-image>` only: see Media Pipeline → `<cms-image>` (`data-cms-label` is the required `label`) |
| `data-cms-name` | Optional name of a field for the admin's field bar and window subtitles; read by `values.js` `fieldName`. No tag helper emits it yet |
| `data-cms-fallback`, `data-cms-empty`, `data-cms-invalid` | Flags: English shows the Greek fallback, no value, invalid link |
| `data-cms-list`, `data-cms-item` | Every list item copy (`data-cms-item` is the item's Id) |
| `data-cms-list-nav`, `data-cms-list-label` | List item copies of a list with `cms-list-nav` / `cms-list-label` (`CmsListTagHelper`, admin only) |
| `<template data-cms-list-template="{listKey}" data-cms-add-label="...">` | After each list: one empty copy of the item markup, cloned by the Add button; `data-cms-add-label` is the list's `cms-add-label` (the Add button's text) |
| `data-cms-section`, `data-cms-section-label`, `data-cms-section-link` | Sections (with their required `cms-section-label`) and their menu links |
| `data-cms-instagram-count`, `data-cms-instagram-count-key` | The Instagram feed section only (`CmsInstagramSectionTagHelper`): the stored post count and its content key |
| `data-cms-active="false"` | Inactive list items, sections and section links; shown dimmed |
| `<script type="application/json" id="cms-config">` | `CmsEditorConfig` (`Web/Content/CmsEditorConfig.cs`): `pageKey`, `locale`, `saveUrl`, `historyUrl`, `savesUrl`, `antiforgeryToken`, `opensInNewTab`, `seo` (the page's SEO title and description, each with its Greek value when English falls back; the link-preview `image` (`{value, previewUrl}`, the 960 px file or the largest) and `imageAlt` (`{value, greek}`); the booleans `index` and `follow`; null on non-site pages) and `pages` (every site page: `key`, `path` in the page's language, Greek and English names; feeds the link window's "Site page" list) |
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

**Section activation hook.** After the commit, the cache eviction and the audit line, `ContentService` calls every registered `ISectionActivationHandler` (`Core/Interfaces/`: `SectionKey` = the full section key such as `home.instagram`, and `OnActiveChangedAsync(isActive, ct)`) whose key matches a section whose final state differs from its state before the batch (`ContentBatch.SectionChanges`; a section with no row counts as active; a toggle that returns to the original state fires nothing). A handler that throws is logged at Error and the save still succeeds. The only handler so far is `InstagramSectionHandler` (see Instagram Feed → Section switch).

### Sanitizing and validation

Both run inside `ContentService.ApplyChangesAsync` (`ContentBatch.SetFieldAsync`), so every writer is covered (the save endpoint, the Phase 7 seed, jobs), not only the HTTP endpoint.

| Value type | Rule |
|---|---|
| `RichText` | Passed through `IRichTextSanitizer` (`Core/Interfaces/`), implemented by `RichTextSanitizer` (`Infrastructure/Content/`, HtmlSanitizer 11.0.16, registered as a singleton). Allowed tags: `p`, `br`, `strong`, `em`, `a`, `ul`, `ol`, `li`, `h3`. The only allowed attribute is `href`; no classes, CSS or `data-` attributes. Allowed URL schemes: `https`, `mailto`, `tel`, and only on `a`. Disallowed tags are unwrapped (children kept), except `script`, `style`, `iframe`, `frame`, `frameset`, `object`, `embed`, `applet`, `noscript`, `template`, `textarea`, `title`, `svg` and `math`, which are dropped with their content. A result that is only empty paragraphs becomes `""` |
| `Link` | An empty value clears the link. Otherwise the JSON must parse with `ContentLink.TryParse`: an `https:`, `mailto:` or `tel:` URL, or `page:{key}` of a known site page (`SitePageKeys`), and a non-blank label; if not, the save is rejected with an error naming the field ("'x' is not a site page." for an unknown page key). Links inside rich text never accept `page:` (the sanitizer allows only `https`, `mailto`, `tel`) |
| `Text` | Stored as sent, except `home.instagram.post-count` (`ContentBatch.IsValidPostCount`): locale `el` only, type `Text`, and empty or a whole number 1-12 (`InstagramPostCount.IsValid`); an English value or another number rejects the save. `contact.map.embed-url` (`ContactMap.IsValidEmbedUrl`, checked in `ContentBatch`): locale `el` only, type `Text`, and empty, or `https://www.google.com/maps/embed` followed by the end of the string, `?` or `/`, with no whitespace; anything else rejects the save. `{page}.seo.index` / `{page}.seo.follow` (`PageRobots.IsRobotsKey`): locale `el` only, type `Text`, value `true` or `false`, a known site page |
| `Image` | Checked in `ContentBatch` (`SetFieldAsync`, then `ValidateImagesAsync` once all changes are applied): see Media Pipeline → Save checks |

### History

- `ContentHistory.SaveId` (`docs/domain-model.md`): one id per `ApplyChangesAsync` call, shared by all history rows of that save; a field gets at most one row per save.
- `GetFieldHistoryAsync(key, locale, listItemId)` returns a field's versions newest first; `GetSaveAsync(saveId)` returns the fields of a save with their `PreviousValue` and `CurrentValue` (computed in `ContentService` from the field's other history rows).
- History holds **field values only**. List structure (add, delete, reorder, hide) and section toggles are not in history; a deleted list item takes its history with it.
- **Edit history is kept forever.** The History window restores a version or undoes a whole save, both as pending changes saved with Save page.

### Content seeding

The site starts with its texts already in the database. Seeding is automatic at startup, not a CLI command.

```
Program.cs: Content:SeedAtStartup true? ─► IContentSeeder.SeedAsync
   IDistributedLock "saronida:content-seed" (Hangfire PostgreSQL lock, 2 min)
      for each group (sorted by name):
         marker seed.{group} >= version? ─ yes ─► skip
                │ no
         fill only what has NO row: field (key + locale) / list with NO items
                │
         ContentService.ApplyChangesAsync (editor "seed") ─► marker = version
```

| Part | Rule |
|---|---|
| Seed groups | One group per page section set (`home`, `trips`, `boat`, `contact`, `terms`, `privacy`, `accessibility`) or shared block (`shared.footer`, `shared.contact`, `shared.social`, `shared.prices`, `shared.charter-costs`, `shared.cookies`, `shared.menu-header`, `shared.menu-footer`). A group is a JSON file `Web/Seed/{group}.json`, an embedded resource read by `EmbeddedContentSeedSource` (`IContentSeedSource`, groups sorted by name). Shape: `group`, `version`, `fields` (key → `type`, per-locale values; a link field has one `url` and per-locale labels) and `lists` (list key → items of fields). Every key must equal the group name or start with `{group}.`; the file name must equal `group`; **images cannot be seeded** (alt texts can) |
| Shipped groups and versions | `home` 1, `trips` 2, `boat` 1, `contact` 1, `terms` 1, `privacy` 1, `accessibility` 1, `shared.footer` 1, `shared.contact` 2, `shared.social` 1, `shared.prices` 1, `shared.charter-costs` 1, `shared.cookies` 1, `shared.menu-header` 1, `shared.menu-footer` 1 |
| Version marker | `IntegrationSettings` key `seed.{group}` (`ContentSeeder.MarkerKey`) holds the applied version. A group whose marker is missing or lower than its file's `version` is applied; the marker is written after the content save, in the same commit |
| Fill-only-missing | Never overwrites. A field value (key + locale) that already has a row is skipped, whatever its value; a list is seeded only when it has **no items**, so a list the admin emptied is never re-seeded. A database that already holds some rows (the dev database) still gets every missing key. Raising a group's `version` is how new seed content ships; it adds only what is missing, so old rows that are no longer rendered stay as orphans |
| Writing | Through `IContentService.ApplyChangesAsync`, so the sanitizer, validation, history (`ChangedBy` = `seed`), cache eviction and the audit line `content-saved user=seed` all apply. An invalid seed throws, writes nothing and stops startup |
| Two pods | The whole run is under `IDistributedLock` (`Core/Interfaces/`, implemented by `Infrastructure/Jobs/HangfireDistributedLock`, which uses Hangfire's storage lock: no raw SQL), 2 minute timeout; the emptiness checks run inside the lock, so a second pod starting together finds the work done |
| Setting | `Content:SeedAtStartup` (`appsettings.json` `true`; the test host defaults it to false). Seeding runs after the CLI branches (`admin`, `migrate`) and before the Instagram schedule sync |
| Logging | The "Seeded content group" Information line is dropped by the NLog rules (Warn and above only for that logger), so the audit lines `content-saved ... user=seed` are the record |

A web host started with `Database:MigrateAtStartup=false` on a database that has no Hangfire schema waits the full 2 minutes for the lock; production gets the schema from `migrate`.

**Placeholders in seeds.** Unknown owner values are bracketed (`[ΤΙΜΗ]`, `[PRICE]`, `[EMAIL]`, `[HOURS]`, `[MONTHS]`) or `example.com`; the JSON-LD tag helper treats exactly these as "not set" (see SEO → Structured data), so keep that style for any new placeholder. English texts in the seeds are drafts for the owner's review.

### Admin toolbar

`Pages/Shared/_AdminToolbar.cshtml`, at the top of the page (`role="region"`):

| Control | Notes |
|---|---|
| Save page | Disabled until there are pending changes; Ctrl/Cmd+S does the same while edit mode is on |
| Unsaved counter | "No unsaved changes" / "N unsaved changes" (fields plus list and section operations) |
| More | Opens the panel with the rest; hidden from 720 px up, where the panel's controls sit inline |
| Edit mode | Toggle, `aria-pressed`. Off = the page looks exactly as for a visitor |
| Discard | Does nothing when there are no pending changes; otherwise opens an in-page confirmation ("Discard changes?", buttons "Keep editing" (focused) and "Discard"), then reloads the page |
| Page settings | Only on a site page |
| "N missing in English" | English pages only; hidden when the count is 0 |
| Language link, Jobs, Instagram, Settings, Log out | Instagram links to `/admin/instagram` (after Jobs), Settings to `/admin/settings`. Log out is a POST form with an antiforgery token |
| Instagram warning | `a.cms-toolbar-warning` to `/admin/instagram`, outside the More panel: "Instagram not connected — connect" before the first connect, "Instagram disconnected — reconnect" after expiry, revocation or a failed renewal. Shown only while the `home.instagram` section is active (`IInstagramWarningService`, `Core/Services/InstagramWarningService.cs`, read in `_AdminToolbar.cshtml`). Under 720 px it is its own full-width 44 px line under the toolbar; from 720 px it sits inline |

A status line (`role="status"`, `aria-live="polite"`) and an error list (`role="alert"`) sit below the controls on a phone. From 720 px the status sits **between the unsaved counter and the buttons** (CSS `order`; it takes the remaining width and is cut with an ellipsis; the full text is in its `title` attribute and is announced through `aria-live`), so the buttons stay at the same place whatever the status says.

### Editing UI (client)

`wwwroot/js/cms-edit.js` is the entry module: it reads `#cms-config` and wires the modules in `wwwroot/js/cms/`. It is loaded only for the admin. Plain ES modules, no framework.

| Module | Role |
|---|---|
| `pending.js` | The pending model. Field changes are keyed by key + locale + list item id and dropped when the value returns to the original; list and section operations live in named **op slots** (`list:{listKey}`, `section:{sectionKey}`) that are replaced as a whole. `toBatch()` puts the ops first, then the `setField` changes |
| `fields.js` | Reads and writes one field element: original value, preview of the new value, `data-cms-dirty` / `-empty` / `-invalid` / `-fallback` flags, hand-off to `pending`. A field rendered more than once on a page (the header and footer Instagram link) is previewed in **every** copy (`findAll`) with one pending change; the "N missing in English" count uses distinct fields. `previewKeptLink` fills the `cms-link-label` slots of a keep-content link |
| `inline.js` | Inline editing of `text` fields (`contenteditable="plaintext-only"`). Enter finishes, Escape reverts. Clicks on editable links are blocked in edit mode, except links inside a `cms-list-nav` list, which navigate like a visitor's click. Clicks and the Space key-up inside an editable field in a `<summary>` do not toggle its `<details>` (the Useful-info questions can be edited with real spaces) |
| `toolbar.js` | Save (one `fetch` POST with the `RequestVerificationToken` header; on an antiforgery 400 it reloads the page HTML once for a fresh token and retries), Discard (through `confirmDialog`), counter, status text (also set as its `title`), More panel, `beforeunload` warning, the `--cms-toolbar-height` CSS variable, the "Saved" message after the reload, messages for 400 / 401 / 403 / 413 |
| `edit-mode.js` | Edit mode on/off. Starts **on**; remembered per tab in `sessionStorage` (`cms-edit-mode`). Off adds the class `cms-view` to `<html>` |
| `fieldbar.js` | The small bar next to a focused or clicked field: "History" for every field, plus "Edit text" / "Edit link" / "Edit image"; each button's accessible name adds the field name ("Edit text: title"). Clicking a rich-text block, a link or an image opens its window directly. **No field bar and no click-to-edit on a link inside a `cms-list-nav` list** (`navigates`): the item bar carries those buttons (see Menus). When the field bar would overlap the item bar it moves below it |
| `windows.js`, `dialog.js` | `dialog.js` builds the native `<dialog>` windows (focus return) and the in-page confirmation; `windows.js` opens the window types (rich text, link, image, history, page settings). See **Windows and confirmations** below |
| `rich-window.js`, `quill-loader.js`, `rich-html.js` | Rich-text window: Quill 2 limited to heading (h3), bold, italic, numbered and bulleted lists, link and clear formatting. `quill-loader.js` loads Quill lazily. `rich-html.js` is a client-side mirror of the sanitizer's allowed tags, for preview only; the server's sanitizer is the authority. Links not starting with `https://`, `mailto:` or `tel:` are refused |
| `link-window.js`, `values.js` | Link window: type picker (Web page / Email / Phone, which adds `https://`, `mailto:` or `tel:`, and **Site page**, a labelled `<select>` of the pages in `cms-config` `pages`, named in the editor's language, stored as `page:{key}`), label, "Remove link" (asks through `confirmDialog`, "Remove link?"). `values.js` holds shared helpers (text normalize, link parse, allowed schemes, `fieldName`) |
| `history-window.js` | Loads `historyUrl`; image versions show thumbnails (`imagePreview`) and restore as an image change, and an image that no longer exists cannot be restored; "Restore this version" sets the field as a pending change; "Undo this whole save" loads `savesUrl{saveId}`, lists the fields that differ from before the save, flags those changed again by a later save (undo overwrites them), and sets them back as pending changes |
| `image-window.js`, `image-data.js` | Image window and its shared helpers (see Media Pipeline → Image window). `image-data.js` holds the media API client (`loadAsset` with a per-id cache), `imagePreview`, `recordImage` (puts the image id and the alt texts into `pending`), and builders for the placeholder button and the `<img>` element, so the page shows the new image before Save |
| `settings-window.js` | Page settings: `{page}.seo.title` and `{page}.seo.description` in the current language, with the Greek value shown as guidance when English is empty, plus a "Link preview image" row (`{page}.seo.image` and its alt `{page}.seo.image-alt`) that opens the image window, and a "Search engines" group with two checkboxes ("Show this page in search results", "Let search engines follow this page's links") that write `{page}.seo.index` / `{page}.seo.follow` as Greek-only pending changes, so they save from the English page too |
| `map-window.js` | Map location window (see Map window below) |
| `missing.js` | Counts elements flagged `data-cms-fallback` and jumps to the next one |
| `lists.js` | List editing (below) |
| `sections.js` | Section bar (below) |
| `instagram-count.js` | `createInstagramCount`: adds the "Posts shown" select (1-12) to the section bar of every element with `data-cms-instagram-count` (see Instagram Feed → Post count) |

**Lists** (`lists.js`): each list item gets an **item bar** (drag handle, Move up, Move down, Hide/Show, Delete; items of a `cms-list-nav` list also get "Edit link" and "History" before Delete), shown while the pointer or keyboard focus is inside the item. Each list ends with an Add button whose text is the list's required `cms-add-label` (carried by `data-cms-add-label` on the list template; "Add item" only as a script fallback): it clones the `data-cms-list-template`, gives the copy a new GUID and focuses its first field. Delete asks first in a window titled with the item's name (Delete, "Hide instead", Cancel); a saved item has no history, so its deletion cannot be undone after Save. The list operations are **computed from the DOM** against the state at page load, not recorded as clicks: `addListItem` for new ids, `deleteListItem` for missing ids, `setListItemActive` for changed visibility, and one `reorderList` when the order differs from the expected one. The result goes into the slot `list:{listKey}`. Pending edits of a deleted item's fields are dropped. Drag-and-drop uses SortableJS, loaded lazily only when the page has a list; if it fails to load, the buttons still work.

**Sections** (`sections.js`): every `data-cms-section` gets an always-visible **section bar** showing the section's name (`data-cms-section-label`) and the button "Hide section" / "Show section" (visible as "{label} · Hide section"; the bar's and the button's accessible names include the label). The toggle also dims or restores the section's menu links (`data-cms-section-link`) and sets the slot `section:{sectionKey}` with a `setSectionActive` op (empty when the state returns to the original). Status messages name the section ("Section 'Intro' hidden from visitors (both languages). Save page to apply."). The same visibility applies to both languages. A section can also carry its own control in the bar: the Instagram section's "Posts shown" select (`instagram-count.js`).

**Distinct controls.** Every admin control has a name that tells it from the others of its kind, for people using a screen reader or voice control. Templates must give: `cms-add-label` on every `cms-list`, `label` on every `<cms-image>`, `cms-section-label` on every `cms-section` (a missing or blank value throws at render time). The client adds: an **item bar** whose button names include the item title (`itemName` in `lists.js`: the first text field of the item, else the label of its link, cut to 40 characters, else "item N"; "Move up: Intro", "Hide item: Intro"; when the list has `cms-list-label`, `, {label}` is appended, e.g. "Delete: Το σκάφος, Footer menu", so lists that repeat the same item names stay distinct), kept current as the text changes; a **field bar** whose button names include the field name (`fieldName` in `values.js`: `data-cms-name`, else an image's `data-cms-label`, else the last segment of the key with `-` as spaces); and window **subtitles** that name the field ("{field name} · {key}") in the History, rich-text and link windows. An image placeholder reads "Add image: {label}" and the image window is titled "Edit image: {label}".

**Windows and confirmations** (`dialog.js`): `openDialog` builds a native `<dialog>` opened with `showModal()`. Questions are asked in the page, not with the browser's `confirm()`, because some environments (in-app browsers, Chrome's "prevent additional dialogs") answer a native `confirm()` "no" without showing it. `confirmDialog({ title, message, confirmLabel, cancelLabel, returnFocus })` returns a promise of `true` / `false`; the Cancel button ("Keep editing" for discards) is focused, Escape means cancel, and it stacks on top of an open window. It is used by toolbar Discard, "Remove link" and closing a window that has edits (`requestCancel`: "Discard changes?" with "Keep editing" / "Discard"). A dialog that the browser closes by itself (Chromium's close watcher: a second Escape without a click or key in between) counts as **cancel without asking**: the pending confirmation is dismissed, the element is removed and focus returns. `isDialogOpen()` counts only dialogs that are open, so the field bar does not react while a window is up.

**Phone layout** (under 720 px; `wwwroot/css/cms-edit.css` is mobile-first with `min-width: 720px` and `min-width: 960px` breakpoints; the media library grid has 2 columns on phones, 3 from 720 px and 4 from 960 px): the toolbar stays in one line (Save page, counter, More), the status sits below it, and More opens a panel below; windows are full-screen (`100vw` x `100dvh`) and become centred dialogs from 720 px; the item drag handle is hidden, so items move with the up/down buttons; buttons in the item and section bars and the toolbar are 44 px (2.75rem) touch targets, shrinking from 720 px; `scroll-padding-top` follows `--cms-toolbar-height` so focused fields are not hidden under the toolbar.

### Vendored libraries

Self-hosted in `wwwroot/lib/`, each folder with a `README.md` (version, source, files, date) and the license. Both load lazily and only for the admin.

| Library | Version | Files | Loaded by |
|---|---|---|---|
| Quill (BSD-3-Clause) | 2.0.3 | `lib/quill/quill.js` (UMD, sets `window.Quill`), `quill.snow.css` | `quill-loader.js`, the first time a rich-text window opens |
| SortableJS (MIT) | 1.15.6 | `lib/sortablejs/sortable.core.esm.js` (core ES module, no plugins) | `lists.js`, `import()` when the page has a list. No auto-scroll while dragging; the up/down buttons cover long lists |

The app's CSP (see Security → Security headers) is own-origin only for visitors, and for admins it adds only `style-src 'unsafe-inline'` and `img-src data:` (for pasted rich text and the Hangfire dashboard). Admin JS written for this project still never adds `style=""` attributes or `<style>` elements; setting `element.style` through the CSSOM is allowed.

### Dev sandbox

`/dev/cms-sandbox` and `/en/dev/cms-sandbox` (`Pages/Dev/CmsSandbox.cshtml`; the English route is added in `Program.cs` only in Development). `CmsSandboxModel` returns 404 outside Development. It uses keys under `shared.sandbox*` (a text, a rich-text block, a link, a list with text / rich text / link fields, a plain list, two sections and a `cms-section-link`), so the editing UI can be exercised before the real pages exist. In Phase 4 the UI is verified by driving the Claude browser on this page, not with Playwright.

### Site page links

A Link value whose `url` is `page:{key}` points to one of the seven site pages (`{key}` is a page key such as `trips`). The keys are `Core/Services/SitePageKeys.cs`; Core cannot see `Web/Localization/SitePages`, so the list is repeated there and a guard test (`SitePageKeysTests` in `MenuTests.cs`) fails when the two drift. `ContentLink.TryParse` accepts a known key and `ContentBatch` rejects an unknown one. `CmsLinkTagHelper` renders the visitor's-language path through `ILocalizedUrlService.GetPath` (same tab, no new-tab notice, `aria-current="page"` on the current page), so the link follows the language switch and a changed slug. Any Link field can use it; the link window offers it as the fourth type. Rich-text links cannot (see Sanitizing and validation).

### Menus

| Part | Rule |
|---|---|
| Header menu | `Pages/Shared/_Header.cshtml`: list `shared.menu-header` (item field `link`) inside `#site-menu`. The whole `<nav>` is left out for visitors when no active item has a valid link; the admin always gets it. The language link and the Book button are not part of it |
| Footer menu | `Pages/Shared/_Footer.cshtml`: list `shared.menu-footer` in the Pages column; the "Cookie settings" button stays the last `<li>`, fixed |
| Seeds | `shared.menu-header` v1: Trips, The boat, Instagram (the profile URL `https://www.instagram.com/saronidasailing/`, so it opens in a new tab), Contact. `shared.menu-footer` v1: Trips & booking, The boat, Contact, Terms, Privacy policy, Accessibility. Items are `page:{key}` links with Greek and English labels (`Seed/shared.menu-header.json`, `Seed/shared.menu-footer.json`) |
| Book button | Wrapped in the section `shared.book` (`cms-section-label="Book button"`): Hide / Show like any section, absent for visitors while hidden. Its target (Trips) and text are fixed in code (`UiText.Book`) |
| Editing | Both lists carry `cms-list-nav`: in edit mode a click on an item navigates like a visitor's, and editing is done from the **item bar** ("Edit link", "History", plus Move up / down, Hide, Delete). The buttons are named after the item and `cms-list-label` ("Header menu", "Footer menu"); closing the window returns focus to the button. A keyboard-focused menu link shows no field bar of its own. Unsaved changes are protected by the `beforeunload` guard in `toolbar.js`. Header items bar wraps; on the phone panel the bar sits inside the item's row |
| Cache | A save touches `shared`, so every page is evicted (see Output Caching) |

### Map window

`wwwroot/js/cms/map-window.js` (`createMapWindow`, wired in `cms-edit.js`). The Contact page shows an admin-only "Map location" button over the map (`data-cms-map-edit`, rendered only when `ContactModel.IsEditor`; the static map image keeps its own click). The window takes an `<iframe ...>` embed code or an address:

1. An `<iframe>` paste is parsed with `DOMParser` (never inserted into the page) and its `src` is used; any other text is entity-decoded.
2. The result is checked with the same rule as the server (`ContactMap.IsValidEmbedUrl`: `https://www.google.com/maps/embed`...). A normal Maps link is refused with a message, the field gets `aria-invalid` and Apply is disabled.
3. "Show preview" loads the map inside the window only on click.
4. Apply writes a pending `setField` for `contact.map.embed-url`, locale `el` (shared by both languages, so it saves from the English page too). "Remove map" asks for confirmation and clears it. After Apply the event `saronida:map-change` makes `contact-map.js` switch the live map at once, without reload.

The Map notice (`contact.map.notice` and the "Cookie settings" button) is rendered only when a valid embed URL exists or the viewer is an admin (`ContactModel.ShowMapNotice`). `missing.js` ignores the hidden embed-URL element when counting missing English fields.

### Other rules

- **One save per page:** every change (fields, list operations, section toggles) is kept in the browser as pending until the admin clicks **Save page**. Save sends all pending changes in **one request**, written in **one database transaction**, followed by **one** cache eviction. The page warns before navigating away with unsaved changes. Save publishes immediately (no draft/publish workflow). Only changed fields are sent, so two admins editing different fields of a page don't overwrite each other; the same field: last save wins.
- Image files are uploaded and processed when chosen (the file must be processed anyway, `POST /admin/api/media`); assigning the image to the field is a pending change like any other, saved with Save page. A file uploaded but never assigned stays in the library (no cleanup at launch).
- **Page settings window:** per page and language, the SEO fields `{page}.seo.title` (browser tab / search result title) and `{page}.seo.description` (search result snippet), and "Link preview image" `{page}.seo.image` (shown by Facebook, WhatsApp, etc. through the `og:image` tag) with its alt text `{page}.seo.image-alt`, and the "Search engines" switches `{page}.seo.index` / `{page}.seo.follow` (one value for both languages; see SEO).

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
| Files | Public: `{slug}-{first 16 hex chars of the hash}-{width}.webp` under `Media:RootPath` (`MediaFileNames.PublicFileName`, URL prefix `/media/`). Private original: `{full hash}{original extension}` (`.jpg`, `.png`, `.webp`) under `Media:OriginalsPath`, never served (it keeps its metadata, so every size can be regenerated). Every file is written to `{name}.{guid}.tmp` and renamed, so a half-written file is never visible; a failed write deletes the temp file. File names contain the content hash, so a file that already exists is never rewritten (`WriteAtomicAsync` returns at once), and a lost rename race (an `IOException` or `UnauthorizedAccessException` while the final file is present, e.g. another pod or an open file on Windows) counts as success and deletes the temp file. `WriteAtomicAsync` is `internal`, visible to the tests through `InternalsVisibleTo` in `Infrastructure.csproj` |
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

`<cms-image key="home.hero.image" label="Hero photo" sizes="(min-width: 960px) 50vw, 100vw" alt-source="title" loading="lazy|eager" class="..." />`, or `field="image"` instead of `key` inside a `cms-list` (`CmsImageTagHelper`; the alt text is read from `{key}-alt` / `{field}-alt`).

| Attribute | Rule |
|---|---|
| `key` / `field` | One of them, resolved as for the other tag helpers (`CmsFieldResolver`) |
| `label` | Required: the image's name for the admin (placeholder "Add image: {label}", window "Edit image: {label}"); missing or blank throws. Not rendered for visitors |
| `sizes` | Required: the `sizes` attribute of the `<img>`; a missing value throws |
| `loading` | `lazy` (default) or `eager` (above-the-fold images); anything else throws |
| `alt-source` | Admin only: the field whose text proposes the alt text. With `key`, a full content key (`shared.sandbox.title`); inside a list, a field of the same item (`title`). Rich text is turned into plain text. Written to `data-cms-alt-proposal` |
| other attributes (`class`, ...) | Kept on the `<img>` |

- **Visitors:** an `<img>` with `src` (the 960 px file, else the widest), `srcset` of every generated width (`url 480w, ...`), `sizes`, `width` and `height` (the source dimensions, so the layout does not jump), `alt`, `loading`, `decoding="async"`. Nothing is rendered when the field is empty, holds a value of another type (warning logged) or refers to a missing asset (warning logged).
- **Admin:** the same `<img>` plus `data-cms-key`, `data-cms-type="image"`, `data-cms-label`, `data-cms-value` (media id), `data-cms-alt-key`, `data-cms-alt` (alt in the current language, empty when English falls back), `data-cms-alt-el` (the Greek alt), `data-cms-sizes`, `data-cms-loading`, `data-cms-alt-proposal` (when `alt-source` is set) and the flags `data-cms-fallback` (English alt falls back to Greek) and `data-cms-empty`. With no image the element is `<button type="button" class="... cms-image-placeholder">Add image: {label}</button>`; the layout `class` is kept.
- One query per page loads every image the page refers to (see CMS Editing Flow → Content snapshot).
- **`og:image`** for the page image: see SEO.

### Image window (client)

`wwwroot/js/cms/image-window.js` (`openImageWindow`) opens (titled "Edit image: {label}") from a click on an image or its "Add image: {label}" placeholder, from the field bar's "Edit image", and from Page settings. Helpers shared with Page settings and History are in `image-data.js`.

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

Implemented in Phase 6 and verified only against stub Instagram answers (a fake HTTP handler in the tests); the first real connection is on the Phase 8 preview (see **Not yet verified** below).

- **API:** Instagram API with Instagram Login (Business or Creator account, scope `instagram_business_basic`, no Facebook Page). The old Basic Display API was shut down in December 2024. The owner's account must be Business or Creator.
- **Placement:** the feed is a view component (`<vc:instagram-feed>`, `Web/ViewComponents/InstagramFeedViewComponent.cs`, view `Pages/Shared/Components/InstagramFeed/Default.cshtml`, view models `Web/Models/InstagramFeedViewModel.cs`) placed on the Home page (`Pages/Index.cshtml`, page key `home`, section `home.instagram`), styled with the design in Phase 7: heading `home.instagram.title`, a `text` field, 3 / 6 column square tiles, and a "Follow us on Instagram" keep-content link to `shared.social.instagram`. The dev sandbox does not show it: the sandbox loads only `shared.*` content, so the section needs the `home` page's content.

```
Connect (admin)                                 Hangfire: 06:00 and 18:00 Europe/Athens, or Fetch now, or right after Connect
POST /admin/instagram?handler=Connect                         │
  state + cookie ─► instagram.com login                      ▼
  ◄─ /admin/instagram/callback?code&state    InstagramFeedService.FetchAsync
  CompleteConnectAsync:                        token ─► GET me/media (latest 12) ─► new posts: download thumbnail
   code ─► short-lived ─► long-lived token                 ─► MediaService.ImportAsync (Source=Instagram)
   ─► profile ─► encrypted token + settings    ─► add / update / remove InstagramPosts ─► one commit
   ─► enqueue a fetch                          ─► changed? evict page:home (all pods)
```

### Configuration and token

| Item | Rule |
|---|---|
| Meta App ID and App secret | **Entered by the admin on `/admin/instagram`** ("Meta app" section), not in configuration: there are no `Instagram:AppId` / `Instagram:AppSecret` settings and no Kubernetes Secret or user secret for them. `IInstagramAppSettingsService` (`Core/Services/InstagramAppSettingsService.cs`) reads them from `IntegrationSettings` on every use (keys `instagram.app-id`, `instagram.app-secret`, constants in `InstagramAppSettingKeys`) and hands them to the API client as `InstagramAppCredentials(AppId, AppSecret)`. The App ID must be 5-32 digits; the secret 16-128 characters without spaces |
| App secret storage | Encrypted with Data Protection through `ISecretProtector`, like the token; **write-only**: the page never shows it again (the field reads "Replace App secret (optional)" once saved). A stored secret that can no longer be decrypted (changed keys) counts as not set up and the page asks for it again |
| Changing the app | Saving a **different App ID while an access token exists** asks for an in-page confirmation ("Save and disconnect"; also enforced on the server, `ConfirmDisconnect`), then removes the connection keys, because the token belongs to the old app (stored posts stay). Replacing only the secret keeps the connection. **Remove app settings** (in-page confirmation) deletes both values and disconnects. Disconnect itself keeps the app settings. Audit events: `instagram-app-saved`, `instagram-app-removed` (flags only, never the values) |
| Not set up | Without a readable App ID and secret the app starts normally, the status has `CanConnect` false and Connect is disabled; the page points to the "Meta app" form ("Instagram is not set up yet: enter the Meta App ID and App secret under Meta app"). A callback that arrives after the credentials were removed redirects with `result=notconfigured` and audits `instagram-connect-failed` `reason=not-configured` |
| Base URLs | `Instagram:AuthorizeUrl`, `ApiBaseUrl` and `GraphBaseUrl` (`InstagramOptions`, the only remaining `Instagram` settings) default to the real endpoints and must be absolute http(s) URLs (validated on start); the tests point them at a stub |
| OAuth redirect URI | `{Site:BaseUrl}/admin/instagram/callback` (`InstagramPaths.RedirectUri`); it must be listed in the Meta app, one entry per environment that will connect (preview, production; a local run would need an HTTPS tunnel, none is part of the setup) |
| Token storage | Long-lived token (about 60 days) in `IntegrationSettings` key `instagram.access-token`, **encrypted with ASP.NET Data Protection** through `ISecretProtector` (`Infrastructure/Instagram/DataProtectionSecretProtector.cs`, purpose `SaronidaSailing.Instagram.AccessToken.v1`). The keys live on the file volume (`DataProtection:KeysPath`), not in the database, so a `pg_dump` holds no usable token. Lost keys mean the token cannot be decrypted: the connection shows as disconnected and the admin reconnects |
| Other settings keys | `instagram.token-expires-at`, `token-issued-at`, `user-id`, `username`, `connected-at`, `connected-by`, `token-invalid-at`; status keys `last-fetch-at`, `last-fetch-result`, `last-refresh-at`, `last-refresh-result` (constants in `Core/Services/InstagramSettingKeys.cs`; see `docs/domain-model.md`) |

### Connection status

`IInstagramConnectionService.GetStatusAsync` (`Core/Services/InstagramConnectionService.cs`) returns one of three states:

| State | When |
|---|---|
| `NotConnected` | No `instagram.access-token` row |
| `Connected` | A token exists, `instagram.token-invalid-at` is unset, the expiry is in the future and the token decrypts |
| `Disconnected` | A token exists but `token-invalid-at` is set, the expiry has passed or the token cannot be decrypted |

**Decision (tech lead, 2026-11-13): only `TokenInvalid` marks the connection disconnected.** Only an answer with Meta error code 190 (`InstagramApiErrorKind.TokenInvalid`) sets `instagram.token-invalid-at`, in a fetch or in a renewal. A transient or other failure keeps the connection `Connected`: the token stays valid for weeks, the next scheduled run retries, and the status turns `Disconnected` by itself at the expiry. Posts already stored stay in every case.

### API client rules (`Infrastructure/Instagram/InstagramApiClient.cs`)

- A typed `HttpClient` (`AddInstagram`), 30 second timeout. Tokens and the secret travel as `access_token` / `client_secret` query parameters or form fields; the `HttpClient` log lines do not contain the token or the secret (`InstagramOptionsTests.HttpClientLogs_NeverContainTheTokenOrTheSecret`) and NLog drops `System.*` up to Info, so a token never reaches a log.
- Errors are `InstagramApiException` with a kind: `TokenInvalid` (code 190), `Transient` (codes 1, 2, 4, 17, 341, HTTP 5xx, timeouts, network errors) or `Other`. The message carries the HTTP status, the code and Meta's message with the token, the secret and every URL replaced (`[redacted]`, `[url]`), cut at 300 characters.
- Media mapping: `IMAGE` uses `media_url`; `VIDEO` its `thumbnail_url`; `CAROUSEL_ALBUM` the first child (image `media_url`, video `thumbnail_url`), else the post's own `media_url`. A post with `media_product_type` `REELS` is stored as `REELS`. A post with no usable image or a missing field (id, permalink, type, timestamp) is skipped with a Warning.
- `DownloadImageAsync` accepts only absolute `https` URLs whose host is an Instagram CDN host and refuses more than `MediaLimits.MaxUploadBytes`. Allowed hosts: `InstagramOptions.ImageHosts` (`Instagram:ImageHosts`); when empty, `InstagramOptions.DefaultImageHosts` = `cdninstagram.com` and `fbcdn.net`. A host matches when it equals an entry or ends with `.` + the entry, case-insensitively. The URL is checked before the request and the final URI is checked again after redirects, before the body is read; both fail with `InstagramApiErrorKind.Other` ("Image URL host is not an Instagram image host."). Known accepted limit: a redirect target is still requested before it is rejected (the URLs come from the Instagram API over TLS).
- `ExchangeCodeAsync`, `ExchangeForLongLivedAsync` and `BuildAuthorizeUrl` take an `InstagramAppCredentials` argument (the client has no `IsConfigured` and reads no secret itself) and throw `ArgumentException` for a blank App ID or secret; callers get the credentials from `IInstagramAppSettingsService.GetCredentialsAsync` and stop when it returns null. `ExchangeCodeAsync` strips a trailing `#_` from the code.

### OAuth state and `SameSite=Strict`

The admin cookie is `SameSite=Strict`, so the browser does not send it on the cross-site return from instagram.com and the callback cannot rely on a signed-in session. `InstagramOAuthState` (`Web/Instagram/InstagramOAuthState.cs`, singleton):

1. Connect (`POST /admin/instagram?handler=Connect`, Admin policy, antiforgery) creates a `state`: a Data Protection payload (purpose `SaronidaSailing.Instagram.OAuthState.v1`) holding the admin's id, a random nonce and an expiry 10 minutes ahead (checked against `TimeProvider`), and sets the cookie `saronida.ig-oauth` with the nonce (`Lax`, `HttpOnly`, `Secure`, path `/admin/instagram`, 10 minutes). `Lax` cookies are sent on a top-level GET navigation, which is what the return is.
2. The callback (`/admin/instagram/callback`, anonymous) accepts the request only when the `state` decrypts, has not expired and its nonce equals the cookie (fixed-time comparison); the cookie is deleted on every attempt. It also requires the admin to exist, have 2FA enabled and not be locked out. The connect is then audited under that admin's email.
3. The callback answers 200 with a meta refresh and a "Continue" link to `/admin/instagram?result=...` (`noindex`, `no-referrer`, `no-store`); it never echoes `code` or `state`. Result codes: `connected`, `denied`, `expired`, `invalid`, `failed`, `notconfigured` (the credentials were removed meanwhile).

### Admin pages

| Path | Purpose |
|---|---|
| `/admin/instagram` | Admin policy. A "Meta app" section (App ID, write-only App secret, the redirect URI to add in Meta, **Save**, **Remove app settings**). Shows the state (not set up / not connected / connected as `@name` with the access expiry, connected date and admin / disconnected), **Connect Instagram** or **Reconnect**, **Fetch now** (only while connected), **Disconnect** (in-page confirmation, `wwwroot/js/admin-instagram.js`; it removes the connection keys, the posts stay), and a Status block: "Scheduled fetches: on / off", posts stored, last fetch and last access renewal with their result. All times are shown in Athens time (`Web/Instagram/AthensTime.cs`, invariant culture) |
| `/admin/instagram/callback` | The OAuth return (above) |
### Fetch, renewal and posts

- **Fetch** (`IInstagramFeedService.FetchAsync`, `Core/Services/InstagramFeedService.cs`): needs a usable token (else result `skipped:not-connected`); asks for the latest 12 posts (`PostLimit`); downloads and imports a thumbnail **only for a post not stored yet** (through `IMediaService.ImportAsync` with `MediaSource.Instagram`, before staging post changes because `ImportAsync` commits itself); updates a stored post's caption and permalink when they changed; removes stored posts that left the latest 12 (their `MediaAsset` stays); then one commit and, only if something was added, updated or removed, an eviction of `page:home`. A failed download or a rejected image skips that post. Any failure or an empty answer keeps the stored posts (`failed:{kind}`, `skipped:no-posts`). `TokenInvalid` marks the connection disconnected. The result is stored in `instagram.last-fetch-*` (`ok:{count}` on success).
- **Token renewal** (`RefreshTokenAsync`): skips when not connected or when the token is younger than 24 hours (`instagram.token-issued-at`; Meta refuses younger tokens); else stores the renewed protected token, expiry and issue time. Result in `instagram.last-refresh-*`.
- The status keys survive Disconnect.
- **Posts as stored:** `InstagramPost` rows (`docs/domain-model.md`); thumbnails go through the media pipeline, so the page never depends on Instagram's image URLs. `MediaAsset.Source` (`Upload` / `Instagram`) keeps Instagram thumbnails out of the media library (`GET /admin/api/media` and `ExistAsync` see only `Upload`; `GET /admin/api/media/{id}` answers 404 for an Instagram asset; `GetByIdAsync` / `GetByIdsAsync` stay unfiltered for rendering). An upload of the same bytes as an Instagram thumbnail turns that asset into an `Upload` (`MediaService.ReuseAsync`).

### Rendering

- Markup: `<section cms-section="home.instagram" cms-section-label="Instagram feed" cms-instagram-count="N" cms-instagram-empty="...">`, a heading `home.instagram.title`, and a list of tiles; each tile links to the post's permalink with the post's own WebP files (`srcset` from `PageImage.SrcSet`, `sizes`, `width` / `height`, `loading="lazy"`). The view component reads the post count from the page content snapshot and the posts from `IInstagramFeedService.GetFeedAsync` (stored posts whose permalink is `https` and whose image exists).
- **Visitors:** the first `count` posts, newest first; nothing at all (no section) when there are no posts. **Admin:** all stored posts, those beyond `count` marked `hidden` (so the select previews live), and with no posts a note "No Instagram posts yet." linking to `/admin/instagram`.
- **Alt text** (`Core/Services/InstagramAltText.cs`): the first non-blank line of the caption, whitespace collapsed, cut to at most 125 characters including the "…" and at a word boundary; with no caption "Ανάρτηση στο Instagram, {date}" (Greek) or "Instagram post, {date}" (English), the date being the post date in Athens time.
- **Post count:** the content field `home.instagram.post-count` (Text, 1-12, default 6), **Greek only** (an English value is rejected on save, both languages follow the Greek one). Edited with the "Posts shown" select in the section bar (`instagram-count.js`; label "Posts shown", accessible name "Posts shown: Instagram feed", hint "Same for both languages."): changing it previews live and becomes a pending `setField` (locale `el`) saved with Save page, with history; Discard restores it. The fetch always stores 12, so changing the number needs no refetch.
- Visitors keep seeing the last good posts whatever happens to the connection.

### Section switch

Deactivating `home.instagram` removes the recurring jobs; activating registers them. `InstagramSectionHandler` (an `ISectionActivationHandler`, see CMS Editing Flow → Write service) calls `IInstagramJobScheduler.Remove()` / `Register()` (`Infrastructure/Jobs/InstagramJobScheduler.cs`, `IRecurringJobManager`; both idempotent). At startup `IInstagramScheduleSync.SyncAsync` runs in `Program.cs` after `InitializeDatabaseAsync` and after the `admin` CLI branch: it registers the jobs only when the section is active and removes them otherwise, so a restart never brings them back while the section is off; a failure stops startup. The toolbar warning is shown only while the section is active, and `/admin/instagram` always states "Scheduled fetches: on/off".

### Not yet verified

Decided 2026-11-13: the developer will not use a personal Facebook or Instagram profile, so there was **no real Meta round trip** in Phase 6 (connect, fetch, feed, renewal, revoke, development mode). The first real connection happens on the Phase 8 preview with the owner (`PLAN.md` → Phase 8); the App ID and App secret are first entered on the preview's `/admin/instagram`.

**Risk for Phase 8 (finding 2026-11-13).** A Meta app the developer created (Business type) offered only "API setup with Unified login" (needs a business portfolio, Facebook Login for Business with a `config_id`, `facebook.com/dialog/oauth`, `graph.facebook.com`, a system-user token and `instagram_basic`) and "API setup with Facebook login", not the "API setup with Instagram login" this code uses. Meta's guide still says the "Other" use case with the Business type adds Instagram login automatically, but developers report newer apps no longer get it. If the owner's app cannot get Instagram login, the API client, the Connect flow and the token renewal need a Unified-login rework before go-live. The first Phase 8 Instagram task is to create the app and check this.

## Background Jobs (Hangfire)

| Job | Id | Schedule (Europe/Athens) |
|---|---|---|
| Instagram fetch | `instagram-fetch` | `0 6,18 * * *` (06:00 and 18:00) |
| Instagram token renewal | `instagram-token-refresh` | `0 4 * * 1` (Mondays 04:00) |

Ids and cron strings are in `Core/Services/InstagramJobIds.cs`; the time zone is `InstagramJobScheduler.TimeZoneId`. The schedule is registered only while the `home.instagram` section is active (see Instagram Feed → Section switch). The database backup is not a Hangfire job: a nightly `pg_dump` Kubernetes CronJob does it (agreed design, see `docs/deployment.md` → Backups).

- Storage: `Hangfire.PostgreSql` 1.21.1 (`Infrastructure/DependencyInjection.cs`), schema `hangfire` in the content database, same connection string (`ConnectionStrings:Hangfire` no longer exists). The schema is installed by `migrate` (or at startup in Development), never by a pod in production: `PrepareSchemaIfNecessary` equals `Database:MigrateAtStartup`.
- Server: Hangfire 1.8.25, **2 workers**, registered in `AddInfrastructure`. **Every pod runs a server**; Hangfire's distributed locks keep each recurring job running once (the single-run behaviour across two pods is checked in the Phase 8 rehearsal).
- Job classes (`Infrastructure/Jobs/`): `InstagramFetchJob` and `InstagramTokenRefreshJob` are thin (one call into `IInstagramFeedService` / `IInstagramConnectionService`), with `[AutomaticRetry(Attempts = 0)]` (the next scheduled run is the retry) and `[DisableConcurrentExecution(600)]`. `IInstagramJobQueue.EnqueueFetch()` (`InstagramJobQueue`) enqueues a one-off fetch: after a successful connect and from **Fetch now**.
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
| `home-content` | `Infrastructure/Health/HomeContentHealthCheck.cs` | At least one `ContentItem` with page key `home` and locale `el` exists (unhealthy "No Greek home content." otherwise, e.g. a database the seed never reached) |

Configuration keys:

| Key | Production (`appsettings.json`) | Development |
|---|---|---|
| `Health:HomePageUrl` | `http://127.0.0.1:8080/` (the container's own port) | `http://localhost:5000/` |
| `Media:RootPath` | `/var/saronida/media` | `data/media` |
| `Media:OriginalsPath` | `/var/saronida/data/originals` | `data/originals` |

The media and originals folders are created at startup **only in Development** (`MediaFolder.EnsureExists` in `Web/Program.cs`). Elsewhere they are bind mounts, and a missing or unwritable folder must fail `/health` rather than be silently created inside the container.

**Not checked:** Instagram (an outage there must not fail health and roll back a good deploy) and maintenance mode (handled by nginx, see `docs/deployment.md`).

Reachable only for direct requests: `UseHealthDirectOnly` (`Web/Health/HealthEndpointAccess.cs`) answers a plain **404** for `/health` and anything below it (path matched case-insensitively) when the request carries `X-Forwarded-For` or `X-Original-For`, i.e. when it came through nginx. The middleware runs before `UseForwardedHeaders`, which would otherwise consume the header. nginx also denies `/health` from the internet with a case-insensitive `location` (nginx config planned, Phase 8), so the two guards cover each other. Used by `deploy.ps1` after every deploy and, planned, by the pod's readiness and liveness probes.

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
- A new admin's 2FA is enrolled by whoever logs in first with that password: a password-only login redirects to `/admin/two-factor/setup` (`Pages/Admin/Login.cshtml.cs`), and the first correct code there turns 2FA on (`Pages/Admin/TwoFactorSetup.cshtml.cs`). So hand the password over through a safe channel and do the first login straight away.
- **Password policy** (`AddInfrastructureIdentity` in `Infrastructure/DependencyInjection.cs`): minimum 12 characters, no character-class rules. Email is the user name and must be unique.

**Pages** (all under `Web/Pages/Admin/`, layout `Pages/Shared/_AdminLayout.cshtml`): English only and not localized, `noindex, nofollow` meta, and `[ResponseCache(NoStore = true)]`. Not linked from the public site.

| Path | Purpose |
|---|---|
| `/admin`, `/admin/` | `302` to `/admin/login` (GET and HEAD, `Cache-Control: no-store`; `Web/Endpoints/AdminEndpoints.cs`). A signed-in admin who follows it is sent on to `/` |
| `/admin/login` | Email + password. An already signed-in admin is redirected to `/` |
| `/admin/login/code` | TOTP code (second step) |
| `/admin/login/recovery` | Recovery code instead of a TOTP code |
| `/admin/two-factor/setup` | Mandatory authenticator setup |
| `/admin/logout` | The GET page only shows a confirm form; signing out is a POST. The POST renews the admin's security stamp (`UpdateSecurityStampAsync`) before signing out, so the admin's sessions in other browsers end at their next stamp check (within a minute) |
| `/admin/instagram` | Instagram connection and status (Admin policy; see Instagram Feed → Admin pages) |
| `/admin/settings` | Site settings (Admin policy, linked as "Settings" in the toolbar). One text area, "robots.txt contents", saved to `IntegrationSettings` `site.robots-txt` through `ISiteSettingsService` (`Core/Services/SiteSettingsService.cs`): line endings become `\n`, trailing spaces and blank lines are trimmed, at most 10,000 characters, empty removes the row (= generated default). Shows the default text and what is "served now", **Save** and **Use default**, a note that a preview server always blocks all crawlers, and a link to `/robots.txt`. Audit `robots-txt-saved` (`characters=N` or `default`, never the text). A save evicts no cache: `robots.txt` is not output-cached |
| `/admin/instagram/callback` | OAuth return from Instagram; **anonymous**, because the `SameSite=Strict` admin cookie is not sent on the cross-site return (see Instagram Feed → OAuth state and `SameSite=Strict`) |

```
password ──► 2FA on?  yes ─► /admin/login/code ─► signed in (amr=mfa) ─► returnUrl or /
                      no  ─► signed in WITHOUT amr ─► /admin/two-factor/setup ─► code ─► signed in (amr=mfa)
```

1. `/admin/login` checks the password with `lockoutOnFailure: false` (`Web/Pages/Admin/Login.cshtml.cs`): a wrong password never counts toward lockout, so someone who only knows an admin's email cannot keep that admin locked out; the per-IP rate limiter and the 12-character minimum guard the password. An account that is already locked is still refused here. Failures show one generic message ("Invalid email or password, or the account is temporarily locked.") whether the user is unknown, the password is wrong or the account is locked.
2. With 2FA on, the user goes to `/admin/login/code`; until the code is accepted the user is held only in the Identity two-factor cookie (`Secure`, `HttpOnly`, `SameSite=Strict`, `Web/Identity/ConfigureTwoFactorUserIdCookie.cs`). A wrong TOTP or recovery code shows a generic message too and counts toward lockout (only someone who already has the password reaches this step).
3. With 2FA off, the password login signs the user in without the `amr` claim, so the `Admin` policy still fails, and the user lands on `/admin/two-factor/setup` (which is also the `AccessDeniedPath`).
4. The setup page shows a QR code (QRCoder, inline SVG, `Web/Identity/AuthenticatorQr.cs`) and the same key in groups of 4. A valid code enables 2FA, signs the user in with `amr=mfa`, and the POST response shows **10 recovery codes once**; the page cannot show them again and the app never logs them.
5. No "remember this browser" and no "remember me": the sign-in is never persistent. `returnUrl` is honoured only when it is a local URL, otherwise `/`.
6. A lost phone is recovered with a recovery code; a forgotten password with `admin reset-password`.

**Authorization:** the `Admin` policy (`Web/Identity/AdminPolicy.cs`) = authenticated user + claim `amr=mfa`. Only a completed 2FA login carries that claim.

**Session:** cookie `saronida.auth`, `Secure` (always), `HttpOnly`, `SameSite=Strict`, 12 hours, sliding. The antiforgery cookie uses the same flags. The security stamp is re-validated every **1 minute**, so a password reset or removal takes effect quickly. A refresh builds a new principal from the user store, which would drop the `amr` claim and lock the admin out of the `Admin` policy; `MfaClaimPreserver` (`OnRefreshingPrincipal`) copies `amr` from the old principal.

**Lockout:** 5 failed attempts → 15 minutes (also for new users), counted **only at the 2FA step**: wrong TOTP and recovery codes count, wrong passwords do not (see step 1).

**Endpoint authorization guard:** `AdminEndpointAuthorizationTests` enumerates every `/admin…` endpoint (Razor Pages, minimal APIs, the Hangfire dashboard) and fails if one does not declare its authorization. Anonymous is allowed only for `/admin` (the redirect, `.AllowAnonymous()`), login, login/code, login/recovery and instagram/callback; plain `[Authorize]` only for logout and two-factor/setup; everything else must use the `Admin` policy. Both allowlists are checked for stale routes.

**Data Protection keys** are persisted to `DataProtection:KeysPath` (`/var/saronida/data/keys`; Development `data/keys`), application name `SaronidaSailing` — otherwise every deploy logs admins out and invalidates antiforgery tokens. The keys are **not encrypted at rest** (file permissions only). When a key is created, `XmlKeyManager` logs a warning that reaches the app log.

- **2FA secrets are encrypted at rest** with the same keys. `Infrastructure/Identity/ProtectedTokenUserStore` (registered with `.AddUserStore<ProtectedTokenUserStore>()` in `Infrastructure/DependencyInjection.cs`, so the web app and the CLI both use it) overrides `SetTokenAsync` / `GetTokenAsync` for the `[AspNetUserStore]` tokens `AuthenticatorKey` and `RecoveryCodes` only, with purpose `SaronidaSailing.Identity.Tokens.v1`; other `AspNetUserTokens` rows are stored as Identity writes them. A stored value that cannot be unprotected (an old plain-text row, or lost keys) reads as **not set**, with a Warning that names the token and the user id and never the value; there is no plain-text fallback. Consequence: a database dump alone no longer bypasses 2FA, and losing the keys (or restoring a database without them) means every admin is re-created with `admin remove` + `admin add` and enrols 2FA again.
- The Instagram access token is protected with these same keys (purpose `SaronidaSailing.Instagram.AccessToken.v1`), as is the Meta App secret (same `ISecretProtector`) and the Instagram OAuth `state`; the keys must therefore be backed up, **apart from the database dumps** (see `docs/deployment.md` → Backups), or after a restore the admin must enter the App secret again, reconnect, and every admin must be re-created (2FA secrets, see above).
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
| `login-locked-out` | A TOTP or recovery attempt hit lockout, or any attempt reached an account that is already locked (a wrong password alone never locks) |
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

Instagram events (`InstagramConnectionService`, `Web/Pages/Admin/Instagram*.cshtml.cs`):

| Event | Written when |
|---|---|
| `instagram-connected` | The callback completed the connect (user: the admin's email; details `username=@name`) |
| `instagram-connect-failed` | The callback refused or the exchange failed. Details `reason=denied`, `expired`, `invalid-state`, `missing-code` or `invalid-user` (user `-`: the request is anonymous), `reason=not-configured` (the App ID or secret was removed before the exchange; user: the admin), or `kind=` the `InstagramApiErrorKind` plus `code=` when Meta gave one (user: the admin) |
| `instagram-disconnected` | **Disconnect** on `/admin/instagram` |
| `instagram-fetch-requested` | **Fetch now** on `/admin/instagram` |
| `instagram-app-saved` | The Meta app form was saved with a change (details `app-id-changed= secret-replaced= disconnected=`, each `true` / `false`; never the values) |
| `instagram-app-removed` | **Remove app settings** (details `disconnected=`) |
| `robots-txt-saved` | The robots.txt text was saved on `/admin/settings` (details `characters=N`, or `default` when emptied; never the text) |

The scheduled fetch and renewal write no audit line: their outcome is the status line on `/admin/instagram` (`instagram.last-fetch-*`, `instagram.last-refresh-*`).

**Known gap (recorded 2026-11-13, NLog rules not changed).** The routing rules drop Info lines of every logger not named in them (the last rule writes only Warn and above), so the Info lines of the Core services never reach a file or the console: `Instagram schedules synced at startup` (`InstagramScheduleSync`), `Instagram schedules registered/removed` (`InstagramSectionHandler`), `Instagram fetch finished: added= updated= removed= skipped=` (`InstagramFeedService`) and `Instagram access token renewed` (`InstagramConnectionService`). Warnings and errors do get through: a failed fetch or renewal, a skipped post, an empty answer from Instagram, a section handler that threw. Until the rules change, the admin page's Status block is the place to see what the jobs did.

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

**Health transitions** (`HealthTransitionPublisher`, an `IHealthCheckPublisher`, first run 5 s after start, then every `HealthCheckPeriodSeconds`): logs changes only, per check (`database`, `hangfire`, `media`, `home-page`, `home-content`) and for `overall`.

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
| Lockout | 5 failed attempts → 15 minutes, per account; counted only at the 2FA step (TOTP and recovery failures), never for wrong passwords |
| 2FA secrets | The authenticator key and recovery codes are stored encrypted (Data Protection, `ProtectedTokenUserStore`), so a database dump alone cannot be used to bypass 2FA; see Admin Accounts |
| Sessions | Logout renews the security stamp, ending the admin's other sessions at the next 1-minute stamp check |
| Security headers | Sent by the app on every response, see Security headers below |
| `/health` | Refused (404) for requests that came through the proxy (`X-Forwarded-For` / `X-Original-For`), see Health Check |
| Admin endpoint authorization | Every `/admin…` endpoint must declare it; enforced by `AdminEndpointAuthorizationTests` (see Admin Accounts) |
| CSRF | Antiforgery tokens on all admin write endpoints (the save API reads the `RequestVerificationToken` header, token from `#cms-config`); antiforgery cookie `Secure`, `HttpOnly`, `SameSite=Strict` |
| Admin API | `/admin/api/*` requires the `Admin` policy, answers 401 / 403 instead of a login redirect, sends `Cache-Control: no-store`, and rejects bodies over 256 KB with 413 |
| Admin pages in caches | Pages rendered for an admin carry `Cache-Control: no-store` and are never stored by the output cache |
| XSS | Razor encoding by default; rich text sanitized with HtmlSanitizer on save, inside `ContentService` (see CMS Editing Flow); link URLs restricted to `https:`, `tel:`, `mailto:` and validated on save |
| Transport | Cloudflare SSL mode **Full (strict)**; Let's Encrypt cert on the origin |
| Instagram image downloads | Only from Instagram CDN hosts (`Instagram:ImageHosts`), checked before the request and after redirects (see Instagram Feed → API client rules) |
| Instagram access token | Stored only encrypted (ASP.NET Data Protection, keys on the file volume, so a database dump holds no usable token); never logged; API error messages and HttpClient logs have token, secret and URLs removed. The Meta App secret is entered by the admin, stored only encrypted (Data Protection), never shown again and never audited |
| Instagram OAuth | Connect issues a Data-Protection-protected `state` (admin id, nonce, 10 minute expiry) plus a short-lived `SameSite=Lax` cookie with the nonce; the anonymous callback accepts only a matching pair and an admin who still exists with 2FA and no lockout. The callback never echoes `code` or `state`. See Instagram Feed → OAuth state and `SameSite=Strict` |
| Uploads | `POST /admin/api/media` is admin-only with antiforgery; the file is validated by decoding (not by extension or content type), limited to 25 MB and 50 megapixels, one at a time per pod; re-encoding strips all metadata; originals are kept outside the served folder, and startup fails if they would be inside it. Only `.webp` is served from the media folder in Development; file names are generated from a slug and the content hash, never from the raw upload name |

### Security headers

`Web/Security/SecurityHeaders.cs` (`UseSecurityHeaders()`, the first middleware) sets the headers in `Response.OnStarting`, so every response the app serves carries them (nginx adds only HSTS and, for the `/media/` files it serves itself, `nosniff`; see `docs/deployment.md`). The same in every environment.

| Header | Value |
|---|---|
| `Content-Security-Policy` (Public) | `default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; connect-src 'self'; frame-src https://www.google.com; frame-ancestors 'self'; base-uri 'self'; form-action 'self' <origin of Instagram:AuthorizeUrl>; object-src 'none'` |
| `Content-Security-Policy` (Admin) | Identical, except `style-src 'self' 'unsafe-inline'` and `img-src 'self' data:` |
| `X-Content-Type-Options` | `nosniff` |
| `Referrer-Policy` | `strict-origin-when-cross-origin` |

- **Which policy:** the Admin CSP only for an authenticated user who has the MFA claim (`amr=mfa`, the same test as the `Admin` policy). Visitors, password-only users in 2FA setup and responses produced before authentication (429 from the rate limiter, proxied `/health`) get the Public one.
- **Why admins are relaxed (decided 2026-11-20):** pasted rich text carries inline styles (Google Docs `<span style="font-weight:700">`), which Quill needs to turn into bold / italic and which the strict CSP blocked; the Hangfire dashboard uses a `data:` favicon and chart-injected `<style>` elements. Scripts stay `'self'` only for everyone.
- **Safe with caching:** the output cache never serves or stores authenticated requests and admin-rendered pages are `no-store`, so the relaxed policy cannot reach a visitor.
- `form-action` includes the Instagram origin because the Connect POST redirects to Instagram's authorize page.

### Accepted limits

- **TOTP code reuse.** A TOTP code can be used again within its validity window (about 30 seconds, plus Identity's tolerance). ASP.NET Core Identity does not track used codes, and the admin login does not add that check (`Pages/Admin/LoginCode.cshtml.cs`). The password, the per-IP login rate limit and the 2FA-step lockout still apply. Accepted 2026-11-20.
- **Instagram image redirects.** `InstagramApiClient.DownloadImageAsync` checks the final URI after redirects before it reads the body, but a redirect target is still requested before the check rejects it. Accepted 2026-11-20 because the image URLs come from the Instagram API over TLS. See Instagram Feed, API client rules.

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
- Banner titles and texts are CMS content (`shared.cookies.*`), in both languages; button labels and status messages are fixed `UiText`.
- Map location is admin-editable: an embed URL, accepted only if it starts with `https://www.google.com/maps/embed` (see Sanitizing and validation). The basic embed is free and needs no API key.

**Implemented (Phase 7)**

| Part | File | Behaviour |
|---|---|---|
| Banner markup | `Pages/Shared/_CookieConsent.cshtml` | Placed right after the skip link in `_Layout` |
| Logic | `wwwroot/js/consent.js` | Choice stored in `localStorage` key `saronida.consent` = `{ "v": 1, "external": bool, "at": ISO time }`. A stored value with another `v` (or an unreadable one) counts as no choice, so a future category re-asks. Blocked storage: the choice applies until the page closes. Exports `hasExternalConsent`, `openConsentSettings`; fires the event `saronida:consent-change` |
| When it shows | By itself on a visitor's **first visit to any page** until a choice is stored; afterwards only from "Cookie settings" in the footer or the map spot's button. The auto-shown banner never takes focus |
| Views | Banner (identical Reject and Accept buttons, "Choose by category", Privacy link) and settings (Necessary "Always on"; External content as a `role="switch"`; Reject all / Save / Accept all; Back). Escape goes back to the banner or closes. After a choice a `role="status"` message with Change and Close. Settings focus their heading; focus returns to the opener (or the map) |
| Map | `wwwroot/js/contact-map.js` | The server HTML never contains an iframe: the page has the static `contact.map.image`, `contact.map.notice` and a "Cookie settings" button. With external-content consent **and** an embed URL the script adds an `<iframe>` (title `UiText.MapTitle`, lazy) and removes it again when consent is withdrawn, without a reload. The embed URL (`contact.map.embed-url`, Greek only) is read from a `hidden` `<p data-map-url>` and edited in the admin's map window (see CMS Editing Flow → Map window); it is not seeded, so the static image shows (and for visitors no notice) until the owner's embed is pasted. After an Apply, `contact-map.js` listens to `saronida:map-change` and updates the live map |
| Cacheability | | Everything is client-side, so every visitor gets the same cached HTML |
| Known limit | | With a live map the admin cannot click the static image (withdraw consent to edit it); the map location itself is changed with the "Map location" button |

**Focus not obscured (WCAG 2.4.11).** While the banner, settings or status is showing, `consent.js` keeps the CSS variable `--consent-offset` (viewport height − panel top + 16 px) and the class `html.consent-open` (ResizeObserver and resize), and `site.css` sets `html { scroll-padding-bottom: var(--consent-offset, 0px) }` (technique C43), so a focused element scrolls above the panel. Below 720 px `.sticky-book` and `.to-top` are `visibility: hidden` while it is open (mobile-first; restored from 720 px).

The Privacy page must cover Cloudflare (hosting/CDN, analytics), Google Maps, and the server logs. Owner/lawyer review before go-live. Any new third-party embed or tracker needs its own consent category.

## Admin Devices

Edit mode is designed for **laptop/desktop**. On phones (under 720 px) it works for quick fixes: the toolbar collapses behind a More button, windows go full-screen, and up/down buttons replace dragging (see CMS Editing Flow → Editing UI).

## Accessibility

Target **WCAG 2.2 AA** (per the design notes): real buttons/links, keyboard focus outline (3 px), text contrast ≥ 4.5:1, touch targets ≥ 44 px, alt text on every content image (alt text is editable content), accessibility statement page (built, `/prosvasimotita`; its conformance wording is a draft until the owner reviews it).

**Audit method (Phase 7, 2026-11-14).** No Playwright. axe-core 4.10.2 (tags wcag2a/aa, 21a/aa, 22aa, best-practice) is injected into each page in the built-in Claude browser, as a logged-out visitor gets it: 18 URLs (7 pages × 2 languages, the three trip tabs, both 404 pages) at 1280 and 375 px = 36 runs, **0 violations**; 8 interactive states (banner desktop and phone, settings desktop and phone, status, phone menu open, lightbox open in el and en), **0 violations**. A manual keyboard pass with real key presses covered the focus order (skip link, consent banner, logo, menu), the 3 px ring on every stop, back-to-top, and the lightbox (swipe, Escape, focus return).

**Found and fixed:** WCAG 2.4.11 Focus Not Obscured (focused content could sit behind the consent panel; fix described in Cookies & Privacy), and the lightbox photo now keeps a 16 / 24 px bottom gap.

**Not covered:** a real touch device, a screen-reader pass, and the Instagram feed (the dev database has no posts). The checks are **not automatic** on later changes; the axe run is repeated on the Phase 8 preview before go-live.

Code conventions that keep it so: the skip link is first, `<main id="main" tabindex="-1">`; menu and link targets are 44 px; the short trip tab labels are contained in the long names (Label in Name, tested).

## Open Items (owner-provided, not blocking development)

Full list to send the owner: [`OWNER-QUESTIONS.md`](../OWNER-QUESTIONS.md).


- Booking platform names and URLs
- Prices, max guests, cabins, email, contact hours, season
- Photos / video
- English texts
- Legal texts reviewed (ideally by a lawyer) before go-live
- English texts: Claude drafts translations of the seed content; owner reviews
