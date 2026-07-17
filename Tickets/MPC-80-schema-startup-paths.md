# MPC-80 — schema startup path inventory

## Runtime paths before MPC-80

```text
PriceCrawler.Web/Program.cs
  -> DatabaseSchemaStartupService.ValidateAndInitializeAsync(environment)
     -> optional SchemaBootstrapper.EnsureSchemaAsync()
     -> DatabaseSchemaVersionReader.ReadAsync()
  -> app.Run()

PriceCrawler.Worker/Program.cs
  -> host.Build()
  -> DatabaseSchemaStartupService.ValidateAndInitializeAsync(environment)
     -> optional SchemaBootstrapper.EnsureSchemaAsync()
     -> DatabaseSchemaVersionReader.ReadAsync()
  -> resolve and execute Worker use case
```

No calls to EF Core `EnsureCreated`, `Migrate`, or `MigrateAsync` exist.

## Mutation performed by SchemaBootstrapper

`SchemaBootstrapper.EnsureSchemaAsync()` can:

1. rename legacy application tables;
2. execute all DDL in `schema.sql`;
3. execute every changed `db/routines/*.sql` file;
4. insert/update `db_routine_script` hashes;
5. migrate legacy application rows;
6. create temporary migration objects;
7. drop legacy tables;
8. commit all operations in a transaction.

The method is called directly outside startup only by PostgreSQL integration tests in:

- `WorkerIntegrationTests`;
- `ProductCatalogRepositoryTests`;
- `CrawlerRunStatisticsIntegrationTests`.

These are test preparation paths, not Web or Worker runtime paths.

## Deployment-only mutation assets

- `db/migrations/0001_baseline.sql` — version 1 baseline for an empty database.
- `db/scripts/bootstrap-schema-version.sql` — explicit operator registration of an existing compatible schema.
- `scripts/deploy-stage.ps1` — controlled Stage deployment path.

Neither baseline nor bootstrap is invoked by Stage or Production application startup.

## Required path after MPC-80

```text
Web / Worker
  -> DatabaseSchemaStartupCoordinator
     -> DatabaseSchemaStartupPolicy hard guard
     -> Ensure
        -> DatabaseSchemaInitializer
           -> empty DB: 0001_baseline.sql
           -> existing Dev/Test DB: SchemaBootstrapper
        -> DatabaseSchemaValidator (read-only)
     -> ValidateOnly
        -> DatabaseSchemaValidator (read-only)
```

The policy must run before initializer or version reader access. `Ensure` is allowed only for Development and Test. Stage, Staging, Production, and unknown environments must reject `Ensure` rather than silently coercing it.

