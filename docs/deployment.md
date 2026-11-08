# Deployment

Status: **agreed design, not yet implemented.** Scripts and configs land in the Production phase of `PLAN.md`.

## Infrastructure

| Item | Choice | Notes |
|---|---|---|
| Server | DigitalOcean Basic droplet, Regular CPU, 1 vCPU / 1 GiB RAM / 25 GiB SSD, 1,000 GiB monthly outbound transfer | ~$6/mo per third-party sources — confirm in the DO control panel. Add a 1–2 GB swap file. Upgrade path: CPU/RAM-only resize to 2 GiB (reversible) |
| Region | FRA1 (Frankfurt) | Closest DO region to Greece; EU (GDPR) |
| OS | Ubuntu LTS | |
| App runtime | Docker, one container (Docker Compose) | SQLite file and media folder are bind mounts on the host |
| Reverse proxy | nginx on the host (not in Docker) | |
| TLS | Let's Encrypt via `certbot --nginx` on the host | certbot package handles auto-renewal |
| DNS / CDN | Cloudflare Free | Nameservers point to Cloudflare; domain stays at its registrar. SSL mode **Full (strict)**. Free-plan request body limit 100 MB |

## Host Layout

| Path | Purpose |
|---|---|
| `/opt/saronida/docker-compose.yml` | Compose file (from `deploy/docker-compose.yml`) |
| `/var/saronida/data/` | SQLite database (bind mount → container) |
| `/var/saronida/media/` | Uploaded/generated images (bind mount → container; served directly by nginx) |
| `/etc/nginx/sites-available/saronida` | Site config (from `deploy/nginx/`) |

The .NET container images run as the non-root `app` user (UID 1654) and listen on port 8080. The bind-mounted folders must be writable by UID 1654. The container port is published on loopback only: `127.0.0.1:5000:8080`.

## nginx

- `location /media/` → `alias /var/saronida/media/;` with `Cache-Control: public, max-age=31536000, immutable`.
- Everything else → `proxy_pass http://127.0.0.1:5000;` with `X-Forwarded-For` / `X-Forwarded-Proto`.
- Real client IP: `real_ip_header CF-Connecting-IP;` plus `set_real_ip_from` for each Cloudflare IP range (published at cloudflare.com/ips; update when they change).
- `client_max_body_size` set for image uploads.

## Deploy Script — `deploy/deploy.ps1`

PowerShell 7 (runs on Windows, macOS, Linux). Run from the developer's PC:

1. Refuse to run if the git working tree is dirty.
2. `docker build --platform linux/amd64 -t saronida-web:<commit-sha> .`
3. Transfer the image to the droplet (default: `docker save | ssh <host> docker load`; a registry is an open decision).
4. Over SSH: update the image tag in the compose environment, `docker compose up -d`.
5. Poll `/health`; on failure, roll back to the previous tag.

EF Core migrations are applied at app startup.

## Backups

**Open decision.** Options:

| Option | Cost |
|---|---|
| DO droplet backups | Weekly +20% / daily +30% of droplet price (third-party sources — confirm on DO) |
| Nightly script: `VACUUM INTO` the SQLite DB + copy media folder off the server | Free |

Without one of these, losing the droplet means losing all content. DB and media must always be backed up together.
