# Deployment

Status: **agreed design, not yet implemented.** Scripts and configs land in the Production phase of `PLAN.md`.

## Quick Actions (run on the droplet)

| Need | Command |
|---|---|
| **Take the site offline (maintenance mode ON)** | `touch /var/saronida/maintenance.on` |
| **Bring the site back (maintenance mode OFF)** | `rm /var/saronida/maintenance.on` |
| Check app health | `curl -i http://127.0.0.1:5000/health` |
| Restart the app | `cd /opt/saronida && docker compose restart` |
| App logs | `/var/saronida/logs/app-YYYY-MM-DD.log` (console: `cd /opt/saronida && docker compose logs --tail 200 -f`) |
| Audit log (logins, admin actions) | `/var/saronida/logs/audit-YYYY-MM-DD.log` |
| Add an admin | `saronida-admin add <email>` (prompts for the password) |
| Reset an admin's password | `saronida-admin reset-password <email>` |
| List / remove admins | `saronida-admin list` · `saronida-admin remove <email>` |

Maintenance mode takes effect immediately — no restart, no deploy. Details under nginx → Maintenance Mode.

## Infrastructure

| Item | Choice | Notes |
|---|---|---|
| Server | DigitalOcean Basic droplet, Regular CPU, 1 vCPU / 1 GiB RAM / 25 GiB SSD, 1,000 GiB monthly outbound transfer | ~$6/mo per third-party sources — confirm in the DO control panel. Add a 1–2 GB swap file. Upgrade path: CPU/RAM-only resize to 2 GiB (reversible) |
| Region | FRA1 (Frankfurt) | Closest DO region to Greece; EU (GDPR) |
| OS | Ubuntu LTS | |
| App runtime | Docker, one container (Docker Compose) | Data and media folders are bind mounts on the host |
| Reverse proxy | nginx on the host (not in Docker) | |
| TLS | Let's Encrypt via `certbot --nginx` on the host | certbot package handles auto-renewal |
| DNS / CDN | Cloudflare Free | Nameservers point to Cloudflare; domain stays at its registrar. SSL mode **Full (strict)**. Free-plan request body limit 100 MB |
| Analytics | Cloudflare Web Analytics | Enabled from the Cloudflare dashboard; cookieless |

## Host Layout

| Path | Purpose |
|---|---|
| `/opt/saronida/docker-compose.yml` | Compose file (from `deploy/docker-compose.yml`) |
| `/var/saronida/data/saronida.db` | Content database (bind mount → container) |
| `/var/saronida/data/hangfire.db` | Hangfire job storage |
| `/var/saronida/data/backup/` | Nightly `VACUUM INTO` copy of the content database |
| `/var/saronida/media/` | Generated images (bind mount → container; served directly by nginx) |
| `/var/saronida/data/originals/` | Original uploads (private, never served; used to regenerate sizes) |
| `/var/saronida/data/keys/` | ASP.NET Core Data Protection keys (keep admins logged in across deploys) |
| `/var/saronida/logs/` | NLog files, 90-day retention |
| `/usr/local/bin/saronida-admin` | Wrapper for the admin CLI commands |
| `/etc/nginx/sites-available/saronida` | Site config (from `deploy/nginx/`) |

The .NET container images run as the non-root `app` user (UID 1654) and listen on port 8080. The bind-mounted folders must be writable by UID 1654. The container port is published on loopback only: `127.0.0.1:5000:8080`.

## nginx

**Canonical address: `https://www.saronidasailing.gr`.**

| Request | Response |
|---|---|
| `http://saronidasailing.gr/...` or `http://www.saronidasailing.gr/...` | 301 → `https://www.saronidasailing.gr/...` |
| `https://saronidasailing.gr/...` | 301 → `https://www.saronidasailing.gr/...` |
| `https://www.saronidasailing.gr/...` | Served |

- Let's Encrypt certificate covers **both** names (`certbot --nginx -d www.saronidasailing.gr -d saronidasailing.gr`), so the no-www HTTPS request can be redirected without a certificate warning.
- Cloudflare: DNS records for both `saronidasailing.gr` and `www` proxied; "Always Use HTTPS" on. nginx enforces the same rules, so they hold even if Cloudflare settings change.

- `location /media/` → `alias /var/saronida/media/;` with `Cache-Control: public, max-age=31536000, immutable`.
- Everything else → `proxy_pass http://127.0.0.1:5000;` with `X-Forwarded-For` / `X-Forwarded-Proto`.
- Real client IP: `real_ip_header CF-Connecting-IP;` plus `set_real_ip_from` for each Cloudflare IP range (published at cloudflare.com/ips; update when they change).
- `client_max_body_size` set for image uploads.
- `location /health` → `deny all;` from the internet (only reachable on the droplet via `127.0.0.1:5000`).

### Maintenance Mode

