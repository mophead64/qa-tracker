[QA Tracker](../README.md) › [Docs](README.md) › Development

# Development

Working on QA Tracker itself: running it from source, the stylesheet, tests, database
migrations, CI and the documentation screenshots.

- [Prerequisites](#prerequisites)
- [Run it from source](#run-it-from-source)
- [Tech stack](#tech-stack)
- [Tailwind CSS](#tailwind-css)
- [Tests](#tests)
- [Database migrations](#database-migrations)
- [Continuous integration](#continuous-integration)
- [Documentation screenshots](#documentation-screenshots)

---

## Prerequisites

- .NET 10 SDK
- Node.js 20+ (Tailwind runs during `dotnet build`)
- Docker with Docker Compose
- PowerShell (`pwsh`), to install the Playwright browsers for the end-to-end tests

## Run it from source

### The app on your machine, the database in Docker

```bash
cp .env.example .env
docker compose up -d db
cd src/QaTracker.Web
dotnet run
# http://localhost:5281  (or: dotnet run --launch-profile https → https://localhost:7214)
```

The `.env` at the repository root is picked up automatically. If you're testing the
Microsoft Entra ID sign-in locally, use the HTTPS profile; see the correlation-cookie note
in [Single sign-on](sso.md#troubleshooting).

### Everything in Docker

`docker compose up --build` builds the app and runs it with PostgreSQL, Keycloak, Garage
and Jaeger. The addresses and test accounts are in
[Getting started](getting-started.md#try-the-full-stack-from-source).

## Tech stack

| | |
|---|---|
| Language / runtime | C# · .NET 10 |
| Web | Blazor Web App with **static server-side rendering**: forms post and the page re-renders, so there's no SignalR circuit and any instance can serve any request |
| Data | PostgreSQL · Entity Framework Core · Npgsql |
| Sign-in | ASP.NET Core Identity (cookies) plus an optional generic OpenID Connect handler |
| Storage | AWS SDK for .NET (S3) · Azure Storage Blobs |
| Observability | OpenTelemetry · Azure Monitor distro |
| UI | Tailwind CSS, plus small plain-JavaScript enhancements in `wwwroot/js` |
| Packaging | Docker · Docker Compose |

## Tailwind CSS

`dotnet build` regenerates `wwwroot/app.css` from `Styles/app.css`. While working on
styles:

```bash
cd src/QaTracker.Web
npm run css:watch
```

Pass `-p:SkipTailwind=true` to skip the build step. The Docker build does this, because it
builds the stylesheet in its own stage.

## Tests

```bash
dotnet test tests/QaTracker.UnitTests
```

The end-to-end tests (`tests/QaTracker.E2ETests`, Playwright with NUnit) run against a live
instance and skip themselves when it's unreachable:

```bash
# from tests/QaTracker.E2ETests, the first time only:
pwsh bin/Debug/net10.0/playwright.ps1 install --with-deps

QATRACKER_E2E_BASEURL=http://localhost:5281 \
QATRACKER_E2E_EMAIL=<a QA account> QATRACKER_E2E_PASSWORD=<its password> \
dotnet test
```

To run everything exactly as CI does, use [`run-all-tests.sh`](../run-all-tests.sh). It
builds, runs the unit tests, builds the Docker image, starts PostgreSQL, Garage and the app
as containers, and runs the end-to-end suite against them. `--keep` leaves the stack
running, `--filter` runs some of the end-to-end tests, and `--help` lists the rest.

## Database migrations

```bash
cd src/QaTracker.Web
dotnet ef migrations add <Name> --output-dir Data/Migrations
```

Migrations are applied automatically the next time the app starts, before it serves
requests (see [Deployment](deployment.md#database-migrations)).

## Continuous integration

[`.github/workflows/ci.yml`](../.github/workflows/ci.yml) runs on pull requests, pushes to
`master`, and version tags:

1. **build:** restore, build, unit tests, then build the Docker image.
2. **e2e:** start a fresh PostgreSQL, Garage and the built image, then run the Playwright
   suite against them.
3. **publish** (not for pull requests): push the tested image to
   `ghcr.io/mophead64/qa-tracker`.
4. **release:** on `master` only, create a GitHub release with notes drawn from the merged
   pull requests.

## Documentation screenshots

The screenshots in these docs come from [`tools/docs-screenshots`](../tools/docs-screenshots/README.md).
It starts a throwaway copy of the app, fills it with a fictional team's year of projects,
and captures every screenshot in dark mode with Playwright:

```bash
tools/docs-screenshots/run.sh
```

When a feature changes how a page looks, re-run it, or re-take just the affected shots
(`run.sh dashboard defect`). When you add a feature worth showing, add a shot for it.
The tool's README explains how.

---

Back to the [documentation index](README.md)
