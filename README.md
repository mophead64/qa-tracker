# <img src="docs/qa-logo.png" alt="QA" height="45" align="top"> Tracker

QA Tracker is a lightweight tool for the kind of short engagement where a developer builds
for a week and a tester checks it for a day or two — too small for a full ticketing
system, but still needing a shared record of what was tested and what broke.

It gives a **QA** and one or more **developers** a single place to track projects, write
functional and non-functional test cases, raise and work through defects, and see what
needs doing — with the whole defect fix/verify loop, file evidence, comments and
notifications built in.

- [Features](#features)
- [Tech stack](#tech-stack)
- [Quick start](#quick-start)
- [Configuration](#configuration)
- [Running locally](#running-locally)
- [Deployment](#deployment)
- [Development](#development)

---

## Features

### Projects

- Create a project with free-text notes and any number of **custom link buttons**
  (labelled links to a repo, a staging URL, a spec — `http`, `https` or `mailto`).
- Status is **Inactive** → **Active** → **Completed**. A project flips to *Active*
  automatically the moment its first test scope or defect is created; *Completed* is set
  by hand.
- A **project switcher** in the top bar scopes the whole side navigation to one project at
  a time. The all-projects page groups by status and surfaces *"your projects"* (the ones
  you're on the team for).

### Test cases

- **Project → test scope → test case.** A scope is a named area of testing
  (*"Authentication"*), tagged **Functional** or **Non-functional**; test cases live under
  it.
- Each case has a scenario, steps, and a result — **Not run / Passed / Failed** — set from
  the case view with one click.
- Per-case **comment thread**.
- **Export a project's test cases to CSV** for documentation.

### Defects

- Raised standalone or straight from a failing test case, and linked to **zero or more**
  test cases (link/unlink any time, or spin up a new test case from a defect with the
  repro steps pre-filled).
- Summary, repro steps, expected vs actual results, **severity** (Low / Medium / High /
  Critical) and **status** (Not fixed / Fixing / To check / Fixed / Not a defect).
- **Assignee** picker limited to the project's team, plus *assign to me* / *unassign*.
- A guided **fix workflow**: a developer hits *Start fixing* then *Mark as fixed*, which
  moves the defect to *To check* and hands it to a project QA; the QA then *rejects* it
  back to the developer or *verifies* it as fixed (recording who fixed and who tested it).
- Per-defect **comment thread**.
- The defect list is grouped into *to verify* / *to fix* / *unassigned* / *other open* /
  *closed*, ordered most-severe-first.

### Project dashboard

- Test pass/fail tally and an **open-defect count** (defects agreed to be "not a defect"
  don't inflate it).
- An **"actions needed"** panel that only appears when there's something to do: defects
  assigned to you to verify or fix, test cases failed with no defect raised, test cases
  whose linked defects are all closed and are ready to re-test, and passed test cases that
  still have an open defect.

### Attachments

- Upload files as **project resources**, **test-case resources** or **defect evidence** —
  staged in a modal, multiple at a time, with a per-file size limit.
- Stored in **S3-compatible object storage** (AWS S3, MinIO, Garage) **or Azure Blob
  Storage** — the app proxies every download, so the bucket/container stays private and is
  never linked directly.

### Team

- Assign **Dev** and **QA** team members to a project from the dashboard. Membership drives
  the defect assignee list, the *"your projects"* section, and who gets notified.

### Notifications

- A bell in the top bar with a live badge and toasts for defect events relevant to you —
  new defects (for a project's developers), assignment and *ready to check* (for QA),
  comments, and a defect bounced back to *not fixed*. Dismiss individually or clear all.
- Delivered by lightweight browser polling — no WebSocket/SignalR service, and it works
  across multiple app instances because every instance reads the same database.

### Authentication

- **Auth-first**: every page requires a signed-in user; anonymous requests go straight to
  the login page. There is no self-service sign-up.
- **Local accounts** always work. A first **QA** admin is seeded from environment
  variables; further users are created by a QA in *System settings*.
- Optionally add **one** external **OpenID Connect** provider (Microsoft Entra ID or
  Keycloak) alongside local login — roles come from the token, accounts are provisioned on
  first sign-in. See **[SETUP.md](SETUP.md)** for the full walkthrough.

### Self-service settings

- Per-user **appearance**: Light / Dark / System, stored server-side and applied before
  first paint.
- Local-account users can **change their own email and password**; everyone can see their
  roles and last sign-in.

### System settings (QA only)

- **User management** — create, edit, delete users; one role (QA or Dev) each.
- **System stats** — project / test-case / defect / file counts and storage used.
- **System status** — which storage backend, auth mechanism and telemetry exporter are
  active, and the running version.
- **Developer permissions** — three toggles (default on) that let developers, not just QA,
  create/edit/delete projects, test cases and defects. Purely-QA actions (setting a
  result, a defect status, an assignee) stay QA-only.

### Telemetry

- Optional **OpenTelemetry** export of traces (ASP.NET Core requests, outbound HTTP,
  PostgreSQL), metrics and logs to **any OTLP endpoint** (SigNoz, Grafana, a Collector) or
  to **Azure Monitor / Application Insights**. Exceptions ride along as span events. Static
  asset requests are kept out of traces.

### Logging

- One line per HTTP request on the console — `GET /Account/Login 200 47ms` — with a full
  UTC (Zulu) timestamp. EF Core SQL and framework per-request noise are filtered out;
  errors are surfaced.

---

## Tech stack

| | |
|---|---|
| Language / runtime | C# · .NET 10 |
| Web | Blazor Web App — **static server-side rendering** (no SignalR circuit; stateless, scales horizontally with no session affinity) |
| Data | PostgreSQL · Entity Framework Core · Npgsql |
| Auth | ASP.NET Core Identity (cookie) + optional OpenID Connect |
| Storage | AWS SDK for .NET (S3) · Azure Storage Blobs |
| Observability | OpenTelemetry · Azure Monitor distro |
| UI | Tailwind CSS (dark mode, green-accented theme) |
| Packaging | Docker · Docker Compose |

The container **applies its own EF Core migrations on startup** — QA Tracker is designed
to be deployed as an image into your own environment, not through a CI/CD pipeline with a
separate migration step (though [that mode is supported too](#deployment)).

---

## Quick start

Everything in Docker — app, PostgreSQL, Keycloak (SSO), MinIO (storage), and an
OpenTelemetry collector with Jaeger:

```bash
cp .env.example .env
docker compose up --build
```

- App: <http://localhost:8080>
- Keycloak admin: <http://localhost:8081> (`admin` / `admin`) — dev users
  `qa@example.com` / `dev@example.com`, password `Passw0rd!`
- MinIO console: <http://localhost:9001> (`minioadmin` / `minioadmin`)
- Jaeger (traces): <http://localhost:16686>

First boot takes a minute while Keycloak imports its realm. To set a local admin login,
put `QATRACKER_ADMIN_EMAIL` / `QATRACKER_ADMIN_PASSWORD` in `.env` before starting. To run
without SSO, set `QATRACKER_AUTH_PROVIDER=` (empty).

---

## Configuration

All configuration comes from environment variables — a `.env` file locally, real
environment variables or Key Vault references when hosted.
**[`.env.example`](.env.example)** is the annotated reference; the essentials:

| Variable | Purpose | Default |
|---|---|---|
| `QATRACKER_DB_HOST` / `_PORT` / `_NAME` / `_USER` / `_PASSWORD` | PostgreSQL connection parts | `localhost` / `5432` / `qatracker` ×3 |
| `ConnectionStrings__DefaultConnection` | Full Npgsql connection string (overrides the parts above) | — |
| `QATRACKER_ADMIN_EMAIL` / `QATRACKER_ADMIN_PASSWORD` | If both set, a confirmed **QA** user is seeded on first startup | — |
| `QATRACKER_AUTH_PROVIDER` | Add SSO: `Entra` or `Keycloak`, or unset for local accounts only — see **[SETUP.md](SETUP.md)** | — |
| `QATRACKER_STORAGE_PROVIDER` | Attachment storage: `S3`, `Azure`, or unset (uploads disabled) | — |
| `QATRACKER_MAX_UPLOAD_MB` | Per-file upload ceiling (also the Kestrel body limit) | `20` |
| `QATRACKER_TELEMETRY_PROVIDER` | `Otlp` or `AzureMonitor`, or unset (off) | — |
| `QATRACKER_MIGRATE_ON_STARTUP` | Apply migrations + seed on startup | `true` |
| `QATRACKER_FORWARDED_HEADERS` | Trust `X-Forwarded-*` from a TLS-terminating proxy | `false` |
| `QATRACKER_HTTPS_REDIRECT` | In-app HTTP→HTTPS redirection (off when TLS is at a proxy) | `false` |
| `ASPNETCORE_ENVIRONMENT` | `Development` enables the dev exception page + EF endpoint; anything else runs the production pipeline | `Production` |

When `QATRACKER_STORAGE_PROVIDER` or `QATRACKER_TELEMETRY_PROVIDER` is set, that provider's
own variables (bucket/keys, connection string, endpoint) become required and the app fails
fast on startup if they're missing. All provider-specific variables are documented in
[`.env.example`](.env.example).

---

## Running locally

### App on the host, database in Docker

```bash
docker compose up -d db
cd src/QaTracker.Web
dotnet run
# http://localhost:5281  (or: dotnet run --launch-profile https  → https://localhost:7214)
```

The `.env` at the repo root is picked up automatically. If you're testing the Entra
sign-in flow locally, use the **HTTPS** profile — see the correlation-cookie note in
[SETUP.md](SETUP.md#troubleshooting).

### Prerequisites

- .NET 10 SDK
- Node.js 20+ (Tailwind runs during `dotnet build`)
- Docker + Docker Compose

---

## Deployment

QA Tracker ships as a container image built from the repository [`Dockerfile`](Dockerfile)
(three stages: Tailwind build → `dotnet publish` → ASP.NET runtime). Run it against a
PostgreSQL database and, optionally, object storage and an OIDC provider.

- **Migrations** run automatically on startup under a PostgreSQL advisory lock, so
  concurrent starts (a rolling deploy, autoscale) are safe. For a dedicated migration
  step, run the same image with `--migrate-only` first and set
  `QATRACKER_MIGRATE_ON_STARTUP=false` on the app instances — they then refuse to start
  while any migration is pending.
- **Horizontal scaling** needs no extra infrastructure: the app holds no per-user server
  state, Data Protection keys and notifications live in the database, and sessions are
  stateless cookies. No Redis, no sticky sessions.
- **Health endpoints**: `/health/live` (process only — a failing database must not
  restart-loop the fleet) and `/health/ready` (checks the database).
- **Behind a proxy**: terminate TLS at the proxy and set `QATRACKER_FORWARDED_HEADERS=true`
  so redirect and OIDC callback URLs are built as `https://`.
- **Data Protection keys** are stored unencrypted in the `DataProtectionKeys` table — wrap
  them with a certificate or Key Vault for production.

---

## Development

### Tailwind CSS

`dotnet build` regenerates `wwwroot/app.css` from `Styles/app.css` via an MSBuild target.
While working on styles:

```bash
cd src/QaTracker.Web
npm run css:watch
```

Pass `-p:SkipTailwind=true` to skip the build step (the Docker build does this — it builds
the stylesheet in a dedicated stage).

### Tests

```bash
dotnet test tests/QaTracker.UnitTests
```

End-to-end tests (`tests/QaTracker.E2ETests`, Playwright + NUnit) run against a running
instance and self-skip when it's unreachable:

```bash
# from tests/QaTracker.E2ETests, first time only:
pwsh bin/Debug/net10.0/playwright.ps1 install --with-deps

QATRACKER_E2E_BASEURL=http://localhost:5281 dotnet test
```

Some E2E tests additionally need a QA account's credentials in `QATRACKER_E2E_EMAIL` /
`QATRACKER_E2E_PASSWORD` and self-skip without them.

### Database migrations

```bash
cd src/QaTracker.Web
dotnet ef migrations add <Name> --output-dir Data/Migrations
```

Applied automatically on the next app startup (`app.InitializeDatabaseAsync()`), before
the host serves requests.
