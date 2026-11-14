[QA Tracker](../README.md) › [Docs](README.md) › Getting started

# Getting started

This gets QA Tracker running with Docker (attachments included), signs you in as the first
QA, and covers what most teams set up next: their colleagues' accounts, and where files are kept.

- [What you need](#what-you-need)
- [Run it](#run-it)
- [Sign in](#sign-in)
- [Add your team](#add-your-team)
- [Attachment storage](#attachment-storage)
- [Try the full stack from source](#try-the-full-stack-from-source)
- [Next steps](#next-steps)

---

## What you need

- **Docker** with Docker Compose.
- That's it for a trial: PostgreSQL and Garage (file storage) run alongside the app in the
  example below. In production you can point QA Tracker at any PostgreSQL database and at
  Amazon S3, Azure Blob Storage or your own S3-compatible store (see
  [Deployment](deployment.md)).

## Run it

QA Tracker is published as a container image, `ghcr.io/mophead64/qa-tracker`. Save this as
`docker-compose.yml`, change the passwords, the storage key and the Garage RPC secret
(`openssl rand -hex 32` makes one), and run `docker compose up -d`. Alongside the app it runs
PostgreSQL and **Garage**, a small S3-compatible store that keeps attachments:

```yaml
services:
  db:
    image: postgres:17-alpine
    environment:
      POSTGRES_DB: qatracker
      POSTGRES_USER: qatracker
      POSTGRES_PASSWORD: change-me          # must match QATRACKER_DB_PASSWORD below
    volumes:
      - qatracker-db:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U qatracker -d qatracker"]
      interval: 5s
      retries: 10

  # Attachments are kept in Garage, a small S3-compatible store. It creates its
  # bucket and access key on first start.
  garage:
    image: dxflrs/garage:v2.4.1
    command: ["/garage", "server", "--single-node", "--default-bucket"]
    environment:
      GARAGE_RPC_SECRET: 0f2a9c1d4e7b8a3c6d5e2f1a9b8c7d6e5f4a3b2c1d0e9f8a7b6c5d4e3f2a1b0c  # openssl rand -hex 32
      GARAGE_DEFAULT_BUCKET: qatracker-attachments
      GARAGE_DEFAULT_ACCESS_KEY: qatracker
      GARAGE_DEFAULT_SECRET_KEY: change-me-storage-key  # 16+ characters, must match QATRACKER_S3_SECRET_KEY
    configs:
      - source: garage
        target: /etc/garage.toml
    volumes:
      - qatracker-files:/data
    healthcheck:
      test: ["CMD", "/garage", "health"]
      interval: 5s
      retries: 10

  app:
    image: ghcr.io/mophead64/qa-tracker:latest
    depends_on:
      db:
        condition: service_healthy
      garage:
        condition: service_healthy
    environment:
      QATRACKER_DB_HOST: db
      QATRACKER_DB_PASSWORD: change-me
      # The first QA account, created on first start-up.
      QATRACKER_ADMIN_EMAIL: you@example.com
      QATRACKER_ADMIN_PASSWORD: Change-me-1!
      QATRACKER_STORAGE_PROVIDER: S3
      QATRACKER_S3_ENDPOINT: http://garage:3900
      QATRACKER_S3_FORCE_PATH_STYLE: "true"
      QATRACKER_S3_BUCKET: qatracker-attachments
      QATRACKER_S3_ACCESS_KEY: qatracker
      QATRACKER_S3_SECRET_KEY: change-me-storage-key
    ports:
      - "8080:8080"

# Garage's settings, inline so this stays one file (needs Docker Compose 2.23 or later).
configs:
  garage:
    content: |
      metadata_dir = "/data/meta"
      data_dir = "/data/data"
      db_engine = "lmdb"
      replication_factor = 1
      rpc_bind_addr = "[::]:3901"
      rpc_public_addr = "127.0.0.1:3901"

      [s3_api]
      s3_region = "us-east-1"  # QA Tracker's default region (QATRACKER_S3_REGION)
      api_bind_addr = "[::]:3900"

volumes:
  qatracker-db:
  qatracker-files:
```

The app creates its own database tables on first start-up, and upgrades them itself when
you move to a newer image.

**Image tags.** `latest` is the newest release. Each release is also tagged with its date,
for example `2026.11.11`; pin one of those to control when you upgrade. Releases are listed
on the project's [GitHub releases page](https://github.com/mophead64/qa-tracker/releases).

> **Update check.** QA Tracker checks GitHub for a newer release shortly after start-up
> and every few hours, and shows the result under *System settings*. Set
> `QATRACKER_UPDATE_CHECK=false` to switch it off; the app then never contacts github.com.

## Sign in

Open <http://localhost:8080> and sign in with `QATRACKER_ADMIN_EMAIL` and
`QATRACKER_ADMIN_PASSWORD`. That account is a **QA**, so it can reach **System settings**
(bottom of the side navigation), where you manage users and the app.

There is no self-service sign-up: every account is created by a QA, or by your identity
provider when you [connect single sign-on](sso.md).

## Add your team

Go to **System settings → Users → New user**, and give each person one role:

| Role | Can |
|---|---|
| **QA** | Everything: set test results, defect status, severity and assignee, verify or reject fixes, and manage users and system settings. |
| **Dev** | Comment, attach files, pick up defects (*Start fixing*, *Mark as fixed*, *Assign to me*) and, by default, create, edit and delete projects, test cases and defects — including reassigning a defect to anyone. |

The three *Developer permissions* switches under System settings decide whether developers
can manage projects, test cases and defects. QA-only actions stay QA-only either way.

Prefer company accounts? With **Microsoft Entra ID** or **Keycloak** connected, people sign
in with their existing account, their role comes from the identity provider, and their QA
Tracker account is created the first time they sign in. See [Single sign-on](sso.md).

## Attachment storage

The example above keeps files in its own Garage service, in the `qatracker-files` volume.
To keep them somewhere else, remove the `garage` service (and its `configs` block) and
swap the `QATRACKER_STORAGE_*` / `QATRACKER_S3_*` lines in the `app` service's `environment`
for one of these. Without any storage settings at all, uploads are switched off.

```yaml
      # Amazon S3
      QATRACKER_STORAGE_PROVIDER: S3
      QATRACKER_S3_BUCKET: my-qatracker-files
      QATRACKER_S3_REGION: eu-west-2
      QATRACKER_S3_ACCESS_KEY: <access key>
      QATRACKER_S3_SECRET_KEY: <secret key>

      # …or another S3-compatible store (Garage, MinIO, …): the above plus its address.
      # QATRACKER_S3_REGION must match the store's own region setting.
      QATRACKER_S3_ENDPOINT: https://s3.example.internal
      QATRACKER_S3_FORCE_PATH_STYLE: "true"

      # …or Azure Blob Storage
      QATRACKER_STORAGE_PROVIDER: Azure
      QATRACKER_AZURE_STORAGE_CONNECTION_STRING: <connection string>
      QATRACKER_AZURE_CONTAINER: qatracker-attachments
```

Files are always downloaded through the app, so the bucket or container stays private.
`QATRACKER_MAX_UPLOAD_MB` (default 20) sets the largest file the server accepts; QAs can
lower the limit under *System settings → Uploads*. All the storage settings are in
[Configuration](configuration.md#attachment-storage).

## Try the full stack from source

The repository's own `docker-compose.yml` builds the app from source and runs everything
around it: PostgreSQL, **Keycloak** (single sign-on with two ready-made users), **Garage**
(S3-compatible storage) and an OpenTelemetry collector with **Jaeger** for traces.

```bash
git clone https://github.com/mophead64/qa-tracker.git
cd qa-tracker
cp .env.example .env
docker compose up --build
```

| | |
|---|---|
| QA Tracker | <http://localhost:8080> — *Sign in with Keycloak* as `qa@example.com`, `dev@example.com` or `viewer@example.com`, password `Passw0rd!` |
| Keycloak admin | <http://localhost:8081> (`admin` / `admin`) |
| Garage web console | <http://localhost:3909> — buckets and an object browser; the S3 API is on port 9000 |
| Jaeger (traces) | <http://localhost:16686> |

The first start takes a minute while Keycloak imports its realm. To have a local admin
account too, set `QATRACKER_ADMIN_EMAIL` and `QATRACKER_ADMIN_PASSWORD` in `.env` before
starting. For a stack without SSO, set `QATRACKER_AUTH_PROVIDER=` (empty).

## Next steps

- [User guide](user-guide.md): run your first project end to end.
- [Single sign-on](sso.md): connect Microsoft Entra ID or Keycloak.
- [Configuration](configuration.md): every setting.
- [Deployment](deployment.md): run it in production.

---

Next: [User guide](user-guide.md) · Back to the [documentation index](README.md)
