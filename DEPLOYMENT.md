# Brewvio — Environment, Local Dev & Deployment

This guide covers running Brewvio locally, the environment variables it reads, the test setup,
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

### Tests

| Variable | Purpose | Default |
|----------|---------|---------|
| `BREWVIO_TEST_PG` | Base Npgsql connection string for the test database | `Host=localhost;Port=5433;Username=postgres;Password=postgres` |

### Lambda secrets (SSM Parameter Store)

The Lambda function reads its secrets at startup from SSM Parameter Store under `/brewvio`.
These are created once, out-of-band — CloudFormation cannot manage `SecureString` parameters.
**You never pass these as Lambda environment variables or deploy-time arguments.**

| SSM parameter | Purpose |
|---------------|---------|
| `/brewvio/DATABASE_URL` | Supabase **transaction pooler** URI, port `6543` |
| `/brewvio/JWT_KEY` | Production JWT signing key (≥ 32 bytes) |

To create or update them:

```bash
aws ssm put-parameter --name /brewvio/DATABASE_URL --type SecureString \
  --value 'postgres://postgres:<pass>@db.<ref>.supabase.co:6543/postgres'

aws ssm put-parameter --name /brewvio/JWT_KEY --type SecureString \
  --value '<production-signing-key-min-32-bytes>'
```

Use `--overwrite` to update an existing parameter. Once set, you never touch them again for
normal redeployments — the Lambda picks them up automatically on each cold start.

> The Lambda's IAM role has least-privilege `ssm:GetParametersByPath` / `GetParameter` on
> `/brewvio/*` plus `kms:Decrypt` scoped to the SSM service. This is wired up in `template.yaml`.

---

## 2. Local development

### 2a. Start PostgreSQL (port 5433)

The app and tests expect PostgreSQL on port `5433` with a `postgres`/`postgres` superuser.
The quickest way is Docker:

```bash
docker run -d --name brewvio-pg -p 5433:5432 \
  -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=brewvio postgres:16
```

Any existing local Postgres works too — just point `ConnectionStrings__Default` /
`BREWVIO_TEST_PG` at it (see §1).

### 2b. Apply EF Core migrations

```bash
dotnet ef database update --project src
```

This includes the `EnableRowLevelSecurity` migration (see §5). On a plain local Postgres the
Supabase-only role statements are skipped — safe no-op.

### 2c. Run the API + SPA

```bash
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

```bash
# from repo root — needs Postgres on :5433 (see §2a)
dotnet test tests/

# or from the tests folder
cd tests && dotnet test
```

Tests use a shared database per test class with per-test transaction rollbacks — fast (~24s for
64 tests) and fully isolated. They never touch the dev database. Override the server with
`BREWVIO_TEST_PG` (see §1).

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
**Lambda** (ASP.NET Core 8, arm64) → **Supabase Postgres** over SSL. No VPC/NAT/RDS Proxy needed.

### Prerequisites

- **AWS CLI v2** — <https://docs.aws.amazon.com/cli/latest/userguide/getting-started-install.html>
- **.NET 8 SDK** + Amazon Lambda tools: `dotnet tool install -g Amazon.Lambda.Tools`
- Configured credentials: `aws configure`

### First-time setup: create SSM secrets (once only)

Before the first deploy, create the two SSM parameters (see §1). After that, redeployments
**do not require touching SSM** — the Lambda reads them at cold start automatically.

### Deploy the API

```bash
dotnet lambda deploy-serverless \
  --template template.yaml \
  --stack-name brewvio \
  --s3-bucket <your-sam-artifact-bucket> \
  --region ap-southeast-2
```

No secrets or keys are passed here. The deployed Lambda reads `/brewvio/DATABASE_URL` and
`/brewvio/JWT_KEY` from SSM at startup. To use a different SSM path prefix:

```bash
# add to the command above:
--template-parameters SsmParameterPath=/my-path
```

Outputs after deploy:
- `SiteUrl` — public CloudFront URL (frontend + `/api` on one domain)
- `FrontendBucketName` — S3 bucket to upload the SPA into
- `ApiEndpoint` — direct API Gateway URL (bypasses CloudFront, useful for testing)

### Publish the frontend

```bash
aws s3 sync src/wwwroot/ s3://<FrontendBucketName>/ --delete

# Invalidate CloudFront cache so changes are served immediately:
aws cloudfront create-invalidation --distribution-id <id> --paths "/*"
```

### Database migrations against Supabase

Use the **session-mode** pooler (port `5432`) for migrations, not the transaction pooler:

```bash
ConnectionStrings__Default='Host=db.<ref>.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=<pass>;SSL Mode=Require' \
  dotnet ef database update --project src
```

### Backup & disaster recovery

```bash
DATABASE_URL='postgres://postgres:<pass>@db.<ref>.supabase.co:5432/postgres'
pg_dump "$DATABASE_URL" --no-owner --no-privileges --format=plain \
  | gzip -9 > "brewvio-$(date -u +%Y%m%dT%H%M%SZ).sql.gz"
aws s3 cp brewvio-*.sql.gz s3://my-brewvio-backups/
```

> Use the session-mode pooler (port `5432`) for `pg_dump`. Restore with:
> `gunzip -c brewvio-<stamp>.sql.gz | psql "$DATABASE_URL"`

| Objective | Suggested target |
|-----------|-----------------|
| RPO (max data loss) | ≤ 24h |
| RTO (max downtime) | ≤ 1h |

---

## 5. Database security (Row Level Security)

The `EnableRowLevelSecurity` EF migration locks down Supabase's auto-generated PostgREST API:

- Enables RLS on all 11 application tables
- Adds an `authenticated_all_access` policy (`FOR ALL TO authenticated`) — the `anon` role is
  blocked entirely
- Revokes `EXECUTE` on `public.rls_auto_enable()` from `anon`/`authenticated`

The Brewvio API connects as the `postgres` role (`BYPASSRLS`), so these policies don't affect
the app — they're defense-in-depth for the PostgREST surface only.

Apply like any other migration (session-mode pooler, port `5432`):

```bash
ConnectionStrings__Default='Host=db.<ref>.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=<pass>;SSL Mode=Require' \
  dotnet ef database update --project src
```

---

## 6. Reference

- [Deploy ASP.NET Core Web API to Lambda](https://docs.aws.amazon.com/lambda/latest/dg/csharp-package.html)
- [aws/aws-lambda-dotnet](https://github.com/aws/aws-lambda-dotnet)
- [Supabase connection pooling](https://supabase.com/docs/guides/database/connecting-to-postgres)
- [Supabase Row Level Security](https://supabase.com/docs/guides/database/postgres/row-level-security)
