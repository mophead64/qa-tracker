# QA Tracker

Lightweight collaboration between a developer and a tester for short engagements —
projects, functional/non-functional test cases, and defect tracking without the ceremony
of full project planning.

**Stack:** C# / Blazor Web App (.NET 10, Interactive Server) · PostgreSQL + EF Core ·
ASP.NET Core Identity · Tailwind CSS · Docker.

The container applies its own EF Core migrations on startup — it is never deployed via CI/CD.

## Development phases

| Phase | Scope | Status |
|------|-------|--------|
| 1 | Docker dev env + basic (local) authentication | **in progress** |
| 2 | Project creation | — |
| 3 | Test case creation | — |
| 4 | Defect management | — |
| 5 | Test case dashboard | — |
| 6 | Evidence uploads on defects | — |
| 7 | SSO / OAuth (Entra, Keycloak, …) | — |
| 8 | Assign developers to projects | — |

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

There is no self-service sign-up or account-management UI: the login page only signs
users in. The first user comes from the `QATRACKER_ADMIN_EMAIL` / `_PASSWORD` seed
(below); further users are added by an admin.

## Logging

The console logger writes one line per entry, prefixed with a UTC (Zulu) timestamp.
`RequestLoggingMiddleware` adds a single summary line per HTTP request
(`GET /Account/Login 200 47ms`); unhandled exceptions are logged at Error and rethrown.
EF Core SQL and the framework's own per-request INFO lines are filtered out in
`appsettings*.json`.

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
| `QATRACKER_HTTPS_REDIRECT` | Enable in-app HTTP→HTTPS redirection (leave off when TLS is terminated at a proxy) | `false` |

Data Protection keys (auth cookies, antiforgery tokens) are stored in the database
(`DataProtectionKeys` table) so they survive restarts and are shared across instances.
They are unencrypted at rest — wrap them with a certificate or Key Vault for production.

## Running locally

### Everything in Docker

```bash
docker compose up --build
# app on http://localhost:8080
```

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
