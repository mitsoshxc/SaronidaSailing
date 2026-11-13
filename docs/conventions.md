# Conventions

These rules apply to every file in the codebase. The Dev agent enforces them on every brief.

---

## Build

| Rule | Detail |
|---|---|
| Package versions live only in `Directory.Packages.props` | Central package management: `<PackageReference>` in a `.csproj` has no `Version`. |
| Warnings are errors | `TreatWarningsAsErrors` is set for every project in `Directory.Build.props`; fix the warning, do not suppress it. |
| `nuget.config` keeps `nuget.org` as the only source | Machine-level feeds are cleared so restores are the same locally and in Docker. |
| Shared settings (`net10.0`, nullable, implicit usings) live in `Directory.Build.props` | Not repeated in project files. |

---

## Layering

| Rule | Detail |
|---|---|
| Entities live only in `Core/Entities/` | No EF attributes on entity classes — configuration goes in EF configuration classes. |
| Repository and service interfaces live in `Core/Interfaces/` | Repository implementations in `Infrastructure/Repositories/`; service implementations in `Core/Services/`. |
| EF configurations live in `Infrastructure/Data/Configurations/` | One `IEntityTypeConfiguration<T>` per entity. |
| `AppDbContext` lives in `Infrastructure/Data/` | Never accessed from `Web` directly — go through repositories. |
| Media file I/O and SkiaSharp code live in `Infrastructure/Media/` | `MediaService` (the import flow), `ImageProcessor` (SkiaSharp, internal), `MediaProcessingGate`, `MediaOptions`, `MediaFolder`; behind `IMediaService` / `IMediaAssetRepository` in `Core/Interfaces/`. Core has no SkiaSharp |
| Media names, slugs and limits live in `Core/Services/` | `MediaFileNames` (public file name, URL and original file name: the only place that builds them), `Slugs`, `MediaLimits` (25 MB, 50 MP, widths, WebP quality), `MediaDimensions`; do not rebuild file names or repeat the limits elsewhere |
| Instagram API client lives in `Infrastructure/Instagram/` | `InstagramApiClient` behind `IInstagramApiClient` in `Core/Interfaces/`, with its options (`InstagramOptions`), DTOs and `DataProtectionSecretProtector` (behind `ISecretProtector`). The connection, fetch, schedule-sync and warning logic lives in `Core/Services/Instagram*` behind `IInstagram*` interfaces; Web only calls them. See **Instagram client rules** below |
| Hangfire job classes live in `Infrastructure/Jobs/` | Thin: one call into a service. Recurring jobs are registered by a scheduler class (`InstagramJobScheduler`, ids and cron strings in `Core/Services/InstagramJobIds.cs`), not in `Program.cs`: at startup by `IInstagramScheduleSync`, and on a section switch by an `ISectionActivationHandler` |
| View components live in `Web/ViewComponents/` | `{Name}ViewComponent`, with its view at `Pages/Shared/Components/{Name}/Default.cshtml` (e.g. `InstagramFeedViewComponent` and `Components/InstagramFeed/Default.cshtml`); used from a page as `<vc:instagram-feed>`. A component that renders editable content reads it through `IPageContentAccessor` and the admin state through `ICmsEditContext`, like a tag helper; its view model is a record in `Web/Models/` |
| Reacting to a section being shown or hidden goes through `ISectionActivationHandler` | Interface in `Core/Interfaces/` (`SectionKey` = full section key, `OnActiveChangedAsync`), implementation in `Core/Services/` (`InstagramSectionHandler`), registered scoped in `Program.cs`. `ContentService` calls it; never check section toggles in an endpoint. See **Section activation handlers** below |
| Razor Pages, tag helpers, admin API endpoints live in `Web` | No business logic in page models or endpoints — delegate to services. |
| ViewModels live in `Web/Models/` | Never pass entities to pages/views. |
| Auth policy and helpers live in `Web/Identity/` | `AdminPolicy`, `AdminLoginRateLimit`, `MfaClaimPreserver`, `AuthenticatorQr`. Admin pages are in `Web/Pages/Admin/` |
| CLI commands live in `Web/Cli/` | `AdminCommandRunner` and its console prompts (first argument `admin`), `MigrateCommand` (first argument `migrate`); both selected in `Program.cs`, which builds the host without command-line args and exits without starting the web server |
| Runtime diagnostics live in `Web/Diagnostics/` | Lifecycle and crash logging, the resource snapshot, request and health-transition logging, `DiagnosticsOptions`; registered with `AddRuntimeDiagnostics`. Settings are `Diagnostics:*` with defaults in code |
| Cross-pod cache eviction is split across layers | Interfaces `ICacheTagEvictor` (evict tags in this pod) and `ICacheEvictionBus` (publish tags to the other pods) in `Core/Interfaces/`; `OutputCacheTagEvictor` in `Web/Caching/` (needs the output-cache store); the Postgres bus, listener and channel name in `Infrastructure/Caching/` |
| The log instance (pod) name is resolved in one place | `Web/LogInstance.cs` (`Logs:Instance`); never read the machine name directly for logging |
| Audit logger writer `AuditLog` lives in `Infrastructure/Logging/` | Behind `IAuditLog` in `Core/Interfaces/` |
| Repositories only stage changes | They never call `SaveChanges`; the service commits once through `IUnitOfWork` (`Core/Interfaces/`, implemented in `Infrastructure/Data/UnitOfWork.cs`) |
| Content key parsing, locales and link validation live in `Core/Services/` | `ContentKeys`, `ContentLocales`, `ContentLink`; do not re-parse keys or locales elsewhere |
| Output-cache code lives in `Web/Caching/` | `CacheTags` (policy and tag names), `OutputCachePageInvalidator` (implements `IPageCacheInvalidator` from `Core/Interfaces/`: evicts locally, then publishes), `OutputCacheTagEvictor` |
| The per-request content snapshot lives in `Web/Content/` | `IPageContentAccessor`; tag helpers in `Web/TagHelpers/` read content only through it |
| Strongly typed options live in `Web/` | e.g. `SiteOptions` (`Site:BaseUrl`), validated on start |
| The HTML sanitizer lives behind `IRichTextSanitizer` | Interface in `Core/Interfaces/`, `RichTextSanitizer` (HtmlSanitizer) in `Infrastructure/Content/`. Sanitizing and link validation run inside `ContentService`, never only in an endpoint |
| Minimal-API endpoints live in `Web/Endpoints/` | One static `{Feature}Endpoints` class with a `Map{Feature}Endpoints` extension called from `Program.cs` (`ContentEndpoints`, `MediaEndpoints`, `AdminEndpoints` for the `/admin` redirect); request/response records in `{Feature}ApiModels.cs`. Admin API routes are under `/admin/api/` |
| Admin-render context lives in `Web/Content/` | `ICmsEditContext` (is this request an admin; sets `no-store`), `CmsEditorConfig` (the `#cms-config` JSON) |

