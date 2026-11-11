# Domain Model

Status: **`BaseEntity`, `ApplicationUser`, the four content entities (`ContentItem`, `ContentListItem`, `ContentSection`, `ContentHistory`) and `MediaAsset` are implemented on PostgreSQL: the first five in the migration `InitialCreate`, `MediaAsset` in `AddMediaAssets`. `InstagramPost` and `IntegrationSetting` are planned, not yet implemented.**

Storage: PostgreSQL through the Npgsql EF Core provider. `Guid` columns are `uuid`; every `DateTime` (`CreatedAt`, `UpdatedAt`) is `timestamp with time zone` and read back as UTC (`docs/conventions.md` → EF Core and PostgreSQL). Table names are the ones below (`ContentItems`, `ContentListItems`, `ContentSections`, `ContentHistory`, `MediaAssets`, plus the Identity `AspNet*` tables) in the default `public` schema. Hangfire's tables are not EF entities: they live in the separate schema `hangfire` of the same database, created by the `migrate` command (`docs/architecture.md` → Background Jobs).

## Content Keys

Every editable spot has a key: `{page}.{section}.{field}` — lowercase, kebab-case segments.

| Example | Meaning |
|---|---|
| `home.hero.title` | Home page, hero section, title text |
| `home.hero.image` | Home page, hero section, image (value: a `MediaAsset` id, Greek only) |
| `home.hero.image-alt` | The image's alt text (`Text`, localized; the Greek one is required while an image is set) |
| `shared.footer.phone-1` | Footer (on every page), first phone |
| `home.highlights` | A list (no field segment) |
| `home.seo.title` / `home.seo.description` / `home.seo.image` / `home.seo.image-alt` | Page SEO fields (section key `seo` is reserved); the image and its alt text feed the Open Graph tags |

Page key `shared` is reserved for content on every page (header, footer). Writing to it evicts the cache of all pages.

Parsing and validation live in `Core/Services/ContentKeys.cs`: each segment matches `[a-z0-9]+(-[a-z0-9]+)*` and is at most 100 characters; a field key has exactly 3 segments, a list key or section key exactly 2. Locales are `el` and `en` (`Core/Services/ContentLocales.cs`).

## Entities

All entities except `ApplicationUser` inherit `BaseEntity` (`Core/Entities/BaseEntity.cs`, implemented): `Id` (Guid, `init`, defaults to `Guid.CreateVersion7()`), `CreatedAt` (DateTime, UTC, private setter), `UpdatedAt` (DateTime, UTC, private setter). The timestamps are stamped automatically by `AppDbContext.SaveChanges*` (`CreatedAt` and `UpdatedAt` on insert, `UpdatedAt` on update) and are never set by hand.

### ContentItem

Implemented (`Core/Entities/ContentItem.cs`, table `ContentItems`). The current value of one editable field in one locale — either a standalone field or a field of a list item. Created with `ContentItem.Create(...)`; `Value` and `UpdatedBy` change only through `SetValue`.

| Field | Type | Notes |
|---|---|---|
| `PageKey` | string (max 100) | e.g. `home` |
| `SectionKey` | string (max 100) | e.g. `hero`; for a list item's field, the list's `ListKey` |
| `FieldKey` | string (max 100) | e.g. `title` |
| `Locale` | string (max 5) | `el`, `en` |
| `ListItemId` | Guid? | FK → `ContentListItem`; null for standalone fields |
| `Type` | `ContentType` enum | `Text`, `RichText`, `Link`, `Image` (`Core/Entities/ContentType.cs`); stored as a string (max 20) |
| `Value` | string | Plain text, sanitized HTML, link JSON (`url`, `label`), or `MediaAsset.Id` (as text; `Image` values exist for locale `el` only, see MediaAsset) |
| `UpdatedBy` | string (max 256) | The editing admin's **email**, plain text, no FK to `ApplicationUser` — history survives `admin remove` and continues if the same email is re-added |

PostgreSQL treats NULLs as distinct in unique indexes, so uniqueness is two filtered (partial) unique indexes (`Infrastructure/Data/Configurations/ContentItemConfiguration.cs`):

