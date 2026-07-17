# Ticket: Extend release package with database migrations and schema metadata

## Summary

Extend the PriceCrawler release package so every release contains:

- Web application artifacts;
- Worker/Crawler artifacts;
- database migrations;
- database bootstrap/support files required for deployment;
- release metadata describing application and database schema compatibility.

Target package structure:

```text
PriceCrawler-<version>.zip
├── web/
├── crawler/
├── db/
│   ├── migrations/
│   │   └── 0001_baseline.sql
│   └── scripts/
│       └── bootstrap-schema-version.sql
└── release.json
```

`release.json` must include the database schema versions required by the application.

This package will later be consumed by `deploy-stage.ps1` and `deploy-production.ps1`.

---

## Required project context

Before implementation, read:

- `AGENTS.md`;
- `docs/database-environments.md`;
- `docs/database-provisioning.md`;
- `db/README.md`;
- `db/migrations/`;
- current schema-versioning implementation;
- current Stage/Production `ValidateOnly` implementation;
- current release build scripts;
- `scripts/howdeploy.md`;
- current versioning configuration;
- current artifact directory structure;
- Graphify repository analysis;
- code-review-graph analysis.

Follow the project pre-ticket workflow.

Before editing, identify:

1. the current release build entry point;
2. current Web and Crawler publish paths;
3. current ZIP layout;
4. how application version is resolved;
5. where expected database schema version is defined;
6. whether `release.json` already exists;
7. which database files must be included;
8. which files must never enter the package.

---

# Goals

1. Add database migration files to every release package.
2. Add a validated `release.json`.
3. Keep application and database schema versions consistent.
4. Fail release creation when required database artifacts are missing or invalid.
5. Make the release package deterministic and inspectable.
6. Prevent secrets, backups, dumps, logs, and environment-specific configuration from entering the ZIP.
7. Add automated tests for package layout and metadata.
8. Update release documentation.

---

# Non-goals

This ticket must not:

- execute migrations;
- deploy Stage or Production;
- implement `deploy-stage.ps1`;
- implement `deploy-production.ps1`;
- change the database schema;
- introduce schema version `2`;
- create or overwrite databases;
- include database dumps or backups;
- include real connection strings or credentials;
- make Production changes automatically.

---

# Required release layout

The produced ZIP must contain:

```text
web/
crawler/
db/
├── migrations/
│   └── 0001_baseline.sql
├── scripts/
│   └── bootstrap-schema-version.sql
release.json
```

If additional required database documentation or migration runner files exist, they may be included under `db/`, but the package must remain minimal.

Do not include repository-only files such as:

- investigation reports;
- implementation reports;
- local logs;
- temporary SQL dumps;
- test databases;
- Graphify databases;
- code-review-graph databases;
- IDE files;
- `.git`;
- secrets;
- local `appsettings.*` containing credentials;
- `artifacts/db/backups`;
- `artifacts/db/bootstrap`;
- `artifacts/db/logs`.

---

# `release.json`

Create or extend:

```text
release.json
```

Recommended structure:

```json
{
  "product": "PriceCrawler",
  "version": "v0.4.1-alpha",
  "commit": "<git-commit>",
  "builtAtUtc": "2026-07-17T00:00:00Z",
  "database": {
    "minimumSchemaVersion": 1,
    "targetSchemaVersion": 1
  },
  "components": {
    "web": true,
    "crawler": true
  }
}
```

## Required fields

- `product`;
- `version`;
- `commit`;
- `builtAtUtc`;
- `database.minimumSchemaVersion`;
- `database.targetSchemaVersion`;
- component presence metadata.

Field naming may follow current project conventions, but semantics must remain explicit.

## Version rules

- application version must come from the existing canonical versioning mechanism;
- commit must identify the exact source revision;
- database target version must come from the shared expected schema version contract;
- schema version must not be duplicated as an unrelated hard-coded build-script value;
- `minimumSchemaVersion` must not exceed `targetSchemaVersion`;
- for the current release both values are `1`.

If a release contains no migration beyond the current schema, this is valid:

```json
{
  "minimumSchemaVersion": 1,
  "targetSchemaVersion": 1
}
```

---

# Release build script

Update the current release builder, expected to be:

```text
scripts/build-release.ps1
```

or the actual existing equivalent.

The script must:

1. resolve repository root reliably;
2. resolve application version;
3. resolve Git commit;
4. read expected database schema version from the canonical source;
5. publish Web;
6. publish Crawler/Worker;
7. copy required database files;
8. generate `release.json`;
9. validate package staging contents;
10. create ZIP;
11. validate final ZIP contents;
12. print artifact path, size, version, commit, and schema version.