`Core` must not reference `Infrastructure` or `Web`. `Infrastructure` must not reference `Web`.

---

## Naming

| Concern | Convention | Example |
|---|---|---|
| Page model | `{Page}Model` in `Pages/{Page}.cshtml.cs` | `IndexModel`, `BoatModel` |
| ViewModel | `{Feature}{Purpose}ViewModel` | `ContentEditViewModel` |
| Repository interface | `I{Entity}Repository` | `IContentItemRepository` |
| Service interface | `I{Feature}Service` | `IContentService`, `IMediaService` |
| EF configuration | `{Entity}Configuration` | `ContentItemConfiguration` |
| Tag helper | `{Name}TagHelper` | `CmsTagHelper`, `CmsListTagHelper` |
| Hangfire job | `{Purpose}Job` | `InstagramFetchJob` |
| Content key | `{page}.{section}.{field}`, lowercase kebab-case | `home.hero.title` |
| Cache tag | `page:{pageKey}`, plus `pages:all` | `page:home` |
| Admin API request record | `{Op}Request` (derived from `ContentChangeRequest`); responses `{Thing}Response` | `SetFieldRequest`, `SaveContentResponse` |
| Client JS module | `wwwroot/js/cms/{role}.js`, exports `create{Role}` or `open{Role}Window` | `pending.js` → `createPending` |

