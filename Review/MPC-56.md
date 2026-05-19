# MPC-56 - Documentation and tests for varprice / varprice_stage

## Ticket

[Varus] Обновить документацию и тесты для схемы `varprice` / `varprice_stage`.

## What was done

- Added a dedicated developer guide: `docs/varprice-stage.md`.
- Linked the guide from README quick start.
- Documented the two PostgreSQL databases:
  - `varprice` for Dev.
  - `varprice_stage` for Stage.
- Documented Docker startup and database verification command.
- Documented explicit Dev / Stage selection through `Database:Target`.
- Documented Stage schema bootstrap policy:
  - skipped by default on Stage.
  - allowed only with explicit `Database:AllowStageSchemaBootstrap=true`.
- Documented the safety-policy rule for schema/data mutation operations.
- Added focused test commands to README and the guide.

## Existing test coverage used for this ticket

- `TargetDatabaseResolverTests`
  - Dev selects `varprice`.
  - Stage selects `varprice_stage`.
  - invalid target fails fast.
  - missing connection string fails fast.
  - target/database mismatch fails fast.
- `StageSafetyGuardTests`
  - Dev startup schema bootstrap is allowed.
  - Stage startup schema bootstrap is skipped by default.
  - Stage bootstrap can be explicitly allowed.
  - direct Stage schema bootstrap fails fast by default.
  - destructive operation guard blocks Stage.

## Key implementation/doc points

- Main guide: `docs/varprice-stage.md`.
- README keeps a short quick-start summary and points to the guide.
- Any operation that can change schema, clean data, seed, reset, backfill, or run a batch mutation must pass through safety policy before touching the database.
- Future mutation code should call `StageSafetyGuard.EnsureDestructiveOperationAllowed("<operation-name>")` or a more specific safety-policy method.

## Validation

- Checked README and `docs/varprice-stage.md` for old `ConnectionStrings:Postgres` / `ConnectionStrings__Postgres` drift.
- `dotnet test VarPrice.Web.Tests\VarPrice.Web.Tests.csproj --filter TargetDatabaseResolverTests` passed.
- `dotnet test VarPrice.Web.Tests\VarPrice.Web.Tests.csproj --filter StageSafetyGuardTests` passed.

## Notes

- The first parallel `StageSafetyGuardTests` run hit a transient SourceLink `obj` file lock because another test command was building at the same time. The test was rerun alone and passed.
- Full integration tests were not run because existing integration tests touch/truncate the local `varprice` database.