Use paths relative to the solution/repository root.

Do not depend on the caller's current working directory.

---

# Database artifact selection

Include only deployable database files.

Required:

```text
db/migrations/0001_baseline.sql
db/scripts/bootstrap-schema-version.sql
```

If future migration files exist, include all numbered migrations in deterministic order:

```text
0001_*.sql
0002_*.sql
0003_*.sql
```

The build must validate:

- migration filenames follow the numbering convention;
- version numbers are unique;
- numbering is strictly increasing;
- no duplicate migration version exists;
- target schema version matches the highest migration version represented by the release contract;
- required baseline/bootstrap files exist.

Do not modify released migration files during packaging.

---

# Package validation

Before ZIP creation, fail when:

- Web publish output is missing;
- Crawler publish output is missing;
- a required database file is missing;
- `release.json` cannot be generated;
- application version is empty;
- Git commit is unavailable without an explicitly supported fallback;
- schema version cannot be resolved;
- schema metadata is inconsistent;
- duplicate migration numbers exist;
- forbidden files are present;
- real secrets are detected in staged configuration.

After ZIP creation:

1. list archive entries;
2. verify required entries exist;
3. verify no forbidden path exists;
4. parse `release.json` from the package;
5. compare metadata with build inputs;
6. calculate SHA-256 for the ZIP.

---

# Determinism

For identical source revision, version, configuration, and toolchain, the package contents should be stable.

At minimum:

- deterministic file ordering;
- no temporary files;
- no random names inside the ZIP;
- normalized relative paths;
- UTC timestamps in metadata;
- no machine-specific absolute paths in `release.json`.

Byte-for-byte reproducibility is desirable but not mandatory if ZIP timestamps prevent it. Document the actual guarantee.

---

# Configuration policy

Release ZIP may contain safe configuration templates, but must not contain real environment secrets.

Allowed:

- placeholder configuration;
- non-secret defaults;
- logging structure;
- schema startup mode defaults where appropriate.

Forbidden:

- real database passwords;
- actual Production connection strings;
- API tokens;
- local `.env`;
- `.pgpass`;
- deployment secret files;
- developer machine paths.

Stage and Production runtime configuration must be supplied externally during deployment.

---

# Existing environment rules

The package must preserve the established behavior:

```text
Development / Test → Ensure
Stage / Production → ValidateOnly
```

Packaging must not rewrite Stage or Production into `Ensure`.

The release metadata must not authorize schema changes by application startup.

Database migrations are deployment artifacts, not startup instructions.

---

# Release artifact naming

Use the existing convention or adopt:

```text
PriceCrawler-<version>.zip
```

Example:

```text
PriceCrawler-v0.4.1-alpha.zip
```

Recommended output:

```text
artifacts/releases/
    PriceCrawler-v0.4.1-alpha.zip
    PriceCrawler-v0.4.1-alpha.zip.sha256
```

Do not overwrite a release with the same version silently.

Preferred behavior:

- fail if artifact exists;
- or require an explicit `-ReplaceExistingArtifact` switch for local non-final builds.

Never silently replace a release that may already have been deployed.

---

# Build parameters

Recommended interface:

```powershell
.\scripts\build-release.ps1 `
    -Configuration Release `
    -Version "v0.4.1-alpha"
```

Optional:

```powershell
-OutputDirectory
-ReplaceExistingArtifact
-SkipTests
```

If `-SkipTests` exists, it must be explicit and logged in metadata or console output.

Default release build should run required validation and tests.

---

# Tests

Add automated tests for release packaging.

## Metadata tests

- `release.json` is valid JSON;
- product is `PriceCrawler`;
- version matches build input/canonical version;
- commit matches source revision;
- timestamp is valid UTC;
- minimum schema version is `1`;
- target schema version is `1`;
- minimum does not exceed target;
- component metadata matches archive contents.

## Layout tests

- `web/` exists;
- `crawler/` exists;
- `db/migrations/0001_baseline.sql` exists;
- bootstrap script exists;
- `release.json` exists;
- paths use forward/reliable archive separators;
- no unexpected root nesting exists.

The package must not accidentally produce:

```text
PriceCrawler-v0.4.1-alpha/
    PriceCrawler-v0.4.1-alpha/
        web/
```

## Safety tests

Verify absence of:

- `*.dump`;
- database backups;
- log files;
- `.env`;
- `.pgpass`;
- secrets;
- Graphify/CRG databases;
- test result directories;
- source `.git`;
- developer-specific absolute paths.

## Consistency tests

- expected schema version equals package target version;
- highest migration version is compatible with target version;
- duplicate migration versions fail;
- missing baseline fails;
- missing Web output fails;
- missing Crawler output fails;
- existing artifact replacement follows explicit policy.