---

## C#

- ViewModels, DTOs, requests, responses: `record`.
- Entity properties: `init` where never reassigned; `private set` + a named method where domain logic mutates them. No plain `public set` on rule-governed properties.
- Navigation collections: `public ICollection<T> Items { get; init; } = [];`
- No comments unless the WHY is non-obvious. No commented-out code, no leftover debug logs.

---

## EF Core and PostgreSQL — portability rules

The database is PostgreSQL (Npgsql provider). The data layer stays provider-neutral so that a provider switch is only a provider change plus a fresh initial migration.

| Rule | Reason |
|---|---|
| LINQ only — no raw SQL, no PostgreSQL-specific functions in queries. **Exceptions** (the only ones, all in `Infrastructure/`): the cache-eviction bus and listener use Npgsql directly for `pg_notify` / `LISTEN` (`Infrastructure/Caching/`), and `MigrateAsync` installs the Hangfire schema through `PostgreSqlObjectsInstaller` (`DatabaseInitializer`) | Provider portability; `LISTEN/NOTIFY` and the Hangfire installer have no EF equivalent |
| Dates as `DateTime`, always UTC — not `DateTimeOffset` | Npgsql maps `DateTime` to `timestamp with time zone` and rejects non-UTC values; one type everywhere |
| Migrations are provider-specific | A provider switch means a fresh initial migration (this is how SQLite was replaced) |
| Migrations are applied by `migrate`, not by pods | With `Database:MigrateAtStartup` false a pod only checks that no migration is pending and fails startup otherwise; the flag is true only in `appsettings.Development.json` (see `docs/architecture.md` → Data layer) |
| `CreatedAt` / `UpdatedAt` are stamped by `AppDbContext.SaveChanges*` from `TimeProvider` — never set by hand, never read `DateTime.UtcNow` for them | One clock, testable with a fake `TimeProvider`; the setters on `BaseEntity` are private |
| Every `DateTime` is read back as UTC by a convention converter in `AppDbContext` | Npgsql returns `Kind=Utc` for `timestamptz`; the converter keeps that guaranteed |
| Backups are `pg_dump`, never a copy of the data folder while PostgreSQL runs | Avoid corrupt copies (agreed design, Phase 8; see `docs/deployment.md` → Backups) |

---

## Output Caching

- Every public page model uses `[OutputCache(PolicyName = CacheTags.PolicyName, Tags = [CacheTags.HomePage])]` — the policy `CmsPage` plus its own tag `page:{pageKey}` (add a constant or use `CacheTags.Page(pageKey)` for other pages). A guard test fails if a built site page lacks either.
- Every content write — field save, list item add/delete/reorder/activate — evicts `page:{pageKey}`; writes to page key `shared` evict `pages:all`. A write without eviction is a bug.
- `UseOutputCache()` comes after `UseAuthentication()` and `UseAuthorization()`.
- Evict only through `IPageCacheInvalidator` (content writers) or `ICacheTagEvictor` (the listener): the invalidator evicts locally and publishes to the other pods. Never call `IOutputCacheStore` directly to evict, or the other pod keeps serving the old page.
- The app must be correct with two pods: no state that matters lives only in one pod's memory, except the output cache (kept right by the eviction bus) and the login rate limiter (accepted: the limit is per pod).

---

## Logging

- NLog only. Audit events (logins, admin actions, content changes) go through `IAuditLog.Record(event, user, ip, details)` (`Core/Interfaces`); never write to the `Audit` logger directly, because `IAuditLog` sanitizes the fields (see `docs/architecture.md` -> Logging). Warnings/errors go to the app log.
- Never log passwords, 2FA codes, recovery codes, tokens or cookies.
- Logs go to files, never to the database. All pods share the same daily files; the pod name is the 2nd field of every line (`Logs:Instance`), so a log line format must not contain `|` inside a field without sanitizing it.
- Diagnostics lines (`Lifecycle`, `Metrics`, `Diagnostics` loggers) are written through NLog loggers of those names so the routing rules in `appsettings.json` apply; values taken from requests (paths) go through `RequestDiagnosticsMiddleware.CleanForLog`. Formats are in `docs/architecture.md` → Logging → Diagnostics.

