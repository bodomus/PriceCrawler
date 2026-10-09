# MPC-81 — implementation plan

## Scope

Implement and execute the one-time environment provisioning workflow without changing application schema version, baseline SQL, runtime business behavior, or existing Development data.

## Plan

1. Implement `scripts/initialize-database-environments.ps1`.
   - Use `SupportsShouldProcess` and explicit operation switches.
   - Validate unique, safe database identifiers before tool access.
   - Require replacement switches for existing Test/Stage.
   - Require `-ConfirmInitialProductionBootstrap` for Production.
   - Support native PostgreSQL CLI or explicit Docker container execution.
   - Never accept or print a password/full connection string.

2. Implement shared preflight and validation.
   - Confirm CLI tools/container and PostgreSQL connectivity.
   - Resolve expected version from `DatabaseSchema.cs`.
   - Validate Development metadata and all critical baseline objects.
   - Refuse source/destination collisions and active Development clients.
   - Capture critical Development row counts.

3. Implement artifacts.
   - Custom-format Development dump with `--no-owner`, `--no-privileges`, and consistency options.
   - Non-zero size and SHA-256 validation.
   - Stage pre-replacement backup with checksum.
   - Production initial backup with checksum.
   - Sanitized operator log and Markdown bootstrap report.

4. Implement environment operations.
   - Test: recreate only when explicitly authorized, apply baseline, verify version/objects, verify zero business rows.
   - Stage: back up when replacing, recreate, restore Development dump, verify version/objects/counts.
   - Production: refuse any initialized target, create and restore once, verify, write durable independence marker, back up, verify a second attempt is rejected.
   - Optionally grant an explicitly named existing runtime role without creating or handling its credentials.

5. Add automated tests.
   - Fast process tests for name/operation/confirmation/replacement/tool guards and secret-safe output.
   - Docker PostgreSQL integration test using temporary source/Test/Stage/Production names.
   - Verify baseline-only Test, Stage/Production row-count equality, marker, backup/checksum/report, and second-bootstrap refusal.
   - Preserve cleanup of temporary databases even on failure.

6. Add safe connection-string templates and update documentation.
   - Test/Stage/Production targets are distinct.
   - Stage/Production remain `ValidateOnly`.
   - Document Docker/native commands, replacement rules, backups/restores, runtime-role requirements, and secret sources.
   - State: `After initial bootstrap, Production must never be replaced from Development.`

7. Validate before touching real targets.
   - Parser/guard tests and `-WhatIf`.
   - Focused integration test on temporary databases.
   - Solution build/tests.

8. Execute actual provisioning only after tests pass.
   - `varprice_test` with explicit replacement.
   - `varprice_stage` with explicit replacement and pre-backup.
   - `varprice_prod` with one-time confirmation.
   - Verify all databases, versions, objects, counts, artifacts, marker, and second Production refusal.
   - Smoke Web/Worker in `ValidateOnly` with explicit temporary connection overrides; do not start crawler work.

9. Run post-change CRG/Graphify analysis, final build/tests, secret scan, and create `Review/review-MPC-81.md`.

## Follow-up plan: runtime roles

1. Add `scripts/provision-database-runtime-roles.ps1` as an operation independent from database bootstrap.
2. Create four parameterized login roles with safe fixed defaults and passwords read only from named environment variables.
3. Revoke database/schema creation and PUBLIC routine execution; preserve the separate deploy/object-owner identity.
4. Grant current application table/sequence access, a Web routine allowlist, and the complete Worker operational routine catalog; configure safe default privileges.
5. Verify attributes, ownership, schema version, `CREATE TABLE` denial, and `ALTER TABLE` denial.
6. Update Web/Worker templates with distinct usernames and document secret-store connection-string injection.
7. Add temporary PostgreSQL integration coverage that starts Stage/Production Web and Worker in `ValidateOnly` under the new roles.
8. Do not execute Production bootstrap or change schema version.

## Expected blast radius

- Direct: provisioning PowerShell script, its tests, environment connection templates, DB/deployment docs.
- Operational: local Docker PostgreSQL databases `varprice_test`, `varprice_stage`, and new `varprice_prod`; ignored dump/backup/log artifacts.
- Adjacent: Web/Worker startup smoke only; no runtime source changes expected.
- No impact: Domain/Application contracts, EF mappings, routines, baseline version, crawler concurrency/queue behavior.
