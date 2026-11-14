[QA Tracker](../README.md) › [Docs](README.md) › Deployment examples

# Deployment examples

Step-by-step set-ups for **Azure**, **AWS** and **your own server**. Each one comes in two
flavours, with ready-made files in [`deploy/examples`](../deploy/examples):

| | Sign-in | Attachments | Telemetry |
|---|---|---|---|
| **A. Local accounts** | QA Tracker's own accounts; the first QA comes from `QATRACKER_ADMIN_*` | ✓ | — |
| **B. Single sign-on** | Microsoft Entra ID (people's roles come from app roles) | ✓ | Application Insights |

Start with A to get running, then move to B when single sign-on is ready. Nothing in the
database changes between them. [Deployment](deployment.md) covers what applies everywhere
(migrations, scaling, health checks, upgrades), and [Configuration](configuration.md) lists
every setting.

- [The minimal settings](#the-minimal-settings)
- [Azure](#azure): Container Apps, PostgreSQL Flexible Server, Blob Storage, Key Vault, Application Insights
- [AWS](#aws): ECS on Fargate (Graviton), RDS for PostgreSQL, S3, Secrets Manager
- [Your own server](#your-own-server): Docker Compose with PostgreSQL, Garage and Caddy

> **Any CPU.** The image is published for both `linux/amd64` and `linux/arm64` under the
> same tags, so it runs natively on Intel/AMD, AWS Graviton, Azure Cobalt/Ampere and Apple
> silicon. Don't force `platform: linux/amd64` on an arm64 machine: .NET can't run under
> that emulation, and the app crashes on start-up.

---

## The minimal settings

Whatever the platform, these are all QA Tracker needs. Pass the ones marked 🔒 as secrets.

**A. Local accounts + attachments**

| Setting | Example |
|---|---|
| `QATRACKER_DB_HOST`, `QATRACKER_DB_USER`, `QATRACKER_DB_PASSWORD` 🔒 | `qa-db.example.net`, `qatracker`, … (or one `ConnectionStrings__DefaultConnection` 🔒) |
| `QATRACKER_STORAGE_PROVIDER` | `Azure` or `S3` |
| Azure: `QATRACKER_AZURE_STORAGE_CONNECTION_STRING` 🔒 | `DefaultEndpointsProtocol=https;AccountName=…` |
| S3: `QATRACKER_S3_BUCKET`, `QATRACKER_S3_REGION`, `QATRACKER_S3_ACCESS_KEY` 🔒, `QATRACKER_S3_SECRET_KEY` 🔒 | `acme-qatracker-files`, `eu-west-2`, … |
| `QATRACKER_ADMIN_EMAIL`, `QATRACKER_ADMIN_PASSWORD` 🔒 | The first QA, created on first start |
| `QATRACKER_FORWARDED_HEADERS` | `true` whenever TLS ends at a load balancer, ingress or proxy (all three examples) |

**B. Single sign-on + attachments + Application Insights** is the above, minus the admin
account, plus:

| Setting | Example |
|---|---|
| `QATRACKER_AUTH_PROVIDER` | `Entra` |
| `QATRACKER_OIDC_TENANT_ID`, `QATRACKER_OIDC_CLIENT_ID`, `QATRACKER_OIDC_CLIENT_SECRET` 🔒 | From the app registration ([Single sign-on](sso.md#microsoft-entra-id)) |
| `QATRACKER_LOCAL_AUTH_ENABLED` | `false` to make single sign-on the only way in; leave it out to keep a local break-glass QA |
| `QATRACKER_TELEMETRY_PROVIDER` | `AzureMonitor` |
| `QATRACKER_AZURE_MONITOR_CONNECTION_STRING` 🔒 | The Application Insights connection string |

Application Insights works from any cloud. To send telemetry to something else (Grafana,
SigNoz, an OpenTelemetry Collector, AWS X-Ray through the ADOT collector…), use
`QATRACKER_TELEMETRY_PROVIDER=Otlp` and `QATRACKER_OTLP_ENDPOINT` instead.

---

## Azure

**What you'll create:** a resource group holding

- **Azure Container Apps**: runs the image, with HTTPS ingress, scaling and health probes.
- **Azure Database for PostgreSQL – Flexible Server**: the database.
- **Storage account**: attachments, in a private blob container the app creates itself.
- **Key Vault**: every secret. The app reads them with a **user-assigned managed identity**,
  so no secret is pasted into the app's configuration.
- **Log Analytics workspace**: container logs, plus (B) **Application Insights** for traces,
  metrics and logs.

The example files are
[`containerapp.local-auth.yaml`](../deploy/examples/azure/containerapp.local-auth.yaml) (A) and
[`containerapp.sso-appinsights.yaml`](../deploy/examples/azure/containerapp.sso-appinsights.yaml) (B).
Run the steps from the repository root, in one shell (they share variables), signed in with
`az login`.

### 1. Names and prerequisites

```bash
RG=rg-qatracker
LOCATION=uksouth
PREFIX=qatracker$RANDOM          # storage account / Key Vault names are global: lower-case letters and digits
ADMIN_EMAIL=you@example.com      # (A) the first QA

az extension add --name application-insights --upgrade
az provider register --namespace Microsoft.App
az provider register --namespace Microsoft.OperationalInsights
az group create --name $RG --location $LOCATION
```

### 2. Managed identity and Key Vault

```bash
az identity create -g $RG -n qatracker-identity
IDENTITY_ID=$(az identity show -g $RG -n qatracker-identity --query id -o tsv)
IDENTITY_PRINCIPAL=$(az identity show -g $RG -n qatracker-identity --query principalId -o tsv)

az keyvault create -g $RG -n ${PREFIX}kv -l $LOCATION --enable-rbac-authorization true
KV_ID=$(az keyvault show -n ${PREFIX}kv --query id -o tsv)

# You write the secrets; the app's identity reads them.
az role assignment create --assignee-object-id "$(az ad signed-in-user show --query id -o tsv)" \
  --assignee-principal-type User --role "Key Vault Secrets Officer" --scope $KV_ID
az role assignment create --assignee-object-id $IDENTITY_PRINCIPAL \
  --assignee-principal-type ServicePrincipal --role "Key Vault Secrets User" --scope $KV_ID
```

Role assignments can take a minute to apply. If a `secret set` below is refused, wait and
run it again.

### 3. PostgreSQL

```bash
DB_PASSWORD=$(openssl rand -base64 30 | tr -d '/+=')
az postgres flexible-server create -g $RG -n ${PREFIX}-db -l $LOCATION \
  --tier Burstable --sku-name Standard_B1ms --storage-size 32 --version 17 \
  --admin-user qatracker --admin-password "$DB_PASSWORD" \
  --database-name qatracker --public-access 0.0.0.0
az keyvault secret set --vault-name ${PREFIX}kv -n db-password --value "$DB_PASSWORD"
```

`--public-access 0.0.0.0` lets services running in Azure reach the server. For a production
network, put the server and the Container Apps environment in the same virtual network
(`--vnet` on both) instead. The server requires TLS, which the app uses automatically.
Burstable B1ms is plenty for a team. Use General Purpose for heavier use or high availability.

### 4. Storage for attachments

```bash
az storage account create -g $RG -n ${PREFIX}files -l $LOCATION \
  --sku Standard_LRS --kind StorageV2 --min-tls-version TLS1_2 --allow-blob-public-access false
az keyvault secret set --vault-name ${PREFIX}kv -n storage-connection \
  --value "$(az storage account show-connection-string -g $RG -n ${PREFIX}files -o tsv)"
```

The app creates its private `qatracker-attachments` container on first upload, and every
download goes through the app, so nothing in the account is ever public.

### 5. Logs (and Application Insights for B)

```bash
az monitor log-analytics workspace create -g $RG --workspace-name qatracker-logs -l $LOCATION
LOGS_ID=$(az monitor log-analytics workspace show -g $RG --workspace-name qatracker-logs --query customerId -o tsv)
LOGS_KEY=$(az monitor log-analytics workspace get-shared-keys -g $RG --workspace-name qatracker-logs --query primarySharedKey -o tsv)

# B only:
az monitor app-insights component create -g $RG --app qatracker-insights -l $LOCATION \
  --workspace "$(az monitor log-analytics workspace show -g $RG --workspace-name qatracker-logs --query id -o tsv)"
az keyvault secret set --vault-name ${PREFIX}kv -n appinsights-connection \
  --value "$(az monitor app-insights component show -g $RG --app qatracker-insights --query connectionString -o tsv)"
```

### 6. The Container Apps environment

```bash
az containerapp env create -g $RG -n qatracker-env -l $LOCATION \
  --logs-workspace-id $LOGS_ID --logs-workspace-key $LOGS_KEY
ENV_ID=$(az containerapp env show -g $RG -n qatracker-env --query id -o tsv)
```

### 7. Deploy with local accounts (A)

```bash
az keyvault secret set --vault-name ${PREFIX}kv -n admin-password --value 'Change-me-1!'

sed -e "s|<location>|$LOCATION|" -e "s|<identity-id>|$IDENTITY_ID|g" -e "s|<environment-id>|$ENV_ID|" \
    -e "s|<key-vault>|${PREFIX}kv|g" -e "s|<postgres-server>|${PREFIX}-db|" -e "s|<admin-email>|$ADMIN_EMAIL|" \
    deploy/examples/azure/containerapp.local-auth.yaml > containerapp.yaml
az containerapp create -g $RG -n qatracker --yaml containerapp.yaml

FQDN=$(az containerapp show -g $RG -n qatracker --query properties.configuration.ingress.fqdn -o tsv)
echo "https://$FQDN"
```

The first start applies the database schema, which takes a few seconds. Then sign in at
`https://$FQDN` with `ADMIN_EMAIL` and the admin password, and change that password under
*Settings*. Delete `containerapp.yaml` once deployed: it holds resource IDs, but no secrets.

### 8. Switch to single sign-on and Application Insights (B)

1. Register QA Tracker in Entra ID as in [Single sign-on](sso.md#microsoft-entra-id), with
   the redirect URI `https://$FQDN/signin-oidc` (and your custom domain's, if you add one).
   Give yourself the **QA** app role.
2. Store the client secret, fill in the B file, and update the app:

```bash
az keyvault secret set --vault-name ${PREFIX}kv -n oidc-client-secret --value '<client-secret-value>'

sed -e "s|<location>|$LOCATION|" -e "s|<identity-id>|$IDENTITY_ID|g" -e "s|<environment-id>|$ENV_ID|" \
    -e "s|<key-vault>|${PREFIX}kv|g" -e "s|<postgres-server>|${PREFIX}-db|" \
    -e "s|<tenant-id>|<directory-tenant-id>|" -e "s|<client-id>|<application-client-id>|" \
    deploy/examples/azure/containerapp.sso-appinsights.yaml > containerapp.yaml
az containerapp update -g $RG -n qatracker --yaml containerapp.yaml
```

The B file sets `QATRACKER_LOCAL_AUTH_ENABLED=false`, so after this everyone signs in with
Entra ID. Remove those two lines from the file to keep local accounts working alongside.

### Afterwards

- **Custom domain:** `az containerapp hostname add` / `hostname bind` with a managed
  certificate. Add its `/signin-oidc` redirect URI to the app registration.
- **Upgrades:** `az containerapp update -g $RG -n qatracker --image ghcr.io/mophead64/qa-tracker:<release>`.
  The new revision migrates the database on start-up. Pin a release tag rather than `latest`
  if you want to choose when upgrades happen.
- **Scale:** the files run 1–3 replicas. Instances share everything through the database,
  so raise `maxReplicas` freely ([Scaling out](deployment.md#scaling-out)).

---

## AWS

**What you'll create:**

- **ECS on Fargate**: runs the image as a service, on **Graviton (arm64)** for a lower price.
  Change `runtimePlatform` to `X86_64` if you prefer.
- **Application Load Balancer**: HTTPS with an ACM certificate, health-checking `/health/ready`.
- **RDS for PostgreSQL**: the database, in private subnets.
- **S3**: attachments, in a private bucket. The app uses an IAM user's access key limited to
  that bucket ([`s3-policy.json`](../deploy/examples/aws/s3-policy.json)).
- **Secrets Manager**: every secret, injected into the task by ECS.
- **CloudWatch Logs**: container logs. With B, traces also go to Application Insights.

The example files are
[`task-definition.local-auth.json`](../deploy/examples/aws/task-definition.local-auth.json) (A) and
[`task-definition.sso-appinsights.json`](../deploy/examples/aws/task-definition.sso-appinsights.json) (B).
They assume an existing VPC with public subnets (for the load balancer) and private subnets
with outbound internet access (for the tasks, to pull the image from GHCR).

### 1. Names

```bash
REGION=eu-west-2
ACCOUNT=$(aws sts get-caller-identity --query Account --output text)
BUCKET=acme-qatracker-files            # globally unique
VPC_ID=vpc-…  PRIVATE_SUBNETS=subnet-…,subnet-…  APP_SG=sg-…  DB_SG=sg-…
# APP_SG: inbound 8080 from the load balancer's security group.
# DB_SG:  inbound 5432 from APP_SG.
```

### 2. Database

```bash
DB_PASSWORD=$(openssl rand -base64 30 | tr -d '/+=@"')
aws rds create-db-instance --region $REGION --db-instance-identifier qatracker \
  --engine postgres --engine-version 17 --db-instance-class db.t4g.micro --allocated-storage 20 \
  --master-username qatracker --master-user-password "$DB_PASSWORD" --db-name qatracker \
  --vpc-security-group-ids $DB_SG --no-publicly-accessible --storage-encrypted --backup-retention-period 7
aws secretsmanager create-secret --region $REGION --name qatracker/db-password --secret-string "$DB_PASSWORD"
```

When it's available, note its endpoint (`aws rds describe-db-instances --db-instance-identifier
qatracker --query 'DBInstances[0].Endpoint.Address'`). RDS requires TLS by default, which
the app uses automatically.

### 3. Attachments bucket and its key

```bash
aws s3api create-bucket --region $REGION --bucket $BUCKET \
  --create-bucket-configuration LocationConstraint=$REGION
aws s3api put-public-access-block --bucket $BUCKET --public-access-block-configuration \
  BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true

aws iam create-user --user-name qatracker-files
sed "s|<bucket>|$BUCKET|g" deploy/examples/aws/s3-policy.json > s3-policy.json
aws iam put-user-policy --user-name qatracker-files --policy-name qatracker-files --policy-document file://s3-policy.json
aws iam create-access-key --user-name qatracker-files      # note AccessKeyId and SecretAccessKey
aws secretsmanager create-secret --region $REGION --name qatracker/s3-access-key --secret-string '<AccessKeyId>'
aws secretsmanager create-secret --region $REGION --name qatracker/s3-secret-key --secret-string '<SecretAccessKey>'
```

(Leave out `--create-bucket-configuration` in `us-east-1`.)

### 4. Secrets for sign-in (and telemetry)

```bash
# A:
aws secretsmanager create-secret --region $REGION --name qatracker/admin-password --secret-string 'Change-me-1!'
# B (after registering the app in Entra ID; see "Single sign-on" below):
aws secretsmanager create-secret --region $REGION --name qatracker/oidc-client-secret --secret-string '<client-secret-value>'
aws secretsmanager create-secret --region $REGION --name qatracker/appinsights-connection --secret-string '<connection string>'
```

### 5. The task execution role

ECS uses this role to pull secrets and write logs (the app itself needs no AWS role):

```bash
aws iam create-role --role-name qatracker-task-execution --assume-role-policy-document \
  '{"Version":"2012-10-17","Statement":[{"Effect":"Allow","Principal":{"Service":"ecs-tasks.amazonaws.com"},"Action":"sts:AssumeRole"}]}'
aws iam attach-role-policy --role-name qatracker-task-execution \
  --policy-arn arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy
aws iam put-role-policy --role-name qatracker-task-execution --policy-name qatracker-secrets --policy-document \
  "{\"Version\":\"2012-10-17\",\"Statement\":[{\"Effect\":\"Allow\",\"Action\":\"secretsmanager:GetSecretValue\",\"Resource\":\"arn:aws:secretsmanager:$REGION:$ACCOUNT:secret:qatracker/*\"},{\"Effect\":\"Allow\",\"Action\":\"logs:CreateLogGroup\",\"Resource\":\"*\"}]}"
```

### 6. Register the task definition

Secrets Manager adds a random six-character suffix to every secret's ARN. The files use the
ARN without it, which ECS resolves within the same account and region. If a task can't
read a secret, swap in the full ARN from `aws secretsmanager describe-secret --secret-id <name>`.

```bash
sed -e "s|<region>|$REGION|g" -e "s|<account-id>|$ACCOUNT|g" -e "s|<bucket>|$BUCKET|" \
    -e "s|<rds-endpoint>|<the RDS endpoint>|" -e "s|<admin-email>|you@example.com|" \
    deploy/examples/aws/task-definition.local-auth.json > task-definition.json
aws ecs register-task-definition --region $REGION --cli-input-json file://task-definition.json
```

### 7. Load balancer and service

Create (console or CLI) an **Application Load Balancer** in the public subnets with an
**HTTPS listener** (an ACM certificate for your domain) forwarding to a target group:

```bash
aws elbv2 create-target-group --name qatracker --protocol HTTP --port 8080 --vpc-id $VPC_ID \
  --target-type ip --health-check-path /health/ready
```

Then run the service behind it:

```bash
aws ecs create-cluster --cluster-name qatracker
aws ecs create-service --region $REGION --cluster qatracker --service-name qatracker \
  --task-definition qatracker --desired-count 1 --launch-type FARGATE \
  --health-check-grace-period-seconds 60 \
  --network-configuration "awsvpcConfiguration={subnets=[$PRIVATE_SUBNETS],securityGroups=[$APP_SG],assignPublicIp=DISABLED}" \
  --load-balancers "targetGroupArn=<target group ARN>,containerName=qatracker,containerPort=8080"
```

Point your DNS name at the load balancer and sign in with the admin account. The load
balancer adds `X-Forwarded-Proto`, which the task trusts (`QATRACKER_FORWARDED_HEADERS=true`).

### Single sign-on and Application Insights (B)

Register the app in Entra ID ([Single sign-on](sso.md#microsoft-entra-id)) with the redirect
URI `https://<your domain>/signin-oidc`, and create an Application Insights resource (or use
`Otlp` for another backend). Add the two secrets from step 4, register
`task-definition.sso-appinsights.json` the same way (also replacing `<tenant-id>` and
`<client-id>`), and roll the service onto it:

```bash
aws ecs update-service --region $REGION --cluster qatracker --service qatracker \
  --task-definition qatracker --force-new-deployment
```

**Upgrades:** register the task definition again with a new image tag (or keep `latest`)
and run the same `update-service` command.

---

## Your own server

**What you'll run:** [`deploy/examples/on-prem/docker-compose.yml`](../deploy/examples/on-prem/docker-compose.yml)

- **QA Tracker**, published only through Caddy.
- **PostgreSQL 17**, with its data in a Docker volume.
- **Garage**: S3-compatible storage for attachments, in a Docker volume.
- **Caddy**: HTTPS with automatic Let's Encrypt certificates, and HTTP→HTTPS redirects.

You need Docker with Compose 2.23 or later, a DNS name pointing at the server, and ports
80 and 443 open to it.

```bash
cd deploy/examples/on-prem
cp local-auth.env.example .env          # A: local accounts + attachments
#  or: cp sso-appinsights.env.example .env   # B: Entra ID + attachments + Application Insights
# edit .env: the domain, passwords, the Garage secret (openssl rand -hex 32)…
docker compose up -d
```

Open `https://<your domain>` and sign in. For B, register the app in Entra ID first, with
the redirect URI `https://<your domain>/signin-oidc`.

- **Behind your own proxy or load balancer instead of Caddy?** Remove the `caddy` service,
  publish the app's port 8080 to that proxy only, and set `QATRACKER_KNOWN_PROXIES` to its
  address ([Behind a reverse proxy](deployment.md#behind-a-reverse-proxy)).
- **Already have PostgreSQL or S3-compatible storage?** Remove the `db` / `garage` service
  and point the `QATRACKER_DB_*` / `QATRACKER_S3_*` settings at yours.
- **Keycloak instead of Entra ID?** Use `QATRACKER_AUTH_PROVIDER=Keycloak` with
  `QATRACKER_OIDC_AUTHORITY` ([Single sign-on](sso.md#keycloak)).
- **Backups:** back up the `qatracker-db` volume (or run `pg_dump`) and the
  `qatracker-files` volume. Together they hold everything.
- **Upgrades:** `docker compose pull && docker compose up -d`. Pin `QATRACKER_IMAGE_TAG` to
  a release to choose when.

---

Next: [Development](development.md) · Back to the [documentation index](README.md)
