# Conventions

These rules apply to every file in the codebase. The Dev agent enforces them on every brief.

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
| LINQ only — no raw SQL, no SQLite-specific functions | Provider portability |
| Money (if ever needed) as `long` cents, or display-only | SQLite provider can't compare/order `decimal` in queries |
| Dates as `DateTime`, always UTC — not `DateTimeOffset` | SQLite provider has limited `DateTimeOffset` query support |
| Migrations are provider-specific | A provider switch means a fresh initial migration |
| Enable WAL mode at startup (`PRAGMA journal_mode=WAL`) | Concurrent readers while the admin writes |
| Backups via `VACUUM INTO`, never a raw file copy while running | Avoid corrupt copies |

---

## Output Caching

- Every public page uses `[OutputCache(PolicyName = "CmsPage", Tags = new[] { "page:{pageKey}" })]`.
- Every content write — field save, list item add/delete/reorder/activate — evicts `page:{pageKey}`; writes to page key `shared` evict `pages:all`. A write without eviction is a bug.
- `UseOutputCache()` comes after `UseAuthentication()` and `UseAuthorization()`.

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
