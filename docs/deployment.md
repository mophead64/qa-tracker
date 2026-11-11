[QA Tracker](../README.md) › [Docs](README.md) › Deployment

# Deployment

QA Tracker is one container image plus a PostgreSQL database. Object storage, single
sign-on and telemetry are optional extras. This page covers running it in production.

- [The container image](#the-container-image)
- [Production checklist](#production-checklist)
- [Database migrations](#database-migrations)
- [Scaling out](#scaling-out)
- [Health checks](#health-checks)
- [Behind a reverse proxy](#behind-a-reverse-proxy)
- [Upgrading](#upgrading)
- [Monitoring and logs](#monitoring-and-logs)
- [Building the image yourself](#building-the-image-yourself)

---

## The container image

```bash
docker pull ghcr.io/mophead64/qa-tracker:latest
```

| Tag | Meaning |
|---|---|
| `latest` | The newest release |
| `2026.11.11` (a date) | A specific release; pin one to control when you upgrade |
| `sha-<commit>` | The image built from that commit |

The image listens on port **8080** over plain HTTP and runs as a non-root user. Put TLS in
front of it with your platform's ingress or a reverse proxy.

## Production checklist

- **PostgreSQL:** any reachable PostgreSQL works. Set `QATRACKER_DB_*` or a full
  `ConnectionStrings__DefaultConnection` ([Configuration](configuration.md#database)).
- **Storage for attachments:** AWS S3, an S3-compatible store, or Azure Blob Storage
  ([Configuration](configuration.md#attachment-storage)). Without it, uploads are off.
- **Sign-in:** create the first QA with `QATRACKER_ADMIN_EMAIL` / `QATRACKER_ADMIN_PASSWORD`,
  or connect [single sign-on](sso.md) and give at least one person the QA role.
- **TLS:** terminate it at a proxy and set `QATRACKER_FORWARDED_HEADERS=true`
  ([see below](#behind-a-reverse-proxy)).
- **Secrets:** pass the database password, storage keys and OIDC client secret as secrets
  (Key Vault references, Docker or Kubernetes secrets), not in an image or a committed file.
- **Data Protection keys** (which protect sign-in cookies) are stored in the database so
  every instance shares them and they survive restarts. They're stored unencrypted. For
  stricter environments, wrap them with a certificate or Key Vault.

## Database migrations

By default each instance applies any pending migrations when it starts, before it serves
requests. A PostgreSQL advisory lock makes this safe when several instances start at once,
for example during a rolling deploy or a scale-out.

To run migrations as their own step instead, run the same image once with
`--migrate-only` (it migrates, seeds, and exits), and start the app instances with
`QATRACKER_MIGRATE_ON_STARTUP=false`. Those instances then check the schema is up to date
and refuse to start if it isn't.

```bash
docker run --rm --env-file qatracker.env ghcr.io/mophead64/qa-tracker:2026.11.11 --migrate-only
```

## Scaling out

Run as many instances as you like behind a load balancer. No sticky sessions, Redis or
SignalR backplane are needed:

- The UI is server-rendered and holds no per-user state in memory.
- Sign-in cookies are protected with keys every instance reads from the database.
- Notifications are stored in the database and fetched by browser polling, so a
  notification raised on one instance shows up for someone connected to another.

## Health checks

| Endpoint | Checks | Use it for |
|---|---|---|
| `/health/live` | The process is up (no dependencies) | Liveness: a database outage shouldn't restart every instance |
| `/health/ready` | The database is reachable | Readiness: take an instance out of rotation |

Both are anonymous and left out of request logs and telemetry. The image's own Docker
`HEALTHCHECK` uses `/health/live`.

## Behind a reverse proxy

When TLS ends at a proxy or platform ingress (Azure App Service, Container Apps, nginx,
Traefik…), set `QATRACKER_FORWARDED_HEADERS=true`. The app then builds `https://` URLs for
redirects and the single sign-on callback (`/signin-oidc`), and logs the real client IP.
If you run your own proxy, restrict which proxies are trusted with
`QATRACKER_KNOWN_PROXIES` or `QATRACKER_KNOWN_NETWORKS`. Leave `QATRACKER_HTTPS_REDIRECT`
off unless the app itself faces the internet.

## Upgrading

Pull the newer tag and restart. Database changes are applied automatically on start-up (or
by your `--migrate-only` step). Each [GitHub release](https://github.com/mophead64/qa-tracker/releases)
lists what changed and the image tag to pull.

*System settings* shows the version you're running, linked to its release notes, and
whether a newer release exists. That check runs shortly after start-up and every few
hours; set `QATRACKER_UPDATE_CHECK=false` to switch it off entirely.

## Monitoring and logs

- **Logs:** one line per request on the console, such as `GET /projects 200 47ms`, with
  UTC timestamps. Errors include the request ID shown on the error page.
- **Telemetry:** set `QATRACKER_TELEMETRY_PROVIDER` to `Otlp` (SigNoz, Grafana, an
  OpenTelemetry Collector…) or `AzureMonitor` to export traces (requests, outbound HTTP,
  PostgreSQL), metrics and logs. Exceptions are attached to their traces, and static
  files are left out. See [Configuration](configuration.md#telemetry).

## Building the image yourself

The repository's [`Dockerfile`](../Dockerfile) builds the stylesheet with Tailwind,
publishes the app, and copies it into the ASP.NET runtime image:

```bash
docker build -t qatracker .
```

Pass `APP_VERSION`, `GIT_BRANCH`, `GIT_COMMIT`, `GIT_COMMIT_SHORT` and `BUILD_DATE` as build
arguments to stamp the version shown in *System settings*; CI does this for published
images. See [Development](development.md) for working on the code.

---

Next: [Development](development.md) · Back to the [documentation index](README.md)
