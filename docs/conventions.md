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
| Media file I/O lives in `Infrastructure/Media/` | Behind an interface in `Core/Interfaces/`. |
| Razor Pages, tag helpers, admin API endpoints live in `Web` | No business logic in page models or endpoints — delegate to services. |
| ViewModels live in `Web/Models/` | Never pass entities to pages/views. |

`Core` must not reference `Infrastructure` or `Web`. `Infrastructure` must not reference `Web`.

---

## Naming

| Concern | Convention | Example |
|---|---|---|
| Page model | `{Page}Model` in `Pages/{Page}.cshtml.cs` | `IndexModel`, `ContactModel` |
| ViewModel | `{Feature}{Purpose}ViewModel` | `ContentEditViewModel` |
| Repository interface | `I{Entity}Repository` | `IContentItemRepository` |
| Service interface | `I{Feature}Service` | `IContentService`, `IMediaService` |
| EF configuration | `{Entity}Configuration` | `ContentItemConfiguration` |
| Tag helper | `{Name}TagHelper` | `CmsTagHelper` |
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
| Backups via `VACUUM INTO` / `.backup`, never a raw file copy while running | Avoid corrupt copies |

---

## Output Caching

- Every public page uses `[OutputCache(PolicyName = "CmsPage", Tags = new[] { "page:{pageKey}" })]`.
- Every content write evicts `page:{pageKey}`; writes to page key `shared` evict `pages:all`. A write without eviction is a bug.
- `UseOutputCache()` comes after `UseAuthentication()` and `UseAuthorization()`.
- Pages with forms: antiforgery sets a cookie, so the default policy will not cache them — do not "fix" this by disabling antiforgery.

---

## Views and CMS markup

- Every admin-editable text or image is rendered through `<cms key="..." />` / `<cms-image key="..." />`. Never hardcode content that the admin is meant to edit.
- Never render `RichText` values that did not pass the sanitizer.
- Admin-only markup (`data-cms-*`, edit script) is emitted only for authenticated admins.
- POST forms include `@Html.AntiForgeryToken()` (Razor Pages forms do this automatically with the form tag helper).

---

## CSS

- Custom CSS in `wwwroot/css/site.css`; no CSS framework.
- **Mobile-first**: defaults target small screens; enhance with `@media (min-width: …)`. No `max-width` media queries.
- Breakpoints: `720px` (tablet) and `960px` (desktop) only, unless the tech lead approves another.
- No inline styles where a class will do.

---

## JavaScript

- `let` / `const` only — never `var`.
- Plain JS, no framework. Keep scripts small and scoped; no globals.
- `cms-edit.js` is loaded only for authenticated admins.
