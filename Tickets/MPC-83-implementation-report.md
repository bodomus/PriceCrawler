# Implementation Report

## Ticket

MPC-83 — Implement `deploy-stage.ps1`

## Workflow

- Level: 2 operational/database deployment change.
- Graphify: queried before implementation and incrementally refreshed afterward.
- CRG: updated before and after implementation; no application runtime flow impact found.
- Base/branch: `Codex/MPC-82` (`d30d737`) → `Codex/MPC-83`.
- Working tree before changes: clean.

## Scope

- Direct: `Scripts/deploy-stage.ps1`, Stage-only runtime provisioning mode, deployment tests/templates/docs.
- Database impact: orchestration only; no migration or schema-version change.
- Operational impact: replaces unsafe legacy Stage deploy with a fail-closed phase workflow.
- Production: unsupported and rejected.

## Investigation

- Actual Stage root is repository-local `stage/`, with versioned releases and an active `current` copy.
- Entry points are `PriceCrawler.Web` and `PriceCrawler.Worker`; the old script incorrectly used `PriceCrawler.Crawler`.
- Web uses port 8080 by convention and exposes `/health`.
- Stage database is `varprice_stage`; Web/Worker roles are separate.
- Worker is a one-shot CLI, so its arguments are explicit operator input rather than an implicit crawler probe.
- Existing machine-local Stage configs currently target Development and lack `ValidateOnly`; they were not modified and are correctly rejected by the new script.

## Changes

- Added package/sidecar/release metadata, migration inventory, path traversal, forbidden-file, size, component, entry-point, and Stage-only provisioning-contract validation.
- Added production-name/database-identity guards, read-only preflight, exclusive/stale lock handling, and secret-redacted phase logging.
- Added verified custom-format Stage backup and explicit guarded Development-to-Stage refresh.
- Added forward-only ordered migration execution with checksum and version verification after every file.
- Extended runtime-role provisioning with mutually exclusive `-StageOnly` / `-ProductionOnly` and dynamic `-ExpectedSchemaVersion`; Stage deploy selects only Stage and reads no Production secret.
- Added PID-file-owned Worker→Web stop, listener release check, immutable versioned extraction, external configuration validation/overlay, and safe `current.new` switch.
- Added Stage/ValidateOnly Web start, listener ownership, health retries, health-gated Worker start, and stabilization verification.
- Added deployment state, phase-rich text log, secret-free JSON report, retained failure evidence, and no automatic DB rollback.
- Added non-mutating `-WhatIf`, placeholder-only external config templates, automated tests, and operator documentation.

## Graph and source validation

- Graphify located release/schema/startup/test neighborhoods; source confirmed the exact ZIP contract, Web health endpoint, executable names, Worker CLI behavior, and `ValidateOnly` coordinator.
- CRG post-change: 18 changed files, 19 nodes, 44 edges, no affected runtime flows; operational risk score 0.50.
- Graph test-gap labels were checked against actual executed tests; focused and full suites passed.
- Graphify refreshed to 2242 nodes / 5407 edges / 116 communities.

## Post-change impact

- Application Domain/Application/Infrastructure/Web/Worker behavior is unchanged.
- Database schema, migrations, `DatabaseSchema.ExpectedVersion`, and Production appsettings are unchanged.
- Runtime provisioning remains least privilege and now supports a Stage-only deployment call.
- Current application rollback is never automatic after a schema change; database downgrade/restore is never automatic.

## Validation

- PowerShell AST: passed for deploy and provisioning scripts.
- Deploy-focused tests: 11/11.
- Deploy + runtime-role focused tests: 13/13.
- Runtime role Docker integration: 2/2, including ValidateOnly and DDL-denial probes.
- Verification release: 435 entries, SHA-256 `b57bb16f919577c4794418a1d8383aed1edd4f23c714cc66894eb252918a8630`.
- Real Docker read-only `-WhatIf`: passed; target StageRoot remained absent.
- Controlled full temporary Stage deployment: Success; backup, grants, release/current, config, owned port, healthy HTTP, Worker stabilization, state/log/report all verified; test processes stopped afterward.
- Release build: 0 warnings, 0 errors.
- Full Release suite: Worker 21/21 + Web 306/306 = 327/327.

## Documentation

Updated `CHANGELOG.md`, `README.md`, `Status.md`, `Scripts/howdeploy.md`, `db/README.md`, database environment/provisioning docs, and added `docs/stage-deployment.md` plus external config templates.

## Remaining risks

- A real operator deployment must supply valid external Stage credentials and an intentional Worker workload. The repository's current machine-local Stage configs are intentionally rejected until corrected outside Git.
- Full application deployment was validated with isolated process/PostgreSQL adapters; real PostgreSQL role/grant behavior was separately validated against the local Docker PostgreSQL integration fixture.
