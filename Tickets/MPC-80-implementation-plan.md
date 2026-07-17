# MPC-80 — implementation plan

1. Replace boolean initialization options with `DatabaseSchemaStartupMode` and a single `StartupMode` option whose safe default is `ValidateOnly`.
2. Add a pure startup policy that permits `Ensure` only for Development/Test and throws an actionable configuration exception before database access for every other environment.
3. Separate responsibilities:
   - `DatabaseSchemaInitializer` performs explicit Ensure behavior;
   - `DatabaseSchemaValidator` performs read-only metadata validation;
   - `DatabaseSchemaStartupCoordinator` applies policy, executes the selected path, and emits structured logs.
4. Use `0001_baseline.sql` for an empty Development/Test database; retain `SchemaBootstrapper` only for an existing approved Development/Test schema.
5. Replace Web and Worker startup calls with the coordinator. Keep validation before `app.Run()` and before Worker use-case resolution/execution; move the Worker “command started” message after successful validation.
6. Update Web/Worker configuration for Development, Test, Stage, Staging, and Production. Add the missing `appsettings.Stage.json` expected by deployment. Copy the baseline into Web/Worker output/publish assets.
7. Expand tests:
   - options/configuration precedence and environment-variable override;
   - hard guard before database access;
   - empty and repeated Ensure initialization;
   - Stage/Production exact/missing/older/newer validation;
   - unchanged schema object/application row counts;
   - successful validation with a login role lacking DDL privileges;
   - Web failed-start port gating;
   - Worker failed-start work gating;
   - structured, actionable, secret-free errors/logging contracts.
8. Update database, architecture, deployment, startup, status, and changelog documentation without changing schema version or deployment scripts.
9. Run focused PostgreSQL/process tests, Worker integration tests, Release build, full solution tests, runtime smoke checks, CRG post-impact analysis, and Graphify refresh.
10. Produce `Review/review-MPC-80.md`, add a YouTrack completion comment, and set MPC-80 to Done only after all checks pass.

