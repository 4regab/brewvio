# Brewvio — Environment, Local Dev & Deployment

This guide covers running Brewvio locally, the environment variables it reads, the test setup,
and deploying to AWS (Lambda + S3 + CloudFront) with AWS SAM.

---

## 1. Environment variables

### API (local & Lambda)

| Variable | Purpose | Example |
|----------|---------|---------|
| `ConnectionStrings__Default` | Npgsql connection string (local dev / tests) | `Host=localhost;Port=5433;Database=brewvio;Username=postgres;Password=postgres` |
| `DATABASE_URL` | Supabase pooler URI (Lambda). Parsed into an Npgsql string with `SslMode=Require`. Takes precedence over `ConnectionStrings__Default` when set | `postgres://postgres:pass@db.xyz.supabase.co:6543/postgres` |
| `JWT_KEY` | HMAC signing key for JWTs (**≥ 32 bytes**) | `brewvio-local-dev-signing-key-min-32-bytes!!` |
| `ASPNETCORE_ENVIRONMENT` | `Development` locally; unset/`Production` on Lambda | `Development` |
| `ASPNETCORE_URLS` | Kestrel bind address (local only; ignored on Lambda) | `http://localhost:5000` |
| `Jwt:Issuer` / `Jwt:Audience` | Token issuer/audience — already in `appsettings.json` | `brewvio` |

> Precedence in `Program.cs`: `DATABASE_URL` (if set) → `ConnectionStrings__Default`.
> `JWT_KEY` env var → `Jwt:Key` (Development only). The app **throws on startup** if no key is set.

### Tests

| Variable | Purpose | Default |
|----------|---------|---------|
| `BREWVIO_TEST_PG` | Base Npgsql string; each test appends a unique `Database=brewvio_test_<guid>` | `Host=localhost;Port=5433;Username=postgres;Password=postgres` |

### AWS deploy (SAM parameters + credentials)

| Name | Purpose |
|------|---------|
| `SsmParameterPath` (SAM param) | SSM path prefix for secrets (default `/brewvio`) |
| `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` / `AWS_REGION` | Standard AWS credentials for `sam deploy` |

On Lambda the app reads secrets from **SSM Parameter Store** (not env vars). Create these
`SecureString` parameters under the path before/after deploying — CloudFormation cannot create
`SecureString` parameters itself, so they live out-of-band:

| SSM parameter | Maps to config key | Purpose |
|---------------|--------------------|---------|
| `/brewvio/DATABASE_URL` | `DATABASE_URL` | Supabase **transaction pooler** URI, port `6543` |
| `/brewvio/JWT_KEY` | `JWT_KEY` | Production JWT signing key (≥ 32 bytes) |

```bash
aws ssm put-parameter --name /brewvio/DATABASE_URL --type SecureString \
  --value 'postgres://postgres:<pass>@db.<ref>.supabase.co:6543/postgres'
aws ssm put-parameter --name /brewvio/JWT_KEY --type SecureString \
  --value '<production-signing-key-min-32-bytes>'
```

> The Lambda's IAM role is granted least-privilege `ssm:GetParametersByPath`/`GetParameter`
> on `/brewvio/*` plus `kms:Decrypt` scoped to the SSM service (`kms:ViaService`). The app
> loads the path at startup via `AddSystemsManager`. Locally, the same keys are read from
> env vars (`DATABASE_URL`, `JWT_KEY`) so no AWS calls happen in development.

---

## 2. Local development

### 2a. Start PostgreSQL (port 5433)

This repo includes a no-sudo helper that initialises and starts a user-owned Postgres 18 cluster
on port `5433` with a `postgres`/`postgres` superuser:

```bash
bash scripts/pg-setup.sh     # initialise + start the cluster (idempotent)
bash scripts/pg-initdb.sh    # create the 'brewvio' database
```

Alternatively, with Docker:

```bash
docker run -d --name brewvio-pg -p 5433:5432 \
  -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=brewvio postgres:16
```

### 2b. Apply EF Core migrations

```bash
# Uses ConnectionStrings__Default / DATABASE_URL, falling back to localhost:5433
dotnet ef database update --project src/Brewvio
```

(The included `scripts/ef-update.sh` does this against `:5433`.)

### 2c. Run the API + SPA

```bash
# Windows
scripts\run-api.cmd
# or cross-platform
dotnet run --project src/Brewvio
```

The app serves the SPA from `wwwroot/` and the API under `/api/*` on
`http://localhost:5000`. On first run it **seeds demo data** (see below).

### Demo accounts

| Username | Password | Role | Status |
|----------|----------|------|--------|
| `manager` | `Manager@123` | Manager | Active |
| `cashier` | `Cashier@123` | Cashier | Active |
| `newcashier` | `Pending@123` | Cashier | Pending (in the approval queue) |

---

## 3. Tests

### Backend (xUnit, real Postgres)

```bash
# needs Postgres on :5433 (scripts/pg-setup.sh)
dotnet test            # or: bash scripts/test.sh
```

Tests spin up an isolated database per test (`brewvio_test_<guid>`) and drop it on dispose, so
they never touch the dev database. 27 tests cover auth, registration/approval, orders, inventory,
reporting (periods + margins), and shifts.

