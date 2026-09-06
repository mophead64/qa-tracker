# <img src="docs/qa-logo.png" alt="QA" height="28" align="top"> Tracker

Lightweight collaboration between a developer and a tester for short engagements —
projects, functional/non-functional test cases, and defect tracking without the ceremony
of full project planning.

**Stack:** C# / Blazor Web App (.NET 10, static server-side rendering) · PostgreSQL + EF Core ·
ASP.NET Core Identity · Tailwind CSS · Docker. No SignalR circuit — the app is stateless and
scales horizontally without session affinity.

The container applies its own EF Core migrations on startup — it is never deployed via CI/CD.

## Development phases

| Phase | Scope | Status |
|------|-------|--------|
| 1 | Docker dev env + basic (local) authentication | done |
| 2 | Project creation | done |
| 3 | Test case creation | done |
| 4 | Defect management | done |
| 5 | Test case / project dashboard | done |
| 6 | Attachment uploads (projects, test cases, defects) | done |
| 7 | Assign team members to projects | done |
| 8 | System settings (user CRUD, stats, status) | done |
| 9 | Self-service user settings | done |
| 10 | Defect notifications | done |
| 11 | Telemetry (OpenTelemetry / Azure Monitor) | done |
| 12 | SSO / OIDC (Entra, Keycloak) | done |
| 13 | Horizontal-scaling plumbing (advisory-lock migrations, forwarded headers, health checks, Npgsql retry) | done |
| 14 | Static SSR conversion — removed the Blazor Server circuit; stateless, no session affinity | done |

## Authentication & shell

QA Tracker is an internal tool: every page requires a signed-in user, and anonymous
requests are redirected straight to the login page (pages under `Components/Pages`
inherit `[Authorize]` via a folder `_Imports.razor`; opt out with `[AllowAnonymous]`).
Pre-login pages use a minimal `AuthLayout`; the authenticated app uses `MainLayout`
(left `SideNav` + `TopBar`). The top-right menu shows the user's name with a dropdown
to **Settings** and **Logout**.

Settings → Appearance lets each user pick Light / Dark / System; the choice is stored on
`AspNetUsers.Theme` and surfaced as a claim so `App.razor` can set the colour scheme
before first paint.

There is no self-service sign-up: the login page only signs users in. The first user
comes from the `QATRACKER_ADMIN_EMAIL` / `_PASSWORD` seed (below); further local users are
added by a QA in **System settings → Users**. Local-password accounts can change their own
email and password on the Settings page.

### SSO / OIDC

Local accounts always work. Setting `QATRACKER_AUTH_PROVIDER` adds **one** external OpenID
Connect provider alongside them (never two at once) — a "Sign in with …" button then
appears on the login page. One generic OIDC handler serves both providers:

| `QATRACKER_AUTH_PROVIDER` | Authority (`QATRACKER_OIDC_AUTHORITY`) |
|---|---|
| `Keycloak` | `https://<host>/realms/<realm>` |
| `Entra` (aliases `EntraId`, `AzureAd`, `AAD`) | `https://login.microsoftonline.com/<tenant-id>/v2.0` |