| Index | Columns | Filter |
|---|---|---|
| `UX_ContentItems_Standalone` (unique) | `PageKey`, `SectionKey`, `FieldKey`, `Locale` | `ListItemId IS NULL` |
| `UX_ContentItems_ListField` (unique) | `ListItemId`, `FieldKey`, `Locale` | `ListItemId IS NOT NULL` |
| `IX_ContentItems_PageKey_Locale` | `PageKey`, `Locale` | none; serves the page read |

A list item's fields carry the list's `PageKey` and its `ListKey` as `SectionKey`. `ContentPersistenceTests` proves both indexes on PostgreSQL.

`Value` is sanitized and validated by `ContentService.ApplyChangesAsync` before it is stored: `RichText` goes through `IRichTextSanitizer`, `Link` JSON must pass `ContentLink.TryParse` (see `docs/architecture.md` → CMS Editing Flow). Link JSON is read and checked by `Core/Services/ContentLink.cs` (`https:`, `tel:`, `mailto:` only). `Image` values are checked in `ContentBatch` (Greek only, an existing media id, a Greek alt text present).

### ContentListItem

Implemented (`Core/Entities/ContentListItem.cs`, table `ContentListItems`). One entry of a repeating block (a highlight, a trip card, a gallery photo...). Its fields are `ContentItem` rows with `ListItemId` set. A list item has no locale (its fields do).

| Field | Type | Notes |
|---|---|---|
| `PageKey` | string (max 100) | |
| `ListKey` | string (max 100) | e.g. `highlights` (full key `home.highlights`) |
| `SortOrder` | int | Admin reorders by drag; changed with `MoveTo` |
| `IsActive` | bool | Default true; changed with `SetActive`. Inactive items are hidden from visitors, shown dimmed to the admin |

`Id` is normally generated by the client (`Create(..., id)` falls back to `Guid.CreateVersion7()`). Index `IX_ContentListItems_PageKey_ListKey_SortOrder`. Deleting a list item cascades (database `ON DELETE CASCADE`) to its `ContentItem` rows and from there to their `ContentHistory` rows.

### ContentSection

Implemented (`Core/Entities/ContentSection.cs`, table `ContentSections`). Visibility of a whole page section (e.g. hide the Instagram section).

| Field | Type | Notes |
|---|---|---|
| `PageKey` | string (max 100) | |
| `SectionKey` | string (max 100) | Unique together with `PageKey` (`UX_ContentSections_PageKey_SectionKey`) |
| `IsActive` | bool | Default true; a section with no row counts as active. Inactive sections are hidden from visitors (and their menu links), shown dimmed to the admin |

The section bar in edit mode toggles it (`SetSectionActive`). Toggling `home.instagram` will also remove / re-register the Instagram recurring jobs (planned, Phase 6, see `docs/architecture.md` → Instagram Feed).

### ContentHistory