### Frontend (Playwright E2E)

```bash
cd uitest
npm install            # first time
node uitest.mjs        # API/SPA must be running on :5000 with a seeded DB
```

The smoke test drives: role select → manager login → place order → receipt → dashboard →
inventory → menu performance → sign-up → manager approval → "Account approved!" → cashier login.
Screenshots land in `uitest/shots/`.

---

## 4. AWS deployment (SAM)

Architecture: **CloudFront** (one domain) → S3 (static `wwwroot/`) + API Gateway **HTTP API** →
**Lambda** (ASP.NET Core 8, arm64) → **Supabase Postgres** over SSL. No VPC/NAT/RDS Proxy needed.

The API runs on Lambda via `Amazon.Lambda.AspNetCoreServer.Hosting`
(`builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi)` in `Program.cs`). The SAM
function's `Handler: Brewvio` is the assembly name — the hosting package bootstraps the app.

### Prerequisites (not present in this dev environment — install before deploying)

- **AWS CLI v2** — <https://docs.aws.amazon.com/cli/latest/userguide/getting-started-install.html>
- **AWS SAM CLI** — <https://docs.aws.amazon.com/serverless-application-model/latest/developerguide/install-sam-cli.html>
- **.NET 8 SDK** + the Amazon.Lambda tools: `dotnet tool install -g Amazon.Lambda.Tools`
- Configure credentials: `aws configure`

### Build & deploy the API

First create the secrets in SSM Parameter Store (one time, see the SSM table in §1):

```bash
aws ssm put-parameter --name /brewvio/DATABASE_URL --type SecureString \
  --value 'postgres://postgres:<pass>@db.<ref>.supabase.co:6543/postgres'
aws ssm put-parameter --name /brewvio/JWT_KEY --type SecureString \
  --value '<production-signing-key-min-32-bytes>'
```

Then build and deploy. Secrets are no longer passed on the command line — the Lambda
reads them from SSM at startup:

```bash
sam build
sam deploy --guided
# Optional: override the SSM path prefix (default /brewvio)
#   --parameter-overrides SsmParameterPath=/brewvio
```

`sam deploy` outputs:
- `SiteUrl` — the public CloudFront URL (frontend + `/api` on one domain),
- `FrontendBucketName` — the S3 bucket to upload the SPA into,
- `ApiEndpoint` — the direct API Gateway URL (bypasses CloudFront).

### Publish the frontend

```bash
aws s3 sync src/Brewvio/wwwroot/ s3://<FrontendBucketName>/ --delete
# Invalidate the CDN cache so the new SPA is served immediately:
aws cloudfront create-invalidation --distribution-id <id> --paths "/*"
```

> The Lambda also serves `wwwroot/` (via `UseStaticFiles`), so the API works standalone for
> testing at `ApiEndpoint`. In production, CloudFront serves the static assets from S3 and only
> routes `/api/*` to the Lambda.

### Database migrations against Supabase

Run migrations with the **session-mode** pooler (port `5432`), then point the running app at the
**transaction-mode** pooler (port `6543`):

```bash
ConnectionStrings__Default='Host=db.<ref>.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=<pass>;SSL Mode=Require' \
  dotnet ef database update --project src/Brewvio
```

### Alternative: `dotnet lambda` (without SAM)

The project is also `dotnet lambda`-compatible (`AWSProjectType=Lambda`):

```bash
dotnet lambda deploy-function --project-location src/Brewvio
```

### Backup & disaster recovery

Supabase provides its own managed backups (cadence and point-in-time recovery depend on your
Supabase plan — confirm what your tier includes and document it alongside your RPO/RTO targets).
For an **independent** copy you control, `scripts/backup-db.sh` takes a compressed `pg_dump` and
uploads it to S3 with a timestamped key, pruning copies older than `RETENTION_DAYS` (default 30):

```bash
DATABASE_URL='postgres://postgres:<pass>@db.<ref>.supabase.co:5432/postgres' \
BACKUP_BUCKET='my-brewvio-backups' \
  bash scripts/backup-db.sh
```

> Use the **session-mode** pooler (port `5432`) for `pg_dump`, not the transaction pooler (`6543`).
> Run it on a schedule (cron, a GitHub Actions scheduled workflow, or an EventBridge-triggered
> runner). Restore with `gunzip -c brewvio-<stamp>.sql.gz | psql "$DATABASE_URL"`.

**Recommended targets to document for the team:**

| Objective | Suggested target | Basis |
|-----------|------------------|-------|
| RPO (max data loss) | ≤ 24h | Daily `backup-db.sh` + Supabase PITR if on a paid tier |
| RTO (max downtime) | ≤ 1h | Redeploy stack + restore latest dump |

---

## 5. Reference

- [Deploy ASP.NET Core Web API to Lambda (AWS docs)](https://docs.aws.amazon.com/lambda/latest/dg/csharp-package.html)
- [aws/aws-lambda-dotnet](https://github.com/aws/aws-lambda-dotnet) — hosting package + Lambda Test Tool
- [Supabase connection pooling (Supavisor)](https://supabase.com/docs/guides/database/connecting-to-postgres)
- API contract: see [`api-contract.md`](./api-contract.md).
