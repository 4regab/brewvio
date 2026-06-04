# Brewvio

An all-in-one Point-of-Sale (POS) and business management system for micro, small, and
medium-sized coffee shops. Brewvio streamlines daily operations by automating order
processing, inventory tracking, and sales reporting in a single web-based application.

> BSIT Free Elective 2 — Group 7, 2-1 — Polytechnic University of the Philippines, Quezon City

## Core Functionality

1. **Point-of-Sale & Transaction Management** — touch-friendly ordering, discounts, split
   payments, receipts, refunds, and shift management.
2. **Inventory & Stock Management** — automatic ingredient deduction per sale, recipes,
   low-stock thresholds, and alerts.
3. **Reporting & Analytics Dashboard** — sales metrics, menu performance, profitability, and
   PDF/CSV export.
4. **User Access & System Administration** — Manager/Cashier roles, self-service sign-up with
   manager approval, user management, and audit logging.

## Tech Stack

| Layer         | Technology                                              |
|---------------|---------------------------------------------------------|
| Frontend      | HTML5, CSS3, JavaScript, Bootstrap 5 (static SPA)       |
| Backend       | ASP.NET Core 8 (LTS) Web API — C# (OOP)                 |
| Database      | Supabase (managed PostgreSQL) via EF Core / Npgsql      |
| Hosting       | AWS: CloudFront + S3 (frontend), API Gateway + Lambda (API) |

## Architecture

Brewvio is a decoupled serverless app: a **static frontend** (HTML/CSS/JS) served from S3 via
CloudFront, and a **C# ASP.NET Core Web API** running on AWS Lambda behind API Gateway. The API
keeps a classic layered, OOP design (`Controllers → Services → Data`) and talks to a managed
**Supabase PostgreSQL** database through the built-in Supavisor connection pooler. A single
CloudFront distribution fronts both origins, so the frontend and `/api/*` share one domain
(no CORS).

