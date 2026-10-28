# AGENTS.md — Copilot implementation instructions

## Task and source of truth
Implement **only the minimum Cart Web API**, not the entire platform. Read `docs/architecture.md` for the conceptual target and `docs/cart-api-implementation-plan.md` for exact coding priorities. If repository layout differs, use the corresponding paths. Do not implement features merely because they appear in the conceptual architecture.

## Hard technical choices
- **.NET 8** ASP.NET Core Web API, EF Core 8, **SQL Server**, one DbContext and feature-oriented organization. Use **one production `.csproj`** (`OmniCommerce.Api`) with `Controllers/`, `Application/`, `Domain/` and `Infrastructure/` folders. Domain contains entities/rules; Application contains cart services/DTOs; Infrastructure contains the single DbContext, EF mappings and migrations. These are organizational conventions, not enforced project boundaries. Do not create generic repositories just to wrap DbContext. No separate schemas, microservices, repositories around EF Core, mediator framework, Redis, broker, identity provider, frontend or Azure provisioning.
- `Cart`, `CartItem`, minimal seeded `Product/SKU`, migrations, positive quantity and FK/unique constraints. No authoritative pricing/inventory/checkout in this PoC.
- `Cart.RowVersion` is SQL Server `rowversion`; an item mutation **must update the Cart row in the same SQL transaction**, otherwise concurrency is not protected. Expose the version as a **strong ETag** and require `If-Match` on existing-cart mutations; missing header → `428 Precondition Required`, stale ETag → `412 Precondition Failed`. Return new ETag on successful updates. Check expected `rowversion` in the SQL write and do not silently retry conflicts.
- Validate input, use appropriate HTTP statuses and ProblemDetails. Never trust caller-provided price or ownership as authoritative.
- Do **not** use EF Core InMemory for persistence/integration proof. Use SQL Server via Docker Compose for reproducibility. For isolated pure unit tests, no database is necessary.

## Workflow
1. Inspect existing code and implement the smallest runnable slice first.
2. Work in coherent, verified stages; **stop for human review before commits**. The human developer will make meaningful incremental Git commits.
3. Require three checks: cart creation + adding an item, rejection of invalid quantity, and SQL Server-backed rejection of a stale ETag. Add further tests only if practical.
4. **Docker Compose startup is required** for API + SQL Server: `docker compose up --build` with reliable database readiness and migration/seed behavior. Provide a clear README for configuration, example calls, limitations and troubleshooting. Never commit real secrets.
5. Distinguish implemented, tested, proposed and future features; do not claim Azure, OAuth, idempotent payments or outbox functionality exists.
6. Do not make public production deployment assumptions: demo endpoints may lack authentication; label them development-only.
7. Disclose AI-assisted implementation/documentation and review its output manually. Never assert tests passed without executing them.

## Delivery boundaries
Conceptual CI/CD uses automatic staging after merge to `main`, but **production requires explicit approval/promotion of the same tested image**. For PoC, local Docker Compose and working tests are higher priority than cloud deployment. Keep the implementation readable, pragmatic and small.

## Repository bootstrap
Start with an empty Git repository. Create one .NET 8 ASP.NET Core API project plus one test project; do not scaffold an entire enterprise architecture. Keep `AGENTS.md` at repository root and documentation in `docs/`. Commit work incrementally.
