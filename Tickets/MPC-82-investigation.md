# MPC-82 Investigation
## Workflow

- Level: 2 (release/deployment operational contract).
- Base branch: `codex/MPC-81`, commit `4288531ada4bfbc7973f7086db4533a03348f942`.
- Graphify: queried existing graph for release builder, Web/Worker publish, schema contract, migrations and tests.
- CRG: incremental update and change detection completed; release packaging is a PowerShell/test/documentation boundary with no application runtime flow changes.
- Database source of truth reviewed: `docs/database-environments.md`.

## Current behavior

`Scripts/build-release.ps1` already:

- resolves the repository root from `$PSCommandPath`;
- publishes Web and Worker into `web/` and `crawler/`;
- copies `db/migrations`, `db/scripts`, `db/README.md`, and runtime-role provisioning support;
- reads `DatabaseSchema.ExpectedVersion` from the C# contract;
- validates baseline/bootstrap presence and migration numbering;
- generates a basic `release.json`;
- creates a ZIP and performs a narrow database-entry check.

This partial behavior was introduced while completing MPC-79/MPC-81 and is useful source-verified groundwork, but it does not satisfy the complete MPC-82 release contract.

## Implementation gaps

1. The existing ZIP is deleted unconditionally, so a released version can be silently overwritten.
2. There is no `-OutputDirectory` or explicit `-ReplaceExistingArtifact` policy.
3. No SHA-256 sidecar is generated or verified.
4. `release.json` uses `createdUtc` and an array of component names; full required metadata semantics and archive/component consistency are not validated.
5. The default version is an exact Git tag rather than the canonical Nerdbank.GitVersioning value. NBGV is available through the project MSBuild `GetBuildVersion` target; `AssemblyInformationalVersion` resolves to `0.4.1-alpha.6+4288531ada` at the baseline commit.
6. Staged contents are not validated before ZIP creation.
7. Final archive validation checks only selected database paths and schema versions. It does not verify host entry points, root layout, exact commit/version/timestamp, forbidden paths, secrets, or unexpected nesting.
8. `Compress-Archive` does not make deterministic entry ordering explicit.
9. Migration validation reports non-contiguous numbering but does not model an explicit inventory/duplicate contract reusable by tests.
10. Existing tests are mostly static source assertions and do not cover packaging behavior, replacement policy, checksum, metadata/archive consistency, or safety exclusions.

## Canonical contracts

- Application version: NBGV `GetBuildVersion` output from `PriceCrawler.Application.csproj`; an explicit `-Version` remains a supported operator override and is logged in metadata.
- Git revision: `git rev-parse HEAD`.
- Database target: `PriceCrawler.Infrastructure.Persistence.DatabaseSchema.ExpectedVersion` (`1`).
- Migration inventory: numbered SQL files under `db/migrations`, currently `0001_baseline.sql`.
- Bootstrap support: `db/scripts/bootstrap-schema-version.sql`.
- Stage/Production startup: `ValidateOnly`; packaging must not execute SQL or alter configuration to `Ensure`.

## Safety and impact

- Database schema/data impact: none. No migration or database command is executed.
- Runtime Web/Worker behavior: unchanged.
- Direct impact: release builder, release packaging tests, operator documentation.
- Adjacent impact: future Stage/Production deployment scripts consuming `release.json` and `db/`.
- Forbidden input/output: dumps, backups, logs, `.env`, `.pgpass`, secrets, graph databases, test results, repository metadata, and machine-specific absolute paths.
- Existing generated `artifacts/` content is not production source and will not be edited manually.

## Smallest coherent change

Harden the existing builder rather than replace the release pipeline:

- add explicit output/replacement policy;
- resolve canonical NBGV metadata and exact commit;
- build a validated migration inventory;
- validate staged and archived package contracts with a shared allow/deny policy;
- create the ZIP in sorted entry order;
- generate and verify a SHA-256 sidecar;
- expand automated tests and documentation;
- keep schema version `1` and preserve `ValidateOnly`.

## Required validation

- PowerShell AST parsing and `git diff --check`;
- focused `ReleaseDatabasePackagingTests`;
- actual release creation from a non-repository working directory;
- archive listing, metadata parse, checksum comparison and forbidden-content scan;
- explicit existing-artifact refusal and replacement opt-in test;
- Release solution build and full test suite;
- post-change CRG update and impact inspection.
