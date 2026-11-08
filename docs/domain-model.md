# Domain Model

Status: **planned, not yet implemented.** Booking-related entities are out of scope until that decision is made (see `docs/architecture.md` → Open Decisions).

## Content Key Format

Every editable spot on the site has a key: `{page}.{section}.{field}` — lowercase, kebab-case segments.

| Example | Meaning |
|---|---|
| `home.hero.title` | Home page, hero section, title text |
| `home.hero.image` | Home page, hero section, image |
| `shared.footer.phone` | Footer (on every page), phone text |

Page key `shared` is reserved for content rendered on every page (header, footer). Saving it evicts the cache of all pages.

## Entities

All entities except `ApplicationUser` inherit `BaseEntity`: `Id` (Guid), `CreatedAt` (DateTime, UTC), `UpdatedAt` (DateTime, UTC).

### ContentItem

The current value of one editable field in one locale.

| Field | Type | Notes |
|---|---|---|
| `PageKey` | string | e.g. `home` |
| `SectionKey` | string | e.g. `hero` |
| `FieldKey` | string | e.g. `title` |
| `Locale` | string | e.g. `el`, `en` |
| `Type` | `ContentType` enum | `Text`, `RichText`, `Image` |
| `Value` | string | Plain text, sanitized HTML, or `MediaAsset.Id` for `Image` |
| `UpdatedBy` | string | FK → `ApplicationUser.Id` |

Unique index: (`PageKey`, `SectionKey`, `FieldKey`, `Locale`).

### ContentHistory

Append-only log of previous values, used for undo.

| Field | Type | Notes |
|---|---|---|
| `ContentItemId` | Guid | FK → `ContentItem` |
| `PreviousValue` | string | |
| `ChangedBy` | string | FK → `ApplicationUser.Id` |
| `ChangedAt` | DateTime (UTC) | |

### MediaAsset

One uploaded image and its generated files.

| Field | Type | Notes |
|---|---|---|
| `OriginalFileName` | string | |
| `ContentHash` | string | Part of the generated file names |
| `Slug` | string | Part of the generated file names |
| `Widths` | string | Generated widths, e.g. `480,960,1600` |
| `Width` / `Height` | int | Original dimensions |

Alt text is a separate `Text` `ContentItem` (e.g. `home.hero.image-alt`) so it is localized like any other text.

### ApplicationUser

Extends `IdentityUser` (string PK). One admin account, created by a seed step from configuration on first run.
