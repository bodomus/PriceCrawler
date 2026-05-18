# MPC-55 - Stage database safety guards

## Ticket

[Varus] Добавить safety-guards для stage-базы `varprice_stage`.

## What was done

- Added `StageSafetyGuard` in infrastructure.
- Startup schema bootstrap is disabled by default for `Database:Target=Stage`.
- Web and Worker now skip `SchemaBootstrapper.EnsureSchemaAsync()` on Stage unless explicitly allowed.
- Direct calls to `SchemaBootstrapper.EnsureSchemaAsync()` are guarded and fail fast on Stage by default.
- Added a general guard method for destructive operations on Stage.
- Added config key `Database:AllowStageSchemaBootstrap`, default `false`.
- Updated README with Stage schema bootstrap policy and environment variable.

## Key implementation points

- Guard implementation: `VarPrice.Infrastructure/Persistence/StageSafetyGuard.cs`.
- DI registration: `AddVarPriceInfrastructure`.
- `SchemaBootstrapper` now requires `StageSafetyGuard` and checks it before schema changes.
- Web/Worker startup uses `ShouldRunStartupSchemaBootstrap()`:
  - Dev: schema bootstrap runs as before.
  - Stage: schema bootstrap is skipped by default and a warning is logged.
  - Stage with `Database:AllowStageSchemaBootstrap=true`: schema bootstrap can run intentionally.
- `EnsureDestructiveOperationAllowed(operationName)` blocks named dangerous operations for Stage.

## Validation

- `dotnet build` passed.
- `dotnet test VarPrice.Web.Tests\VarPrice.Web.Tests.csproj --filter StageSafetyGuardTests` passed.
- `dotnet test VarPrice.Web.Tests\VarPrice.Web.Tests.csproj --filter TargetDatabaseResolverTests` passed.

## Notes for next tickets

- `MPC-56` should document the Stage schema bootstrap policy and use `StageSafetyGuardTests` as coverage for guard logic.
- If future reset/seed/admin code is added, call `StageSafetyGuard.EnsureDestructiveOperationAllowed("<operation>")` before destructive database work.
- Existing SQL seed scripts are still manual scripts; README warns they must be run only against local/dev `varprice`.
