# MPC-84 implementation report

## Implemented

- Added `Scripts/deploy-production.ps1` with strict ZIP/sidecar/release validation and exact successful Stage-report approval.
- Added explicit real-deploy confirmation, deployment lock, safe-root/database-name guards, Production independence marker and role separation checks.
- Added mandatory custom-format backup with size, `pg_restore --list`, and SHA-256 verification before process/database/application mutation.
- Added Worker-first/Web-second PID-owned shutdown, port release, forward-only ordered migrations, and packaged `-ProductionOnly` runtime grants/DDL probes.
- Added immutable version release extraction, explicit replacement evidence, external Production config overlay, safe `current.new` activation, and previous-current evidence.
- Added Production + `ValidateOnly` process environment, Web listener ownership, health gate, Worker stabilization, post-deploy schema/process checks, deployment state, text log, and JSON report.
- Added non-mutating `-WhatIf`, sanitized failure reporting, and explicit `databaseRollbackAutomatic=false` / `databaseCopySupported=false` evidence.
- Added separate Production Web/Worker configuration templates with external secret placeholders.
- Added automated Production deployment contract, Stage mismatch, confirmation, marker, template, and dry-run tests.
- Added `docs/production-deployment.md` and updated release, environment, provisioning, database, README, and changelog documentation.

## Explicit exclusions

The Production script contains no Development/Stage database copy, database refresh, logical restore, database drop/recreate, bootstrap, schema downgrade, generic force bypass, runtime-role migration, or automatic database rollback path. Production bootstrap was not run and schema version was not changed.
