# Domain Model

Status: **planned, not yet implemented.** Exact uniqueness/index details are settled in the Phase 3 brief.

## Content Keys

Every editable spot has a key: `{page}.{section}.{field}` — lowercase, kebab-case segments.

| Example | Meaning |
|---|---|
| `home.hero.title` | Home page, hero section, title text |
| `home.hero.image` | Home page, hero section, image |
| `shared.footer.phone-1` | Footer (on every page), first phone |
| `home.highlights` | A list (no field segment) |
| `home.seo.title` / `home.seo.description` / `home.seo.image` | Page SEO fields (section key `seo` is reserved) |

Page key `shared` is reserved for content on every page (header, footer). Writing to it evicts the cache of all pages.

## Entities

All entities except `ApplicationUser` inherit `BaseEntity`: `Id` (Guid), `CreatedAt` (DateTime, UTC), `UpdatedAt` (DateTime, UTC).

### ContentItem

The current value of one editable field in one locale — either a standalone field or a field of a list item.

| Field | Type | Notes |
|---|---|---|
| `PageKey` | string | e.g. `home` |
| `SectionKey` | string | e.g. `hero` |
| `FieldKey` | string | e.g. `title` |
| `Locale` | string | `el`, `en` |
| `ListItemId` | Guid? | FK → `ContentListItem`; null for standalone fields |
| `Type` | `ContentType` enum | `Text`, `RichText`, `Link`, `Image` |
| `Value` | string | Plain text, sanitized HTML, link JSON (`url`, `label`), or `MediaAsset.Id` |
| `UpdatedBy` | string | FK → `ApplicationUser.Id` |

Uniqueness: one row per (`PageKey`, `SectionKey`, `FieldKey`, `Locale`, `ListItemId`). Note: SQLite and Postgres treat NULLs as distinct in unique indexes, so standalone fields (null `ListItemId`) need their own enforcement — decided in the Phase 3 brief.

### ContentListItem

One entry of a repeating block (a highlight, a trip card, a gallery photo...). Its fields are `ContentItem` rows with `ListItemId` set.

| Field | Type | Notes |
|---|---|---|
| `PageKey` | string | |
| `ListKey` | string | e.g. `highlights` (full key `home.highlights`) |
| `SortOrder` | int | Admin reorders by drag |
| `IsActive` | bool | Inactive items are hidden from visitors, shown dimmed to the admin |

Deleting a list item deletes its `ContentItem` rows.

### ContentSection

Visibility of a whole page section (e.g. hide the Instagram section).

| Field | Type | Notes |
|---|---|---|
| `PageKey` | string | |
| `SectionKey` | string | Unique together with `PageKey` |
| `IsActive` | bool | Inactive sections are hidden from visitors (and their menu links), shown dimmed to the admin |

Toggling `home.instagram` also removes / re-registers the Instagram recurring jobs (see `docs/architecture.md` → Instagram Feed).

### ContentHistory

Append-only log of previous values, used for undo. Never pruned — all history is kept.

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
| `Width` / `Height` | int | Dimensions after orientation fix |

Alt text is a separate `Text` content field (e.g. `home.hero.image-alt`), so it is localized.

### InstagramPost

Cached posts for the Home page feed.

| Field | Type | Notes |
|---|---|---|
| `InstagramId` | string | Unique |
| `Permalink` | string | Link to the post |
| `Caption` | string? | |
| `MediaAssetId` | Guid | FK → `MediaAsset` (downloaded thumbnail) |
| `PostedAt` | DateTime (UTC) | |

### IntegrationSetting

Key/value store for integration state, e.g. the Instagram access token and its expiry.

| Field | Type | Notes |
|---|---|---|
| `Key` | string | Unique, e.g. `instagram.access-token` |
| `Value` | string | |

### ApplicationUser

Extends `IdentityUser` (string PK). One or more admins, created only with the `admin add` command (see `docs/architecture.md` → Admin Accounts). No registration page, no seeding from configuration.
