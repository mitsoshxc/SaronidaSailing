# Architecture

Status: **Phases 0-4 are implemented (decisions, foundation, admin authentication, CMS core, inline editing: save API, sanitizing, history, admin rendering and the editing UI); everything else (media, Instagram, jobs, public pages, deploy) is agreed design, not yet built.** Update as phases land.

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
  SaronidaSailing.Core/            Entities/ (BaseEntity, ApplicationUser, Content*), Interfaces/ (repositories, IContentService, IRichTextSanitizer, IUnitOfWork, IPageCacheInvalidator, PageContent), Services/ (ContentService, ContentBatch, ContentKeys, ContentLocales, ContentLink); no EF, no ASP.NET
  SaronidaSailing.Infrastructure/  Data/ (AppDbContext, DatabaseInitializer, UnitOfWork, Configurations/, Migrations), Repositories/, Content/ (RichTextSanitizer), Health/, Logging/ (AuditLog), Media/, DependencyInjection.cs
  SaronidaSailing.Web/             Program.cs, SiteOptions.cs, Caching/ (CacheTags, OutputCachePageInvalidator), Content/ (PageContentAccessor, CmsEditContext, CmsEditorConfig), Endpoints/ (ContentEndpoints, ContentApiModels), TagHelpers/, Cli/, Identity/, Localization/, Health/, Pages/ (Pages/Admin/, Pages/Dev/), wwwroot (css/, js/cms/, lib/quill/, lib/sortablejs/), appsettings*.json
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
- `DatabaseInitializer.InitializeDatabaseAsync` runs in `Web/Program.cs` before the app accepts requests: creates the database folder, applies migrations, then sets `PRAGMA journal_mode=WAL`. Migrations so far: an empty `InitialCreate`, `AddIdentity` (the Identity tables) and `AddContent` (`ContentItems`, `ContentListItems`, `ContentSections`, `ContentHistory`; see `docs/domain-model.md`) and `AddContentHistorySaveId` (adds `ContentHistory.SaveId`, backfills `SaveId = Id` for existing rows, adds an index).
- Entity configurations are in `Infrastructure/Data/Configurations/` (one per content entity). Repositories (`Infrastructure/Repositories/`: `ContentItemRepository`, `ContentListItemRepository`, `ContentSectionRepository`, `ContentHistoryRepository`) only **stage** changes; `IUnitOfWork` (`Infrastructure/Data/UnitOfWork.cs`) has `SaveChangesAsync` (one commit, one transaction) and `DiscardChanges`. All are scoped (`AddInfrastructure`).
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

- **Implemented (Phase 3):** `<cms-seo fallback-title="...">` (`Web/TagHelpers/CmsSeoTagHelper.cs`) in the `<head>` of `Pages/Shared/_Layout.cshtml`. On a site page (one in `SitePages`) it emits `<title>` (`{page}.seo.title`, else the fallback title), `<meta name="description">` (`{page}.seo.description`, omitted when empty), `og:title`, `og:description`, `og:type` (`website`), `og:site_name`, `og:url`, `og:locale` and `og:locale:alternate` (`el_GR` / `en_GB`). On other pages (admin, errors) it emits only `<title>`. Values are read as `Text` fields in the visitor's language (English falls back to Greek). `og:image` from `{page}.seo.image` is deferred to Phase 5 (media).
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
                                       └─► everything else → app container (127.0.0.1:5000)
                                                              │
                                                  Output cache hit? ─ yes ─► cached HTML
                                                              │ no
                                                        Razor Page → ContentService → SQLite
