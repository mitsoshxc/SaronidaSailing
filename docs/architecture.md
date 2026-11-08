# Architecture

Status: **agreed design, not yet implemented.** Update as phases land.

## Tech Stack

| Concern | Choice | Why |
|---|---|---|
| Web framework | ASP.NET Core 10, Razor Pages | Team's main stack; fixed page templates fit Razor Pages well |
| ORM | EF Core 10 | Keeps the app independent of the database engine |
| Database | SQLite (WAL mode) | One admin, rare writes, tiny data set; no DB server to run. Swap to Postgres/SQL Server later via EF Core if needed |
| Auth | ASP.NET Core Identity, cookie auth, TOTP 2FA | Single admin account |
| Caching | ASP.NET Core Output Caching (in-memory, in the app process) | Visitors are served rendered pages from memory; DB is hit only after a cache miss |
| Media | Files on the droplet disk, served by nginx, cached by Cloudflare | Zero extra cost; no object storage subscription |
| Hosting | DigitalOcean Basic droplet, Docker | See `docs/deployment.md` |
| Reverse proxy / TLS | nginx + Let's Encrypt (certbot) on the host | |
| DNS / CDN | Cloudflare Free | DNS, edge caching of static files, DDoS protection, no cost |

**Explicitly rejected**

| Option | Reason |
|---|---|
| Elasticsearch | Second datastore to sync; needs ≥1 GB RAM on its own (forces a bigger droplet). Output caching already avoids DB reads |
| SQL Server | SQL Server on Linux needs ≥2 GB RAM. Not needed for this data volume |
| Redis | Only useful with multiple app instances; we run one |
| DigitalOcean Spaces | Paid subscription; local disk + Cloudflare is enough at this scale |
| CI/CD pipeline | Deploys run from the developer's PC via `deploy.ps1` (see `docs/deployment.md`) |

## Solution Structure (planned)

```
SaronidaSailing.slnx
src/
  SaronidaSailing.Core/            entities, interfaces, services (no EF, no ASP.NET)
  SaronidaSailing.Infrastructure/  AppDbContext, EF configurations, migrations, repositories, media storage
  SaronidaSailing.Web/             Razor Pages, tag helpers, admin API endpoints, wwwroot, Program.cs
tests/
  SaronidaSailing.Tests/           xUnit, SQLite in-memory
deploy/
  deploy.ps1, docker-compose.yml, nginx/ (site config)
Dockerfile
```

`Core` references nothing. `Infrastructure` references `Core`. `Web` references both.

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

- Named policy `CmsPage`: expiration **1 hour**, tag `pages:all`.
- Each page adds its own tag `page:{pageKey}` (e.g. `page:home`) via `[OutputCache(PolicyName = "CmsPage", Tags = new[] { "page:home" })]`.
- On admin save of a field on page X: `IOutputCacheStore.EvictByTagAsync("page:X")`.
- On admin save of shared content (header, footer — page key `shared`): evict `pages:all`.
- Authenticated requests (the admin) are neither served from nor stored in the cache — built-in default policy behaviour. Requires `UseOutputCache()` **after** `UseAuthentication()`/`UseAuthorization()`.
- Responses that set cookies are not cached (default policy). Antiforgery sets a cookie, so pages with forms (contact form) need a workaround — decided when the contact form is built.
- Cache is lost on restart/redeploy; pages rebuild on first request.

## CMS Editing Flow

1. Razor templates mark each editable spot with a tag helper: `<cms key="home.hero.title" />` (text) or `<cms-image key="home.hero.image" />`.
2. For visitors the tag helper renders the stored value only.
3. When the admin is signed in, it additionally renders `data-cms-key` / `data-cms-type` attributes, and the layout loads `wwwroot/js/cms-edit.js`.
4. `cms-edit.js` makes marked elements editable (inline for text, modal for rich text / images) and saves through admin-only minimal API endpoints (`/admin/api/content`, `/admin/api/media`), with antiforgery tokens.
5. Save → write `ContentItem` + append `ContentHistory` row → evict the page's output-cache tag.
6. Rich-text HTML is sanitized server-side (HtmlSanitizer library) before storage.

## Media Pipeline

1. Admin uploads an image through `/admin/api/media`.
2. The app resizes it to a fixed set of widths and converts to WebP (library: see Open Decisions).
3. Files are written as `{slug}-{contentHash}-{width}.webp` under the media root (`Media:RootPath`, production `/var/saronida/media`, bind-mounted into the container).
4. A `MediaAsset` row records the file set; the `ContentItem` of type `Image` references it.
5. nginx serves `/media/` directly with `Cache-Control: public, max-age=31536000, immutable` (safe because a changed image gets a new filename). In development the app serves the same folder via static files.

## Security

| Concern | Measure |
|---|---|
| Admin login | Identity cookie auth + TOTP 2FA; built-in rate limiter on the login endpoint |
| Real client IP | nginx `realip` module sets the client IP from `CF-Connecting-IP` (Cloudflare ranges only); app uses `UseForwardedHeaders` trusting nginx (127.0.0.1) |
| CSRF | Antiforgery tokens on all admin write endpoints and forms |
| XSS | Razor encoding by default; rich text sanitized with HtmlSanitizer before storage |
| Transport | Cloudflare SSL mode **Full (strict)**; Let's Encrypt cert on the origin |
| Uploads | Admin-only; validate type by decoding the image, not by extension; size limit |

## Open Decisions

| Decision | Status |
|---|---|
| Online bookings / payments | **Open** — affects domain model and scope |
| Languages (Greek + English?) | **Open** — `ContentItem.Locale` exists from day one regardless |
| Image library (ImageSharp vs SkiaSharp vs NetVips) | **Open** — check ImageSharp's license terms before choosing it |
| Contact form email provider | **Open** |
| Backups (DO droplet backups vs nightly off-server copy) | **Open** — see `docs/deployment.md` |
| Image transfer for deploys (container registry vs `docker save \| ssh docker load`) | **Open** — default: no registry |
