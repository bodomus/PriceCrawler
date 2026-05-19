# varprice / varprice_stage developer guide

This project uses one Docker PostgreSQL instance with two databases:

- `varprice` - local development database.
- `varprice_stage` - stage database for actual collection data.

## Create and verify databases

Start PostgreSQL:

```bash
docker compose up -d postgres
```

On a clean Docker volume, `varprice` is created by `POSTGRES_DB` and
`varprice_stage` is created by `db/init/001__create_stage_database.sql`.
PostgreSQL init scripts in `/docker-entrypoint-initdb.d` run only when the data
directory is initialized for the first time. If `var_pg_data` already exists,
new init scripts are not replayed automatically.

Verify both databases:

```bash
docker exec var_postgres psql -U var -d postgres -c "\l varprice*"
```

Expected databases:

- `varprice`
- `varprice_stage`

## Select Dev or Stage

The application chooses the database through `Database:Target`.

Dev:

```powershell
$env:Database__Target = "Dev"
$env:ConnectionStrings__PostgresDev = "Host=localhost;Port=55432;Database=varprice;Username=var;Password=myPassword"
dotnet run --project VarPrice.Web
```

Stage:

```powershell
$env:Database__Target = "Stage"
$env:ConnectionStrings__PostgresStage = "Host=localhost;Port=55432;Database=varprice_stage;Username=var;Password=myPassword"
dotnet run --project VarPrice.Web
```

Worker on Stage:

```powershell
$env:Database__Target = "Stage"
$env:ConnectionStrings__PostgresStage = "Host=localhost;Port=55432;Database=varprice_stage;Username=var;Password=myPassword"
dotnet run --project VarPrice.Worker -- --once --job vegetables
```

Invalid targets, missing connection strings, invalid connection strings, and
target/database mismatches fail at startup.

## Stage schema and data safety policy

When `Database:Target=Stage`, startup schema bootstrap is skipped by default.
This prevents automatic schema changes, legacy migrations, and legacy cleanup
from running against `varprice_stage`.

To intentionally apply schema bootstrap to Stage, set this only for that reviewed
run:

```powershell
$env:Database__AllowStageSchemaBootstrap = "true"
```

Any operation that can change schema, clean data, seed, reset, backfill, or run a
batch mutation must pass through the safety policy before it touches the
database. New code paths for these operations should use
`StageSafetyGuard.EnsureDestructiveOperationAllowed("<operation-name>")` or a
more specific safety-policy method before doing the work.

Manual SQL seed scripts are dev-only and must be run only against `varprice`.
Do not run seed/reset/backfill scripts against `varprice_stage`.

## Verification checklist

1. Run `docker compose up -d postgres`.
2. Run `docker exec var_postgres psql -U var -d postgres -c "\l varprice*"`.
3. Confirm `varprice` and `varprice_stage` exist.
4. Run Web with `Database__Target=Dev`; startup logs should show `Dev` and `varprice`.
5. Run Web or Worker with `Database__Target=Stage`; startup logs should show `Stage` and `varprice_stage`.
6. Confirm Stage logs say schema bootstrap was skipped unless `Database__AllowStageSchemaBootstrap=true` was set intentionally.
7. Run focused tests:

```bash
dotnet test VarPrice.Web.Tests/VarPrice.Web.Tests.csproj --filter TargetDatabaseResolverTests
dotnet test VarPrice.Web.Tests/VarPrice.Web.Tests.csproj --filter StageSafetyGuardTests
```
