# MPC-82 Verification Report

## Static and focused validation

- PowerShell AST parse: passed.
- `-ValidatePackageInputsOnly`: passed for schema version `1` and `0001_baseline.sql`.
- `ReleaseDatabasePackagingTests`: `7/7` passed.
- Duplicate migration version: rejected by automated test.
- Missing baseline: rejected by automated test.
- Existing ZIP/checksum without opt-in: rejected before publish.
- Explicit `-ReplaceExistingArtifact`: successful in isolated output directory.
- Different caller working directory: successful.
- `git diff --check`: no errors; only repository-configured LF/CRLF notices.

## Actual package validation

Command:

```powershell
.\Scripts\build-release.ps1 `
    -Configuration Release `
    -Version v0.4.1-mpc82-verification `
    -OutputDirectory artifacts/releases/mpc82-verification `
    -ReplaceExistingArtifact `
    -SkipTests `
    -AllowDirtyWorkingTree
```

Result:

- ZIP creation: passed.
- Staging validation: passed.
- Final archive validation: passed.
- Entry count: `435`.
- Required Web/Crawler/database/metadata entries: present.
- Forbidden entry count: `0`.
- `release.json` JSON parse and exact input comparison: passed.
- Component metadata versus archive: passed.
- Schema compatibility: `1 -> 1`.
- SHA-256 sidecar comparison: passed.
- Plaintext local password `myPassword`: absent from packaged appsettings.

An additional build without `-Version` resolved `v0.4.1-alpha.6+4288531ada` with `versionSource=Nerdbank.GitVersioning`, matching commit `4288531ada4bfbc7973f7086db4533a03348f942`.

## Build and tests

```text
dotnet build PriceCrawler.sln -c Release --no-restore
Build succeeded: 0 warnings, 0 errors

dotnet test PriceCrawler.sln -c Release --no-build
Worker: 21/21
Web: 295/295
Total: 316/316
```

The full suite used only the local Docker PostgreSQL test container and temporary test databases, followed by container stop. No Stage or Production database was contacted or modified.

## Graph validation

- Graphify preflight query reused the existing graph and identified release builder, schema contract, Web/Worker publish and packaging tests as the relevant neighborhood.
- CRG incremental update completed after implementation.
- CRG found no affected application runtime flow; operational risk remains isolated to release packaging and future deployment consumption.
- Source inspection resolved graph noise around runtime persistence: no C#, EF, SQL routine or schema implementation changed.

## Safety confirmation

- No migration, baseline or bootstrap was executed.
- No schema version changed.
- No Stage/Production deployment was performed.
- Stage/Production application configuration remains `ValidateOnly`.
- No real connection string or credential was added.