- While `/var/saronida/maintenance.on` exists, nginx answers **every public request** with **503** and a static "Back soon" page (Greek + English, no external assets), plus `Retry-After: 3600`.
- Page source: `deploy/nginx/maintenance.html`, installed to `/var/www/saronida/maintenance.html`.
- 503 tells search engines the outage is temporary, so rankings are not affected.
- Independent of `/health` and the app: you can deploy and test on `127.0.0.1:5000` while maintenance mode is on.

## Security Headers (nginx)

- `Content-Security-Policy`: own origin only, plus `frame-src https://www.google.com` for the Maps embed (after consent). No other third-party sources.
- `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, `frame-ancestors 'self'` (in the CSP) so other sites can't frame ours.
- `Strict-Transport-Security`: enabled **after launch is verified**, starting with a short `max-age` (e.g. 1 day), raised step by step (to 6–12 months). No preload list for now.

## Server Hardening

- SSH key login only; password login and root login disabled.
- A dedicated `deploy` user in the `docker` group. Membership in `docker` is effectively root — protect that SSH key accordingly.
- Firewall: only 22, 80, 443 open.
- Automatic security updates (`unattended-upgrades`), with an automatic reboot at night when an update requires one. The container uses `restart: unless-stopped`, so the site comes back on its own.
- Base images pinned to exact versions in the `Dockerfile`.

## Deploy Script — `deploy/deploy.ps1`

PowerShell 7 (Windows, macOS, Linux). Run from the developer's PC. The droplet never builds and never has access to the repo.

1. `git clone` `main` into a temp folder (so local uncommitted work is never deployed).
2. `docker build --platform linux/amd64 -t saronida-web:<commit-sha> .`
3. `docker save saronida-web:<commit-sha> | ssh deploy@<host> docker load`
4. Over SSH: point the compose file at the new tag, `docker compose up -d`.
5. Poll `/health` on the droplet (`127.0.0.1:5000`); on non-200, switch back to the previous tag.
6. `docker image prune` on the droplet, keeping the current and previous tags; delete the temp folder locally.

EF Core migrations are applied at app startup. After the very first deploy, create the first admin with `saronida-admin add <email>`.

The compose file caps Docker's container console log (json-file `max-size` / `max-file`).

## Monitoring

- **UptimeRobot** (free) checks `https://www.saronidasailing.gr/` every few minutes and emails the developer if it goes down.

## Preview (owner demo)

After local development and testing, the site is shown to the owner on a **separate DigitalOcean droplet** at a subdomain of the developer's domain `visualpivot.net` (A record → droplet IP). Same Docker image and setup as production, with these differences:

| Concern | Preview |
|---|---|
| Host name | `<subdomain>.visualpivot.net` — name TBD |
| TLS | Let's Encrypt certificate for that subdomain |
| **Not crawlable** | (1) nginx **HTTP Basic Auth** on the whole site (crawlers only ever get 401), (2) `X-Robots-Tag: noindex, nofollow` on every response, (3) `robots.txt` → `Disallow: /`, no `sitemap.xml`. Basic Auth pending developer confirmation |
| App config | `ASPNETCORE_ENVIRONMENT=Preview`; `Site:BaseUrl` = the preview URL, used for canonical, `hreflang`, Open Graph and JSON-LD URLs |
| Redirects | No canonical `www.saronidasailing.gr` redirect and no old-site redirects |
| Instagram | The Meta app needs the preview URL as an allowed OAuth redirect URI too |
| Monitoring / DO backups | Not needed |

Open: whether the preview droplet later **becomes production** (content entered by the owner during the preview carries over) or is thrown away.

## Go-Live (switchover)

1. Deploy to the droplet and test everything **before** touching DNS: on the developer's PC, a hosts-file entry points `www.saronidasailing.gr` (and the no-www name) to the droplet's IP. Certificates at this stage: issue them right after DNS points to the droplet, or test over HTTP/self-signed until then.
2. Copy **every** existing DNS record (MX, SPF, DKIM, verification records, subdomains) into Cloudflare — see `OWNER-QUESTIONS.md` 1–4.
3. A day before: lower the DNS TTL.
4. Switch: point DNS to the droplet (via Cloudflare), issue the Let's Encrypt certificate, check pages, redirects (old URLs, http, no-www), admin login, Instagram, map, backups.
5. Keep the old site running a few days as a fallback, then cancel it.
6. Submit the new `sitemap.xml` in Google Search Console / Bing Webmaster Tools; enable HSTS once everything is verified.

## Backups

| Layer | What |
|---|---|
| DigitalOcean droplet backups | Paid add-on, **weekly** (kept 4 weeks; +20% of droplet price). Daily backups are listed only for some regions, not FRA1 — check the control panel for a usage-based plan there |
| Nightly Hangfire job | `VACUUM INTO /var/saronida/data/backup/saronida.db` — guarantees each DO backup contains a consistent copy of the database |

DO backups are stored in the same datacenter as the droplet. Worst case data loss with weekly backups: up to 7 days of admin edits.