```

Cloudflare by default caches static file types (images, CSS, JS), not HTML, so HTML freshness is controlled by the app's output cache only.

## Output Caching

Implemented in Phase 3 (`Web/Program.cs`, `Web/Caching/`).

- Named policy `CmsPage` (`CacheTags.PolicyName`): expiration **1 hour**, tag `pages:all`, built on the default policy, with the query string ignored (`SetVaryByQuery([])`), so `?x=1` variants share one entry.
- Each page declares the policy and its own tag on its page model: `[OutputCache(PolicyName = CacheTags.PolicyName, Tags = [CacheTags.HomePage])]` (tag `page:{pageKey}`, e.g. `page:home`). Only `IndexModel` is cached so far; a guard test (`OutputCacheTests`) requires every built site page to carry the policy and its page tag, so a new page without it fails the tests.
- `IContentService.ApplyChangesAsync` calls `IPageCacheInvalidator` once per save with the touched page keys. `OutputCachePageInvalidator` (singleton, `IOutputCacheStore.EvictByTagAsync`) evicts `page:X` for each touched page; if `shared` is touched it evicts `pages:all` only.
- A successful Instagram fetch evicts `page:home` (planned, Phase 6).
- Authenticated requests (the admin) are neither served from nor stored in the cache — built-in default policy behaviour (and admin-rendered pages are sent with `Cache-Control: no-store`). `app.UseOutputCache()` is placed **after** `UseAuthentication()`/`UseAuthorization()`.
- Cache is lost on restart/redeploy; pages rebuild on first request.

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
| `<cms-seo fallback-title="...">` | Page `<title>`, description and Open Graph tags (see SEO) |

A field with no value in either language omits its element for visitors; the admin gets an empty element flagged `data-cms-empty` to click into. An invalid key, or `cms-field` outside a `cms-list`, throws. `Web/TagHelpers/CmsFieldResolver.cs` holds the shared key checks. **`<cms-image>` (with `srcset`, `alt-source`) is planned, Phase 5.**

### Content snapshot

Tag helpers read a per-request snapshot, `IPageContentAccessor` (`Web/Content/PageContentAccessor.cs`, scoped): the first call asks `IContentService.GetPageContentAsync(page, locale)` for the current page (from `ILocalizedUrlService`; `shared` when the route is not a site page) and the UI-culture locale, and every later call reuses it. The snapshot (`Core/Interfaces/PageContent.cs`) holds the page's content plus `shared` content, loaded in one pass (three queries: items, list items, sections). English values that are missing or blank fall back to the Greek value, flagged `IsFallback`. For visitors, inactive list items and sections are not rendered.

### Admin rendering

`ICmsEditContext.IsEditorAsync()` (`Web/Content/CmsEditContext.cs`, scoped) evaluates the `Admin` policy once per request. For an admin it also sets `Cache-Control: no-store` on the response (in `OnStarting`, after antiforgery's own header). Only then do the tag helpers and `Pages/Shared/_Layout.cshtml` emit admin markup:

| Emitted | Where |
|---|---|
| `data-cms-key`, `data-cms-type` (`text`, `richText`, `link`) | Every field element |
| `data-cms-value` | Rich-text and link fields: the stored HTML / link JSON, so the editors start from the saved value |
| `data-cms-fallback`, `data-cms-empty`, `data-cms-invalid` | Flags: English shows the Greek fallback, no value, invalid link |
| `data-cms-list`, `data-cms-item` | Every list item copy (`data-cms-item` is the item's Id) |
| `<template data-cms-list-template="{listKey}">` | After each list: one empty copy of the item markup, cloned by "Add item" |
| `data-cms-section`, `data-cms-section-link` | Sections and their menu links |
| `data-cms-active="false"` | Inactive list items, sections and section links; shown dimmed |
| `<script type="application/json" id="cms-config">` | `CmsEditorConfig` (`Web/Content/CmsEditorConfig.cs`): `pageKey`, `locale`, `saveUrl`, `historyUrl`, `savesUrl`, `antiforgeryToken`, `opensInNewTab` and `seo` (the page's SEO title and description, each with its Greek value when English falls back; null on non-site pages) |
| `_AdminToolbar` partial, `~/css/cms-edit.css`, `~/js/cms-edit.js` (module) | Layout |

### Save API

`Web/Endpoints/ContentEndpoints.cs` (`MapContentEndpoints`, called in `Program.cs`) maps the group `/admin/api/content`, which requires the `Admin` policy and sets `Cache-Control: no-store` through an endpoint filter. Request and response records are in `Web/Endpoints/ContentApiModels.cs`; endpoints only translate and delegate to `IContentService`.

| Endpoint | Behaviour |
|---|---|
| `POST /admin/api/content` | Body `{ "changes": [ ... ] }`, each change a polymorphic record with an `op` discriminator: `setField`, `addListItem`, `deleteListItem`, `reorderList`, `setListItemActive`, `setSectionActive` (see Write service). Field `type` is a camel-case string (`text`, `richText`, `link`). The antiforgery token must be sent in the `RequestVerificationToken` header (`HeaderName` set in `Program.cs`). Success: `200 {"saveId": "..."}` (`saveId` is null when the batch changed nothing). Invalid changes: `400 {"errors": [...]}`, nothing written |
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
| `fieldbar.js` | The small bar next to a focused or clicked field: "History" for every field, plus "Edit text" / "Edit link". Clicking a rich-text block or a link opens its window directly |
| `windows.js`, `dialog.js` | `dialog.js` builds the native `<dialog>` windows (focus return, Escape asks before discarding changes); `windows.js` opens the four window types |
| `rich-window.js`, `quill-loader.js`, `rich-html.js` | Rich-text window: Quill 2 limited to heading (h3), bold, italic, numbered and bulleted lists, link and clear formatting. `quill-loader.js` loads Quill lazily. `rich-html.js` is a client-side mirror of the sanitizer's allowed tags, for preview only; the server's sanitizer is the authority. Links not starting with `https://`, `mailto:` or `tel:` are refused |
| `link-window.js`, `values.js` | Link window: type picker (Web page / Email / Phone, which adds `https://`, `mailto:` or `tel:`), label, "Remove link". `values.js` holds shared helpers (text normalize, link parse, allowed schemes) |
| `history-window.js` | Loads `historyUrl`; "Restore this version" sets the field as a pending change; "Undo this whole save" loads `savesUrl{saveId}`, lists the fields that differ from before the save, flags those changed again by a later save (undo overwrites them), and sets them back as pending changes |
| `settings-window.js` | Page settings: `{page}.seo.title` and `{page}.seo.description` in the current language, with the Greek value shown as guidance when English is empty. The image field comes in Phase 5 |
| `missing.js` | Counts elements flagged `data-cms-fallback` and jumps to the next one |
| `lists.js` | List editing (below) |
| `sections.js` | Section bar (below) |

