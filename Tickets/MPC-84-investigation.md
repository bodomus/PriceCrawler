# MPC-84 investigation

## Repository intelligence

Graphify located the deployment boundary around `deploy-stage.ps1`, release metadata, runtime-role provisioning, schema versioning, `ValidateOnly`, and the Web/Worker host tests. CRG confirmed no production application symbol or DI change is required: the change is operational tooling, configuration templates, tests, and documentation. Source inspection remained authoritative for PowerShell, ZIP, SQL, and configuration behavior.

## Existing contracts

- Production root defaults to repository-local `production/`; filesystem root and repository root are unsafe targets.
- configured Production database defaults to `varprice_prod`; Development/Test/Stage names are distinct guard inputs.
- deploy identity is supplied explicitly and authenticates externally; runtime identities are `pricecrawler_prod_web` and `pricecrawler_prod_worker`.
- release entry points are `web/PriceCrawler.Web(.exe|.dll)` and `crawler/PriceCrawler.Worker(.exe|.dll)`.
- Production URL is explicit; health defaults to `/health`.
- external configs default to `production/config/web/appsettings.Production.json` and `production/config/crawler/appsettings.Production.json`.
- Stage report fields are `environment`, `result`, `finishedAtUtc`, package version/commit/SHA, database target result, Web port/health, and Worker start.
- existing process lifecycle uses PID JSON under `runtime`, exact executable/command-line ownership, port ownership, and hidden processes.
- database backups are retained; the deploy script never deletes them. Retention/off-host policy belongs to operators/DBA.

## Risk conclusions

- Production bootstrap must not run and the database-level independence comment must be read-only verified.
- no database source-copy, restore, drop/recreate, downgrade, or automatic recovery path belongs in Production deploy.
- package migrations and runtime grants must execute with the separate deploy identity; applications remain `ValidateOnly`.
- current activation must occur only after verified backup, forward migration/no-op, grants, and complete extraction.
- Web listener ownership and health must gate Worker startup.