Implemented (`Core/Entities/ContentHistory.cs`, table `ContentHistory`). Append-only: **one row per saved version** of a field, the first save included. Never pruned — all history is kept; used for undo (the History window restores one field's version or undoes a whole save).

| Field | Type | Notes |
|---|---|---|
| `ContentItemId` | Guid | FK → `ContentItem`, cascade delete; index `IX_ContentHistory_ContentItemId` |
| `SaveId` | Guid | One id per `ContentService.ApplyChangesAsync` call (`Guid.CreateVersion7()`), shared by every row written by that save; index `IX_ContentHistory_SaveId` |
| `Value` | string | The value as saved in that version |
| `ChangedBy` | string (max 256) | The admin's email, plain text, no FK |

The change time is `CreatedAt`. There is no `ChangedAt` and no `PreviousValue`; the current value stays in `ContentItem`, and a version's previous value is the next-older row of the same item.

A field gets **one history row per field per save**: a batch with two `SetField` for the same key, locale and list item keeps only the last. History covers field values only, not list structure or section toggles.

### MediaAsset

Implemented (`Core/Entities/MediaAsset.cs`, table `MediaAssets`, migration `AddMediaAssets`). One uploaded image and the set of files generated from it. Created with `MediaAsset.Create(...)` by `MediaService` (`docs/architecture.md` → Media Pipeline); every property is `init`.

| Field | Type | Notes |
|---|---|---|
| `OriginalFileName` | string (max 255) | The uploaded file's name as given (cut to 255); display only, never part of a path |
| `ContentHash` | string (max 64) | SHA-256 of the uploaded bytes, lowercase hex. The first 16 characters are part of the public file names; the whole hash names the private original. **Unique** |
| `Slug` | string (max 50) | From the original file name (`Slugs.FromFileName`); part of the public file names |
| `Widths` | string (max 50) | Generated widths, ascending, comma-separated, e.g. `480,960,1600` (`GetWidths()` parses it). Never wider than the image; an image narrower than 480 has one entry, its own width |
| `Width` / `Height` | int | Dimensions after the orientation fix |
| `OriginalFileExtension` | string (max 10) | `.jpg`, `.png` or `.webp`, chosen from the decoded format. Together with `ContentHash` it names the private original (`MediaFileNames.OriginalFileName`); the table stores **no path**, the folder comes from `Media:OriginalsPath` |

| Index | Columns |
|---|---|
| `UX_MediaAssets_ContentHash` (unique) | `ContentHash`. Makes an upload of the same bytes reuse the row; the loser of a concurrent insert gets PostgreSQL error `23505` for this constraint and returns the winner's row |

Files on disk (names built by `Core/Services/MediaFileNames.cs`): `{Slug}-{ContentHash[..16]}-{width}.webp` under `Media:RootPath` for each width, and `{ContentHash}{OriginalFileExtension}` under `Media:OriginalsPath`. A `MediaAsset` is never deleted at launch, so a content value or history row never points at a missing row.

**How content refers to an asset.** An `Image` content value (`ContentItem.Value`, type `Image`) is the `MediaAsset.Id` as text. It exists **in Greek only** (`el`); English shows the Greek value (the save is rejected for `en`). There is no foreign key (content values are strings); the save checks that the id exists (`docs/architecture.md` → Media Pipeline → Image values and save checks), and a template or page read that meets a missing id logs a warning and renders no image.

**Alt text** is a separate localized `Text` content field whose key is the image's key plus `-alt` (`home.hero.image` → `home.hero.image-alt`; a list item field `image` → `image-alt`; `home.seo.image` → `home.seo.image-alt`). The Greek alt is required whenever an image is set; the English alt is optional and falls back to Greek like any other field. History keeps image values and alt texts like every other field.

**`PageImage`** (`Core/Interfaces/PageImage.cs`) is the read model: id, width, height and the file list `[{Width, Url}]` (`PageImageSource`), built from a `MediaAsset` by `ContentService.LoadImagesAsync` for the images a page's content refers to; `HeightAt(width)` gives a file's height. `ContentValue.DefaultLocaleValue` (`Core/Interfaces/PageContent.cs`) carries the Greek value next to the value shown in English, so the admin can see the Greek alt text on an English page.

### InstagramPost

Planned. Cached posts for the Home page feed.

| Field | Type | Notes |
|---|---|---|
| `InstagramId` | string | Unique |
| `Permalink` | string | Link to the post |
| `Caption` | string? | |
| `MediaAssetId` | Guid | FK → `MediaAsset` (downloaded thumbnail) |
| `PostedAt` | DateTime (UTC) | |

### IntegrationSetting

Planned. Key/value store for integration state, e.g. the Instagram access token and its expiry.

| Field | Type | Notes |
|---|---|---|
| `Key` | string | Unique, e.g. `instagram.access-token` |
| `Value` | string | |

### ApplicationUser

Extends `IdentityUser` (string PK) in `Core/Entities/ApplicationUser.cs`; implemented with no extra fields. The Identity `AspNet*` tables live in the same database as the content tables (migration `InitialCreate`). One or more admins, created only with the `admin add` command (see `docs/architecture.md` → Admin Accounts). No registration page, no seeding from configuration.