Also required: `QATRACKER_OIDC_CLIENT_ID`, `QATRACKER_OIDC_CLIENT_SECRET`. Register the
redirect URI `<app-base-url>/signin-oidc` with the provider. Full var list — scopes,
role-claim mapping, HTTPS-metadata toggle — is in `.env.example`. Logout is local only
(the provider's own SSO session is left intact).

**Roles** come from the token: the values of the `QATRACKER_OIDC_ROLE_CLAIM` claim
(default `roles`) are mapped to QA / Dev (`QATRACKER_OIDC_ROLE_QA_VALUE` /
`_DEV_VALUE`, default `QA` / `Dev`) and synced on every sign-in — the provider is the
source of truth. Entra emits a `roles` claim for app roles out of the box; Keycloak needs
a role mapper (the shipped dev realm has one). A token with no recognised role value still
signs the user in, just with no role.

**Provider-managed accounts** are created on first sign-in (or linked by email to an
existing local account). Their email, password and roles are read-only in-app — changed
only at the identity provider. A user's record shows their provider on the Settings page
and in System settings → Users (an `SSO` badge).

**Keycloak for local dev** is wired into `docker-compose` (admin console
<http://localhost:8081>, `admin` / `admin`). It imports `deploy/keycloak/qatracker-realm.json`
on startup: realm `qatracker`, client `qatracker-web`, roles `QA`/`Dev`, and two users
`qa@example.com` / `dev@example.com` (password `Passw0rd!`). The browser reaches Keycloak
at `http://localhost:8081` (the token issuer), so `QATRACKER_OIDC_AUTHORITY` points there;
the `web` container can't resolve that name to Keycloak, so
`QATRACKER_OIDC_METADATA_ADDRESS` overrides just the back-channel discovery fetch to
`http://keycloak:8080/...`. When you run the app on the host (`dotnet run`), drop the
metadata override — `localhost:8081` works for both.

## Logging

The console logger writes one line per entry, prefixed with a UTC (Zulu) timestamp.
`RequestLoggingMiddleware` adds a single summary line per HTTP request
(`GET /Account/Login 200 47ms`); unhandled exceptions are logged at Error and rethrown.
EF Core SQL and the framework's own per-request INFO lines are filtered out in
`appsettings*.json`.

## Telemetry

Set `QATRACKER_TELEMETRY_PROVIDER` to export OpenTelemetry traces (ASP.NET Core requests,
outbound HTTP, PostgreSQL), plus metrics and logs:

| Provider | Extra vars |
|----------|-----------|
| `Otlp` — any OTLP endpoint (SigNoz, Grafana Alloy/Tempo, an OpenTelemetry Collector) | `QATRACKER_OTLP_ENDPOINT` (e.g. `http://localhost:4317`), `QATRACKER_OTLP_PROTOCOL` (`grpc` default, or `http/protobuf`) |
| `AzureMonitor` — Azure Monitor / Application Insights | `QATRACKER_AZURE_MONITOR_CONNECTION_STRING` |

Leave `QATRACKER_TELEMETRY_PROVIDER` unset to disable telemetry entirely.
`QATRACKER_TELEMETRY_SERVICE_NAME` (default `qa-tracker`) sets the `service.name` resource
attribute; `service.version`, `service.instance.id` and `deployment.environment` are set
too. Requests for static assets (CSS, JS, images, the Blazor framework files) are filtered
out of traces (`Telemetry/StaticAssetFilter.cs`).

**Exceptions** are recorded as span events (`exception.type` / `exception.message` /
`exception.stacktrace`) — they show up under Exceptions in SigNoz / App Insights. `ILogger`
records are also exported, at `Warning` and above by default
(`QATRACKER_TELEMETRY_LOG_LEVEL` overrides) so error logs flow but per-request info lines
don't.

Startup logs one line — `Telemetry export: …` — with the resolved target, and the
OpenTelemetry SDK's own export failures (bad endpoint, refused connection, TLS) are
logged instead of swallowed. What's configured is shown on the **System settings** page.

> Rendering note: the UI is static server-side rendering. Every user action is a normal
> HTTP request — a form post that redirects, or an enhanced-navigation fetch — so each one
> shows up as its own HTTP server span.

## Prerequisites

- .NET 10 SDK
- Node.js 20+ (Tailwind builds during `dotnet build`)
- Docker + Docker Compose

## Configuration

All secrets/config come from environment variables (a `.env` file locally, Key Vault
references on Azure, etc.). Copy the template:

```bash
cp .env.example .env
```

| Variable | Purpose | Default |
|----------|---------|---------|
| `QATRACKER_DB_HOST` / `_PORT` / `_NAME` / `_USER` / `_PASSWORD` | PostgreSQL connection parts | `localhost` / `5432` / `qatracker` / `qatracker` / `qatracker` |
| `ConnectionStrings__DefaultConnection` | Full connection string (overrides the parts above) | — |
| `QATRACKER_ADMIN_EMAIL` / `QATRACKER_ADMIN_PASSWORD` | If both set, a confirmed **QA** user is seeded on first startup | — |
| `QATRACKER_AUTH_PROVIDER` | Add an SSO provider: `Keycloak`, `Entra`, or unset (local accounts only) — see **SSO / OIDC** above and `.env.example` for the `QATRACKER_OIDC_*` vars | — |
| `QATRACKER_HTTPS_REDIRECT` | Enable in-app HTTP→HTTPS redirection (leave off when TLS is terminated at a proxy) | `false` |
| `QATRACKER_STORAGE_PROVIDER` | Attachment storage backend: `S3`, `Azure`, or unset (uploads disabled) — see `.env.example` for the per-provider vars | — |
| `QATRACKER_TELEMETRY_PROVIDER` | Export traces/metrics/logs to `Otlp` or `AzureMonitor`, or unset (off) — see **Telemetry** below | — |

Data Protection keys (auth cookies, antiforgery tokens) are stored in the database
(`DataProtectionKeys` table) so they survive restarts and are shared across instances.
They are unencrypted at rest — wrap them with a certificate or Key Vault for production.

## Running locally

### Everything in Docker

```bash
docker compose up --build
# app on http://localhost:8080, Keycloak on http://localhost:8081
```

The compose stack includes Keycloak (SSO) and MinIO (S3 storage); first boot takes a
minute while Keycloak imports its realm. To run the app local-only, set
`QATRACKER_AUTH_PROVIDER=` (empty) in `.env`.

### App on the host, database in Docker

```bash
docker compose up -d db
cd src/QaTracker.Web
dotnet run
# app on http://localhost:5281
```

The `.env` at the repo root is picked up automatically.

## Tailwind CSS

`dotnet build` runs `npm run css:build` (via an MSBuild target) to regenerate
`wwwroot/app.css` from `Styles/app.css`. While working on styles:

```bash
cd src/QaTracker.Web
npm run css:watch
```

Pass `-p:SkipTailwind=true` to skip the build step (used by the Docker build, which
builds the stylesheet in a dedicated stage).

## Tests

```bash
dotnet test tests/QaTracker.UnitTests          # unit tests
```

End-to-end tests (`tests/QaTracker.E2ETests`, Playwright + NUnit) run against a running
instance and self-skip when it is unreachable:

```bash
# from tests/QaTracker.E2ETests, first time only:
pwsh bin/Debug/net10.0/playwright.ps1 install --with-deps
QATRACKER_E2E_BASEURL=http://localhost:5281 dotnet test
```

## Database migrations

```bash
cd src/QaTracker.Web
dotnet ef migrations add <Name> --output-dir Data/Migrations
```

Migrations are applied automatically on app startup (`app.InitializeDatabaseAsync()`),
before the host starts serving requests.

## CI

`.github/workflows/ci.yml` restores, builds, runs unit tests, and builds the Docker
image — pushing it to `ghcr.io/<owner>/qa-tracker` on pushes to `master`/`main` and
version tags.
