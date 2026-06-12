# Chao & Brew

**All-in-one POS and business management system for coffee shops and canteens.**

This POS handles ordering, inventory, reporting, and staff management in a single web app — built for small F&B operations running on tight margins and tighter budgets.

> BSIT Free Elective 2 — Group 7, 2-1 · Polytechnic University of the Philippines, Quezon City

---

## What it does

| Feature | Description |
|---|---|
| **POS** | Touch-friendly order screen, modifiers, discounts, cash/GCash payment, draft orders, receipt printing |
| **Order queue** | Real-time order status (Preparing → Completed), refunds, history |
| **Inventory** | Auto stock deduction per sale via recipes, low-stock alerts, manual adjustments |
| **Reports** | Daily/weekly/monthly/yearly sales, best sellers, category breakdown, profit margin, XLSX/PDF export |
| **Staff management** | Manager/Cashier roles, self-service sign-up with manager approval, audit trail |

---
<img width="800" alt="brewio drawio" src="https://github.com/user-attachments/assets/9c3f5f18-4afd-4ed9-b7a9-1039a92cf6c2" />
                 
## Tech stack

| Layer | Technology |
|---|---|
| Frontend | Vanilla JS SPA · Bootstrap 5 · served from S3 + CloudFront |
| Backend | ASP.NET Core 10 · C# · AWS Lambda + API Gateway |
| Database | Supabase Postgres · EF Core 10 · Npgsql |
| Auth | JWT (HMAC-SHA256) · PBKDF2-SHA256 passwords · token revocation |
| Infrastructure | AWS SAM · CloudFront · S3 · SSM Parameter Store · CloudTrail |

---

## Getting started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Docker Desktop (for the local Postgres container)

### Run locally

```bash
# Start local Postgres
docker run -d --name brewvio-pg \
  -e POSTGRES_PASSWORD=postgres \
  -p 5433:5432 postgres:16

# Apply migrations
dotnet ef database update --project src

# Start the API (auto-seeds demo data on first run)
dotnet run --project src
# → http://localhost:5000
```

**Demo accounts**

| Username | Password | Role |
|---|---|---|
| `manager` | `Manager@123` | Manager |
| `cashier` | `Cashier@123` | Cashier |

### Run tests

```bash
docker start brewvio-pg   # must be running for the integration suite
dotnet test tests/Brewvio.Tests.csproj
```

277 tests across integration and unit suites — auth, orders, inventory, menu, reporting, settings, discount logic, password hashing, line-item building, and input validation.

---

## Deploy to AWS

Deployments are automated via GitHub Actions — push to `master` and the pipeline runs tests, applies EF migrations, deploys the Lambda, syncs the frontend to S3, and invalidates CloudFront. No AWS keys are stored; the workflow uses OIDC (short-lived tokens scoped to this repo).

See [`.github/workflows/deploy.yml`](.github/workflows/deploy.yml) for the full pipeline and [`docs/DEPLOYMENT.md`](./docs/DEPLOYMENT.md) for manual deploy steps, first-time setup, and SSM secrets.

### GitHub Actions secrets required

| Secret | Value |
|---|---|
| `AWS_DEPLOY_ROLE_ARN` | IAM role ARN from `infra/github-oidc-role.yaml` stack |
| `MIGRATION_CONNECTION_STRING` | Supabase session pooler (port 5432) for EF migrations |
| `FRONTEND_BUCKET` | S3 bucket name (CloudFormation output `FrontendBucketName`) |
| `CLOUDFRONT_DISTRIBUTION_ID` | CloudFront distribution ID (CloudFormation output) |

---

## Cost estimate

Running at ~100 orders/day with 1–2 users on AWS (ap-southeast-2). Almost entirely within the AWS free tier.

| Service | Monthly cost |
|---|---|
| AWS Lambda (arm64, 1769 MB, ~21K invocations) | $0.00 |
| Amazon API Gateway (HTTP API, ~21K requests) | $0.00 *(free tier 12 mo, ~$0.02 after)* |
| Amazon S3 (0.5 GB static frontend) | $0.00 |
| Amazon CloudFront (~21K requests, ~0.3 GB out) | $0.00 |
| Amazon CloudWatch (logs, 90-day retention) | $0.00 |
| **Total** | **~$0.00 – $0.02/month** |

Full estimate: [calculator.aws](https://calculator.aws/#/estimate?id=b59a73ed8447219c361238a7d826057c5e454171)

---

## Project structure

```
src/
├── Controllers/     API routes (Auth, Orders, Menu, Inventory, Users, Settings, Reports, Audit, Health)
├── Services/        All business logic
├── Models/          EF Core entities
├── Dtos/            Request / response records
├── Data/            DbContext, design-time factory, seeder
├── Helpers/         JWT claims, password hashing, CSV/PDF export, NoHtml validation
├── Migrations/      EF Core migrations
└── wwwroot/         Static frontend (uploaded to S3)

tests/               xUnit integration tests (real Postgres, per-test transaction rollback)
tests/Unit/          Pure unit tests (in-memory DB, no Postgres required)
infra/
├── template.yaml    AWS SAM infrastructure definition (Lambda, API GW, S3, CloudFront, security headers)
├── github-oidc-role.yaml   GitHub Actions OIDC deploy role
└── cloudtrail.yaml  CloudTrail audit trail for management-plane events
docs/
├── DOCS.md          Full developer reference — API docs, models, services, deployment
└── DEPLOYMENT.md    Step-by-step deployment and ops guide
.github/workflows/   CI/CD pipeline (test → migrate → deploy on push to master)
```

---

## Roles

- **Manager** — full access to every feature including reports, user management, and settings
- **Cashier** — POS, order queue, and own drafts

New staff register from the login page (Pending status). A Manager approves them from the Users screen before they can sign in.

---

## Docs

Full API reference, data models, service descriptions, and infrastructure details are in **[docs/DOCS.md](./docs/DOCS.md)**.

---
