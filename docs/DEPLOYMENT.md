# Chao & Brew — Environment, Local Dev & Deployment

This guide covers running Chao & Brew locally, the environment variables it reads, the test setup,
and deploying to AWS (Lambda + S3 + CloudFront).

---

## 1. Environment variables

### API (local only)

These are only needed when running locally. On Lambda, secrets come from SSM Parameter Store
automatically — you do **not** set them as environment variables on the function.

| Variable | Purpose | Example |
|----------|---------|---------|
| `ConnectionStrings__Default` | Npgsql connection string for local dev/tests | `Host=localhost;Port=5433;Database=brewvio;Username=postgres;Password=postgres` |
| `JWT_KEY` | HMAC signing key for JWTs (**≥ 32 bytes**). In `Development` you can use `Jwt:Key` in `appsettings.Development.json` instead | `brewvio-local-dev-signing-key-min-32-bytes!!` |
| `ASPNETCORE_ENVIRONMENT` | Set to `Development` locally; leave unset on Lambda | `Development` |
| `ASPNETCORE_URLS` | Kestrel bind address (local only; ignored on Lambda) | `http://localhost:5000` |

> `DATABASE_URL` (Supabase pooler URI) is only used on Lambda and is read from SSM — never set it locally unless you want to test against Supabase directly.
>
> `ORIGIN_VERIFY` is only used on Lambda (read from SSM). Leave it unset locally — the origin-lock middleware is skipped when it is absent, so dev/test behave normally.

### Tests

| Variable | Purpose | Default |
|----------|---------|---------|
| `BREWVIO_TEST_PG` | Base Npgsql connection string for the test database | `Host=localhost;Port=5433;Username=postgres;Password=postgres` |

### Lambda secrets (SSM Parameter Store)

The Lambda function reads its secrets at startup from SSM Parameter Store under `/brewvio`.
These are created once, out-of-band — CloudFormation cannot manage `SecureString` parameters.
**You never pass these as Lambda environment variables or deploy-time arguments.**

| SSM parameter | Type | Purpose |
|---------------|------|---------|
| `/brewvio/DATABASE_URL` | SecureString | Supabase **transaction pooler** URI, port `6543` |
| `/brewvio/JWT_KEY` | SecureString | Production JWT signing key (≥ 32 bytes) |
| `/brewvio/ORIGIN_VERIFY` | String | Origin-lock shared secret. CloudFront injects it as the `X-Origin-Verify` header on requests to the API origin; the Lambda rejects requests that lack it. Must be a plaintext **String** (not SecureString) because CloudFront's `{{resolve:ssm:...}}` dynamic reference cannot read SecureString values. |

To create or update them:

```powershell
aws ssm put-parameter --name /brewvio/DATABASE_URL --type SecureString --value 'postgres://postgres:<pass>@db.<ref>.supabase.co:6543/postgres'

aws ssm put-parameter --name /brewvio/JWT_KEY --type SecureString --value '<production-signing-key-min-32-bytes>'

aws ssm put-parameter --name /brewvio/ORIGIN_VERIFY --type String --value '<a-long-random-shared-secret>'
```

Use `--overwrite` to update an existing parameter. Once set, you never touch them again for
normal redeployments — the Lambda picks them up automatically on each cold start.

> The Lambda's IAM role has least-privilege `ssm:GetParametersByPath` / `GetParameter` on
> `/brewvio/*` plus `kms:Decrypt` scoped to the SSM service. This is wired up in `infra/template.yaml`.

---

## 2. Local development

### 2a. Start PostgreSQL (port 5433)

The app and tests expect PostgreSQL on port `5433` with a `postgres`/`postgres` superuser.
The quickest way is Docker:

```powershell
docker run -d --name brewvio-pg -p 5433:5432 -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=brewvio postgres:16
```

Any existing local Postgres works too — just point `ConnectionStrings__Default` /
`BREWVIO_TEST_PG` at it (see §1).

### 2b. Apply EF Core migrations

```powershell
dotnet ef database update --project src
```

This includes the `EnableRowLevelSecurity` migration (see §5). On a plain local Postgres the
Supabase-only role statements are skipped — safe no-op.

### 2c. Run the API + SPA

```powershell
dotnet run --project src
```

Serves the SPA from `wwwroot/` and the API under `/api/*` on `http://localhost:5000`.
On first run it seeds demo data. The JWT signing key comes from `appsettings.Development.json`
(`Jwt:Key`). Check `GET /api/health` for a liveness + DB-connectivity probe.

### Demo accounts

| Username | Password | Role | Status |
|----------|----------|------|--------|
| `manager` | `Manager@123` | Manager | Active |
| `cashier` | `Cashier@123` | Cashier | Active |
| `newcashier` | `Pending@123` | Cashier | Pending (in the approval queue) |

---

## 3. Tests

### Backend (xUnit, real Postgres)

```powershell
# from repo root — needs Postgres on :5433 (see §2a)
dotnet test tests/

# or from the tests folder
cd tests; dotnet test
```

Integration tests use a shared database per test class with per-test transaction rollbacks — fast
and fully isolated. They never touch the dev database. Override the server with
`BREWVIO_TEST_PG` (see §1). The `tests/Unit/` suite runs against EF's in-memory provider (or pure
in-process logic) and needs no Postgres at all.

