# MPC-84 — Implement `deploy-production.ps1`

Source: https://bodomus.youtrack.cloud/issue/MPC-84/Implement-deploy-production.ps1

## Goal

Implement the final Production deployment script that accepts only the exact release ZIP proven successful by a matching Stage deployment report.

## Required flow

```text
Stage-approved ZIP
→ strict Production preflight
→ mandatory verified Production DB backup
→ stop Worker
→ stop/isolate Web
→ forward-only migrations
→ immutable versioned release
→ safe production/current switch
→ external Production configuration
→ Web start, port ownership and health verification
→ Worker start and stabilization
→ text log and JSON report
```

## Safety requirements

- Require explicit `-ConfirmProductionDeployment` for a real deployment.
- Validate ZIP structure, `release.json`, migrations, forbidden archive entries, and SHA-256 sidecar.
- Require a successful Stage report matching package SHA-256, version, commit, target schema, Web health, Worker start, and deployment timestamp.
- Target only the configured independent Production database and verify its durable `initial_bootstrap_completed=true` marker.
- Never copy or refresh Development/Stage into Production; never drop/recreate Production; never downgrade or automatically restore the database.
- Create and verify a custom-format Production backup before any database or application mutation.
- Apply only missing ordered forward migrations with the separate deployment identity.
- Provision/verify only Production runtime grants after migration; Web and Worker identities remain non-superuser and non-DDL.
- Require external Web/Worker configuration using `pricecrawler_prod_web` and `pricecrawler_prod_worker`, with `DatabaseSchema.StartupMode=ValidateOnly`.
- Stop only processes identified by verified PID metadata; Worker stops first and starts last.
- Switch `current` only after package, approval, backup, migration, grants, and release extraction succeed.
- Keep `-WhatIf` non-mutating and preserve evidence on failure.
- Do not log passwords, tokens, `.pgpass`, or connection strings.

## Deliverables

- `Scripts/deploy-production.ps1`
- Production external configuration templates
- automated script/guard tests and isolated Production-like validation
- Production deployment/recovery documentation and updates to existing operational docs
- `implementation-report.md`, `verification-report.md`, and `Review/review-MPC-84.md`

## Acceptance summary

- Exact Stage-approved artifact and explicit confirmation are mandatory.
- Production identity, database independence, backup, role separation, and `ValidateOnly` are fail-closed.
- Migration direction is forward-only and runtime roles never migrate.
- Deployment locking, immutable release/current activation, ordered process lifecycle, port/health gating, logs, and JSON evidence are implemented.
- No Development/Stage-to-Production copy path, generic force bypass, schema downgrade, or automatic database rollback exists.

The complete ticket specification is attached to MPC-84 as `pricecrawler-deploy-production-ticket.md`.
