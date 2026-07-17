# Implementation Report

## Ticket

MPC-82 — Extend release package with database migrations and schema metadata.

## Workflow

- Level: 2.
- Base: `codex/MPC-81` at `4288531ada4bfbc7973f7086db4533a03348f942`.
- Graphify: used for release/schema/Web/Worker/test orientation.
- CRG: preflight and post-change incremental update completed.
- Working tree before MPC-82: clean; only the required local ticket specification was then added.

## Scope

- Projects: release PowerShell tooling, Web test project, operator/release documentation.
- Database impact: none; schema remains version `1`.
- Operational impact: release packages become self-describing, checksum-protected and consumable without repository access.
- Public contract: archive root and `release.json` are explicit deployment contracts.

## Investigation

- Current behavior: MPC-81 already copied database assets and emitted partial metadata.
- Expected behavior: validated minimal package, canonical metadata, deterministic layout, secret exclusions, checksum and explicit overwrite policy.
- Root gaps: silent overwrite, no checksum, partial metadata/archive checks, non-canonical default version, unsanitized local connection string, and static-only tests.
- Main source: `Scripts/build-release.ps1`, `DatabaseSchema.ExpectedVersion`, `version.json`, migration/bootstrap assets and `ReleaseDatabasePackagingTests`.
- Expected blast radius: release creation and future deploy consumers only.

## Changes

- Added `-OutputDirectory`, `-ReplaceExistingArtifact`, and input-only validation mode.
- Default version now resolves through NBGV MSBuild metadata; exact Git revision is cross-checked.
- Migration inventory validates filename format, duplicates, ordering, baseline/bootstrap metadata and target schema version.
- Release staging removes Development/Test appsettings and host-duplicated legacy DB initialization assets.
- Base packaged connection strings are rewritten to safe external-secret placeholders.
- Staging and archive validators enforce required roots/entry points, normalized separators, component/schema metadata, `ValidateOnly`, forbidden paths, secret-like configuration checks and no developer absolute paths.
- ZIP entries are created in ordinal order with one normalized UTC entry timestamp.
- SHA-256 sidecar is generated and re-read for verification.
- Existing artifacts are refused unless replacement is explicitly requested.
- Added behavioral tests for missing/duplicate migrations, caller-independent operation, overwrite refusal/replacement, real ZIP content, metadata, safety and checksum.
- Updated changelog, status, README, database and deployment documentation.

## Graph and source validation

- Graphify findings: package builder is adjacent to Web/Worker publish and the schema version contract; no runtime orchestration change is needed.
- CRG findings: no affected application flow; changed test functions cover the new operational boundary.
- Source validation: NBGV `GetBuildVersion`, `DatabaseSchema.ExpectedVersion=1`, `0001_baseline.sql`, bootstrap metadata, Web/Worker project publish content and environment templates inspected directly.
- Discrepancy resolved: the existing documentation said tag-only version resolution and `artifacts/release`; implementation now uses NBGV by default and `artifacts/releases`.

## Post-change impact

- CRG updated: yes.
- Direct blast radius: builder/tests/docs.
- Unexpected dependants: none.
- Migration/deployment concern: future migrations require updated numbered files and canonical schema contract together; build fails on mismatch.
- Graphify refresh: not required because project/module/runtime relationships did not change.

## Validation

- Focused tests: `ReleaseDatabasePackagingTests` 7/7.
- Actual archive: created and independently inspected; 435 files, four roots, forbidden count 0.
- Release build: passed, 0 warnings / 0 errors.
- Full Release tests: 316/316.
- Checksum: matched (`79cc23933cb810bb04beeeff734b5304a9cc22a65dd41f651763ed1c76af176d`).
- Canonical version build: resolved `v0.4.1-alpha.6+4288531ada` from NBGV without a manual version override.
- Database: only temporary local integration-test databases from the full suite; no Stage/Production access.

## Documentation

Updated:

- `CHANGELOG.md`;
- `README.md`;
- `Status.md`;
- `Scripts/howdeploy.md`;
- `db/README.md`;
- `docs/database-environments.md`;
- MPC-82 investigation, plan, inventory and verification reports.

## Remaining risks

- Byte-for-byte reproducibility is not claimed because `builtAtUtc` changes per build; entry ordering, separators and timestamps are deterministic within each build.
- Official releases should not use `-SkipTests`, `-AllowDirtyWorkingTree`, `-ReplaceExistingArtifact`, or an ad-hoc explicit version.
- Future deployment scripts must validate the checksum and metadata before applying forward migrations.
