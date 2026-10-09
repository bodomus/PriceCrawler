# MPC-83 investigation

## Workflow

- Level 2: operational deployment and database change orchestration.
- Base: `Codex/MPC-82` at `d30d7379481ab89031d929c5a07161092f96d45b`.
- Working tree before changes: clean.
- Graphify and CRG preflight completed; graph findings were checked against scripts, C#, configs, and the local Stage layout.

## Current operational contract

- Stage root: repository-local `stage/`.
- Releases: `stage/releases/<version>`; active copy: `stage/current`.
- External configuration: `stage/config/web/appsettings.Staging.json` and `stage/config/crawler/appsettings.Stage.json` in the current machine state.
- Web entry point: `PriceCrawler.Web.exe` or `dotnet PriceCrawler.Web.dll`.
- Worker entry point: `PriceCrawler.Worker.exe` or `dotnet PriceCrawler.Worker.dll`.
- Web URL convention: `http://localhost:8080`; health endpoint: `/health`.
- Stage database contract: `varprice_stage`; Development: `varprice`; Production: `varprice_prod`.
- PostgreSQL tools support Native and explicitly named Docker-container modes.
- Application schema startup in Stage/Staging must be `ValidateOnly`.
- Worker is a one-shot operational CLI, not a hosted daemon. Deployment therefore requires explicit Worker arguments and treats the selected command as the actual Stage workload, not a synthetic startup probe.

## Current defects

The existing script:

- trusts a separately supplied version instead of `release.json`;
- does not validate the archive, checksum, metadata, traversal, forbidden files, or schema contract;
- does not lock concurrent deployments;
- performs no database backup, refresh guard, migration, or schema verification;
- stops arbitrary processes by name, in Web-before-Worker order, and force-kills them;
- extracts directly into the final release and clears `current` before replacement is ready;
- expects obsolete `WEB` casing and obsolete `PriceCrawler.Crawler` names;
- uses one shared config without checking database/environment/`ValidateOnly`;
- starts Worker before port/health verification;
- has no PID ownership validation, phase timings, structured report, or safe failure evidence;
- has no non-mutating dry-run.

The current untracked machine-local Stage configuration points at Development and lacks `DatabaseSchema.StartupMode`; the implementation must reject it. These machine files are not modified or committed.

## Database and safety assessment

- No repository schema or schema version change is required.
- Stage backup must be verified before refresh or migration.
- Refresh source and target are fixed by explicit Development/Stage parameters; any Production-like database name is rejected.
- Missing metadata, actual schema newer than target, gaps, duplicate migrations, or a missing required migration fail closed.
- Runtime Web/Worker identities never execute migration SQL.
- No automatic database rollback or Production path is implemented.

## Smallest coherent change

Replace the unsafe legacy script with one guarded orchestration script, add C# process-level tests for package/config/dry-run contracts, and update deployment/database documentation. No C# runtime behavior or database schema needs to change.

## Expected blast radius

- Direct: `Scripts/deploy-stage.ps1`, deployment tests, deployment documentation.
- Adjacent: release ZIP contract from MPC-82, external Stage config templates, PostgreSQL tooling, Web health endpoint, Worker CLI.
- Runtime application graph: unchanged.