> The test harness provisions schema with `EnsureCreated()` rather than running migrations, so
> the `EnableRowLevelSecurity` migration (§5) is not exercised here. RLS is validated directly
> against Supabase (anon blocked / authenticated allowed), not by the xUnit suite.

### Manual smoke test

After `dotnet run`, exercise the happy path: role select → manager login → place an order →
receipt → dashboard → inventory → menu performance → sign-up → manager approval →
"Account approved!" → cashier login.

---

## 4. AWS deployment

Architecture: **CloudFront** (one domain) → S3 (static `wwwroot/`) + API Gateway **HTTP API** →
**Lambda** (ASP.NET Core 10, arm64) → **Supabase Postgres** over SSL. No VPC/NAT/RDS Proxy needed.
CloudFront also applies the security-headers policy and injects the `X-Origin-Verify` origin-lock
secret, so the Lambda only trusts traffic that comes through CloudFront.

### 4a. Automated deploys (GitHub Actions — primary path)

Push to `master` and the pipeline handles everything:

1. Runs the full test suite against a Postgres service container
2. Applies EF Core migrations to Supabase (session pooler, port 5432)
3. Deploys the Lambda + infrastructure via `dotnet lambda deploy-serverless --template infra/template.yaml`
4. Syncs `src/wwwroot/` to S3 with correct cache headers
5. Invalidates CloudFront

Authentication uses **OIDC** — no long-lived AWS keys are stored. GitHub mints a short-lived
token per run, scoped to this repo and branch only.

#### First-time GitHub Actions setup (once only)

**1. Deploy the OIDC IAM role:**

```powershell
aws cloudformation deploy `
  --template-file infra/github-oidc-role.yaml `
  --stack-name brewvio-github-oidc `
  --capabilities CAPABILITY_NAMED_IAM `
  --parameter-overrides GitHubOrg=<your-github-username> `
  --region ap-southeast-2
```

**2. Get the role ARN:**

```powershell
aws cloudformation describe-stacks `
  --stack-name brewvio-github-oidc `
  --query "Stacks[0].Outputs" `
  --region ap-southeast-2
```

**3. Add these GitHub repository secrets** (Settings → Secrets and variables → Actions):

| Secret | Value |
|--------|-------|
| `AWS_DEPLOY_ROLE_ARN` | ARN from step 2 |
| `MIGRATION_CONNECTION_STRING` | Supabase session pooler (port 5432) for EF migrations |
| `FRONTEND_BUCKET` | `brewvio-frontendbucket-15gnfq4gkjf0` |
| `CLOUDFRONT_DISTRIBUTION_ID` | `E1CWDAT3NI1LSD` |

After that, every push to `master` deploys automatically.

### 4b. Manual deploy (fallback)

Use this if you need to deploy outside of GitHub Actions.

#### Prerequisites

- **AWS CLI v2** — <https://docs.aws.amazon.com/cli/latest/userguide/getting-started-install.html>
- **.NET 10 SDK** + Amazon Lambda tools: `dotnet tool install -g Amazon.Lambda.Tools`
- Configured credentials: `aws configure`

#### First-time setup: create SSM secrets (once only)

Before the first deploy, create the three SSM parameters (see §1). After that, redeployments
**do not require touching SSM** — the Lambda reads them at cold start automatically.

#### Deploy the API

Run from the **repo root** (the template lives in `infra/`):

```powershell
dotnet lambda deploy-serverless `
  --template infra/template.yaml `
  --stack-name brewvio `
  --s3-bucket aws-sam-cli-managed-default-samclisourcebucket-xczut1dcayng `
  --region ap-southeast-2
```

No secrets or keys are passed here. The deployed Lambda reads `/brewvio/DATABASE_URL`,
`/brewvio/JWT_KEY`, and `/brewvio/ORIGIN_VERIFY` from SSM at startup. To use a different SSM path
prefix:

```powershell
# add to the command above:
--template-parameters SsmParameterPath=/my-path
```

Outputs after deploy:
- `SiteUrl` — public CloudFront URL (frontend + `/api` on one domain)
- `FrontendBucketName` — S3 bucket to upload the SPA into
- `ApiEndpoint` — direct API Gateway URL (bypasses CloudFront; will 403 without `X-Origin-Verify`)

#### Publish the frontend

```powershell
# JS/CSS/img: long-lived cache (files are content-hashed/versioned)
aws s3 sync src/wwwroot/ s3://brewvio-frontendbucket-15gnfq4gkjf0/ `
  --delete `
  --cache-control "max-age=31536000, immutable" `
  --exclude "index.html"

# index.html: never cached
aws s3 cp src/wwwroot/index.html s3://brewvio-frontendbucket-15gnfq4gkjf0/index.html `
  --cache-control "no-store, must-revalidate"

# Invalidate CloudFront cache so changes are served immediately
aws cloudfront create-invalidation --distribution-id E1CWDAT3NI1LSD --paths "/*"
```

---

## 5. Reference

- [Deploy ASP.NET Core Web API to Lambda](https://docs.aws.amazon.com/lambda/latest/dg/csharp-package.html)
- [aws/aws-lambda-dotnet](https://github.com/aws/aws-lambda-dotnet)
- [Supabase connection pooling](https://supabase.com/docs/guides/database/connecting-to-postgres)
- [Supabase Row Level Security](https://supabase.com/docs/guides/database/postgres/row-level-security)
