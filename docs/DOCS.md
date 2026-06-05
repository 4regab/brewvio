# Chao & Brew — Developer Documentation

> **Stack:** ASP.NET Core 10 · Entity Framework Core 10 · PostgreSQL (Supabase) · AWS Lambda + API Gateway · CloudFront + S3 · Vanilla JS SPA

---

## Table of Contents

1. [Project Structure](#1-project-structure)
2. [Architecture Overview](#2-architecture-overview)
3. [Local Development](#3-local-development)
4. [Environment Variables](#4-environment-variables)
5. [Database](#5-database)
6. [Authentication & Authorization](#6-authentication--authorization)
7. [API Reference](#7-api-reference)
   - [Auth](#71-auth)
   - [Orders (POS)](#72-orders-pos)
   - [Menu](#73-menu)
   - [Inventory](#74-inventory)
   - [Users](#75-users)
   - [Settings](#76-settings)
   - [Reports](#77-reports)
   - [Audit](#78-audit)
   - [Health](#79-health)
8. [Data Models](#8-data-models)
9. [Services](#9-services)
10. [Frontend](#10-frontend)
11. [Testing](#11-testing)
12. [Deployment](#12-deployment)

---

## 1. Project Structure

```
brewvio/
├── src/                          # ASP.NET Core web application (also the Lambda handler)
│   ├── Controllers/              # HTTP route handlers (thin — delegate to Services)
│   │   ├── AuthController.cs
│   │   ├── OrdersController.cs
│   │   ├── MenuController.cs
│   │   ├── InventoryController.cs
│   │   ├── UsersController.cs
│   │   ├── SettingsController.cs
│   │   ├── ReportsController.cs
│   │   ├── AuditController.cs
│   │   └── HealthController.cs
│   ├── Data/
│   │   ├── BrewvioDbContext.cs    # EF Core DbContext — all DbSets, model config, precision, FKs
│   │   └── DatabaseInitializer.cs# Startup seeder + --seed-sales / --force-seed-sales CLI modes
│   ├── Dtos/                     # Request/response record types (no business logic)
│   │   ├── AuthDtos.cs
│   │   ├── UserDtos.cs
│   │   ├── MenuDtos.cs
│   │   ├── OrderDtos.cs
│   │   ├── InventoryDtos.cs
│   │   ├── ReportDtos.cs
│   │   ├── SettingsDtos.cs
│   │   └── AuditDtos.cs
│   ├── Helpers/
│   │   ├── CurrentUser.cs        # Resolves JWT claims → (Id, Username, Role)
│   │   ├── ExportHelper.cs       # CSV (ClosedXML), PDF (QuestPDF) generation
│   │   └── PasswordHasher.cs     # PBKDF2-SHA512, 600k iterations, upgrade-on-login
│   ├── Migrations/               # EF Core migration files (do not hand-edit)
│   ├── Models/                   # Domain entities
│   │   ├── AuditLog.cs
│   │   ├── Discount.cs           # Abstract + FixedAmountDiscount + PercentDiscount
│   │   ├── Ingredient.cs
│   │   ├── MenuItem.cs
│   │   ├── Modifier.cs
│   │   ├── Payment.cs
│   │   ├── RecipeIngredient.cs
│   │   ├── Roles.cs              # static string constants
│   │   ├── Transaction.cs
│   │   ├── TransactionItem.cs
│   │   ├── User.cs
│   │   └── UserStatus.cs         # static string constants
│   ├── Services/                 # All business logic lives here
│   │   ├── AuthService.cs
│   │   ├── AuditService.cs
│   │   ├── CurrentUser.cs
│   │   ├── InventoryService.cs
│   │   ├── MenuService.cs
│   │   ├── OrderService.cs
│   │   ├── ReportingService.cs
│   │   ├── SettingsService.cs
│   │   └── UserService.cs
│   ├── wwwroot/                  # Static frontend (uploaded to S3)
│   │   ├── index.html            # SPA shell — loads all scripts, Bootstrap, Bootstrap Icons
│   │   ├── css/
│   │   │   └── app.css           # All custom styles; design tokens as CSS vars
│   │   ├── js/
│   │   │   ├── api.js            # Api helper — JWT attach, JSON parse, error surface
│   │   │   ├── ui.js             # Shared UI kit — el(), modal(), toast(), charts
│   │   │   ├── app.js            # Hash router, auth gate, nav, clock, App.store
│   │   │   ├── pos.js            # Views.pos (POS screen) + Views.activity (Orders page)
│   │   │   ├── manage.js         # Views.inventory, Views.menu, Views.users, Views.settings
│   │   │   ├── reports.js        # Views.reports / Views.performance
│   │   │   └── auth.js           # Views.login, Views.register, Views.waiting
│   │   └── img/                  # Menu item photos, organised by category
│   ├── appsettings.json          # Default connection string (localhost:5433)
│   ├── appsettings.Development.json
│   ├── appsettings.Development.local.json  # Gitignored — real Supabase URL + JWT key
│   ├── Program.cs                # DI wiring, middleware, auth, Lambda hosting
│   └── Brewvio.csproj
├── tests/
│   ├── AuthServiceTests.cs
│   ├── AuditServiceTests.cs
│   ├── DiscountTests.cs
│   ├── InventoryServiceTests.cs
│   ├── InventoryServiceExtendedTests.cs
│   ├── MenuServiceTests.cs
│   ├── MenuServiceExtendedTests.cs
│   ├── OrderServiceTests.cs
│   ├── OrderServiceExtendedTests.cs
│   ├── ReportingServiceTests.cs
│   ├── ReportingServiceExtendedTests.cs
│   ├── SettingsServiceTests.cs
│   ├── SettingsServiceExtendedTests.cs
│   ├── UserServiceTests.cs
│   ├── UserServiceExtendedTests.cs
│   ├── TestSupport.cs            # SharedTestDb, TestScope, TestDb, TestSupport helpers
│   └── Brewvio.Tests.csproj
├── docs/
│   └── README.md                 # ← this file
├── template.yaml                 # AWS SAM — Lambda, API Gateway, S3, CloudFront
└── samconfig.toml                # SAM deploy defaults (generated after first guided deploy)
```

---

## 2. Architecture Overview

```
Browser
  │  HTTPS
  ▼
CloudFront Distribution (d37i8pbdtw6xf4.cloudfront.net)
  ├── /api/*  → API Gateway HTTP API  → Lambda (ASP.NET Core)  → Supabase Postgres
  └── /*      → S3 Bucket (static frontend — index.html + JS/CSS/img)
```

- **Lambda** runs the entire ASP.NET Core app via `Amazon.Lambda.AspNetCoreServer.Hosting`. Cold starts are ~1–3 s on arm64 / 1769 MB (2 vCPUs allocated).
- **Supabase** provides managed Postgres. The app connects through the **Supavisor transaction pooler** (port 6543) with `MaxAutoPrepare=0` and client-side connection pooling disabled (connections go stale in frozen Lambda processes).
- **Optimistic concurrency** on `Ingredient.StockLevel` uses PostgreSQL's `xmin` system column so concurrent orders can't silently oversell stock.
- **Secrets** (`DATABASE_URL`, `JWT_KEY`) are stored in **AWS SSM Parameter Store** as SecureString and loaded at Lambda cold-start via `Amazon.Extensions.Configuration.SystemsManager`.

---

## 3. Local Development

### Prerequisites

- .NET 10 SDK
- Docker Desktop (for the local Postgres container)

### Start the database

```bash
docker run -d \
  --name brewvio-pg \
  -e POSTGRES_PASSWORD=postgres \
  -p 5433:5432 \
  postgres:16
```

### Configure secrets

Create `src/appsettings.Development.local.json` (gitignored):

```json
{
  "DATABASE_URL": "postgresql://postgres.xxxx:password@host:6543/postgres",
  "JWT_KEY": "your-signing-key-minimum-32-bytes"
}
```

Or leave it absent to use the local Docker container via the default `ConnectionStrings.Default` in `appsettings.json`.

### Run migrations

```bash
dotnet ef database update --project src/Brewvio.csproj
```

### Start the API

```bash
dotnet run --project src/Brewvio.csproj
# Serves on http://localhost:5000
```

The app auto-seeds demo data on first run when the Users table is empty.

### Seed sales history (optional)

```bash
# Seed 3 months of demo sales into the connected DB, then exit
dotnet run --project src/Brewvio.csproj -- --seed-sales

# Force re-seed (wipes existing transactions first)
dotnet run --project src/Brewvio.csproj -- --force-seed-sales
```

### Demo accounts

| Username | Password | Role |
|---|---|---|
| `manager` | `Manager@123` | Manager |
| `cashier` | `Cashier@123` | Cashier |

---

## 4. Environment Variables

| Variable | Where set | Description |
|---|---|---|
| `DATABASE_URL` | SSM / local JSON | Supabase pooler URL: `postgresql://user:pass@host:port/db` |
| `JWT_KEY` | SSM / local JSON | HMAC-SHA256 signing key, minimum 32 bytes |
| `SSM_PARAMETER_PATH` | Lambda env var | SSM path prefix, default `/brewvio` |
| `BREWVIO_TEST_PG` | Shell / CI | Override test Postgres, default `Host=localhost;Port=5433;...` |
| `ASPNETCORE_ENVIRONMENT` | Shell | `Development` enables `appsettings.Development*.json` |

---

## 5. Database

### Schema summary

| Table | Purpose |
|---|---|
| `Users` | Accounts; unique `Username` index; status lifecycle: Pending → Active / Rejected |
| `Ingredients` | Stock items with `xmin` optimistic concurrency token |
| `MenuItems` | Sellable items; `IsActive` soft-delete |
| `RecipeIngredients` | Many-to-many: MenuItem ↔ Ingredient with `Quantity` |
| `Modifiers` | Optional add-ons with `PriceDelta`; `IsActive` soft-delete |
| `Transactions` | Sale records; status: Draft → Preparing → Completed / Refunded / Cancelled |
| `TransactionItems` | Line-item snapshot (name/price captured at sale time) |
| `Payments` | Individual tenders (Cash / GCash); multiple rows = split payment |
| `AuditLogs` | Immutable append-only action log |
| `AppSettings` | Key/value store for store config (`TaxRatePercent`, `StoreName`, etc.) |

### Decimal precision

| Context | Precision |
|---|---|
| Money (prices, totals) | `(12, 2)` |
| Quantities (recipe, stock) | `(12, 3)` |
| Unit cost | `(12, 4)` |

### FK cascade rules

- `RecipeIngredient → MenuItem`: **Cascade** delete
- `RecipeIngredient → Ingredient`: **Restrict** (can't delete an ingredient in use)
- `TransactionItem → Transaction`: **Cascade** delete
- `TransactionItem → MenuItem`: **Restrict**
- `Payment → Transaction`: **Cascade** delete
- `Transaction → User (Cashier)`: **Restrict**

---

## 6. Authentication & Authorization

### Flow

1. Client POSTs `{ username, password }` to `/api/auth/login`.
2. Server verifies PBKDF2-SHA512 hash (600k iterations). On success, issues a signed JWT (8-hour expiry).
3. Client stores the JWT in `localStorage` and sends it as `Authorization: Bearer <token>` on every request.
4. ASP.NET Core validates the token via `JwtBearer` middleware. The fallback authorization policy requires authentication on all endpoints unless `[AllowAnonymous]` is present.

### Password hashing

PBKDF2-SHA512, 600k iterations, 16-byte salt, 32-byte hash. On successful login with an old iteration count the hash is transparently upgraded.

### Roles

| Role | Capabilities |
|---|---|
| `Manager` | Full access to all endpoints |
| `Cashier` | POS operations, order queue, own drafts, read-only menu/inventory |

### JWT claims

| Claim | Value |
|---|---|
| `sub` | User ID (integer) |
| `name` | Username |
| `fullname` | Full name |
| `role` | `Manager` or `Cashier` |

---

## 7. API Reference

All routes are prefixed with `/api`. JSON is the default content type. Authenticated endpoints require `Authorization: Bearer <token>`.

Error responses return `{ "message": "..." }` with the appropriate HTTP status code.

---

### 7.1 Auth

#### `POST /api/auth/login`

Public. Rate limit: 100/10 min per IP.

**Request**
```json
{ "username": "cashier", "password": "Cashier@123" }
```

**Response `200`**
```json
{
  "token": "<jwt>",
  "username": "cashier",
  "fullName": "Front Cashier",
  "role": "Cashier"
}
```

**Response `400`** — wrong credentials, pending, rejected, or deactivated account. Returns `{ "message": "..." }` explaining why.

---

#### `POST /api/auth/register`

Public. Rate limit: 5/10 min per IP.

Creates a **Pending** account — a Manager must approve it before the user can sign in.

**Request**
```json
{ "username": "newuser", "fullName": "New User", "password": "P@ssword1", "role": "Cashier" }
```

**Response `200`**
```json
{ "id": 42, "username": "newuser", "status": "Pending" }
```

**Response `400`** — duplicate username or password shorter than 8 characters.

---

#### `GET /api/auth/status?username=<name>`

Public. Rate limit: 60/10 min per IP. Lets a waiting user poll for approval.

**Response `200`**
```json
{ "username": "newuser", "status": "Pending" }
```

**Response `404`** — username not found.

---

#### `GET /api/auth/me`

Authenticated. Returns the current user's identity from JWT claims.

**Response `200`**
```json
{ "id": 5, "username": "cashier", "fullName": "Front Cashier", "role": "Cashier" }
```

---

### 7.2 Orders (POS)

All routes require authentication. Any role can place orders.

#### `POST /api/orders`

Place a new order. Validates items, applies discount and tax, deducts stock, returns receipt.

**Request**
```json
{
  "items": [
    { "menuItemId": 3, "quantity": 2, "modifierIds": [1], "notes": null }
  ],
  "discountAmount": 20.00,
  "payments": [
    { "method": "Cash", "amount": 500.00 }
  ]
}
```

**Payment methods:** `Cash`, `GCash`

**Response `200`** — `ReceiptDto` (see [DTOs → Orders](#dtos--orders))

**Response `400`** — empty cart, inactive item, zero/negative quantity, insufficient payment.

---

#### `GET /api/orders/recent?take=50&from=2026-05-01`

Returns the most recent `take` transactions, optionally filtered to those on or after `from` (ISO date). Ordered newest first.

**Response `200`** — array of `TransactionSummaryDto`

---

#### `GET /api/orders/{id}`

**Response `200`** — `ReceiptDto`  
**Response `404`** — not found

---

#### `GET /api/orders/{id}/pdf`

Returns a PDF receipt for the given transaction.

**Response `200`** — `application/pdf`

---

#### `POST /api/orders/{id}/refund`

Marks a `Completed` transaction as `Refunded` and restocks ingredients.

**Request**
```json
{ "reason": "Customer changed their mind" }
```

**Response `200`** — `ReceiptDto` with `status: "Refunded"`  
**Response `400`** — missing reason, or transaction is not Completed

---

#### `POST /api/orders/{id}/advance`

Advances status: `Preparing → Completed`.

**Response `200`** — `TransactionSummaryDto`  
**Response `400`** — already Completed or in a non-advanceable state

---

#### `GET /api/orders/queue/count`

Returns the number of active (Pending or Preparing) orders.

**Response `200`**
```json
{ "count": 3 }
```

---

#### `GET /api/orders/next-number`

Returns the next expected order ID (max existing ID + 1).

**Response `200`**
```json
{ "nextId": 128 }
```

---

#### `POST /api/orders/cancel`

Logs a pre-payment cancellation (no transaction row created).

**Request**
```json
{ "reason": "Customer walked away" }
```

**Response `204`**

---

#### `POST /api/orders/draft`

Saves the current cart as a Draft — no payment, no stock deduction.

**Request** — same shape as `POST /api/orders` but without `payments`
```json
{
  "items": [{ "menuItemId": 3, "quantity": 1, "modifierIds": [], "notes": null }],
  "discountAmount": 0,
  "paymentMethod": "Cash"
}
```

**Response `200`** — `DraftDto`

---

#### `GET /api/orders/drafts`

Returns all drafts belonging to the authenticated cashier.

**Response `200`** — array of `DraftDto`

---

#### `POST /api/orders/{id}/confirm`

Confirms a Draft — runs full payment/stock logic.

**Request**
```json
{ "payments": [{ "method": "Cash", "amount": 156.80 }] }
```

**Response `200`** — `ReceiptDto`  
**Response `400`** — insufficient payment  
**Response `400`** — draft not found

---

#### `DELETE /api/orders/{id}/draft`

Deletes a Draft (no-op if not found).

**Response `204`**

---

#### `GET /api/orders/export?take=200`

Downloads order history as an XLSX spreadsheet.

**Response `200`** — `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`

---

### 7.3 Menu

#### `GET /api/menu?includeInactive=false`

Authenticated. Returns active menu items with recipe and computed cost. Pass `includeInactive=true` to include soft-deleted items.

**Response `200`** — array of `MenuItemDto`

---

#### `GET /api/menu/{id}`

Authenticated.

**Response `200`** — `MenuItemDto`  
**Response `404`** — not found

---

#### `GET /api/menu/modifiers?includeInactive=false`

Authenticated. Returns modifiers (add-ons). Pass `includeInactive=true` to include inactive.

**Response `200`** — array of `ModifierDto`

---

#### `POST /api/menu` *(Manager)*

**Request**
```json
{
  "name": "Matcha Latte",
  "category": "Matcha Series",
  "price": 69.00,
  "isActive": true,
  "recipe": [
    { "ingredientId": 3, "quantity": 8.0 },
    { "ingredientId": 2, "quantity": 200.0 }
  ]
}
```

**Response `200`** — `MenuItemDto`

---

#### `PUT /api/menu/{id}` *(Manager)*

Replaces all fields including recipe (old recipe rows are deleted and replaced).

**Request** — same as POST  
**Response `200`** — `MenuItemDto`  
**Response `404`** — not found

---

#### `POST /api/menu/{id}/active?active=false` *(Manager)*

Soft-deletes (deactivates) or restores a menu item.

**Response `204`** — toggled  
**Response `404`** — not found

---

#### `POST /api/menu/modifiers` *(Manager)*

**Request**
```json
{ "name": "Extra Shot", "groupName": "Add-ons", "priceDelta": 30.00, "isActive": true }
```

**Response `200`** — `ModifierDto`

---

#### `PUT /api/menu/modifiers/{id}` *(Manager)*

**Request** — same as POST  
**Response `200`** — `ModifierDto`  
**Response `404`** — not found

---

### 7.4 Inventory

#### `GET /api/inventory`

Authenticated.

**Response `200`** — array of `IngredientDto` (all ingredients, ordered by name)

---

#### `GET /api/inventory/low-stock`

Authenticated. Returns ingredients where `StockLevel <= Threshold`.

**Response `200`** — array of `IngredientDto`

---

#### `POST /api/inventory` *(Manager)*

**Request**
```json
{
  "code": "BVRG-01",
  "name": "Cold Brew Coffee",
  "category": "Beverage",
  "unit": "ml",
  "stockLevel": 10000,
  "threshold": 2000,
  "costPerUnit": 0.08
}
```

**Response `200`** — `IngredientDto`

---

#### `PUT /api/inventory/{id}` *(Manager)*

Updates metadata only — stock level changes must go through `/adjust`.

**Request** — same shape as POST  
**Response `200`** — `IngredientDto`  
**Response `404`** — not found

---

#### `POST /api/inventory/{id}/adjust` *(Manager)*

Sets stock to an absolute value. Requires a reason for the audit trail.

**Request**
```json
{ "newQuantity": 8000, "reason": "Weekly stock count" }
```

**Response `200`** — `IngredientDto`  
**Response `400`** — missing reason  
**Response `404`** — not found

---

#### `DELETE /api/inventory/{id}` *(Manager)*

Permanently deletes an ingredient. Fails if the ingredient is used in any recipe.

**Response `204`**  
**Response `404`** — not found

---

#### `GET /api/inventory/export` *(Manager)*

Downloads inventory as CSV.

**Response `200`** — `text/csv`

---

### 7.5 Users

All routes require Manager role.

#### `GET /api/users`

Returns all users ordered by username.

**Response `200`** — array of `UserDto`

---

#### `GET /api/users/pending`

Returns pending sign-up requests.

**Response `200`** — array of `PendingUserDto`

---

#### `POST /api/users/{id}/approve`

Approves a Pending account → Active.

**Response `200`** — `UserDto`  
**Response `400`** — account is not Pending

---

#### `POST /api/users/{id}/reject`

Rejects a Pending account → Rejected.

**Response `200`** — `UserDto`  
**Response `400`** — account is not Pending

---

#### `POST /api/users`

Creates an Active account directly (Manager-created accounts skip the approval flow).

**Request**
```json
{ "username": "barista2", "fullName": "Barista Two", "password": "P@ssword1!", "role": "Cashier" }
```

**Response `200`** — `UserDto`  
**Response `400`** — duplicate username or blank credentials

---

#### `PUT /api/users/{id}`

Updates full name, role, and active status.

**Request**
```json
{ "fullName": "Senior Cashier", "role": "Cashier", "isActive": true }
```

**Response `200`** — `UserDto`  
**Response `404`** — not found

---

#### `POST /api/users/{id}/reset-password`

**Request**
```json
{ "newPassword": "NewP@ss1!" }
```

**Response `200`**  
**Response `400`** — password shorter than 8 characters

---

#### `POST /api/users/{id}/delete`

Permanently deletes a user. Throws if the user has existing transactions (deactivate instead).

**Response `204`**  
**Response `400`** — user has transactions

---

### 7.6 Settings

#### `GET /api/settings/store`

Authenticated (any role). Returns the public subset of store config used by the frontend.

**Response `200`**
```json
{ "storeName": "Chao & Brew", "address": "PUP QC Campus", "currency": "PHP", "taxRatePercent": 12 }
```

---

#### `GET /api/settings` *(Manager)*

Same as above — full settings payload.

---

#### `PUT /api/settings` *(Manager)*

**Request**
```json
{ "storeName": "Chao & Brew", "address": "PUP QC Campus", "currency": "PHP", "taxRatePercent": 12 }
```

**Response `200`** — `StoreSettingsDto`

---

#### `GET /api/settings/backup` *(Manager)*

Downloads a full JSON snapshot of the database (users without password hashes, ingredients, menu items, modifiers, transactions, audit logs, settings).

**Response `200`** — `application/json` file download

---

### 7.7 Reports

All routes require Manager role.

#### `GET /api/reports?from=2026-03-01&to=2026-05-31&period=daily`

Generates a sales report for the given date range.

**Query params**

| Param | Default | Options |
|---|---|---|
| `from` | 7 days ago | ISO date |
| `to` | today | ISO date (inclusive) |
| `period` | `daily` | `daily`, `weekly`, `monthly`, `yearly` |

**Response `200`** — `ReportDto`

```json
{
  "period": "daily",
  "summary": {
    "totalSales": 45200.00,
    "transactionCount": 320,
    "averageOrderValue": 141.25,
    "totalDiscounts": 1200.00,
    "totalTax": 4886.16,
    "itemsSold": 412,
    "profitMarginPercent": 78.4
  },
  "trend": [
    { "label": "May 01", "sales": 1540.80, "transactionCount": 11 }
  ],
  "menuPerformance": [
    { "menuItemId": 3, "name": "Latte", "category": "Cold Brew Coffee",
      "quantitySold": 45, "revenue": 2655.00, "profit": 2100.60, "marginPercent": 79.1 }
  ],
  "bestSellers": [ /* top 5 by quantity */ ],
  "slowSellers": [ /* bottom 5 by quantity */ ],
  "categoryBreakdown": [
    { "category": "Cold Brew Coffee", "quantitySold": 120, "revenue": 7080.00 }
  ]
}
```

---

#### `GET /api/reports/export/csv`

Same params as `GET /api/reports`. Downloads report as CSV.

---

#### `GET /api/reports/export/pdf`

Same params. Downloads report as PDF.

---

### 7.8 Audit

#### `GET /api/audit?take=200` *(Manager)*

Returns the most recent audit log entries, newest first.

**Response `200`** — array of `AuditLogDto`

```json
[
  {
    "id": 1042,
    "timestamp": "2026-06-05T14:30:00Z",
    "username": "manager",
    "action": "SettingsUpdated",
    "details": "Store='Chao & Brew', Tax=12%, Currency=PHP"
  }
]
```

---

### 7.9 Health

#### `GET /api/health`

Public. Used by CloudFront/load balancers.

**Response `200`**
```json
{ "status": "ok", "service": "Chao & Brew", "database": "connected" }
```

---

## 8. Data Models

### User

| Field | Type | Notes |
|---|---|---|
| `Id` | int | PK |
| `Username` | string | Unique index |
| `FullName` | string | |
| `PasswordHash` | string | PBKDF2-SHA512 |
| `Role` | string | `Manager` or `Cashier` |
| `IsActive` | bool | Gate for login |
| `Status` | string | `Pending`, `Active`, `Rejected` |
| `CreatedAt` | DateTime | UTC |

### Ingredient

| Field | Type | Notes |
|---|---|---|
| `Id` | int | PK |
| `Code` | string | Human-readable code e.g. `BVRG-01` |
| `Name` | string | |
| `Category` | string | |
| `Unit` | string | `ml`, `g`, `pc`, `serving` |
| `StockLevel` | decimal(12,3) | Current stock; protected by `xmin` optimistic concurrency |
| `Threshold` | decimal(12,3) | Low-stock alert threshold |
| `CostPerUnit` | decimal(12,4) | Used for margin calculation |

### MenuItem

| Field | Type | Notes |
|---|---|---|
| `Id` | int | PK |
| `Name` | string | |
| `Category` | string | Shown in POS category tabs |
| `Price` | decimal(12,2) | Base price before modifiers |
| `IsActive` | bool | Soft delete; inactive items can't be ordered |
| `Recipe` | `ICollection<RecipeIngredient>` | Ingredients deducted on sale |

### Modifier

| Field | Type | Notes |
|---|---|---|
| `Id` | int | PK |
| `Name` | string | e.g. `Extra Shot` |
| `GroupName` | string | e.g. `Add-ons`, `Size`, `Preference` |
| `PriceDelta` | decimal(12,2) | Added to base price; can be negative |
| `IsActive` | bool | |

### Transaction

| Field | Type | Notes |
|---|---|---|
| `Id` | int | PK — also the order number |
| `Timestamp` | DateTime UTC | |
| `Subtotal` | decimal(12,2) | Before discount |
| `DiscountAmount` | decimal(12,2) | Fixed peso amount |
| `TaxAmount` | decimal(12,2) | Computed on `(subtotal - discount) × taxRate` |
| `TotalAmount` | decimal(12,2) | Net amount due |
| `PaymentMethod` | string | `Cash`, `GCash` |
| `CashierId` | int | FK → User |
| `Status` | string | `Draft` → `Preparing` → `Completed` / `Refunded` / `Cancelled` |
| `Notes` | string? | Refund/cancel reason |

### TransactionItem

Line-item snapshot. Name and price are captured at sale time so historical receipts remain accurate even if the menu item is later updated.

| Field | Type | Notes |
|---|---|---|
| `ItemName` | string | Snapshot of `MenuItem.Name` |
| `UnitPrice` | decimal(12,2) | Base + sum of modifier deltas |
| `Quantity` | int | |
| `LineTotal` | decimal(12,2) | `UnitPrice × Quantity` |
| `Modifiers` | string? | Comma-separated modifier names |

### Payment

| Field | Type | Notes |
|---|---|---|
| `Method` | string | `Cash` or `GCash` |
| `Amount` | decimal(12,2) | Amount tendered for this payment row |

Multiple `Payment` rows per `Transaction` = split payment.

### Discount (abstract)

```csharp
decimal Apply(decimal subtotal)  // returns [0, subtotal], rounded to 2dp
```

- **`FixedAmountDiscount`** — subtracts a fixed peso amount, clamped to subtotal
- **`PercentDiscount`** — subtracts a percentage (0–100%), available for future use

---

## 9. Services

### AuthService

Handles login (PBKDF2 verify → JWT issue), self-service registration (Pending), and account status polling.  
Transparently re-hashes passwords with the current iteration count on successful login.

### UserService

Manager-only CRUD for user accounts. Approval/rejection workflow. Password reset. Prevents deleting users with existing transactions.

### MenuService

Menu item and modifier management. Computes `cost` from ingredient unit costs × recipe quantities. Recipe replacement on update.

### OrderService

Core POS engine. Builds line items, applies `Discount` polymorphically, calculates tax, validates payment, deducts stock with optimistic-concurrency retry (up to 5 attempts on `DbUpdateConcurrencyException`). Manages Draft lifecycle.

### InventoryService

Ingredient CRUD. `AdjustAsync` sets stock to an absolute value (requires a reason). Stock status derived from `StockLevel` vs `Threshold`.

### ReportingService

Aggregates `Completed` transactions over a date range. Groups trend data by `daily` / `weekly` / `monthly` / `yearly` bucket. Computes profit using ingredient recipe costs. Returns best sellers, slow sellers, and category breakdown.

### SettingsService

Key/value store for `StoreName`, `Address`, `Currency`, `TaxRatePercent`. Includes a 5-minute in-process cache for `GetTaxRateAsync` to avoid a DB round-trip on every order. JSON backup export.

### AuditService

Append-only action log. `Add()` enqueues a row on the shared DbContext (committed atomically with the calling operation). `LogAsync()` saves immediately (for standalone events like login).

---

## 10. Frontend

The frontend is a vanilla JS SPA served from S3/CloudFront. No build step — plain ES2020 loaded via `<script defer>` tags.

### Script loading

All scripts use `defer` so they download in parallel while HTML parses. `manage.js` and `reports.js` are **not** included in the initial page load — they are injected dynamically on first navigation to their respective views (lazy loading). This means cashier sessions never download or parse ~38 KB of manager-only code.

`index.html` also includes `<link rel="preload">` hints for `/api/auth/me` and `/api/settings/store` so the browser starts those Lambda requests before JS even finishes loading.

### Routing

`app.js` listens to `window.hashchange`. Routes:

| Hash | View | Auth required |
|---|---|---|
| `#login` | `Views.login` | No |
| `#register` | `Views.register` | No |
| `#waiting` | `Views.waiting` | No |
| `#pos` | `Views.pos` | Yes |
| `#orders` / `#transactions` | `Views.activity` | Yes |
| `#inventory` | `Views.inventory` | Yes |
| `#menu` | `Views.menu` | Yes |
| `#users` | `Views.users` | Manager |
| `#settings` | `Views.settings` | Manager |
| `#reports` / `#performance` | `Views.reports` | Manager |

### JS files

| File | Exports | Loaded | Description |
|---|---|---|---|
| `api.js` | `Api` | Always | Fetch wrapper. Attaches JWT, parses JSON, surfaces `message` from error responses, dispatches `brewvio:unauthorized` on 401. Includes a 2-minute in-memory GET cache (`cachedGet`) and a `bustCache(prefix)` helper. |
| `ui.js` | `UI` | Always | `el()` DOM builder, `modal()` (Bootstrap wrapper), `toast()`, `spinner()`, `empty()`, `lineChart()`, `barChart()`, `doughnutChart()`, `money()`, `dateTime()` |
| `app.js` | `App` | Always | Hash router, auth gate, sidebar nav, topbar clock. Fires `Api.me()` and `Api.settings/store` in parallel on startup. Lazy-loads `manage.js` and `reports.js` on first navigation to their views. |
| `auth.js` | `Views.login`, `Views.register`, `Views.waiting` | Always | Login form, registration form, approval polling |
| `pos.js` | `Views.pos`, `Views.activity` | Always | POS screen (menu grid, cart, payment modal, draft save), Orders page (queue / history / drafts tabs). Order history is paginated at 100 records with a "Load more" button. |
| `manage.js` | `Views.inventory`, `Views.menu`, `Views.users`, `Views.settings` | Lazy (Manager first nav) | All manager admin views |
| `reports.js` | `Views.reports`, `Views.performance` | Lazy (Manager first nav) | Report chart, KPI cards, best sellers table, period selector |

### API caching

`Api.cachedGet(url)` caches GET responses in memory for 2 minutes. Used for menu items, modifiers, and inventory — endpoints read frequently but changed infrequently. The cache is busted by calling `Api.bustCache(prefix)` after any mutation:

| Mutation | Cache busted |
|---|---|
| Save/toggle menu item or modifier | `/api/menu` |
| Adjust or save inventory item | `/api/inventory` |
| Place an order (stock deducted) | `/api/inventory` |

Regular `Api.get()` bypasses the cache and is used for order queues, reports, and other real-time data.

### UI patterns

- **`el(tag, attrs, ...children)`** — builds a DOM node. Handles `class`, `text`, `html`, `dataset`, `onClick` etc.
- **`modal({ title, body, footer })`** — wraps Bootstrap's `Modal`. Only one modal open at a time.
- **`toast(message, type)`** — Bootstrap toast; auto-dismisses.
- Bootstrap's hide animation is async (~300ms). Code that opens a new modal after `closeModal()` must listen for `hidden.bs.modal` first.

---

## 11. Testing

### Run tests

```bash
# Start the test Postgres container first
docker start brewvio-pg

dotnet test tests/Brewvio.Tests.csproj
```

### Test isolation strategy

Each test class uses `IClassFixture<SharedTestDb>`:

1. `SharedTestDb.InitializeAsync()` creates a real Postgres database once per class (schema via `EnsureCreatedAsync`).
2. `fixture.Begin()` returns a `TestScope` that opens a `NpgsqlConnection`, begins a real `NpgsqlTransaction`, and hands it to EF via `UseTransaction()`.
3. On `TestScope.Dispose()` the transaction is rolled back — no data leaks, no DROP/CREATE overhead.
4. `t.NewContext()` opens a second EF context on the same underlying connection+transaction to bypass EF's first-level cache for assertion reads.

The concurrency test uses the legacy `TestDb` (creates and drops an isolated DB) because it genuinely needs two independent connections.

### Test coverage summary

| File | Tests | Covers |
|---|---|---|
| `AuthServiceTests` | 11 | Login, register, status polling, approval/rejection flow |
| `AuditServiceTests` | 8 | ListAsync ordering/take/shape, LogAsync, Add user attribution |
| `DiscountTests` | 16 | `FixedAmountDiscount` + `PercentDiscount` edge cases |
| `InventoryServiceTests` | 5 | Adjust (happy + guard), LowStock, Create, Update |
| `InventoryServiceExtendedTests` | 13 | ListAsync, Delete, null paths, all 3 stock statuses |
| `MenuServiceTests` | 13 | List, Create, Update, SetActive, Modifier CRUD |
| `MenuServiceExtendedTests` | 15 | GetAsync, Delete item/modifier, includeInactive, cost recalc |
| `OrderServiceTests` | 11 | Create (totals, tax, stock, GCash), refund, cancel, concurrency |
| `OrderServiceExtendedTests` | 35 | Modifiers, stock warnings, draft lifecycle, advance, queue, recent |
| `ReportingServiceTests` | 3 | Aggregation, margins, monthly buckets |
| `ReportingServiceExtendedTests` | 16 | Weekly/yearly buckets, empty range, AOV, discounts, slow sellers |
| `SettingsServiceTests` | 6 | Get, Update, idempotency, tax rate cache, backup |
| `SettingsServiceExtendedTests` | 10 | Empty DB defaults, non-numeric tax, backup completeness |
| `UserServiceTests` | 11 | Approve, reject, create, update, reset password |
| `UserServiceExtendedTests` | 15 | ListAsync, delete, null paths, status sync, boundary checks |
| **Total** | **187** | |

---

## 12. Deployment

### Stack

Defined in `template.yaml` (AWS SAM):

| Resource | Type | Notes |
|---|---|---|
| `BrewvioFunction` | `AWS::Serverless::Function` | arm64, .NET 10, 1769 MB (2 vCPUs), 60s timeout |
| `Api` | `AWS::Serverless::HttpApi` | Throttle: 50 burst / 100 rps |
| `BrewvioFunctionLogGroup` | `AWS::Logs::LogGroup` | Lambda log group; 1-day retention to minimize CloudWatch cost |
| `FrontendBucket` | `AWS::S3::Bucket` | Private; CloudFront OAC access only |
| `Distribution` | `AWS::CloudFront::Distribution` | Single domain: `/api/*` → Lambda, `/*` → S3 |

### Deploy backend + infrastructure

```bash
sam build
sam deploy --stack-name brewvio --resolve-s3 --capabilities CAPABILITY_IAM --no-confirm-changeset
```

### Deploy frontend only

```bash
# Upload JS/CSS/img with long-lived immutable cache (safe — files are versioned)
aws s3 sync src/wwwroot/ s3://<bucket-name>/ --delete \
  --cache-control "max-age=31536000, immutable" \
  --exclude "index.html"

# index.html must never be cached — it is the SPA entry point
aws s3 cp src/wwwroot/index.html s3://<bucket-name>/index.html \
  --cache-control "no-store, must-revalidate"

# Invalidate CloudFront edge caches
aws cloudfront create-invalidation --distribution-id <id> --paths "/*"
```

### Secrets (SSM Parameter Store)

Create these as `SecureString` before first deploy:

```bash
aws ssm put-parameter --name /brewvio/DATABASE_URL --type SecureString \
  --value "postgresql://user:pass@host:6543/postgres"

aws ssm put-parameter --name /brewvio/JWT_KEY --type SecureString \
  --value "your-minimum-32-byte-signing-key"
```

### Live URLs

| Resource | URL |
|---|---|
| Site (CloudFront) | `https://d37i8pbdtw6xf4.cloudfront.net` |
| API (direct) | `https://01x7t1hoqe.execute-api.ap-southeast-2.amazonaws.com` |
