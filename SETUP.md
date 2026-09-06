# SSO / OIDC setup guide

QA Tracker signs users in with **local accounts** out of the box. You can additionally
wire up **one** external OpenID Connect provider — Microsoft Entra ID or Keycloak are
supported and documented here, and any spec-compliant OIDC provider should work through
the same generic handler (see [Other providers](#other-oidc-providers)).

- [How SSO behaves in QA Tracker](#how-sso-behaves-in-qa-tracker)
- [Common configuration](#common-configuration)
- [Microsoft Entra ID](#microsoft-entra-id)
- [Keycloak](#keycloak)
- [Other OIDC providers](#other-oidc-providers)
- [Troubleshooting](#troubleshooting)

---

## How SSO behaves in QA Tracker

- **Additive, never exclusive.** Local accounts keep working. Setting
  `QATRACKER_AUTH_PROVIDER` adds a *"Sign in with …"* button to the login page next to the
  local email/password form. You can run exactly one external provider at a time — never
  two.
- **Roles come from the token.** The provider is the source of truth for whether a user is
  **QA** or **Dev**. Roles are re-synced on every sign-in. A user whose token carries no
  recognised role value is still signed in — they just have no role until the provider
  grants one. Users have exactly one role (if a token asserts both, QA wins).
- **Accounts are provisioned on first sign-in.** If the token's email matches an existing
  local account, that account is linked to the provider instead. The link itself is keyed
  on the provider's subject identifier (`sub`), not the email.
- **Provider-managed accounts are read-only in-app.** Their name, password and roles can
  only be changed at the identity provider. The name is refreshed from the token on each
  sign-in. A QA admin can change the *email* through a confirmation dialog on the user's
  edit page — but only to match a change already made at the provider.
- **Logout is local only.** Signing out of QA Tracker does not sign the user out of the
  identity provider; the next *"Sign in with …"* click will silently re-authenticate. This
  is normal SSO behaviour.
- **The redirect URI is always `<app-base-url>/signin-oidc`.** Register it with the
  provider for every base URL the app is reached on (e.g. `https://qa.example.com/signin-oidc`).

---

## Common configuration

These environment variables apply to any provider. Full annotated list in
[`.env.example`](.env.example), section 3.

| Variable | Required | Purpose |
|---|---|---|
| `QATRACKER_AUTH_PROVIDER` | yes | `Entra` (aliases `EntraId`, `AzureAd`, `AAD`) or `Keycloak`. Unset / blank = local accounts only. |
| `QATRACKER_OIDC_CLIENT_ID` | yes | OAuth client / application ID. |
| `QATRACKER_OIDC_CLIENT_SECRET` | yes | OAuth client secret. |
| `QATRACKER_OIDC_AUTHORITY` | see note | The provider's issuer URL. **Optional for Entra** if you set `QATRACKER_OIDC_TENANT_ID` instead. |
| `QATRACKER_OIDC_TENANT_ID` | Entra only | Directory (tenant) ID. The app composes the authority `https://login.microsoftonline.com/<tenant-id>/v2.0`. |
| `QATRACKER_OIDC_INSTANCE` | no | Entra login host for national clouds (e.g. `https://login.microsoftonline.us`). Default `https://login.microsoftonline.com`. |
| `QATRACKER_OIDC_METADATA_ADDRESS` | no | Overrides **only** the back-channel discovery URL. Use when the app can't resolve the public issuer host itself (containers reaching an in-network IdP). |
| `QATRACKER_OIDC_SCOPES` | no | Space- or comma-separated. Default `openid profile email` (`openid` is always added). |
| `QATRACKER_OIDC_ROLE_CLAIM` | no | Token claim carrying roles. Default `roles`. |
| `QATRACKER_OIDC_ROLE_QA_VALUE` | no | Claim value that grants QA. Default `QA`. |
| `QATRACKER_OIDC_ROLE_DEV_VALUE` | no | Claim value that grants Dev. Default `Dev`. |
| `QATRACKER_OIDC_REQUIRE_HTTPS_METADATA` | no | Default `true`. Set `false` only for a plain-HTTP dev IdP. |

> **Behind a TLS-terminating proxy** (Azure App Service / Container Apps, nginx, …) also
> set `QATRACKER_FORWARDED_HEADERS=true` so the OIDC callback URLs are built as `https://`.
> See [`.env.example`](.env.example) section 6.

---

## Microsoft Entra ID

### 1. Register the application

1. **Entra admin center** → **App registrations** → **New registration**.
2. Name it (e.g. *QA Tracker*).
3. **Supported account types**: *Accounts in this organizational directory only* (single
   tenant) for an internal tool.
4. **Redirect URI**: platform **Web**, value `<app-base-url>/signin-oidc` — for example
   `https://qa.example.com/signin-oidc`. Add one entry per base URL the app is served on.
5. **Register**.

From the app's **Overview** page, note the **Application (client) ID** and the
**Directory (tenant) ID**.

### 2. Create a client secret

1. **Certificates & secrets** → **Client secrets** → **New client secret**.
2. Set a description and expiry, **Add**, and copy the **Value** immediately (it is shown
   only once).

### 3. Expose the email claim

Entra does **not** send an `email` claim by default for member accounts.

1. **Token configuration** → **Add optional claim**.
2. **Token type**: **ID**.
3. Select **email** (add **`preferred_username`** and **`upn`** too — QA Tracker falls
   back to those when they parse as an address).
4. **Add**. When prompted *"Turn on the Microsoft Graph email permission (required for
   this claim to appear in the token)"*, tick the box.
5. Make sure each signing-in user actually has a **mail** attribute set on their Entra
   profile. Members created without a mailbox have none, and Entra will emit no `email`
   claim even with the optional claim configured — QA Tracker then uses the UPN.

### 4. Define and assign app roles

1. **App roles** → **Create app role** twice:
   - Display name *QA*, value **`QA`**, allowed member types *Users/Groups*.
   - Display name *Dev*, value **`Dev`**, allowed member types *Users/Groups*.
2. **Enterprise applications** → *your app* → **Users and groups** → **Add user/group** →
   assign each person one of the two roles.

Entra emits these in the `roles` claim, which is the QA Tracker default — no role-mapping
variables needed unless you chose different values.

### 5. API permissions

Under **API permissions**, confirm delegated **Microsoft Graph** permissions `openid`,
`profile`, `email`, `User.Read`. Grant admin consent if your tenant requires it.

### 6. Configure QA Tracker

```bash
QATRACKER_AUTH_PROVIDER=Entra
QATRACKER_OIDC_TENANT_ID=<directory-tenant-id>
QATRACKER_OIDC_CLIENT_ID=<application-client-id>
QATRACKER_OIDC_CLIENT_SECRET=<client-secret-value>
# National cloud only:
# QATRACKER_OIDC_INSTANCE=https://login.microsoftonline.us
```

You do **not** need `QATRACKER_OIDC_AUTHORITY` — the app composes
`https://login.microsoftonline.com/<tenant-id>/v2.0` from the tenant ID, which guarantees
the `/v2.0` suffix (the v1 endpoint causes the `AADSTS900561` error). Set
`QATRACKER_OIDC_AUTHORITY` explicitly only to override that.

Restart the app. The login page should now show **Sign in with Microsoft Entra ID**.

---

## Keycloak

### Option A — the bundled dev realm

`docker compose up` starts a Keycloak container and imports
[`deploy/keycloak/qatracker-realm.json`](deploy/keycloak/qatracker-realm.json):

| | |
|---|---|
| Admin console | <http://localhost:8081> (`admin` / `admin`) |
| Realm | `qatracker` |
| Client | `qatracker-web` / secret `qatracker-dev-secret` |
| Realm roles | `QA`, `Dev` (emitted as a flat `roles` claim by a role mapper) |
| Test users | `qa@example.com` / `dev@example.com`, password `Passw0rd!` |
| Redirect URIs | `http://localhost:8080/*`, `http://localhost:5281/*`, `https://localhost:7157/*` |

The compose `web` service is already pointed at it:

```bash
QATRACKER_AUTH_PROVIDER=Keycloak
QATRACKER_OIDC_AUTHORITY=http://localhost:8081/realms/qatracker          # browser-facing issuer
QATRACKER_OIDC_METADATA_ADDRESS=http://keycloak:8080/realms/qatracker/.well-known/openid-configuration  # in-network discovery
QATRACKER_OIDC_CLIENT_ID=qatracker-web
QATRACKER_OIDC_CLIENT_SECRET=qatracker-dev-secret
QATRACKER_OIDC_REQUIRE_HTTPS_METADATA=false
```

Running the app on the host with `dotnet run` instead? Drop
`QATRACKER_OIDC_METADATA_ADDRESS` — `http://localhost:8081` is reachable both ways.

### Option B — your own Keycloak

1. **Create a realm** (or reuse one).
2. **Clients** → **Create client**:
   - Client type **OpenID Connect**, a client ID of your choosing.
   - **Client authentication** **On** (confidential client).
   - **Valid redirect URIs**: `<app-base-url>/signin-oidc`, one per base URL.
   - From the **Credentials** tab, copy the client secret.
3. **Realm roles** → create **`QA`** and **`Dev`** (names must match
   `QATRACKER_OIDC_ROLE_QA_VALUE` / `_DEV_VALUE` if you override the defaults).
4. **Add a role claim to the token.** Keycloak's default `realm access` roles are nested
   under `realm_access.roles`, which this app does not read. Add a **client scope** (or a
   dedicated mapper on the client) of type **User Realm Role** with:
   - **Token Claim Name**: `roles`
   - **Add to ID token** and **Add to userinfo**: on
   - **Multivalued**: on, **Claim JSON Type**: String
5. **Assign roles** to users under **Users** → *user* → **Role mapping**.
6. Configure QA Tracker:

   ```bash
   QATRACKER_AUTH_PROVIDER=Keycloak
   QATRACKER_OIDC_AUTHORITY=https://<keycloak-host>/realms/<realm>
   QATRACKER_OIDC_CLIENT_ID=<client-id>
   QATRACKER_OIDC_CLIENT_SECRET=<client-secret>
   ```

---

## Other OIDC providers

QA Tracker uses one generic OpenID Connect handler. Any provider that publishes a standard
`.well-known/openid-configuration` discovery document, supports the **authorization code
flow with PKCE**, and can emit a roles claim should work:

```bash
QATRACKER_AUTH_PROVIDER=Keycloak        # pick either alias; it only sets the button label
QATRACKER_OIDC_AUTHORITY=https://issuer.example.com
QATRACKER_OIDC_CLIENT_ID=<client-id>
QATRACKER_OIDC_CLIENT_SECRET=<client-secret>
QATRACKER_OIDC_ROLE_CLAIM=<claim-carrying-roles>
QATRACKER_OIDC_ROLE_QA_VALUE=<value-that-means-QA>
QATRACKER_OIDC_ROLE_DEV_VALUE=<value-that-means-Dev>
QATRACKER_OIDC_SCOPES=openid profile email <any-scope-needed-to-release-roles>
```

`QATRACKER_AUTH_PROVIDER` only accepts `Entra` or `Keycloak` today — the value selects the
button label and a couple of claim defaults, nothing provider-specific in the protocol.
Providers other than Entra and Keycloak are not regularly tested; if you run one
successfully, a note in an issue or PR is welcome.

---

## Troubleshooting

### `Correlation failed` / *"'.AspNetCore.Correlation.\<…>' cookie not found"* on the callback

The identity provider is posting the sign-in response back cross-site (Entra does this by
default) and the browser isn't returning the short-lived correlation cookie because the
app is on plain HTTP. Fix by serving the app over **HTTPS**:

- **In production**, terminate TLS at your proxy and set `QATRACKER_FORWARDED_HEADERS=true`
  so the app knows the request was HTTPS.
- **Locally**, run the HTTPS launch profile (`dotnet run --launch-profile https`) and
  register that `https://localhost:<port>/signin-oidc` URI with the provider.

Keycloak on `localhost` avoids this only because it is same-site with the app.

### `AADSTS900561: The endpoint only accepts POST requests. Received a GET request`

The authority is resolving to the Entra **v1** endpoint. Use `QATRACKER_OIDC_TENANT_ID`
(which makes the app build the `/v2.0` authority for you), or make sure a hand-set
`QATRACKER_OIDC_AUTHORITY` ends in `/v2.0`.

### *"The identity provider did not supply an email address"*

The token carried no `email` (or usable `preferred_username` / `upn`) claim. For Entra,
add the optional **ID token** `email` claim and give the user a **mail** attribute — see
[step 3](#3-expose-the-email-claim). For Keycloak, make sure the `email` scope is granted
and the user has an email set.

### Signs in, but the user has no role / can't see admin or Dev/QA features

The roles claim isn't arriving under the expected name/value.

- Check `QATRACKER_OIDC_ROLE_CLAIM` matches the claim your provider emits (`roles` for
  Entra and the bundled Keycloak realm; Keycloak's default is nested `realm_access.roles`,
  which needs the flat-claim mapper from [Keycloak option B, step 4](#option-b--your-own-keycloak)).
- Check the claim's **values** match `QATRACKER_OIDC_ROLE_QA_VALUE` /
  `QATRACKER_OIDC_ROLE_DEV_VALUE` (default `QA` / `Dev`).
- Roles re-sync on sign-in — the user must sign out and back in after a role change at the
  provider.

### `IDX20803` / discovery connection refused from inside a container

The app is trying to fetch discovery from a URL it can't reach (typically a public issuer
hostname that only resolves outside the container). Set `QATRACKER_OIDC_METADATA_ADDRESS`
to the in-network discovery URL while leaving `QATRACKER_OIDC_AUTHORITY` as the
browser-facing issuer.