---

# Documentation updates

Update:

- `scripts/howdeploy.md`;
- release/build documentation;
- `db/README.md`;
- `docs/database-environments.md` where necessary;
- root `README.md` if it describes artifact creation.

Document:

1. exact build command;
2. output location;
3. ZIP structure;
4. `release.json` fields;
5. schema compatibility meaning;
6. how to inspect the package;
7. how to verify SHA-256;
8. forbidden package contents;
9. that Stage/Production migrations are applied by deployment;
10. that application startup remains `ValidateOnly`.

---

# Acceptance criteria

## Package content

- [ ] Release ZIP contains `web/`.
- [ ] Release ZIP contains `crawler/`.
- [ ] Release ZIP contains `db/migrations/`.
- [ ] Release ZIP contains the bootstrap support script.
- [ ] Release ZIP contains `release.json`.
- [ ] No unnecessary repository nesting exists.
- [ ] No backups, dumps, logs, or secrets are included.

## Metadata

- [ ] `release.json` contains product and application version.
- [ ] `release.json` contains exact Git commit.
- [ ] `release.json` contains UTC build timestamp.
- [ ] `release.json` contains minimum schema version.
- [ ] `release.json` contains target schema version.
- [ ] Schema version comes from the canonical application contract.
- [ ] Current schema metadata is `1 → 1`.
- [ ] Metadata matches actual package contents.

## Build behavior

- [ ] Release builder uses repository-relative paths.
- [ ] It works regardless of caller working directory.
- [ ] It fails when required artifacts are absent.
- [ ] It validates migration numbering.
- [ ] It validates staged package contents.
- [ ] It validates final ZIP contents.
- [ ] It calculates SHA-256.
- [ ] It does not silently overwrite an existing release.

## Safety

- [ ] Real credentials are not packaged.
- [ ] Production connection strings are not packaged.
- [ ] Database dumps and backups are not packaged.
- [ ] Application startup policy is not weakened.
- [ ] No schema migration is executed by this ticket.
- [ ] No database is modified.

## Tests and documentation

- [ ] Packaging tests pass.
- [ ] Full solution tests pass.
- [ ] Documentation is updated.
- [ ] A verification report is produced.

---

# Required verification commands

Adapt names to the repository.

## Build

```powershell
dotnet restore .\PriceCrawler.sln
dotnet build .\PriceCrawler.sln -c Release --no-restore
dotnet test .\PriceCrawler.sln -c Release --no-build
```

## Create release

```powershell
.\scripts\build-release.ps1 `
    -Configuration Release `
    -Version "v0.4.1-alpha"
```

## Inspect ZIP

```powershell
tar -tf ".\artifacts\releases\PriceCrawler-v0.4.1-alpha.zip"
```

## Read packaged metadata

```powershell
$extract = Join-Path $env:TEMP "pricecrawler-release-check"

Remove-Item $extract -Recurse -Force -ErrorAction SilentlyContinue
Expand-Archive `
    ".\artifacts\releases\PriceCrawler-v0.4.1-alpha.zip" `
    $extract

Get-Content (Join-Path $extract "release.json") -Raw |
    ConvertFrom-Json |
    Format-List
```

## Verify checksum

```powershell
Get-FileHash `
    ".\artifacts\releases\PriceCrawler-v0.4.1-alpha.zip" `
    -Algorithm SHA256
```

## Secret scan

Run the repository's existing secret scan or an equivalent check against the extracted package.

---

# Deliverables

Codex must provide:

1. updated release build script;
2. generated `release.json` implementation;
3. database artifact packaging;
4. package validation logic;
5. SHA-256 generation;
6. packaging tests;
7. updated documentation;
8. implementation report;
9. verification report;
10. sample archive entry listing;
11. sample `release.json`;
12. explicit confirmation that no database was modified.

Suggested reports:

```text
implementation-report.md
release-package-inventory.md
verification-report.md
```

---

# Review focus

Code review must verify:

- database schema version is not duplicated incorrectly;
- package metadata comes from canonical sources;
- all required migration files are present;
- forbidden artifacts are excluded;
- no credentials are packaged;
- ZIP layout is stable and minimal;
- existing artifacts are not silently overwritten;
- Stage/Production remain `ValidateOnly`;
- packaging does not execute migrations;
- future deployment scripts can consume the package without repository access.

---

# Final architectural rule

```text
Release package
    contains application binaries
    contains database migration artifacts
    declares schema compatibility
    contains no environment secrets

Deployment
    validates release.json
    applies required forward migrations
    supplies environment configuration
    starts application in ValidateOnly
```
