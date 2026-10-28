# OmniCommerce — Cart Web API implementation plan (Copilot handoff)

**Status:** Approved implementation scope. Separate from the high-level architecture. Implement only the requested minimal Cart Web API and database, not the imagined full commerce system. Treat stretch goals as optional.

## Scope and priorities

### P0 — Required deliverables

#### Core API and persistence
- ASP.NET Core Web API + EF Core + **SQL Server**. One deployable API, one DbContext and one production API project organized in layered folders. Target **.NET 8** (ASP.NET Core 8 and EF Core 8); do not add extra libraries without demonstrated need.
- Entities: `Cart` (ID, nullable customer/session owner reference for design, `CreatedAt`, `UpdatedAt`, optional `ExpiresAt`, `RowVersion`); `CartItem` (ID, CartId, SKU/Product ID, Quantity). Add a minimal `Product` table with seeded sample data if needed for valid SKU checks. No real Catalog, Pricing, Inventory, Checkout or Payment implementation.
- Endpoints: create cart, get cart, add item, set item quantity, remove item, optionally clear cart. Sensible HTTP status codes, validation, and `ProblemDetails` for failures.
- SQL constraints: positive quantity, unique `(CartId, SkuId)` to prevent duplicate cart lines, FKs, useful indexes. EF Core migrations and reproducible local startup.
- Provide README with requirements, configuration, migration/startup instructions, example HTTP requests, assumptions, and explicit description of what is NOT implemented.

#### Optimistic concurrency
- Configure SQL Server `rowversion` on `Cart` (EF Core `.IsRowVersion()` / `[Timestamp]`). Return the current version as a **strong ETag** on cart GET/creation responses. Require `If-Match` containing that ETag for mutations of an existing cart; respond with `428 Precondition Required` if missing and `412 Precondition Failed` if stale. Return the updated ETag after successful mutation. Document that ETag is an encoded version, **not** an authentication token.
- **Critical:** changing `CartItem` does not by itself change parent `Cart.RowVersion`. All cart mutations must update the parent cart row in the *same transaction* (e.g. `UpdatedAt`) with the expected row version check; ensure cross-request conflicts are detected. Unique `(CartId, SkuId)` handles competing adds but does not replace concurrency control.
- On stale version, return `412 Precondition Failed` and instruct callers to fetch the current cart/ETag. Do not silently overwrite or blindly retry conflicts. Ensure that the SQL write uses the expected rowversion in its concurrency predicate within the same transaction as item changes. Handle EF Core `DbUpdateConcurrencyException`.
- Add a SQL Server-backed test demonstrating that an earlier ETag is rejected after a cart change. If this cannot be completed, document the gap rather than claiming concurrency safety.

#### Reproducibility and minimum verification
- **Docker Compose for API + SQL Server is required.** Document a tested `docker compose up --build` startup path, including how migrations and sample product data are applied. Use SQL Server readiness/health gating or retry logic so startup is reliable. Do not commit credentials; local-development example values only.
- Required checks: (1) create cart and add item; (2) reject invalid quantity; (3) reject stale `If-Match` with `412`, against real SQL Server. Prefer automated integration tests; record any verification done manually.

### P1 — Optional enhancements
- Additional unit and integration tests (duplicates, unknown product, cart retrieval), health endpoints and basic structured logs, GitHub Actions PR CI.
- Cart expiration policy/cleanup described in README; background cleanup implementation optional.

## Explicitly OUT OF SCOPE
- React/Angular frontend; identity provider setup, production JWT authentication/authorization, BFF; real prices/discounts and final totals; real inventory/stock reservation; orders, checkout, payments, shipping, taxes; Redis, search service, media CDN, messaging broker; production Azure infrastructure; full microservice scaffolding.
- Cart owner checks and authentication must **not** be claimed as implemented if they're only documented. Avoid creating insecure pseudo-production auth flows. If no auth is implemented, say endpoints are a development-only PoC and are not suitable for public deployment.

## Coding discipline and acceptance checks
1. Keep logic focused; use one API project with clear Domain/Application/Infrastructure/Controllers folders rather than elaborate Clean Architecture scaffolding.
2. Work in small verified stages. **Stop and request review before commits**; the human developer makes meaningful incremental commits (explicit assignment requirement).
3. Ensure migrations/build succeed and API routes work end-to-end with SQL Server; test concurrent item updates when concurrency is implemented.
4. Protect data consistency with unique constraints and transactions; avoid overclaiming scalability.
5. Document that AI tools assisted in implementation, with human review/verification. Follow any employer AI-use policy.
6. Final README must distinguish **implemented**, **tested**, **conceptual**, and **future improvements**.

## Implementation details to settle before coding
- Specify exact routes and DTOs, seed a small fixed set of products/SKUs, and choose a clear local SQL Server configuration. Use **ETag / `If-Match`** as the concurrency contract. Do not substitute EF Core InMemory for relational integration tests: SQL Server rowversion, constraints and transactions require real SQL Server. If automatic migrations are used, restrict them to local PoC. Keep decisions small and documented.

## Related files
- `AGENTS.md` — concise coding-agent guardrails to be finalized before Copilot session.


## Demonstration review checklist / delivery strategy
- Prioritize a **working** Cart API, real SQL Server behavior, reproducible Docker startup, and the three minimum checks. Drop P1 enhancements first if needed.
- Keep genuine incremental commits made by the human after review; do not manufacture commit history or insist on an arbitrary number.
- On PR: CI build/tests. On main: automatic non-production deployment is the conceptual production plan; actual prototype only needs runnable Docker Compose and optionally CI. Production deployment requires **manual approval/promotion of the same tested image**, not automatic deployment on every merge.
- Include clear disclosure of AI assistance and a verification record listing commands actually run and tests actually passed.
- No full authentication implementation: mark demo cart endpoints as development-only and do not claim user isolation/security.

## Repository setup and folder conventions
Create an empty Git repository first. Scaffold `OmniCommerce.sln` with `src/OmniCommerce.Api` (one production `.csproj`) and `tests/OmniCommerce.Tests` targeting **.NET 8**. Within the API project use:

```text
Controllers/           HTTP boundary and request/response status mapping
Application/           Cart services, use-case coordination and DTOs
Domain/                Cart, CartItem, minimal Product and business invariants
Infrastructure/        Persistence/CommerceDbContext, Configurations, Migrations
```

Use one DbContext and default SQL Server schema. Avoid needless generic repositories and empty modules. The folders improve navigation and conventions, but **do not enforce dependencies at compile time**; a future split into assemblies or services requires deliberate refactoring. Keep `AGENTS.md` at repo root, `docs/` for architecture and plan, and commit meaningful development stages.
