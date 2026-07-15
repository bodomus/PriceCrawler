# AGENTS.md

## Mandatory pre-ticket workflow

Before starting any non-trivial ticket, feature, bugfix, refactor, investigation,
implementation-planning, database change, deployment change, or code review:

1. Resolve the repository root:

   ```powershell
   git rev-parse --show-toplevel
   ```

2. Read the repository-root file `.codex/PRE_TICKET_WORKFLOW.md`.
3. Use `$graphify-repository-analysis`.
4. Use `$code-review-graph-analysis`.
5. Execute all applicable preflight phases.
6. Do not begin implementation until repository-intelligence preflight is complete.
7. After implementation, update CRG, inspect impact radius, run validation, and
   refresh Graphify when structural relationships changed.

For spelling, formatting, comment-only, or metadata-only changes, the full graph
preflight may be skipped when graph context cannot affect correctness.

## Project scope

This repository contains VARUS Price Crawler / PriceCrawler.

Primary stack:

- .NET SDK 9.0.311 pinned by `global.json`;
- projects currently targeting `net8.0`;
- C# 12 with nullable reference types and implicit usings;
- ASP.NET Core Web host;
- console Worker host;
- Domain, Application, Infrastructure, Web, Worker, and test projects;
- EF Core with Npgsql;
- PostgreSQL 16;
- Docker Compose;
- GitHub Actions;
- Nerdbank.GitVersioning and SourceLink;
- server-rendered MVC/Kendo dashboard plus JavaScript assets.

Analysis must account for:

- dependency-injection registration;
- ASP.NET Core request, anti-forgery, model-binding, and cancellation behavior;
- Worker command parsing and Generic Host startup;
- hosted-service and process exit semantics;
- EF Core model configuration and query translation;
- PostgreSQL transactions, locking, CTEs, and `FOR UPDATE SKIP LOCKED`;
- queue reservation, retries, timeouts, cancellation, and concurrency;
- idempotency, normalization, and duplicate prevention;
- crawler discovery, listing-page, and product-page strategies;
- catalog refresh and price-collection lifecycle;
- development, test, stage, and production database safety;
- configuration precedence and secrets;
- Web, Worker, SQL routines, and schema compatibility;
- release, artifacts, stage deployment, and rollback behavior.

## Repository layout

- `PriceCrawler.Domain/` — domain entities, value concepts, and contracts.
- `PriceCrawler.Application/` — use cases, orchestration, DTOs, and application interfaces.
- `PriceCrawler.Infrastructure/` — EF/Npgsql persistence, SQL integration, crawler adapters,
  queue pipeline, extractors, discovery, queries, and schema bootstrap.
- `PriceCrawler.Web/` — ASP.NET Core MVC/API host, dashboard, static assets, and health endpoint.
- `PriceCrawler.Worker/` — crawler CLI and worker host.
- `PriceCrawler.Web.Tests/` — automated and integration tests.
- `db/` — database routines, initialization, migrations/bootstrap assets, and local-only seeds.
- `docs/` — maintained technical documentation.
- `Tickets/` — ticket specifications and acceptance criteria.
- `Review/` — review reports.
- `artifacts/` — generated release packages; not production source.
- `.codex/` — Codex workflows and repository automation.
- `.agents/skills/` — repository-local Codex skills.
- `.code-review-graph/` — generated CRG state.
- `graphify-out/` — generated Graphify state.

## Generated and non-source directories

Do not treat these as production source:

- `.git/`
- `.idea/`
- `.vs/`
- `**/bin/`
- `**/obj/`
- `**/publish/`
- `**/TestResults/`
- coverage output;
- `artifacts/`
- `logs/`
- `.code-review-graph/`
- `graphify-out/`
- vendored frontend bundles under `PriceCrawler.Web/wwwroot/vendor/`.

Do not index generated, vendor, cache, database, log, release, or build output in
Graphify or CRG unless the ticket explicitly concerns it.

Do not exclude the complete `PriceCrawler.Web/wwwroot/` tree: project-owned JavaScript
and CSS may be behaviorally relevant. Exclude only vendor/generated assets.

## Repository intelligence routing

- Use Graphify for solution architecture, subsystem orientation, orchestration flows,
  discovery-strategy relationships, queue/catalog concepts, and cross-project candidate discovery.
- Use CRG for exact classes, interfaces, implementations, callers, callees, inheritance,
  DI registrations, dependants, tests, review context, and change-impact analysis.
- Treat graph output as candidate evidence.
- Use `rg`, source inspection, SQL inspection, build output, and tests as authoritative.
- Confirm generated or inferred call paths, reflection-based registration, EF mappings,
  extension-method registration, and configuration binding directly in source.
- When Graphify, CRG, documentation, and source disagree, verify executable behavior and
  treat current source plus tests as authoritative.

## Architecture and dependency rules

Preserve the intended dependency direction:

```text
Domain
  ↑
Application
  ↑
Infrastructure
  ↑
Web / Worker
```

Exact project references must be verified before making dependency claims.

General rules:

- Domain must not depend on Infrastructure, Web, Worker, EF Core, or ASP.NET Core.
- Application contracts must not expose EF-specific query types unless already established
  intentionally.
- Web controllers must not introduce ad-hoc EF/SQL access when an application contract and
  infrastructure query service should own it.
- Worker orchestration must reuse application/infrastructure services rather than duplicate
  crawler or persistence logic.
- Source-specific VARUS behavior should remain behind explicit strategies/adapters where practical.
- Avoid static service location and hidden global state.
- Preserve cancellation-token propagation across HTTP, database, queue, and worker boundaries.

## Database and persistence safety

Database changes are high-risk.

Before changing schema, routines, EF mappings, queue logic, or persistence:

1. inspect relevant entities, `DbContext`, configurations, SQL routines, bootstrap scripts,
   and integration tests;
2. identify all environments affected;
3. verify transaction boundaries and locking semantics;
4. preserve idempotency and retry safety;
5. verify nullable/default/backfill behavior;
6. update SQL routines and C# mappings together;
7. update documentation when schema or workflow changes.

Never run destructive local seed scripts against stage, shared, or production databases.

The local debug seed under `db/seeds/` must remain explicitly local/dev-only.

Do not weaken stage/production guards.

Do not log connection strings, credentials, cookies, authentication headers, or sensitive
configuration.

## Queue and crawler safety

For queue, catalog, and crawler changes, inspect:

- URL normalization and deduplication;
- page-kind classification;
- listing/filter versus product-page routing;
- reservation ownership and expiration;
- `SKIP LOCKED` semantics;
- retry and terminal-failure classification;
- timeout and cancellation behavior;
- rate limits and concurrency;
- run/ingestion lifecycle status;
- snapshot and error persistence;
- scheduling fields in `product_catalog`;
- idempotent reprocessing;
- progress-reporting counters.

Do not increase request rate, concurrency, or retry aggressiveness without explicit ticket
requirements and review of blocking risk.

Do not convert HTTP 200 into success without validating extractor output.

## Web and dashboard safety

For dashboard changes:

- preserve anti-forgery protection for state-changing actions;
- avoid implicit live HTTP calls during normal deterministic page loading;
- keep live VARUS refresh explicit and read-only unless a ticket changes that contract;
- preserve server-side paging/filtering/sorting where established;
- keep Web free of direct persistence logic when infrastructure query services exist;
- validate JSON contracts against JavaScript consumers;
- inspect project-owned assets under `wwwroot`, but ignore vendored bundles;
- add tests for controller/application contracts when behavior changes.

## Worker CLI safety

The Worker CLI is a public operational contract.

When changing it:

- validate arguments before host creation and database bootstrap;
- preserve documented exit codes;
- preserve legacy aliases unless removal is explicit;
- do not pass Worker CLI arguments into Generic Host as accidental configuration overrides;
- preserve redirected-output and non-interactive behavior;
- keep progress rendering out of file logs;
- verify cancellation and process termination.

## Build and validation

Canonical commands documented by the repository:

```powershell
dotnet --info
dotnet restore PriceCrawler.sln
dotnet build PriceCrawler.sln
dotnet test PriceCrawler.sln
```

For write-side DB routines or crawler persistence, run the focused integration suite first:

```powershell
dotnet test PriceCrawler.Web.Tests\PriceCrawler.Web.Tests.csproj --filter "FullyQualifiedName~WorkerIntegrationTests"
```

Release builds treat warnings as errors:

```powershell
dotnet build PriceCrawler.sln -c Release
```

Run the narrowest relevant tests first, then the solution suite when justified.

Docker/PostgreSQL validation may require:

```powershell
docker compose up -d postgres
```

Do not assume Docker or a database is available. Report unavailable validation explicitly.

## Documentation and release obligations

- Update `CHANGELOG.md` for user-visible changes.
- Update `README.md`, `Status.md`, and relevant `docs/*` when schema, CLI, workflow,
  deployment, or operational behavior changes.
- Keep `db/routines/*.sql`, C# persistence code, and `WorkerIntegrationTests` aligned.
- Preserve deterministic/versioned release behavior.
- Do not modify generated release artifacts manually.

## Change safety

- Do not modify production code during investigation-only tasks.
- Do not overwrite, reset, clean, stash, revert, or destroy pre-existing user changes.
- Keep scope aligned with the ticket.
- Avoid unrelated refactors.
- Preserve public contracts unless the ticket explicitly changes them.
- Distinguish direct impact, adjacent impact, test-only impact, migration impact,
  deployment impact, and graph-proximity noise.

## Definition of done

A non-trivial ticket is not complete until:

- the applicable pre-ticket workflow was executed;
- important graph findings were validated against source;
- database and operational risks were assessed;
- the smallest coherent implementation was completed;
- tests were added or updated for changed behavior;
- CRG was updated after changes;
- post-change impact was inspected;
- required build/tests were run or explicitly reported unavailable;
- docs and changelog obligations were evaluated;
- remaining risks and unverified assumptions were documented;
- an implementation report was produced.
