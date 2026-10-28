# OmniCommerce — Cart Web API (proof of concept)

OmniCommerce is a proposed retail platform; the full design is in [docs/architecture.md](docs/architecture.md). This repository implements **only its shopping cart**, as a small ASP.NET Core Web API on SQL Server. It demonstrates EF Core migrations, relational constraints, and optimistic concurrency with SQL Server `rowversion` exposed as an HTTP `ETag` / `If-Match` contract.

Pricing, inventory, checkout, payments, identity and cloud deployment from the architecture document are **not implemented**. This is a development-only prototype (see [Limitations](#limitations)).

> **AI disclosure.** This repository was built with AI assistance (GitHub Copilot in VS Code / Copilot CLI, working from `AGENTS.md` and the documents in `docs/`). Work was done in stages, each stopped for developer review before being committed. This README only states what has been run.

## Tech stack and structure

.NET 8, ASP.NET Core Web API (controllers), EF Core 8, SQL Server 2022, Docker Compose, xUnit with `Microsoft.AspNetCore.Mvc.Testing`. One API project and one `DbContext`; the folders are conventions, not enforced boundaries.

```text
src/OmniCommerce.Api/
  Controllers/      CartsController: HTTP mapping, If-Match handling, ProblemDetails
  Application/      CartService (use cases), DTOs, CartETag
  Domain/           Cart, CartItem, Product and cart rules
  Infrastructure/   CommerceDbContext, entity configurations, migrations, health check
tests/OmniCommerce.Tests/   unit tests plus SQL Server-backed service and HTTP tests
scripts/verify-docker.ps1   full Docker-backed verification
```

## Quick start

Requires Docker (Compose v2) with the Docker daemon running (start Docker Desktop first). From the repository root, in Bash or PowerShell:

```text
docker compose up --build
```

The first run downloads the SQL Server image and builds the API, which can take a few minutes. The command keeps running and streams the logs, so use a **second terminal** for the commands below; stop it with `Ctrl+C`. The API is ready once its log shows `Now listening on: http://[::]:8080` (the port inside the container, published on your machine as 8080).

The API is at <http://localhost:8080>, but there is **no endpoint at `/`**: opening that URL in a browser returns 404, which is expected. To check that the application and database are up, both calls should return `Healthy`:

```powershell
Invoke-RestMethod http://127.0.0.1:8080/health/live
Invoke-RestMethod http://127.0.0.1:8080/health/ready
```

```bash
curl -s -w '\n' http://127.0.0.1:8080/health/live
curl -s -w '\n' http://127.0.0.1:8080/health/ready
```

`live` only shows the process is running; `ready` also needs SQL Server (otherwise `503`; see `docker compose logs`). SQL Server listens on `127.0.0.1:1433` with data in the named volume `sqlserver-data`. The API starts only after SQL Server is healthy, applies EF Core migrations, and then begins listening. `docker compose down` stops everything and keeps the data; add `-v` to delete it.

The SQL Server `sa` password defaults to a **local-development example value** in `docker-compose.yml`. Override it with `MSSQL_SA_PASSWORD` (it must meet SQL Server complexity rules and not contain `;` or `$`). By running the SQL Server container you accept Microsoft's SQL Server licence terms (`ACCEPT_EULA=Y` is set in the compose file).

## API

Routes are under `/api/carts`. Prices are never accepted or returned. Mutations return the updated cart and its new `ETag`.

| Method | Route | Body | `If-Match` | Success |
|---|---|---|---|---|
| `POST` | `/api/carts` | – | – | `201`, `Location`, `ETag` |
| `GET` | `/api/carts/{cartId}` | – | – | `200`, `ETag` |
| `POST` | `/api/carts/{cartId}/items` | `{"productId", "quantity"}` | required | `200`; merges into an existing line |
| `PUT` | `/api/carts/{cartId}/items/{productId}` | `{"quantity"}` | required | `200` |
| `DELETE` | `/api/carts/{cartId}/items/{productId}` | – | required | `200` |
| `DELETE` | `/api/carts/{cartId}/items` (clear) | – | required | `200` |
| `GET` | `/health/live`, `/health/ready` | – | – | `200`; ready is `503` if SQL Server is unreachable |

Quantity must be 1–10 000 per line. Errors are `application/problem+json`: **400** invalid body or malformed `If-Match`, **404** unknown cart or product line, **412** stale `If-Match`, **422** unknown product or line limit exceeded, **428** missing `If-Match`.

**Concurrency.** The ETag is a strong tag derived from `Cart.RowVersion` (an encoded version, not a credential). Each mutation runs in one SQL transaction that first executes `UPDATE Carts SET UpdatedAt = @now WHERE Id = @id AND RowVersion = @expected`. If no row matches, the request is rejected with 412 and nothing is written; otherwise the item changes are applied in the same transaction, and any later rejection rolls everything back. Two simultaneous requests with the same ETag cannot both commit, and conflicts are never retried silently.

### Example

Create a cart, add two seeded ceramic mugs (`MUG-CER-350`) using the ETag from the create response, then read the cart back. Expected result: one line, `MUG-CER-350` with quantity 2.

PowerShell (runs top to bottom, nothing to replace):

```powershell
$base = 'http://127.0.0.1:8080/api/carts'
$r    = Invoke-WebRequest -Method Post $base -UseBasicParsing   # 201 Created
$r.StatusCode
$id   = ($r.Content | ConvertFrom-Json).id
$etag = @($r.Headers['ETag'])[0]
Invoke-RestMethod -Method Post "$base/$id/items" -Headers @{ 'If-Match' = $etag } `
  -ContentType 'application/json' `
  -Body '{"productId":"a1000000-0000-0000-0000-000000000003","quantity":2}' | Out-Null
(Invoke-RestMethod "$base/$id").items
```

Bash (also runs top to bottom; it extracts the values with `grep`/`sed`, no `jq` needed):

```bash
BASE=http://127.0.0.1:8080/api/carts
RESP=$(curl -s -i -X POST "$BASE")                                   # 201 Created
CART_ID=$(printf '%s' "$RESP" | grep -o '"id":"[^"]*"' | cut -d'"' -f4)
ETAG=$(printf '%s' "$RESP" | grep -i '^etag:' | sed 's/^[Ee][Tt]ag: *//' | tr -d '\r')
curl -s -o /dev/null -X POST "$BASE/$CART_ID/items" -H "If-Match: $ETAG" \
  -H 'Content-Type: application/json' \
  -d '{"productId":"a1000000-0000-0000-0000-000000000003","quantity":2}'
curl -s "$BASE/$CART_ID"
```

The add changed the cart, so the old ETag is now stale: sending the same add request again with it returns `412 Precondition Failed` and changes nothing. `GET` the cart to obtain the current ETag, then retry.

## Database

The schema comes from the EF Core migration `InitialCreate` in `src/OmniCommerce.Api/Infrastructure/Migrations`: `Carts` (with a `rowversion` column), `CartItems` (unique `(CartId, ProductId)`, `CHECK (Quantity > 0)`, foreign keys) and `Products`. The same migration inserts five sample products with fixed IDs, so applying it repeatedly is safe. Docker Compose applies it on startup; the API refuses to do that outside the `Development` environment.

| Product ID | SKU |
|---|---|
| `a1000000-0000-0000-0000-000000000001` | `TSHIRT-BLK-M` |
| `a1000000-0000-0000-0000-000000000002` | `TSHIRT-WHT-L` |
| `a1000000-0000-0000-0000-000000000003` | `MUG-CER-350` |
| `a1000000-0000-0000-0000-000000000004` | `NOTEBOOK-A5` |
| `a1000000-0000-0000-0000-000000000005` | `BOTTLE-STL-750` |

## Testing

```text
dotnet build OmniCommerce.sln
dotnet test OmniCommerce.sln
```

**A plain `dotnet test` may skip the SQL Server tests.** The service-level concurrency tests (`CartConcurrencyTests`) and the HTTP integration tests (`CartApiTests`) need a real SQL Server; EF InMemory or SQLite cannot prove `rowversion`, transaction or locking behaviour. Without `OMNICOMMERCE_TEST_SQLSERVER` they are skipped, the run still exits 0, and the skip reason reads `INCOMPLETE VERIFICATION`. A green run with skips is **not** full verification.

To run them against your own SQL Server, set a connection string **without** `Database=`; each test class creates and drops its own `OmniCommerce_Test_<guid>` database through the real migrations and never touches existing databases.

```powershell
$env:OMNICOMMERCE_TEST_SQLSERVER = 'Server=127.0.0.1,1433;User Id=sa;Password=<password>;TrustServerCertificate=True'
dotnet test OmniCommerce.sln
```

```bash
export OMNICOMMERCE_TEST_SQLSERVER='Server=127.0.0.1,1433;User Id=sa;Password=<password>;TrustServerCertificate=True'
dotnet test OmniCommerce.sln
```

### Full Docker-backed verification

The script starts the stack with `docker compose up --build -d --wait`, sends a real `POST /api/carts`, runs **all** tests against the Docker SQL Server with `OMNICOMMERCE_REQUIRE_SQLSERVER_TESTS=1`, and checks the TRX results. It exits non-zero if any test fails, is skipped or not discovered, or if the database is unavailable. Exit code 0 means fully verified.

```powershell
# Windows PowerShell (if your execution policy blocks local scripts, this bypasses it for this process only)
powershell -ExecutionPolicy Bypass -File scripts\verify-docker.ps1
```

```bash
# Linux / macOS with PowerShell 7
pwsh ./scripts/verify-docker.ps1
```

Last local run (Windows, Docker Desktop): 95 tests passed, 0 failed, 0 skipped, including both SQL Server-backed classes. This count changes as tests are added.

## CI

`.github/workflows/ci.yml` runs on pull requests to `main` and pushes to `main` (Ubuntu runner, .NET 8, read-only `contents` permission). It restores and builds the solution, then runs the same `scripts/verify-docker.ps1` with `pwsh`, so any failed, skipped or undiscovered SQL Server-backed test fails the job. On failure it uploads the test results and container logs as an artifact and always removes the Docker resources. It uses a throwaway SQL password defined in the workflow, no repository secrets, and does not deploy or publish anything.

The workflow has run successfully on GitHub: [run #1](https://github.com/svensrebot/OmniCommerce/actions/runs/40729044212), a push to `main` (commit `83ba0d1`) in which the verification step passed.

## Limitations

- **Development only.** There is no authentication and cart ownership is not enforced: anyone who knows a cart ID can read and change it. Do not expose this to a public network.
- The compose file and `appsettings.Development.json` contain a local example SQL password, use the `sa` login and `TrustServerCertificate=True`, and run the API in the `Development` environment over plain HTTP. Never commit real credentials.
- Migrations on startup are a local convenience, not a deployment strategy. There is no production deployment.
- `Cart.OwnerReference` and `Cart.ExpiresAt` exist in the schema but are unused; there is no cart expiry or cleanup.
- Clearing an empty cart still bumps its version, and ETags are opaque (`rowversion` values can skip numbers).
- Deadlocks or lock timeouts under heavy load would surface as `500` and were not load-tested. Only single-instance behaviour was tested.