**Lists** (`lists.js`): each list item gets an **item bar** (drag handle, Move up, Move down, Hide/Show, Delete), shown while the pointer or keyboard focus is inside the item. Each list ends with **Add item**: it clones the `data-cms-list-template`, gives the copy a new GUID and focuses its first field. Delete asks first (Delete, "Hide instead", Cancel); a saved item has no history, so its deletion cannot be undone after Save. The list operations are **computed from the DOM** against the state at page load, not recorded as clicks: `addListItem` for new ids, `deleteListItem` for missing ids, `setListItemActive` for changed visibility, and one `reorderList` when the order differs from the expected one. The result goes into the slot `list:{listKey}`. Pending edits of a deleted item's fields are dropped. Drag-and-drop uses SortableJS, loaded lazily only when the page has a list; if it fails to load, the buttons still work.

**Sections** (`sections.js`): every `data-cms-section` gets an always-visible **section bar** with Hide section / Show section. The toggle also dims or restores the section's menu links (`data-cms-section-link`) and sets the slot `section:{sectionKey}` with a `setSectionActive` op (empty when the state returns to the original). The same visibility applies to both languages.

**Phone layout** (under 720 px; `wwwroot/css/cms-edit.css` is mobile-first with a single `min-width: 720px` breakpoint): the toolbar stays in one line (Save page, counter, More) and More opens a panel below; windows are full-screen (`100vw` x `100dvh`) and become centred dialogs from 720 px; the item drag handle is hidden, so items move with the up/down buttons; buttons in the item and section bars and the toolbar are 44 px (2.75rem) touch targets, shrinking from 720 px; `scroll-padding-top` follows `--cms-toolbar-height` so focused fields are not hidden under the toolbar.

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
- Image files are uploaded and processed when chosen (the file must be processed anyway); assigning the image to the field is a pending change like any other. **Planned, Phase 5.**
- **Page settings window:** per page and language, the SEO fields `{page}.seo.title` (browser tab / search result title) and `{page}.seo.description` (search result snippet). `{page}.seo.image` (link-preview image for Facebook, WhatsApp, etc. via Open Graph tags) is planned, Phase 5.

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
| `content-saved` | A content batch was committed (details: `pages=` touched page keys, counts of `fields`, `list-added`, `list-deleted`, `reordered`, `list-activated`, `sections`, and `save=` the save id) |
| `content-save-rejected` | A content batch had an invalid change and nothing was written (details: error count and the first error) |

The user field of the content events is the editor's email.

## Security

| Concern | Measure |
|---|---|
| Admin login | Identity cookie auth + mandatory TOTP 2FA; lockout; per-IP rate limiter on login (see Admin Accounts) |
| Real client IP | nginx `realip` module sets the client IP from `CF-Connecting-IP` (Cloudflare ranges only) and forwards it in `X-Forwarded-For`. The app (`UseForwardedHeaders`, `Web/ForwardedHeadersSetup.cs`) reads `X-Forwarded-For` and `X-Forwarded-Proto` with `ForwardLimit = 1`, and trusts them **only** from the networks in `ForwardedHeaders:KnownNetworks` = `172.30.0.0/24`, the docker compose network subnet. It is not `127.0.0.1`: host nginx reaches the container through the published port, so the app sees the request coming from the compose network gateway. The compose file must pin that subnet (see `docs/deployment.md`) |
| Rate limiting | `UseRateLimiter` with one global partitioned limiter (`Web/Identity/AdminLoginRateLimit.cs`): only **POSTs** to `/admin/login`, `/admin/login/code`, `/admin/login/recovery` and `/admin/two-factor/setup` are limited, 10 per minute per client IP, fixed window, no queue. Rejection: 429, `Retry-After: 60`, plain-text message, audit event `login-rate-limited`. GETs and all other paths are not limited |
| Lockout | 5 failed attempts → 15 minutes, per account; TOTP and recovery failures count |
| CSRF | Antiforgery tokens on all admin write endpoints (the save API reads the `RequestVerificationToken` header, token from `#cms-config`); antiforgery cookie `Secure`, `HttpOnly`, `SameSite=Strict` |
| Admin API | `/admin/api/*` requires the `Admin` policy, answers 401 / 403 instead of a login redirect, sends `Cache-Control: no-store`, and rejects bodies over 256 KB with 413 |
| Admin pages in caches | Pages rendered for an admin carry `Cache-Control: no-store` and are never stored by the output cache |
| XSS | Razor encoding by default; rich text sanitized with HtmlSanitizer on save, inside `ContentService` (see CMS Editing Flow); link URLs restricted to `https:`, `tel:`, `mailto:` and validated on save |
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