---

## Tests

- xUnit in `tests/SaronidaSailing.Tests/`.
- **Database tests run on real PostgreSQL.** `PostgresTestServer` (`tests/SaronidaSailing.Tests/PostgresTestServer.cs`) starts one `postgres:18.6-alpine` Testcontainer per test run (durability off for speed), migrates a template database once, and hands out a fresh database per test class (`CreateDatabaseAsync` = copy of the migrated template, `CreateEmptyDatabaseAsync` = empty); the database is dropped afterwards. **This needs Docker**, so these tests run on the developer's PC, not in cloud sessions.
- Tests that need the web host use `TempDatabaseWebApplicationFactory`: a temp folder per factory holding the media, logs and keys folders, a PostgreSQL database from `PostgresTestServer` (dropped on dispose; `UseDatabaseOf(owner)` lets a second host share another's database, as the two-pod tests do), `Logs:Instance` = `test-pod`, plus the home-page health client routed to the in-memory test server. Override `UseEmptyDatabase`, `MigrateAtStartup` or `LogInstanceName` in a subclass for tests that need them (`ProductionModeWebApplicationFactory` runs with the flag off).
- Every test class that uses the factory is marked `[Collection(WebHostCollection.Name)]`. Hangfire's `JobStorage.Current` is static, so web-host tests must not run in parallel.
- Cross-pod behaviour is tested with two hosts on one database in `CrossInstanceCacheEvictionTests` (save on A evicts B, listener reconnect evicts everything, malformed payload evicts everything); `AtomicFileConcurrencyTests` covers two writers on one log file; `MigrateCommandTests` covers `migrate` and startup with the flag off; `DiagnosticsTests`, `LifecycleLoggingTests`, `LogInstanceTests` and `ContainerMemoryLimitTests` cover the diagnostics.
- The factory sets its base address to `https://localhost`, because the auth and antiforgery cookies are `Secure`.
- The login rate limiter is per client IP, so every test client gets its own IP through the test-only `X-Test-Remote-Ip` header, applied by `TestRemoteIpStartupFilter` in `TempDatabaseWebApplicationFactory.cs`. Do not share one client across tests that post to the login paths.
- NLog's `LogManager` is static, so tests that read log files keep their own factory class (the `LogRetentionTests` pattern: `[Collection(WebHostCollection.Name)]` plus `IClassFixture<TempDatabaseWebApplicationFactory>`).
- Output-cache behaviour is tested against the real host in `OutputCacheTests` (`OutputCacheWebHostTests`, with its own factory class per the rule above); the policy itself is tested without a host (`CmsPageCachePolicyTests`).
- The web-host factory also creates the originals folder and sets `Media:OriginalsPath` (`TempDatabaseWebApplicationFactory.OriginalsPath`, next to `MediaPath`, never inside it); a test that starts the app as a separate process must set `Media__OriginalsPath` too (`LifecycleLoggingTests`).
- Media tests: `MediaServiceTests` (import flow, with generated images from `MediaTestImages`; also `WriteAtomicAsync`: an existing file is skipped, a lost rename counts as success; it is `internal` and reached through `InternalsVisibleTo`), `MediaApiTests` / `MediaLibraryApiTests` (endpoints on the real host), `MediaServingTests` (Development serving, originals never served), `MediaAssetRepositoryTests`, `MediaFileNamesTests`, `SlugsTests`, `CmsImageTagHelperTests`, `CmsSeoTagHelperTests`, `CmsEditorConfigTests`; the image save checks are in `ContentServiceTests`.
- Instagram tests (`InstagramTestSupport` holds the shared stubs): `InstagramApiClientTests` and `InstagramOptionsTests` (client, options, no token or secret in logs), `InstagramConnectionServiceTests`, `InstagramFeedServiceTests`, `InstagramAltTextTests`, `InstagramPersistenceTests`, `InstagramScheduleTests` (scheduler, startup sync, section handler), `InstagramJobTests`, `InstagramWarningServiceTests`, `InstagramFeedRenderingTests` (view component and tag helper), `InstagramAdminTests` (the admin page and the callback on the real host); the section-handler hook and the post-count rules are in `ContentServiceTests`, the `Source` filter in `MediaServiceTests` and `MediaLibraryApiTests`. All Instagram answers come from stub handlers.
- Tests that count log lines match the exact level and event, not the logger name alone: `LifecycleLoggingTests` counts only `|INFO|Lifecycle|(Started|Stopping|Stopped) ` lines (a bare `|Lifecycle|` match would also count any other line of that logger) and prints the whole log when the count is wrong.
- Tag helpers are unit-tested without a host through `TagHelperTestSupport` (builds a `PageContent` snapshot and a `FakePageContentAccessor`); `PageContentAccessorWebHostTests` uses the web host.
- The save API is tested against the real host in `ContentApiTests` (antiforgery header, 256 KB limit, 400/401/403/413, history endpoints); `RichTextSanitizerTests` and `ContentServiceTests` cover sanitizing and save ids; `CmsAdminRenderingTests` and `CmsEditorTagHelperTests` cover the `data-cms-*` output; `AdminRootRedirectTests` covers the `/admin` redirect.
- **No Playwright in Phase 4.** The editing UI (JS and CSS) has no automated tests; it is verified by driving the built-in Claude browser against `/dev/cms-sandbox`. Playwright + axe-core stays planned for the public pages.
- A fake clock (`FakeTimeProvider`) controls `CreatedAt`/`UpdatedAt` only. `BaseEntity.Id` is `Guid.CreateVersion7()` from the real clock, so it does not follow the fake clock. History reads sort by `CreatedAt` descending, then `Id` descending (`ContentHistoryRepository`); a test that needs a deterministic tie-break passes explicit ids built with `Guid.CreateVersion7(timestamp)` (see `ContentPersistenceTests.HistoryReads_AreNewestFirst`).
- Tests that only need the `DbContext` (e.g. `BaseEntityTimestampTests`, `ContentPersistenceTests`) take a database from `PostgresTestServer` directly and use a fake `TimeProvider`.

---

## Background Jobs

- Every job is idempotent (safe to run twice).
- A failing job never leaves the site broken — e.g. a failed Instagram fetch keeps the last good posts.
- Jobs write through services, so cache eviction rules still apply.
- Job classes are thin and carry `[AutomaticRetry(Attempts = 0)]` (the next scheduled run is the retry) and `[DisableConcurrentExecution(timeoutInSeconds: 600)]` (`InstagramFetchJob`, `InstagramTokenRefreshJob`).
- Recurring schedules are in Europe/Athens (`InstagramJobScheduler.TimeZoneId`); registering and removing are idempotent, so startup sync and section switches can call them any number of times.

### Section activation handlers

- A feature that must react when a section is shown or hidden implements `ISectionActivationHandler` (`SectionKey` is the full key, e.g. `home.instagram`; `OnActiveChangedAsync(isActive, ct)`), registered as `AddScoped<ISectionActivationHandler, ...>` in `Program.cs`.
- `ContentService` calls the handlers of a section after the commit and the cache eviction, only when the section's final state differs from its state before the batch (a section with no row counts as active). The handler runs **after** the save is committed: a handler that throws is logged at Error and the save still succeeds, so a handler must be safe to run again and its state must also be reconciled at startup (as `IInstagramScheduleSync.SyncAsync` does).

### Instagram client rules

- `Instagram:AppId` is in `appsettings.json`; `Instagram:AppSecret` is never in the repo: user secrets on the dev PC, the Kubernetes Secret (`Instagram__AppSecret`) in production. A missing AppId or AppSecret must not stop the app from starting: `IsConfigured` is false and callers check it before Connect.
- Never log, return or store in plain text a token, the app secret, an OAuth `code`, or a URL that contains them. `InstagramApiException` messages are cleaned by the client; the token is stored only through `ISecretProtector`.
- Failures are `InstagramApiException` with a kind (`TokenInvalid` for Meta error 190, `Transient`, `Other`). **Only `TokenInvalid` marks the connection disconnected**; any other failure keeps it connected and keeps the stored posts.
- Instagram image URLs are used only to download a thumbnail through `IMediaService.ImportAsync(..., MediaSource.Instagram)`; pages never render an Instagram URL as an image source. Only `https` URLs are downloaded and only `https` permalinks are rendered.
- Tests use a stub `HttpMessageHandler`; no test or code path calls the real Meta endpoints.

---

## Views and CMS markup

- CMS tag helpers are **attributes on the real element**, not wrapper elements: `<h1 cms="home.hero.title">`, `<a cms-link="home.hero.cta">`, `<li cms-list="home.highlights">` with `cms-field="title"` / `cms-link-field="link"` on its children, `<section cms-section="home.instagram">`, `<a cms-section-link="home.instagram">` for menu links to a section. `<cms-image>` is an **element** (no end tag), see below.
- **Images:** `<cms-image key="home.hero.image" label="Hero photo" sizes="..." alt-source="title" loading="lazy" class="..." />`, or `field="image"` inside a `cms-list` (the same `key` / `field` rule as the other helpers). `label` is required (see **Distinct admin controls** below). `sizes` is required (describe how wide the image is laid out, e.g. `(min-width: 960px) 50vw, 100vw`); `loading` is `lazy` (default) or `eager` for above-the-fold images; `alt-source` names the field whose text proposes the alt text in the admin (a full key with `key`, a field of the same item inside a list), and is optional. The alt text is the content field `{key}-alt` (`{field}-alt` in a list), always Greek-required, so templates never write `alt=""` for content images. Images are never rendered by hand with `<img src="/media/...">`. Details: `docs/architecture.md` → Media Pipeline.
- **Distinct admin controls:** the admin must be able to tell every control from its siblings (screen reader, voice control). So these attributes are **required in every template**, and a missing or blank value throws at render time (`CmsFieldResolver.RequireLabel`): `cms-add-label` on every `cms-list` (the text of its Add button, e.g. `cms-add-label="Add card"`), `label` on every `<cms-image>` (e.g. `label="Hero photo"`), `cms-section-label` on every `cms-section` (e.g. `cms-section-label="Instagram feed"`). Use a short human name, unique on the page. Never add a generic control ("Add item", "Add image", "Section tools"). The names in the client UI are built from these; see `docs/architecture.md` → CMS Editing Flow → Editing UI.
- A rich-text field must say so in the template: `cms-rich` next to `cms` or `cms-field` (`<div cms="about.story.body" cms-rich>`). No attribute means plain `Text`; a stored type that does not match the template is logged and the element is omitted.
- A section that carries an extra admin control marks its element with a dedicated tag helper attribute instead of hand-written `data-cms-*`: `cms-instagram-count="N"` (and `cms-instagram-empty`) on the Instagram feed section (`CmsInstagramSectionTagHelper`); the client module reads the `data-cms-instagram-count*` attributes it emits for the admin.
- `cms-section-link` points only to a section of the current page or of `shared`; any other page key throws at render time. See `docs/architecture.md` → CMS Editing Flow.
- Every admin-editable value is rendered through these helpers. Never hardcode content the admin is meant to edit.
- Repeating blocks are always `cms-list`; visitors never see inactive items. `cms-list` cannot be nested; `cms-field` / `cms-link-field` only inside a `cms-list` element.
- Keys must be valid (`{page}.{section}.{field}` for fields, `{page}.{list}` for lists and sections): an invalid key throws. A missing value omits the element for visitors, so the template must not rely on the element being present.
- Every page section is wrapped in `cms-section` so it can be deactivated; visitors never see inactive sections or menu links pointing to them.
- The layout's `<head>` uses `<cms-seo fallback-title="...">` for the title and Open Graph tags (`Pages/Shared/_Layout.cshtml`).
- Third-party content (currently only the Google Map) loads only after consent in its banner category, client-side. Adding any new embed, tracker or cookie is a tech-lead decision (see `docs/architecture.md` → Cookies & Privacy).
- Never render `RichText` values that did not pass the sanitizer.
- Link URLs allowed: `https:`, `tel:`, `mailto:` only. Links to external sites (booking platforms) open in a new tab and say so.
- Admin-only markup (`data-cms-*`, `#cms-config`, the toolbar, `cms-edit.css`, `cms-edit.js`, inactive items and the list `<template>`) is emitted only for the authenticated admin, through `ICmsEditContext`; such responses are `Cache-Control: no-store`.
- A page used only for development (`/dev/...`) returns 404 unless the environment is Development, and its content keys live under `shared.sandbox*`.
- Every page sets `<html lang>`, canonical URL and `hreflang` alternates.

---

## Accessibility (WCAG 2.2 AA)

- Real `<button>` / `<a href>` elements; no click handlers on `div`/`span`.
- Visible keyboard focus: 3 px outline on links and buttons.
- Text contrast ≥ 4.5:1 (≥ 3:1 for text ≥ 24 px). Touch targets ≥ 44 px.
- Every content image has editable alt text; decorative SVGs use `aria-hidden="true"`; icon-only buttons have `aria-label`.

---

## CSS and fonts

- Custom CSS in `wwwroot/css/site.css`; no CSS framework. Colors, type and spacing follow the design canvas.
- `site.css` starts with a base rule `:root { color-scheme: light; background: #ffffff; color: #14232b }`, so a browser that defaults to dark mode never shows a black page behind unstyled content. It is a placeholder: Phase 7 replaces the colours with the design's.
- **Mobile-first**: defaults target small screens; enhance with `@media (min-width: …)`. No `max-width` media queries.
- Breakpoints: `720px` (tablet) and `960px` (desktop) only, unless the tech lead approves another.
- Fonts (Commissioner) are self-hosted `woff2` files in `wwwroot/fonts/` — never loaded from Google's CDN.
- No inline styles where a class will do.

---

## JavaScript

- `let` / `const` only — never `var`.
- Plain JS, no framework. Keep scripts small and scoped; no globals.
- `cms-edit.js` (entry module) and the modules in `wwwroot/js/cms/` are loaded only for the authenticated admin. ES modules, one factory or window function per file, wired in `cms-edit.js`; no globals.
- Build DOM with `createElement` / `textContent`. Rich-text HTML is only parsed into an inert `<template>` and rebuilt through the allowed-tag filter (`rich-html.js`) before it reaches a live element; no `innerHTML` assignment on live elements.
- **CSP:** no `style=""` attributes and no `<style>` elements, in markup or created from JS (the CSP is own-origin only). Set `element.style` through the CSSOM, or use classes.
- Third-party libraries are vendored into `wwwroot/lib/{name}/` with a `README.md` (version, source, files copied, date) and the license; never loaded from a CDN, and loaded lazily when only the editor needs them (Quill, SortableJS).
- Admin editing CSS lives in `wwwroot/css/cms-edit.css` (loaded only for the admin, same mobile-first rule and 720 px / 960 px breakpoints as `site.css`).

---

## Local Development

- The launch profile serves `https://localhost:5001` and `http://localhost:5000` (`Properties/launchSettings.json`). Admin pages need HTTPS: the antiforgery and auth cookies are `Secure` always, so over plain HTTP the browser drops them and login cannot work. Run `dotnet dev-certs https --trust` once.
- Development needs PostgreSQL: `docker compose -f deploy/dev/docker-compose.yml up -d` (port 5432; the connection string is in `appsettings.Development.json`). In Development the app applies migrations and installs the Hangfire schema at startup (`Database:MigrateAtStartup` is true there).
- Development files live under `src/SaronidaSailing.Web/data/` (`media`, `keys`, `logs`); the database lives in the Docker volume `saronida-dev-pgdata`, so deleting the folder does not reset it (`docker compose -f deploy/dev/docker-compose.yml down -v` does).
- To try two pods locally, run two processes against the same database with different ports and `--Logs:Instance` (the `web-a` / `web-b` entries in `.claude/launch.json`).
