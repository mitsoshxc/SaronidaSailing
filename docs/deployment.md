# Deployment

Status: **the `Dockerfile`, the app configuration and the local development PostgreSQL (`deploy/dev/docker-compose.yml`) are implemented. Everything about the droplet, k3s, nginx, the deploy script and backups is agreed design (2026-11-11, `PLAN.md` → Phase 4.5 and Phase 8), not yet built.** The earlier Docker Compose + SQLite + DigitalOcean-backups design is gone.

The app itself is ready for it (Phase 4.5): it runs correctly as two pods against one PostgreSQL database (cache eviction across pods, shared log files, migrations by a separate command). Launch runs one pod.

## Quick Actions (run on the droplet)

Maintenance mode is plain nginx on the host and works as described. The `kubectl` rows are **agreed design (Phase 8)**; the Deployment, namespace and Service names are fixed when `deploy/k8s/` is written.

| Need | Command |
|---|---|
| **Take the site offline (maintenance mode ON)** | `touch /var/saronida/maintenance.on` |
| **Bring the site back (maintenance mode OFF)** | `rm /var/saronida/maintenance.on` |
| Check app health | `curl -i http://127.0.0.1:<NodePort>/health` (the app Service's NodePort; nginx denies `/health` from the internet) |
| Restart the app | `kubectl rollout restart` on the app Deployment |
| App logs | `/var/saronida/logs/app-YYYY-MM-DD.log` (all pods write the same file; the pod name is the 2nd field of every line); console: `kubectl logs` |
| Audit log (logins, admin actions) | `/var/saronida/logs/audit-YYYY-MM-DD.log` |
| Resource metrics (memory, CPU, requests, per minute) | `/var/saronida/logs/metrics-YYYY-MM-DD.log` |
| Kubernetes events (OOMKilled, restarts, failed probes) | `/var/saronida/logs/k8s-events-YYYY-MM-DD.log` |
| Add an admin | `saronida-admin add <email>` (prompts for the password) |
| Reset an admin's password | `saronida-admin reset-password <email>` |
| List / remove admins | `saronida-admin list` · `saronida-admin remove <email>` |

Maintenance mode takes effect immediately — no restart, no deploy. Details under nginx → Maintenance Mode. A `Started` line in `app-*.log` with no `Stopped` before it means the previous run was killed (see `docs/architecture.md` → Logging → Diagnostics).

## Infrastructure

| Item | Choice | Notes |
|---|---|---|
| Server | DigitalOcean Basic droplet, 1 vCPU / 2 GiB RAM, about $12/mo + VAT | Add a swap file. Scale-up path: resize to 2 vCPU / 4 GiB (about $24/mo) and set the app Deployment to `replicas: 2` (see Scaling to two pods) |
| Region | FRA1 (Frankfurt) | Closest DO region to Greece; EU (GDPR) |
| OS | Ubuntu LTS | |
| App runtime | k3s, single node; one app pod at launch (Deployment, `replicas: 1`, readiness/liveness probes) | k3s's bundled Traefik is **disabled**. The app's Service is a NodePort. Docker is used only on the developer's PC to build the image |
| Database | PostgreSQL, one pod (StatefulSet, 1 replica) inside the cluster | Data on the droplet disk under `/var/saronida/pgdata`; never exposed outside the cluster. Version pinned when the manifests are written (local dev and tests use `postgres:18.6-alpine`) |
| Reverse proxy | nginx on the host (not in the cluster) | Proxies to the app Service's NodePort. NodePorts are blocked by the firewall |
| TLS | Let's Encrypt via `certbot --nginx` on the host | certbot package handles auto-renewal |
| DNS / CDN | Cloudflare Free | Nameservers point to Cloudflare; domain stays at its registrar. SSL mode **Full (strict)**. Free-plan request body limit 100 MB |
| Analytics | Cloudflare Web Analytics | Enabled from the Cloudflare dashboard; cookieless |
| Backups | `pg_dump` CronJob on the droplet + pull script on the developer's PC | No DigitalOcean backups. See Backups |

## Host Layout

Agreed design (Phase 8); the media, logs and keys folders are mounted into every app pod (single node, so host folders are shared).

| Path | Purpose |
|---|---|
| `/var/saronida/pgdata/` | PostgreSQL data (mounted into the PostgreSQL pod only) |
| `/var/saronida/backup/` | Nightly `pg_dump` files (last 7 kept on the droplet) |
| `/var/saronida/media/` | Generated images (mounted into the pods; served directly by nginx) |
| `/var/saronida/data/originals/` | Original uploads (private, never served; used to regenerate sizes) |
| `/var/saronida/data/keys/` | ASP.NET Core Data Protection keys (keep admins logged in across deploys and valid on every pod) |
| `/var/saronida/logs/` | NLog files shared by all pods (`app-*`, `audit-*` 90 days, `metrics-*` 30 days) and `k8s-events-*` written by the event recorder |
| `/var/saronida/maintenance.on` | Marker file: while it exists, nginx answers 503 |
| `deploy/k8s/` (in the repo) | Kubernetes manifests: namespace, app Deployment and NodePort Service, PostgreSQL StatefulSet, `migrate` Job, `pg_dump` CronJob, Secrets |
| `/usr/local/bin/saronida-admin` | Wrapper for the admin CLI commands (`kubectl exec` into an app pod) |
| `/etc/nginx/sites-available/saronida` | Site config (from `deploy/nginx/`) |

The .NET container images run as the non-root `app` user (UID 1654) and listen on port 8080. The host folders the app writes (`/var/saronida/data/keys`, `/var/saronida/media`, `/var/saronida/logs`) **must be writable for UID 1654** (e.g. `chown -R 1654:1654`): the app writes the Data Protection keys to `data/keys` and the NLog files to `logs`.

## Dockerfile

Implemented (`Dockerfile`, `.dockerignore` at the repo root).

| Fact | Detail |
|---|---|
| Stages | `build` on `mcr.microsoft.com/dotnet/sdk:10.0.403-noble`, `final` on `mcr.microsoft.com/dotnet/aspnet:10.0.14-noble` (both pinned to exact versions) |
| Why full Noble, not chiseled | The Greek culture needs ICU, which the chiseled images do not ship |
| Restore layer | Root build files and the three `.csproj` files are copied first and restored, so source changes do not re-download packages. `nuget.config` is copied too, so Docker restores from nuget.org only |
| Publish | `dotnet publish src/SaronidaSailing.Web -c Release --no-restore`; `tests/`, `docs/`, `deploy/`, the dev data folder `src/SaronidaSailing.Web/data`, `*.db*` and `*.md` are excluded by `.dockerignore` |
| User / port | `USER $APP_UID` (1654), `EXPOSE 8080`; entrypoint `dotnet SaronidaSailing.Web.dll` |
| No `HEALTHCHECK` | Health is checked by the pod probes and by `deploy.ps1` against `/health` |
| No `/var/saronida` folders in the image | They are host folders mounted into the pods |

The same image runs the web app and the one-off commands: `dotnet SaronidaSailing.Web.dll migrate` (the migrate Job) and `dotnet SaronidaSailing.Web.dll admin <command>`.

## Configuration

Defaults are in `src/SaronidaSailing.Web/appsettings.json`; environment variables (set in the pod spec) override them.

| Key | Production value | Notes |
|---|---|---|
| `ConnectionStrings:Content` | `Host=postgres;Port=5432;Database=saronida;Username=saronida` | Npgsql connection string, **without a password**: the deployment supplies the password from the Kubernetes Secret (Phase 8). `postgres` is the in-cluster host name the file assumes. One string serves EF Core, Hangfire storage (schema `hangfire`) and the cache-eviction bus/listener. `ConnectionStrings:Hangfire` no longer exists |
| `Database:MigrateAtStartup` | `false` | `true` only in `appsettings.Development.json`. With `false` a pod never migrates: startup fails when migrations are pending and names the `migrate` command. The `migrate` command applies the EF migrations and installs the Hangfire schema |
| `Media:RootPath` | `/var/saronida/media` | Must exist and be writable; outside Development it is **not** created by the app, and `/health` fails if it is missing |
| `Health:HomePageUrl` | `http://127.0.0.1:8080/` | URL the `home-page` health check requests from inside the container |
| `Logs:Path` | `/var/saronida/logs` | NLog folder (`audit-*`, `app-*`, `metrics-*`); must be writable for UID 1654; startup fails if the key is missing |
| `Logs:Instance` | not set | Name written as the 2nd field of every log line. Defaults to the machine name, which in Kubernetes is the pod name. An empty value or one containing `\|` or control characters fails startup. As an environment variable: `Logs__Instance` |
| `Diagnostics:*` | not set (defaults in code) | Thresholds and intervals of the runtime diagnostics; values must be greater than 0. Keys and defaults: `docs/architecture.md` → Logging → Diagnostics |
| `DataProtection:KeysPath` | `/var/saronida/data/keys` | Data Protection keys (unencrypted at rest); created if missing, must be writable for UID 1654; startup fails if the key is missing |
| `ForwardedHeaders:KnownNetworks` | `["172.30.0.0/24"]` in `appsettings.json` | CIDR networks allowed to set `X-Forwarded-For` / `X-Forwarded-Proto`; an invalid value fails startup. As an environment variable: `ForwardedHeaders__KnownNetworks__0`. The value in the file is the old Docker Compose subnet; Phase 8 sets it to the k3s network (see below) |

A missing `ConnectionStrings:Content`, `Media:RootPath`, `Logs:Path` or `DataProtection:KeysPath` fails startup.

**Cluster network.** Host nginx reaches the app through the Service NodePort, so the app sees requests coming from the cluster network, not from `127.0.0.1`. `ForwardedHeaders:KnownNetworks` must name exactly the network those requests arrive from; Phase 8 finds it and sets it. If it is wrong, the app ignores the client IP and every login rate-limit bucket becomes one address.

**Admin CLI.** `saronida-admin` (Phase 8) wraps `kubectl exec` into an app pod running `dotnet SaronidaSailing.Web.dll admin <command>`. The commands read the password from a hidden prompt and need a TTY, so `kubectl exec` must allocate one. Without a TTY the command fails with a message and exit code 1.

**Migrations.** The deploy script runs a `migrate` Job (same image, `dotnet SaronidaSailing.Web.dll migrate`) before each rollout; the first deploy creates the schema this way. After the very first deploy, create the first admin with `saronida-admin add <email>`.

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
- Everything else → `proxy_pass` to the app Service's NodePort on the host, with `X-Forwarded-For` / `X-Forwarded-Proto`.
- Real client IP: `real_ip_header CF-Connecting-IP;` plus `set_real_ip_from` for each Cloudflare IP range (published at cloudflare.com/ips; update when they change).
- `client_max_body_size` set for image uploads.
- `location /health` → `deny all;` from the internet (only reachable on the droplet itself).
- nginx access and error logs are kept **30 days** (logrotate).

### Maintenance Mode

- While `/var/saronida/maintenance.on` exists, nginx answers **every public request** with **503** and a static "Back soon" page (Greek + English, no external assets), plus `Retry-After: 3600`.
- Page source: `deploy/nginx/maintenance.html`, installed to `/var/www/saronida/maintenance.html`.
- 503 tells search engines the outage is temporary, so rankings are not affected.
- Independent of `/health` and the app: you can deploy and test against the NodePort on the droplet while maintenance mode is on.

## Security Headers (nginx)

- `Content-Security-Policy`: own origin only, plus `frame-src https://www.google.com` for the Maps embed (after consent). No other third-party sources.
- `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, `frame-ancestors 'self'` (in the CSP) so other sites can't frame ours.
- `Strict-Transport-Security`: enabled **after launch is verified**, starting with a short `max-age` (e.g. 1 day), raised step by step (to 6–12 months). No preload list for now.

## Server Hardening

- SSH key login only; password login and root login disabled.
- A dedicated `deploy` user for deploys (image import and `kubectl`); how it is granted those rights is decided in Phase 8 — the SSH key is effectively a root key, protect it accordingly.
- A separate read-only backup user for the PC backup script (see Backups).
- Firewall: only 22, 80, 443 open; **NodePorts closed**, PostgreSQL never exposed outside the cluster.
- Automatic security updates (`unattended-upgrades`), with an automatic reboot at night when an update requires one. k3s and the pods must come back on boot on their own.
- Base images pinned to exact versions in the `Dockerfile`.

## Deploy Script — `deploy/deploy.ps1`

Agreed design (Phase 8). PowerShell 7 (Windows, macOS, Linux). Run from the developer's PC. The droplet never builds and never has access to the repo.

1. `git clone` `main` into a temp folder (so local uncommitted work is never deployed).
2. `docker build --platform linux/amd64 -t saronida-web:<commit-sha> .`
3. `docker save saronida-web:<commit-sha> | ssh deploy@<host>` into `ctr images import` (k3s's containerd): no registry.
4. Run the `migrate` Job with the new image and wait for it; a failed migration stops the deploy before any pod is replaced.
5. Roll out the new image to the app Deployment and wait for it.
6. Check `/health` on the droplet. What happens on a failed check (the earlier design switched back to the previous tag) is decided when the script is written.
7. Clean up old images on the droplet, keeping the current and previous tags; delete the temp folder locally.

## Crash and Resource Diagnostics

The app logs what it can see itself (lifecycle, crashes, per-minute metrics, slow requests, health changes; `docs/architecture.md` → Logging → Diagnostics). A process killed by the out-of-memory killer or `SIGKILL` cannot log its own death, so the droplet records it from outside (all agreed design, Phase 8):

| Piece | What it records |
|---|---|
| Kubernetes event recorder | A small service on the droplet writes cluster events (`OOMKilled`, failed liveness checks, restarts, evictions) to `/var/saronida/logs/k8s-events-YYYY-MM-DD.log`; Kubernetes forgets events after about an hour |
| Memory requests and limits | Set on the app pod and on PostgreSQL, so .NET's GC works within the limit (the app logs the container limit and the GC heap limit) and a kill is recorded as `OOMKilled` |
| nginx logs | Access and error logs kept 30 days (logrotate) |
| Persistent systemd journal | Size-capped, so the kernel's out-of-memory messages for the whole droplet survive a reboot |

## Monitoring

- **UptimeRobot** (free) checks `https://www.saronidasailing.gr/` every few minutes and emails the developer if it goes down.

## Scaling to two pods

Agreed design (Phase 8). The app is already correct with two pods; the steps are operational:

1. Resize the droplet to 2 vCPU / 4 GiB (about $24/mo).
2. Set the app Deployment to `replicas: 2`.

What the app does with two pods: each pod keeps its own output cache and evicts the other's through PostgreSQL `LISTEN/NOTIFY`; every pod runs a Hangfire server and Hangfire's locks keep recurring jobs single; Data Protection keys, media and originals are shared host folders, so an admin session works on both; both pods write the same daily log files; the login rate limiter is per pod (accepted). Phase 8 includes a two-pod rehearsal on the preview (cache eviction across pods, jobs run once, admin login, both pods in the shared logs, a forced pod kill visible in `k8s-events`, then back to one pod) and a written scale-up runbook.

## Preview (owner demo)

After local development and testing, the site is shown to the owner on the DigitalOcean droplet at **`https://saronida.visualpivot.net`** (A record on the developer's domain `visualpivot.net` → droplet IP). **This droplet later becomes production**: content the owner enters during the preview carries over. Same image and setup as production, with these differences:

| Concern | Preview |
|---|---|
| Host name | `saronida.visualpivot.net` |
| TLS | Let's Encrypt certificate for that subdomain |
| **Not crawlable** | (1) nginx **HTTP Basic Auth** on the whole site (crawlers only ever get 401), (2) `X-Robots-Tag: noindex, nofollow` on every response, (3) `robots.txt` → `Disallow: /`, no `sitemap.xml`. One shared username/password for the owner, kept in an htpasswd file on the droplet (never in the repo) |
| App config | `ASPNETCORE_ENVIRONMENT=Preview`; `Site:BaseUrl` = the preview URL (set as the environment variable `Site__BaseUrl`; required, the app refuses to start without a valid absolute http(s) URL with no path), used for canonical, `hreflang`, Open Graph and JSON-LD URLs |
| Redirects | No canonical `www.saronidasailing.gr` redirect and no old-site redirects |
| Instagram | The Meta app needs the preview URL as an allowed OAuth redirect URI too |
| Monitoring | Not needed |
| Backups | The PC backup script runs from the start of the preview (no DO backups at all), so the owner's content is protected from day one |
| Output cache | Effectively off: Basic Auth adds an `Authorization` header to every request, and the default cache policy skips such requests. Fine for a demo; caching is exercised locally and in production |


## Go-Live (switchover)

1. The preview droplet becomes production — **remove the preview features**: Basic Auth, the `X-Robots-Tag` header, `ASPNETCORE_ENVIRONMENT=Preview` → `Production`, `Site:BaseUrl` → `https://www.saronidasailing.gr`; add the canonical/old-site redirect server blocks; add the production host to the Meta app's redirect URIs. Test via a hosts-file entry on the developer's PC (`www.saronidasailing.gr` and the no-www name → droplet IP) **before** touching DNS. Certificates for the production names are issued right after DNS points to the droplet.
2. Copy **every** existing DNS record (MX, SPF, DKIM, verification records, subdomains) into Cloudflare — see `OWNER-QUESTIONS.md` 1–4.
3. A day before: lower the DNS TTL.
4. Switch: point DNS to the droplet (via Cloudflare), issue the Let's Encrypt certificate, check pages, redirects (old URLs, http, no-www), admin login, Instagram, map, backups.
5. Keep the old site running a few days as a fallback, then cancel it.
6. Submit the new `sitemap.xml` in Google Search Console / Bing Webmaster Tools; enable HSTS once everything is verified.

## Backups

Agreed design (Phase 8). There are **no DigitalOcean backups**; the copies live on the developer's PC.

| Layer | What |
|---|---|
| Nightly `pg_dump` CronJob | Writes to `/var/saronida/backup/`; the last 7 dumps are kept on the droplet |
| PC pull script (`deploy/backup.ps1`) | Run by Windows Task Scheduler (also when a run was missed). Over SSH, with a dedicated read-only backup user, it pulls the dumps, new files in `/var/saronida/data/originals/` and the Data Protection keys into the Proton Drive sync folder. Keeps 30 daily and 12 monthly copies. Warns when the newest backup is more than 3 days old |
| Restore runbook | Written, and a restore tested once |

Not backed up because they can be regenerated: the generated images under `/var/saronida/media/` (rebuilt from the originals). Worst case data loss is the edits since the last successful pull (the droplet keeps the last 7 dumps, but they are on the same disk as the database).
