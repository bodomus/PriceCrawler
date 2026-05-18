# MPC-54 - Target database selection

## Ticket

[Varus] Реализовать выбор target database (Dev / Stage) в приложении.

## What was done

- Added explicit `DatabaseTarget` values: `Dev` and `Stage`.
- Added centralized target database resolution in infrastructure.
- Added separate connection strings:
  - `ConnectionStrings:PostgresDev`
  - `ConnectionStrings:PostgresStage`
- Mapped `Dev` to database `varprice`.
- Mapped `Stage` to database `varprice_stage`.
- Updated Web and Worker startup to log selected target and database name.
- Updated Docker environment variables to use the new config keys.
- Updated README to document the new config keys and Stage switching examples.

## Key implementation points

- Central resolver: `VarPrice.Infrastructure/Persistence/TargetDatabaseResolver.cs`.
- Shared selected database record: `SelectedDatabase`.
- EF `VarPriceDbContext` and raw `PgConnectionFactory` now use the same resolved connection string.
- Invalid target, missing connection string, invalid connection string, and target/database mismatch fail fast.
- Web no longer registers `VarPriceDbContext` separately; database registration is centralized in `AddVarPriceInfrastructure`.

## Validation

- `docker compose config` passed.
- `dotnet build` passed.
- `dotnet test VarPrice.Web.Tests\VarPrice.Web.Tests.csproj --filter TargetDatabaseResolverTests` passed.

## Notes for next tickets

- `MPC-55` should build on `SelectedDatabase.Target` to block stage-dangerous paths.
- `MPC-56` can reference existing README sections and `TargetDatabaseResolverTests`.
- Full integration tests were not run during MPC-54 because they touch/truncate the local `varprice` database.
