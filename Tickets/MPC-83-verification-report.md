# MPC-83 verification report

## Result

PASS. The guarded Stage deployment implementation meets the package, database, application-file, configuration, process, reliability, and reporting contracts without changing Production or the schema version.

## Automated validation

| Check | Result |
|---|---:|
| PowerShell AST | PASS |
| DeployStageScriptTests | 11/11 |
| Deploy + runtime-role focused suite | 13/13 |
| Runtime-role Docker integration | 2/2 |
| Release build | 0 warnings / 0 errors |
| Worker Release tests | 21/21 |
| Web Release tests | 306/306 |
| Full Release total | 327/327 |

## Package validation

- Generated local verification artifact: `PriceCrawler-v0.4.1-mpc83-verification.zip`.
- Entries: 435.
- SHA-256: `b57bb16f919577c4794418a1d8383aed1edd4f23c714cc66894eb252918a8630`.
- Version/commit/schema: `v0.4.1-mpc83-verification` / `d30d7379481ab89031d929c5a07161092f96d45b` / `1 -> 1`.
- MPC-83 deploy validation accepted package structure, metadata, migration inventory, and Stage-only provisioning support.
- Tests prove checksum mismatch, traversal, `.env`, Graphify data, wrong product, and schema metadata mismatch are rejected.

## Dry run

Real Docker read-only preflight against local `varprice_stage` passed with valid isolated configs. It read schema version 1 and printed the complete deployment plan. The requested temporary StageRoot remained absent, proving no lock, backup, extraction, log/report, process, current, or database mutation.

Production-like Stage target test failed before configuration/tool/database access and created no StageRoot.

## Full temporary Stage workflow

A controlled isolated deployment executed every normal phase:

```text
PackageValidation → Preflight → DatabaseBackup → StopProcesses
→ OptionalDatabaseRefresh(no-op) → Migration(no-op at target)
→ RuntimeGrants(StageOnly) → ReleaseExtraction → Configuration
→ CurrentSwitch → WebStart → PortCheck → HealthCheck
→ WorkerStart → PostDeployVerification → Report
```

Result report:

- result: `Success`;
- version: `v9.9.9-mpc83-e2e`;
- schema after: `1`;
- Web listener owned by started process;
- health: `Healthy` with structured `{"ok":true}`;
- Worker survived stabilization;
- verified backup and SHA-256 present;
- deployment state/log/JSON report present;
- Web and Worker test processes stopped after evidence collection.

The integration used an isolated StageRoot, deterministic PostgreSQL command adapters, and non-crawling probe processes. No real crawler run or real Stage/Production mutation occurred. Runtime role SQL/grant/ValidateOnly/DDL behavior was separately exercised against the real local Docker PostgreSQL test fixture.

## Safety confirmations

- Production cannot be selected by Stage deploy.
- Development refresh requires an explicit switch and has no Production destination path.
- Verified Stage backup precedes refresh/migration.
- Only missing contiguous forward migrations run; downgrade is absent.
- Runtime roles do not execute migrations and cannot perform DDL.
- `current` switches only after database success and complete extraction/configuration.
- Web health gates Worker.
- Processes are never selected by name alone.
- External config must use Stage DB, separate runtime roles, and `ValidateOnly`.
- Password parameters/logging and automatic database rollback are absent.
- Migration files, schema contract/version, Production bootstrap, and Production configuration were not changed or executed.
