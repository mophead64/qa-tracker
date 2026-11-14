[QA Tracker](../README.md) › [Docs](README.md) › Configuration

# Configuration

QA Tracker is configured entirely with environment variables: a `.env` file when you run it
from source, and real environment variables (or Key Vault references, secrets and so on)
when it's hosted. The annotated [`.env.example`](../.env.example) is the complete reference,
with every accepted value and more detail than this page.

- [Database](#database)
- [First admin account](#first-admin-account)
- [Sign-in](#sign-in)
- [Attachment storage](#attachment-storage)
- [Behind a proxy and scaling out](#behind-a-proxy-and-scaling-out)
- [Telemetry](#telemetry)
- [Notifications](#notifications)
- [Version and update check](#version-and-update-check)
- [Runtime and errors](#runtime-and-errors)

Booleans are `true` or `false`; enum values are case-insensitive.

---

## Database

PostgreSQL is the only required dependency.

| Variable | Purpose | Default |
|---|---|---|
| `QATRACKER_DB_HOST` | Database host | `localhost` |
| `QATRACKER_DB_PORT` | Database port | `5432` |
| `QATRACKER_DB_NAME` / `QATRACKER_DB_USER` / `QATRACKER_DB_PASSWORD` | Database, user and password | `qatracker` each |
| `QATRACKER_DB_MAX_POOL_SIZE` / `QATRACKER_DB_MIN_POOL_SIZE` | Connection pool bounds per instance | `20` / `1` |
| `ConnectionStrings__DefaultConnection` | A full Npgsql connection string, used instead of the variables above | — |
| `QATRACKER_MIGRATE_ON_STARTUP` | Apply database migrations (and seed roles / the first admin) on start-up. Set `false` when a separate `--migrate-only` step owns the schema; see [Deployment](deployment.md#database-migrations). | `true` |

## First admin account

| Variable | Purpose |
|---|---|
| `QATRACKER_ADMIN_EMAIL` / `QATRACKER_ADMIN_PASSWORD` | When both are set, a **QA** account is created on first start-up (if it doesn't already exist). Skipped when local sign-in is turned off. |

The password must contain upper and lower case letters, a digit and a symbol, and be at
least 6 characters long.

## Sign-in

Local accounts always work unless you turn them off. Adding a provider puts a
*Sign in with …* button on the sign-in page. The [single sign-on guide](sso.md) covers each
provider step by step.

| Variable | Purpose | Default |
|---|---|---|
| `QATRACKER_AUTH_PROVIDER` | `Entra` or `Keycloak` to add single sign-on; unset for local accounts only | — |
| `QATRACKER_LOCAL_AUTH_ENABLED` | `false` makes single sign-on the only way in (ignored without a provider) | `true` |
| `QATRACKER_OIDC_CLIENT_ID` / `QATRACKER_OIDC_CLIENT_SECRET` | The app registration / client credentials | — |
| `QATRACKER_OIDC_TENANT_ID` | Entra only: the directory (tenant) ID, used to build the authority | — |
| `QATRACKER_OIDC_AUTHORITY` | The provider's issuer URL (optional for Entra when the tenant ID is set) | — |
| `QATRACKER_OIDC_INSTANCE` | Entra national clouds, e.g. `https://login.microsoftonline.us` | `https://login.microsoftonline.com` |
| `QATRACKER_OIDC_METADATA_ADDRESS` | Discovery URL for the app's own back-channel calls, when it differs from the browser-facing issuer (containers) | — |
| `QATRACKER_OIDC_SCOPES` | Space- or comma-separated scopes | `openid profile email` |
| `QATRACKER_OIDC_ROLE_CLAIM` | Token claim carrying the role | `roles` |
| `QATRACKER_OIDC_ROLE_QA_VALUE` / `QATRACKER_OIDC_ROLE_DEV_VALUE` / `QATRACKER_OIDC_ROLE_VIEWER_VALUE` | Claim values meaning QA, Dev and Viewer | `QA` / `Dev` / `Viewer` |
| `QATRACKER_OIDC_REQUIRE_HTTPS_METADATA` | Set `false` only for a plain-HTTP development provider | `true` |

## Attachment storage

Uploads are switched off until a provider is set. When it is, that provider's variables are
required and the app won't start without them.

| Variable | Purpose | Default |
|---|---|---|
| `QATRACKER_STORAGE_PROVIDER` | `S3` (AWS S3 or an S3-compatible store) or `Azure`; unset disables uploads | — |
| `QATRACKER_S3_BUCKET` | Bucket name (S3) | — |
| `QATRACKER_S3_REGION` | Region; must match the store's own | `us-east-1` |
| `QATRACKER_S3_ACCESS_KEY` / `QATRACKER_S3_SECRET_KEY` | Credentials (S3) | — |
| `QATRACKER_S3_ENDPOINT` | Endpoint for MinIO, Garage and other S3-compatible stores; leave unset for AWS | — |
| `QATRACKER_S3_FORCE_PATH_STYLE` | Path-style addressing; MinIO and Garage need it | `true` with an endpoint, else `false` |
| `QATRACKER_AZURE_STORAGE_CONNECTION_STRING` | Connection string (Azure) | — |
| `QATRACKER_AZURE_CONTAINER` | Blob container (Azure) | `qatracker-attachments` |
| `QATRACKER_MAX_UPLOAD_MB` | The largest file the server accepts, in MB. QAs can set a lower limit in *System settings → Uploads*. | `20` |

## Behind a proxy and scaling out

| Variable | Purpose | Default |
|---|---|---|
| `QATRACKER_FORWARDED_HEADERS` | Trust `X-Forwarded-*` headers from a TLS-terminating proxy, so redirects and sign-in callbacks use `https://` | `false` |
| `QATRACKER_KNOWN_PROXIES` / `QATRACKER_KNOWN_NETWORKS` | Only trust these proxies (comma-separated IPs / CIDR ranges). Only needed in front of your own proxy; platform ingress needs neither. | any proxy |
| `QATRACKER_FORWARDED_HEADERS_LIMIT` | How many proxy hops to unwrap | no limit |
| `QATRACKER_HTTPS_REDIRECT` | Redirect HTTP to HTTPS in the app. Leave off when TLS ends at a proxy. | `false` |

## Telemetry

| Variable | Purpose | Default |
|---|---|---|
| `QATRACKER_TELEMETRY_PROVIDER` | `Otlp` or `AzureMonitor`; unset turns telemetry off | — |
| `QATRACKER_OTLP_ENDPOINT` / `QATRACKER_OTLP_PROTOCOL` | Collector endpoint, and `grpc` or `http/protobuf` | — / `grpc` |
| `QATRACKER_AZURE_MONITOR_CONNECTION_STRING` | Application Insights connection string | — |
| `QATRACKER_TELEMETRY_SERVICE_NAME` | The `service.name` reported | `qa-tracker` |
| `QATRACKER_TELEMETRY_LOG_LEVEL` | Lowest log level exported | `Warning` |

## Notifications and live pages

Both are browser polling, with no WebSockets or extra services, and both poll endpoints are
kept out of the request log and traces.

| Variable | Purpose | Default |
|---|---|---|
| `QATRACKER_NOTIFICATIONS_POLL_SECONDS` | How often an open page checks for new notifications (5–3600) | `45` |
| `QATRACKER_LIVE_POLL_SECONDS` | How often an open project dashboard, test case, test plan, scope, defect or defects list page checks for other people's changes (2–3600). Each check is a few small queries; the page is only re-fetched when something changed. Hidden tabs don't check. | `10` |

## Version and update check

| Variable | Purpose | Default |
|---|---|---|
| `QATRACKER_UPDATE_CHECK` | Compare the running version with the latest GitHub release (shortly after start-up, then every few hours) and show the result in *System settings*. `false` means the app never contacts github.com. | `true` |
| `QATRACKER_GITHUB_REPO` | The repository to check, if you run a fork | `mophead64/qa-tracker` |

## Runtime and errors

| Variable | Purpose | Default |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Development` enables developer tooling; anything else runs the production pipeline | `Production` |
| `ASPNETCORE_HTTP_PORTS` | Port(s) to listen on; the container image uses 8080 | `8080` in the image |
| `QATRACKER_DETAILED_ERRORS` | Show the detailed developer error page (Development only) | `false` |

---

Next: [Single sign-on](sso.md) · Back to the [documentation index](README.md)
