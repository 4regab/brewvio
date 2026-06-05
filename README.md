# Chao & Brew

**All-in-one POS and business management system for coffee shops and canteens.**

Chao & Brew handles ordering, inventory, reporting, and staff management in a single web app — built for small F&B operations running on tight margins and tighter budgets.

> BSIT Free Elective 2 — Group 7, 2-1 · Polytechnic University of the Philippines, Quezon City

---

## What it does

| Feature | Description |
|---|---|
| **POS** | Touch-friendly order screen, modifiers, discounts, cash/GCash payment, draft orders, receipt printing |
| **Order queue** | Real-time order status (Preparing → Completed), refunds, history |
| **Inventory** | Auto stock deduction per sale via recipes, low-stock alerts, manual adjustments |
| **Reports** | Daily/weekly/monthly/yearly sales, best sellers, category breakdown, profit margin, CSV/PDF export |
| **Staff management** | Manager/Cashier roles, self-service sign-up with manager approval, audit trail |

---

## Tech stack

| Layer | Technology |
|---|---|
| Frontend | Vanilla JS SPA · Bootstrap 5 · served from S3 + CloudFront |
| Backend | ASP.NET Core 10 · C# · AWS Lambda + API Gateway |
| Database | Supabase Postgres · EF Core 10 · Npgsql |
| Auth | JWT (HMAC-SHA256) · PBKDF2-SHA512 passwords |
| Infrastructure | AWS SAM · CloudFront · S3 · SSM Parameter Store |

---

## Architecture

```
Browser → CloudFront
            ├── /api/*  → API Gateway → Lambda (ASP.NET Core)
            └── /*      → S3 (static JS/CSS/HTML)
                                ↓
                        Supabase PostgreSQL
```

One CloudFront domain serves both the frontend and the API — no CORS. The Lambda connects to Supabase via the Supavisor transaction pooler. Secrets live in SSM Parameter Store, not in environment variables.

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
docker start brewvio-pg   # must be running
dotnet test tests/Brewvio.Tests.csproj
```

187 tests across services — auth, orders, inventory, menu, reporting, settings, and discount logic.

---

## Deploy to AWS

```bash
# First time — creates samconfig.toml
sam build && sam deploy --guided

# Subsequent deploys
sam build && sam deploy

# Sync frontend to S3 after JS/CSS changes
aws s3 sync src/wwwroot/ s3://<bucket> --delete
aws cloudfront create-invalidation --distribution-id <id> --paths "/*"
```

Secrets (`DATABASE_URL`, `JWT_KEY`) are stored in SSM Parameter Store as SecureString under `/brewvio`. See [`DEPLOYMENT.md`](./DEPLOYMENT.md) for full setup.

---

## Project structure

```
src/
├── Controllers/     API routes (Auth, Orders, Menu, Inventory, Users, Settings, Reports, Audit)
├── Services/        All business logic
├── Models/          EF Core entities
├── Dtos/            Request / response records
├── Data/            DbContext + seeder
├── Helpers/         JWT claims, password hashing, CSV/PDF export
├── Migrations/      EF Core migrations
└── wwwroot/         Static frontend (uploaded to S3)

tests/               xUnit integration tests (real Postgres, per-test transaction rollback)
docs/DOCS.md         Full developer reference — API docs, models, services, deployment
template.yaml        AWS SAM infrastructure definition
DEPLOYMENT.md        Step-by-step deployment and ops guide
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

## Team

| Name | Role |
|---|---|
| James Gabriele N. Torzar | Lead Developer |
| Charmie V. Frianeza | Developer & Documentation Lead |
| Thea Zoe Paulo | Business Analyst |
| Aldrin M. Butihen | Business Analyst |
| Jean Yno Dagle | Business Analyst |
| Anne Reign M. San Antonio | Designer |
| Miguel Isaac D. Pambid | Designer |
| Rayven M. Malaybay | Developer |
