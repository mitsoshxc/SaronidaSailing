# Deployment

Status: **agreed design, not yet implemented.** Scripts and configs land in the Production phase of `PLAN.md`.

## Quick Actions (run on the droplet)

| Need | Command |
|---|---|
| **Take the site offline (maintenance mode ON)** | `touch /var/saronida/maintenance.on` |
| **Bring the site back (maintenance mode OFF)** | `rm /var/saronida/maintenance.on` |
| Check app health | `curl -i http://127.0.0.1:5000/health` |
| Restart the app | `cd /opt/saronida && docker compose restart` |
| App logs | `cd /opt/saronida && docker compose logs --tail 200 -f` |

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
| `/etc/nginx/sites-available/saronida` | Site config (from `deploy/nginx/`) |

The .NET container images run as the non-root `app` user (UID 1654) and listen on port 8080. The bind-mounted folders must be writable by UID 1654. The container port is published on loopback only: `127.0.0.1:5000:8080`.

## nginx

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

## Server Hardening

- SSH key login only; password login and root login disabled.
- A dedicated `deploy` user in the `docker` group. Membership in `docker` is effectively root — protect that SSH key accordingly.
- Firewall: only 22, 80, 443 open.
- Base images pinned to exact versions in the `Dockerfile`.

## Deploy Script — `deploy/deploy.ps1`

PowerShell 7 (Windows, macOS, Linux). Run from the developer's PC. The droplet never builds and never has access to the repo.

1. `git clone` `main` into a temp folder (so local uncommitted work is never deployed).
2. `docker build --platform linux/amd64 -t saronida-web:<commit-sha> .`
3. `docker save saronida-web:<commit-sha> | ssh deploy@<host> docker load`
4. Over SSH: point the compose file at the new tag, `docker compose up -d`.
5. Poll `/health` on the droplet (`127.0.0.1:5000`); on non-200, switch back to the previous tag.
6. `docker image prune` on the droplet, keeping the current and previous tags; delete the temp folder locally.

EF Core migrations are applied at app startup.

## Backups

| Layer | What |
|---|---|
| DigitalOcean droplet backups | Paid add-on, **weekly** (kept 4 weeks; +20% of droplet price). Daily backups are listed only for some regions, not FRA1 — check the control panel for a usage-based plan there |
| Nightly Hangfire job | `VACUUM INTO /var/saronida/data/backup/saronida.db` — guarantees each DO backup contains a consistent copy of the database |

DO backups are stored in the same datacenter as the droplet. Worst case data loss with weekly backups: up to 7 days of admin edits.
