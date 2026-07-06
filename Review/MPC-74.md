# MPC-74 Review

## Summary

Renamed the product/codebase identity from `VarPrice` to `PriceCrawler` across the active .NET solution.

Implemented on branch:

```text
codex/MPC-74
```

Preserved database and persistent infrastructure identifiers:

```text
varprice
varprice_test
varprice_stage
var_pg_data
```

No database schema changes, migrations, destructive Docker commands, or data cleanup were performed.

## Files changed

- Renamed solution: `VarPrice.sln` -> `PriceCrawler.sln`.
- Renamed active project directories and project files:
  - `VarPrice.Application` -> `PriceCrawler.Application`
  - `VarPrice.Domain` -> `PriceCrawler.Domain`
  - `VarPrice.Infrastructure` -> `PriceCrawler.Infrastructure`
  - `VarPrice.Web` -> `PriceCrawler.Web`
  - `VarPrice.Worker` -> `PriceCrawler.Worker`
  - `VarPrice.Web.Tests` -> `PriceCrawler.Web.Tests`
  - `VarPrice.Worker.Tests` -> `PriceCrawler.Worker.Tests`
- Updated active C# namespaces/usings from `VarPrice.*` to `PriceCrawler.*`.
- Renamed product identifiers:
  - `VarPriceDbContext` -> `PriceCrawlerDbContext`
  - `AddVarPriceApplication` -> `AddPriceCrawlerApplication`
  - `AddVarPriceInfrastructure` -> `AddPriceCrawlerInfrastructure`
- Updated project references, `PriceCrawler.sln`, Dockerfile, GitHub Actions, launch settings, Razor imports/views, active docs, and product branding.
- Updated Worker log file name from `varprice-worker.log` to `pricecrawler-worker.log`.

## Validation performed

```text
dotnet restore PriceCrawler.sln
```

Result: passed.

```text
dotnet build PriceCrawler.sln --no-restore
```

Result: passed, 0 warnings, 0 errors.

```text
dotnet test PriceCrawler.Worker.Tests\PriceCrawler.Worker.Tests.csproj --no-build
```

Result: passed, 21/21 tests.

```text
dotnet test PriceCrawler.sln --no-build
```

Result: timed out twice. The run did not complete after 3 minutes, then again after 10 minutes.

```text
dotnet test PriceCrawler.Web.Tests\PriceCrawler.Web.Tests.csproj --no-build --blame-hang --blame-hang-timeout 90s
```

Result: 160 tests passed before VSTest aborted after inactivity. The active test at hang time was:

```text
PriceCrawler.Web.Tests.WorkerIntegrationTests.RefreshProductCatalogUseCase_DoesNotCreatePriceCollectionData
```

```text
docker compose config
```

Result: passed. Compose output still uses `varprice` database and `var_pg_data` volume.

## Residual old-name audit

Remaining `varprice` occurrences are intentionally preserved database/test database identifiers:

- `varprice` in connection strings, backup scripts, seed guard, README DB backup commands, Docker Compose, and CI.
- `varprice_test` in test fixture and CI/test guidance.
- `varprice_stage` in historical task documentation.

Remaining `VarPrice` occurrences are intentionally preserved historical references:

- This review file documents the old-to-new mapping.
- Historical task files not included in the follow-up cleanup list.

Ticket markdown files under `Tickets/` were updated from `VarPrice` to `PriceCrawler`; lowercase database identifiers such as `varprice` and `varprice_test` were preserved.

The requested historical review/task files and the disabled EF Core snapshot content were updated from `VarPrice` to `PriceCrawler`; lowercase database identifiers were preserved.

No active compiled namespace, using directive, project reference, solution entry, Docker build path, or current CI project path references the old `VarPrice` project name.

## Risks / limitations

- Full Web integration test suite did not complete in this local run. Build and Worker unit tests passed, and Web tests reached 160 passing tests before the hang detector aborted on a DB integration test.
- The old local `VarPrice.Worker` folder may still exist on disk with ignored `bin/obj` build artifacts because Windows blocked whole-directory rename while those artifacts were present. Git-tracked Worker files were moved to `PriceCrawler.Worker`.

## Manual steps

- Re-run the full `dotnet test PriceCrawler.sln --no-build` in an environment where the PostgreSQL integration suite is known to complete.
- If desired, manually remove ignored local build artifacts under the old `VarPrice.Worker` folder after confirming no process is using them.