All application access goes **through the API**, never directly from the browser to the
database. Authorization is enforced in the API layer with JWTs (deny-by-default; see
[Security](#security)). As defense-in-depth, Row Level Security locks down Supabase's
auto-generated PostgREST data API so the public API key can't reach the tables directly.

<img width="1195" height="801" alt="image" src="https://github.com/user-attachments/assets/0da1af68-3afd-429e-9cc9-8acc9b5b191c" />


## Project Structure

```
template.yaml                  AWS SAM infrastructure (Lambda + API Gateway + S3 + CloudFront)
src/Brewvio/
├── Program.cs                 App entry point (host, DI, Lambda hosting, middleware)
├── appsettings.json           Config (connection string, JWT issuer/audience, logging)
├── Properties/                launchSettings.json (local dev profile, http://localhost:5000)
├── Controllers/               Web API endpoints (JSON) — POS, inventory, reports, auth, health
├── Models/                    EF Core entities (Transaction, MenuItem, User, ...)
├── Dtos/                      API request/response contracts (LoginRequest, LoginResponse, ...)
├── Services/                  Application logic (Order, Inventory, Reporting, Auth, Audit, ...)
├── Data/                      BrewvioDbContext, design-time factory, seeder (EF Core / Npgsql)
├── Migrations/                EF Core migrations (InitialCreate ... EnableRowLevelSecurity)
├── Helpers/                   CurrentUser, PasswordHasher, ExportHelper
└── wwwroot/                   Static frontend (HTML/CSS/JS) deployed to S3 + CloudFront

tests/Brewvio.Tests/           xUnit service tests (Auth, User, Inventory, Order, Reporting, Shift)
```

The API runs in Lambda via `Amazon.Lambda.AspNetCoreServer.Hosting`; the static frontend in
`wwwroot/` is uploaded to S3 and served through CloudFront. Backend tests live in
`tests/Brewvio.Tests/` (xUnit against a real PostgreSQL, run with `dotnet test`).

## Getting Started

Prerequisites: [.NET 8 SDK](https://dotnet.microsoft.com/download) and a Supabase project
(free tier). Copy the Supabase connection string into `appsettings.json` or the `DATABASE_URL`
environment variable. Use the **Supavisor pooler**: transaction mode (port `6543`) for the
running app, session mode (port `5432`) for EF Core migrations.

```bash
# Restore & run locally
dotnet run --project src/Brewvio

# Database migrations (EF Core) — use the session-mode (:5432) connection string
dotnet tool install --global dotnet-ef        # once
dotnet ef migrations add InitialCreate --project src/Brewvio
dotnet ef database update --project src/Brewvio
```

### Deploy to AWS

Infrastructure is defined as code in [`template.yaml`](./template.yaml) (AWS SAM):

1. **API** — the ASP.NET Core Web API is packaged as a Lambda (`Amazon.Lambda.AspNetCoreServer.Hosting`,
   .NET 8 on arm64) and exposed through an **API Gateway HTTP API**. Secrets (`DATABASE_URL`,
   `JWT_KEY`) are read at startup from **SSM Parameter Store** under `/brewvio`, not env vars.
2. **Frontend** — `wwwroot/` is uploaded to a private **S3** bucket (Origin Access Control).
3. **CDN** — a single **CloudFront** distribution fronts both: default behavior → S3,
   `/api/*` → API Gateway, so frontend and API share one domain (no CORS).

Deploy with `sam build && sam deploy --guided`. The Lambda connects to Supabase over SSL, so it
needs no VPC, NAT, or RDS Proxy. Full steps — SSM secrets, frontend sync, migrations, backups —
are in [`DEPLOYMENT.md`](./DEPLOYMENT.md).

## Roles

- **Manager** — full access: POS, inventory, reports, and user management.
- **Cashier** — POS interface and own shift summary only.

New staff can **sign up** from the login screen (choosing a requested role). The account starts
as **Pending** and cannot sign in until a Manager **approves** it from the Users screen; the
sign-up's "Authenticating…" screen polls for the decision and advances to "Account approved!"
once activated. See [`DEPLOYMENT.md`](./DEPLOYMENT.md) for environment setup, local dev, tests,
and AWS deployment.

## Security

Authorization is enforced in the API. JWT bearer auth is **deny-by-default**: a global
fallback policy requires an authenticated user on every endpoint, so routes must explicitly
opt out with `[AllowAnonymous]` (login, sign-up) and Manager-only routes use
`[Authorize(Roles = "Manager")]`. Tokens are HMAC-signed with `JWT_KEY` (see `DEPLOYMENT.md`).

Because the database is hosted on Supabase, its tables are also reachable through Supabase's
auto-generated **PostgREST data API** using the public `anon` key — independently of our API.
To close that exposure, **Row Level Security (RLS)** is enabled on every application table in
the `public` schema, each with a single policy granting access only to the `authenticated`
Postgres role; the `anon`/public role matches no policy and is blocked entirely. This is
shipped as EF migration `EnableRowLevelSecurity`, which also revokes `EXECUTE` on the
`rls_auto_enable()` helper from the `anon`/`authenticated` roles.

> The API itself connects as the `postgres` role (which bypasses RLS), so these policies are
> defense-in-depth for the PostgREST surface and do **not** replace the API's JWT
> authorization. Run `dotnet ef database update` to apply them (see `DEPLOYMENT.md` §5).

## Team

| Name                      | Role                          |
|---------------------------|-------------------------------|
| Thea Zoe Paulo            | Business Analyst              |
| Aldrin M. Butihen         | Business Analyst              |
| Jean Yno Dagle            | Business Analyst              |
| Anne Reign M. San Antonio | Designer                      |
| Miguel Isaac D. Pambid    | Designer                      |
| James Gabriele N. Torzar  | Developer                     |
| Rayven M. Malaybay        | Developer                     |
| Charmie V. Frianeza       | Developer & Documentation Lead|
