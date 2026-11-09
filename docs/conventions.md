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
| Media file I/O and SkiaSharp code live in `Infrastructure/Media/` | Behind `IMediaService` / storage interfaces in `Core/Interfaces/`. |
| Instagram API client lives in `Infrastructure/Instagram/` | Behind an interface in `Core/Interfaces/`. |
| Hangfire job classes live in `Infrastructure/Jobs/` | Thin: call a service. Recurring jobs registered in `Program.cs`. |
| Razor Pages, tag helpers, admin API endpoints live in `Web` | No business logic in page models or endpoints — delegate to services. |
| ViewModels live in `Web/Models/` | Never pass entities to pages/views. |
| Auth policy and helpers live in `Web/Identity/` | `AdminPolicy`, `AdminLoginRateLimit`, `MfaClaimPreserver`, `AuthenticatorQr`. Admin pages are in `Web/Pages/Admin/` |
| CLI commands live in `Web/Cli/` | `AdminCommandRunner` and its console prompts; selected in `Program.cs` when the first argument is `admin` |
| Audit logger writer `AuditLog` lives in `Infrastructure/Logging/` | Behind `IAuditLog` in `Core/Interfaces/` |

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

---

## C#

- ViewModels, DTOs, requests, responses: `record`.
- Entity properties: `init` where never reassigned; `private set` + a named method where domain logic mutates them. No plain `public set` on rule-governed properties.
- Navigation collections: `public ICollection<T> Items { get; init; } = [];`
- No comments unless the WHY is non-obvious. No commented-out code, no leftover debug logs.

---

## EF Core and SQLite — portability rules

The database must stay swappable (Postgres / SQL Server) with only a provider and migration change.

| Rule | Reason |
|---|---|
| LINQ only — no raw SQL, no SQLite-specific functions. **Single exception:** the startup `PRAGMA journal_mode=WAL` in `Infrastructure/Data/DatabaseInitializer.cs` | Provider portability |
| Money (if ever needed) as `long` cents, or display-only | SQLite provider can't compare/order `decimal` in queries |
| Dates as `DateTime`, always UTC — not `DateTimeOffset` | SQLite provider has limited `DateTimeOffset` query support |
| Migrations are provider-specific | A provider switch means a fresh initial migration |
| Enable WAL mode at startup (`PRAGMA journal_mode=WAL`, done by `DatabaseInitializer` after migrations) | Concurrent readers while the admin writes |
| `CreatedAt` / `UpdatedAt` are stamped by `AppDbContext.SaveChanges*` from `TimeProvider` — never set by hand, never read `DateTime.UtcNow` for them | One clock, testable with a fake `TimeProvider`; the setters on `BaseEntity` are private |
| Every `DateTime` is read back as UTC by a convention converter in `AppDbContext` | SQLite stores no time zone or `Kind` |
| Backups via `VACUUM INTO`, never a raw file copy while running | Avoid corrupt copies |

---

## Output Caching

- Every public page uses `[OutputCache(PolicyName = "CmsPage", Tags = new[] { "page:{pageKey}" })]`.
- Every content write — field save, list item add/delete/reorder/activate — evicts `page:{pageKey}`; writes to page key `shared` evict `pages:all`. A write without eviction is a bug.
- `UseOutputCache()` comes after `UseAuthentication()` and `UseAuthorization()`.

---

## Logging

- NLog only. Audit events (logins, admin actions, content changes) go through `IAuditLog.Record(event, user, ip, details)` (`Core/Interfaces`); never write to the `Audit` logger directly, because `IAuditLog` sanitizes the fields (see `docs/architecture.md` -> Logging). Warnings/errors go to the app log.
- Never log passwords, 2FA codes, recovery codes, tokens or cookies.
- Logs go to files, never to the database.

---

## Tests

- xUnit in `tests/SaronidaSailing.Tests/`.
- Tests that need the web host use `TempDatabaseWebApplicationFactory`: a temp folder per factory holding the content database, `hangfire.db` and the media folder, plus the home-page health client routed to the in-memory test server. The folder is deleted on dispose (the delete is retried because Hangfire's SQLite storage releases its file late).
- Every test class that uses the factory is marked `[Collection(WebHostCollection.Name)]`. Hangfire's `JobStorage.Current` is static, so web-host tests must not run in parallel.
- The factory sets its base address to `https://localhost`, because the auth and antiforgery cookies are `Secure`.
- The login rate limiter is per client IP, so every test client gets its own IP through the test-only `X-Test-Remote-Ip` header, applied by `TestRemoteIpStartupFilter` in `TempDatabaseWebApplicationFactory.cs`. Do not share one client across tests that post to the login paths.
- NLog's `LogManager` is static, so tests that read log files keep their own factory class (the `LogRetentionTests` pattern: `[Collection(WebHostCollection.Name)]` plus `IClassFixture<TempDatabaseWebApplicationFactory>`).
- Tests that only need the `DbContext` (e.g. `BaseEntityTimestampTests`) use a plain SQLite in-memory connection and a fake `TimeProvider`.

---

## Background Jobs

- Every job is idempotent (safe to run twice).
- A failing job never leaves the site broken — e.g. a failed Instagram fetch keeps the last good posts.
- Jobs write through services, so cache eviction rules still apply.

---

## Views and CMS markup

- Every admin-editable value is rendered through `<cms>`, `<cms-link>`, `<cms-image>` or `<cms-list>`. Never hardcode content the admin is meant to edit.
- Repeating blocks are always `<cms-list>`; visitors never see inactive items.
- Every page section is wrapped so it can be deactivated; visitors never see inactive sections or menu links pointing to them.
- Third-party content (currently only the Google Map) loads only after consent in its banner category, client-side. Adding any new embed, tracker or cookie is a tech-lead decision (see `docs/architecture.md` → Cookies & Privacy).
- Never render `RichText` values that did not pass the sanitizer.
- Link URLs allowed: `https:`, `tel:`, `mailto:` only. Links to external sites (booking platforms) open in a new tab and say so.
- Admin-only markup (`data-cms-*`, edit script, inactive items) is emitted only for the authenticated admin.
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
- **Mobile-first**: defaults target small screens; enhance with `@media (min-width: …)`. No `max-width` media queries.
- Breakpoints: `720px` (tablet) and `960px` (desktop) only, unless the tech lead approves another.
- Fonts (Commissioner) are self-hosted `woff2` files in `wwwroot/fonts/` — never loaded from Google's CDN.
- No inline styles where a class will do.

---

## JavaScript

- `let` / `const` only — never `var`.
- Plain JS, no framework. Keep scripts small and scoped; no globals.
- `cms-edit.js` is loaded only for the authenticated admin.

---

## Local Development

- The launch profile serves `https://localhost:5001` and `http://localhost:5000` (`Properties/launchSettings.json`). Admin pages need HTTPS: the antiforgery and auth cookies are `Secure` always, so over plain HTTP the browser drops them and login cannot work. Run `dotnet dev-certs https --trust` once.
- Development data lives under `src/SaronidaSailing.Web/data/` (databases, `media`, `keys`, `logs`).
