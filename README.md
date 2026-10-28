# OmniCommerce — Cart Web API (proof of concept)

A small ASP.NET Core Web API that implements **only a shopping cart** on SQL Server, to demonstrate a few backend practices from the OmniCommerce architecture (`docs/architecture.md`): EF Core migrations, relational constraints, and optimistic concurrency with SQL Server `rowversion` exposed as HTTP `ETag` / `If-Match`.

It is **not** the commerce platform described in the architecture document, and it is a **development-only PoC** (no authentication, see [Limitations](#security-and-limitations)).

> **AI disclosure.** This repository was built with AI assistance (GitHub Copilot in VS Code / Copilot CLI, working from `AGENTS.md` and the documents in `docs/`). Work was done in stages, each stopped for developer review before being committed. This README only claims what has been run; see [Verification](#running-the-tests).

## Contents

- [Architecture](#architecture) · [Technologies](#technologies) · [Prerequisites](#prerequisites)
- [Run with Docker Compose](#run-with-docker-compose) · [Database and sample data](#database-migrations-and-sample-data)
- [API](#api) · [Example requests](#example-requests) · [Response codes](#response-codes)
- [Running the tests](#running-the-tests) · [Health and logging](#health-and-logging)
- [Security and limitations](#security-and-limitations) · [Status](#implemented-excluded-and-future)

## Architecture

One deployable API project, one `DbContext`, one default SQL Server schema. Folders are conventions, not compile-time boundaries.

```text
src/OmniCommerce.Api/
  Controllers/      CartsController: HTTP mapping, If-Match handling, ProblemDetails
  Application/      CartService (use cases), DTOs, CartETag (ETag format/parse)
  Domain/           Cart, CartItem, Product and cart invariants (no EF dependencies)
  Infrastructure/   CommerceDbContext, entity configurations, migrations, health check
tests/OmniCommerce.Tests/   xUnit: unit tests, SQL Server-backed service and HTTP tests
```

How concurrency works:

1. `GET`/`POST /api/carts` return a **strong ETag** built from `Cart.RowVersion` (e.g. `"00000000000007d1"`). It is an encoded version, not an authentication token.
2. Every cart mutation must send that value in `If-Match`.
3. Each mutation runs in **one SQL transaction** whose first statement is
   `UPDATE Carts SET UpdatedAt = @now WHERE Id = @id AND RowVersion = @expected`.
   If it matches no row the request is rejected (412) and nothing is written. Otherwise the cart row is locked, so concurrent requests holding the same ETag are serialized: the loser re-evaluates the predicate after the winner commits and is rejected. The item changes follow in the same transaction, and any rejection rolls the whole transaction back. Conflicts are never retried silently.

## Technologies

.NET 8, ASP.NET Core Web API (controllers), EF Core 8 with SQL Server, SQL Server 2022 (Docker), xUnit with `Microsoft.AspNetCore.Mvc.Testing`, Docker / Docker Compose. No other runtime libraries.

## Prerequisites

- **Docker Desktop** (or Docker Engine + Compose v2) — required for `docker compose up` and the full verification.
- **.NET 8 SDK or newer** — only to build/test on the host. (Developed with SDK 10 targeting `net8.0`; the .NET 8 runtime is needed to run tests.)
- **Windows PowerShell** for `scripts/verify-docker.ps1`. If your execution policy blocks local scripts (e.g. `AllSigned`), run it as `powershell -ExecutionPolicy Bypass -File scripts\verify-docker.ps1`; this affects that process only.

## Run with Docker Compose

```bash
docker compose up --build
```

- API: <http://localhost:8080> (health: `/health/live`, `/health/ready`)
- SQL Server: `localhost,1433` (loopback only), data in the named volume `sqlserver-data`.
- SQL Server has a health check; the API starts only after it is healthy and applies migrations before it begins listening. The API container has its own health check against `/health/live`.
- Stop with `Ctrl+C` / `docker compose down` (keeps data). `docker compose down -v` also deletes the database volume.

The SQL Server `sa` password defaults to a **local-development example value** from `docker-compose.yml`. To use another one, set `MSSQL_SA_PASSWORD` (or an untracked `.env` file); it must meet SQL Server complexity rules and not contain `;` or `$`. By starting the SQL Server container you accept Microsoft's SQL Server licence terms (`ACCEPT_EULA=Y` is set in the compose file).

## Database migrations and sample data

- The schema is created by the EF Core migration `InitialCreate` (`src/OmniCommerce.Api/Infrastructure/Migrations`). It adds `Carts` (with a `rowversion` column), `CartItems` (unique `(CartId, ProductId)`, `CHECK (Quantity > 0)`, foreign keys) and `Products`.
- The five sample products are inserted **by that migration** (EF `HasData` with fixed IDs), so applying it is idempotent: restarting the API applies nothing new.
- Migrations run automatically **only** when `Database:MigrateOnStartup=true`, which Docker Compose sets. The API refuses to start with that flag outside the `Development` environment.

| Product ID | SKU | Name |
|---|---|---|
| `a1000000-0000-0000-0000-000000000001` | `TSHIRT-BLK-M` | Black T-Shirt (M) |
| `a1000000-0000-0000-0000-000000000002` | `TSHIRT-WHT-L` | White T-Shirt (L) |
| `a1000000-0000-0000-0000-000000000003` | `MUG-CER-350` | Ceramic Mug 350 ml |
| `a1000000-0000-0000-0000-000000000004` | `NOTEBOOK-A5` | A5 Notebook |
| `a1000000-0000-0000-0000-000000000005` | `BOTTLE-STL-750` | Steel Bottle 750 ml |

Running on the host instead (optional): `docker compose up -d --wait sqlserver`, then `dotnet tool restore`, `ASPNETCORE_ENVIRONMENT=Development dotnet ef database update --project src/OmniCommerce.Api`, then `dotnet run --project src/OmniCommerce.Api`. `appsettings.Development.json` points at `127.0.0.1,1433` (the loopback address Docker publishes) with the same dev-only password. To add a migration: `dotnet ef migrations add <Name> --project src/OmniCommerce.Api --output-dir Infrastructure/Migrations` (with `ASPNETCORE_ENVIRONMENT=Development`).

## API

All routes are under `/api/carts`. Mutations return the **updated cart** and its **new ETag**. Prices are never accepted or returned.

| Method | Route | Body | `If-Match` | Success |
|---|---|---|---|---|
| `POST` | `/api/carts` | – | – | `201`, `Location`, `ETag` |
| `GET` | `/api/carts/{cartId}` | – | – | `200`, `ETag` |
| `POST` | `/api/carts/{cartId}/items` | `{ "productId", "quantity" }` | required | `200`, `ETag` — adds a line, or **merges** quantity into an existing one |
| `PUT` | `/api/carts/{cartId}/items/{productId}` | `{ "quantity" }` | required | `200`, `ETag` |
| `DELETE` | `/api/carts/{cartId}/items/{productId}` | – | required | `200`, `ETag` |
| `DELETE` | `/api/carts/{cartId}/items` (clear) | – | required | `200`, `ETag` |
| `GET` | `/health/live`, `/health/ready` | – | – | see [Health](#health-and-logging) |

Quantity must be 1–10 000 per line (merged quantities included).

## Example requests

```bash
# 1. Create a cart. Note the id and the ETag header.
curl -i -X POST http://localhost:8080/api/carts
#   HTTP/1.1 201 Created
#   ETag: "00000000000007d1"

# 2. Add 2 mugs, sending the ETag back in If-Match.
curl -i -X POST http://localhost:8080/api/carts/$CART_ID/items \
  -H 'If-Match: "00000000000007d1"' -H 'Content-Type: application/json' \
  -d '{"productId":"a1000000-0000-0000-0000-000000000003","quantity":2}'
#   HTTP/1.1 200 OK        ETag: "00000000000007d2"   (a new version)

# 3. Reusing the old ETag is rejected and changes nothing.
curl -i -X POST http://localhost:8080/api/carts/$CART_ID/items \
  -H 'If-Match: "00000000000007d1"' -H 'Content-Type: application/json' \
  -d '{"productId":"a1000000-0000-0000-0000-000000000001","quantity":1}'
#   HTTP/1.1 412 Precondition Failed   (application/problem+json)

# 4. Without If-Match: 428. Fetch the current version with GET, then retry.
curl -i http://localhost:8080/api/carts/$CART_ID
```

PowerShell:

```powershell
$r    = Invoke-WebRequest -Method Post http://localhost:8080/api/carts -UseBasicParsing
$id   = ($r.Content | ConvertFrom-Json).id
$etag = $r.Headers['ETag']
Invoke-RestMethod -Method Post "http://localhost:8080/api/carts/$id/items" `
  -Headers @{ 'If-Match' = $etag } -ContentType 'application/json' `
  -Body '{"productId":"a1000000-0000-0000-0000-000000000003","quantity":2}'
```

When quoting `If-Match` in Windows `curl.exe`, escape the inner quotes (`-H 'If-Match: \"0000…\"'`); an unquoted value is rejected with 400.

## Response codes

Errors use `ProblemDetails` (`application/problem+json`). For mutations the checks run in this order: body validation → `If-Match` header → cart exists → version → item/product rules.

| Status | When |
|---|---|
| `400 Bad Request` | Invalid body (quantity outside 1–10 000, missing/empty `productId`, malformed JSON), or a malformed/unsupported `If-Match`. Only **one strong ETag exactly as issued** is accepted; weak tags (`W/"…"`), `*`, lists, unquoted or non-16-hex values are rejected. |
| `404 Not Found` | Unknown cart, or removing/updating a product line the cart does not contain. Nothing is changed and the ETag stays the same. |
| `412 Precondition Failed` | `If-Match` is well formed but stale: the cart changed since that ETag was issued (including a lost race between two requests with the same ETag). Nothing is modified; `GET` the cart and retry. |
| `422 Unprocessable Entity` | Well-formed request that breaks a rule: unknown product, or a merged line quantity above 10 000. |
| `428 Precondition Required` | Mutation without an `If-Match` header. |
| `500` | Unexpected errors (ProblemDetails without internals). |

## Running the tests

```bash
dotnet build OmniCommerce.sln --no-incremental
dotnet test OmniCommerce.sln
```

**A plain `dotnet test` may skip the SQL Server tests.** Tests marked `SqlServerFact`/`SqlServerTheory` need a real SQL Server (EF InMemory/SQLite cannot prove `rowversion`, transaction or locking behaviour, so they are not used). Without `OMNICOMMERCE_TEST_SQLSERVER` they are *skipped*, `dotnet test` still exits 0, and the skip reason says `INCOMPLETE VERIFICATION`. A green run with skips is **not** full verification.

What runs without SQL Server: domain rules, request validation, ETag parsing, EF model checks and the health-endpoint behaviour with an unreachable database. What needs SQL Server: the service-level concurrency tests (`CartConcurrencyTests`) and the HTTP integration tests through the real pipeline (`CartApiTests`: create + add item, invalid quantity → 400, 428, malformed `If-Match` → 400, stale ETag → 412, unchanged cart after every rejection, two simultaneous requests with the same ETag → one 200 and one 412).

To run them against your own server, set a connection string **without** `Database=`; each test class creates a uniquely named `OmniCommerce_Test_<guid>` database through the real migrations and drops it afterwards (existing databases are never touched):

```powershell
$env:OMNICOMMERCE_TEST_SQLSERVER = 'Server=127.0.0.1,1433;User Id=sa;Password=<password>;TrustServerCertificate=True'
dotnet test OmniCommerce.sln
```

If a test process is killed mid-run, a leftover `OmniCommerce_Test_*` database may remain and can be dropped manually.

### Full Docker-backed verification

```powershell
powershell -ExecutionPolicy Bypass -File scripts\verify-docker.ps1      # add -Down to stop the containers afterwards
```

The script (1) runs `docker compose up --build -d --wait`, (2) sends a real `POST /api/carts` to the API container, (3) runs **all** tests against the Docker SQL Server with `OMNICOMMERCE_REQUIRE_SQLSERVER_TESTS=1` (so missing or unreachable SQL Server makes tests fail instead of skip) and (4) parses the TRX result. It exits non-zero unless every test passed, none was skipped or not executed, and both SQL Server-backed test classes were discovered. Exit code 0 means fully verified.

## Health and logging

- `GET /health/live` — always `200` while the process runs; it runs no checks, so it does not fail when SQL Server is down.
- `GET /health/ready` — `200` when the API can connect to SQL Server (5 s timeout), otherwise `503`. It checks connectivity only, not that migrations have been applied.
- Logging uses the built-in `Microsoft.Extensions.Logging` console provider with message templates (e.g. `Cart {CartId}: mutation rejected, the supplied ETag is stale`). EF Core per-command SQL logging is set to `Warning` in `appsettings.json` to keep output readable. No external observability packages are used.

## Security and limitations

- **Development only.** There is **no authentication or authorization** and cart ownership is **not enforced**: anyone who knows a cart ID can read and change it. Do not expose this to a public network. The ETag is a version, not a secret.
- The compose file and `appsettings.Development.json` contain a **local example SQL password**, use the `sa` login and `TrustServerCertificate=True`, run the API in the `Development` environment and publish ports on loopback only. This is not a production configuration. Never commit real credentials.
- Migrations on startup are a local-development convenience, not a deployment strategy.
- HTTP only inside the container; no TLS termination.
- `Cart.OwnerReference` and `Cart.ExpiresAt` exist in the schema but are unused: no ownership checks, expiry or cleanup.
- Clearing an already-empty cart still bumps the version. `rowversion` values are database-wide and may skip numbers (rolled-back writes consume them); treat ETags as opaque.
- SQL deadlocks or lock timeouts under heavy load would surface as `500`; they were not load-tested. Only single-instance behaviour was tested.
- No `If-None-Match`/`304` support. Verified on Windows with Docker Desktop and SQL Server LocalDB only.

## Implemented, excluded and future

| Area | Status |
|---|---|
| Cart, CartItem, seeded Product; EF Core migration; unique/check/FK constraints | **Implemented and tested** (SQL Server) |
| Create/get cart, add (merge), set quantity, remove, clear; validation and ProblemDetails | **Implemented and tested** |
| `rowversion` → strong ETag, mandatory `If-Match`, 412/428/400, atomic version check in one transaction | **Implemented and tested**, including simultaneous requests |
| Docker Compose (API + SQL Server, health gating, migrations + seed on start, named volume) | **Implemented**; verified by `scripts/verify-docker.ps1` on the author's machine |
| `/health/live`, `/health/ready`, structured console logs | **Implemented and tested** |
| Identity provider / OAuth, ownership checks | Conceptual only (`docs/architecture.md`); **not implemented** |
| Catalog, pricing, inventory reservation, checkout, payments, orders | **Out of scope**; not implemented |
| Azure deployment, CI/CD pipeline, Redis, message broker, outbox, idempotent payments, OpenTelemetry | Conceptual only; **not implemented** |

Possible future improvements: authentication and per-owner carts; cart expiry cleanup; GitHub Actions PR build/test; `If-None-Match`; load and deadlock testing; production-grade configuration and secrets handling; non-`sa` database login.

The documents in `docs/` describe the conceptual platform and the implementation plan; the README describes what this repository actually contains.
