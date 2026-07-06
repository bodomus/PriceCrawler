# Ticket: Rename solution and codebase branding from PriceCrawler to PriceCrawler

## Goal

Rename the application/codebase identity from `PriceCrawler` to `PriceCrawler` across the entire .NET solution without breaking build, tests, project references, runtime configuration, database connectivity, Docker persistence, CI, or deployment-related configuration.

This task is a controlled rename refactor.

The task must **not** blindly replace every occurrence of `varprice`.

---

## Context

The project has evolved beyond the original `PriceCrawler` naming. The preferred product and solution name is now:

```text
PriceCrawler
```

The repository contains a .NET solution with application projects, test projects, documentation, configuration files, Docker-related files, and PostgreSQL databases.

Known existing PostgreSQL database names include:

```text
varprice
varprice_stage
```

These database names are infrastructure identifiers and must remain unchanged in this ticket.

A Docker/PostgreSQL volume may also use an existing identifier such as:

```text
var_pg_data
```

Existing persistent volume names must remain unchanged.

---

# Scope

Rename all codebase/product-level naming from `PriceCrawler` to `PriceCrawler`, including where applicable:

- solution file;
- project directories;
- `.csproj` files;
- C# namespaces;
- `using` directives;
- assembly names;
- root namespaces;
- project references;
- test project names;
- test namespaces;
- documentation;
- scripts referring to solution/project file names;
- GitHub Actions workflow references;
- Docker build paths referring to renamed project folders or `.csproj` files;
- launch profiles where project/product naming is used;
- application display names where they represent the product;
- README and project documentation;
- internal comments where `PriceCrawler` clearly refers to the application/product rather than a database identifier.

---

# Explicit non-goals

Do **not** rename or migrate the following in this ticket:

## PostgreSQL databases

Keep existing physical database names unchanged:

```text
varprice
varprice_stage
```

Do not create database migrations for database renaming.

Do not alter connection strings only for the purpose of changing:

```text
Database=varprice
Database=varprice_stage
```

## Persistent Docker volumes

Do not rename existing persistent PostgreSQL volumes, including names such as:

```text
var_pg_data
```

A volume rename can cause PostgreSQL to start with a new empty data directory. Avoid this.

## Historical migrations

Do not rewrite historical EF Core migrations merely to replace textual references to the old application name.

Historical migration class names, migration IDs, generated snapshots, or migration metadata should only be changed if the rename is required for compilation.

Do not regenerate the full migration history.

## Data

Do not modify production, stage, test, or development data.

---

# Required implementation approach

## 1. Inventory all occurrences before changing anything

Search the repository for all relevant naming variants.

At minimum inspect:

```text
PriceCrawler
varprice
PRICECRAWLER
Var.Price
Var-Price
```

Use repository-wide search and classify occurrences into:

1. application/product naming;
2. project/solution naming;
3. namespace/code identifiers;
4. documentation;
5. infrastructure identifiers;
6. database identifiers;
7. Docker persistent resource identifiers;
8. historical migration references.

Do not perform a blind case-insensitive global replacement.

Create a short internal rename map before editing.

Expected primary mapping:

```text
PriceCrawler -> PriceCrawler
```

Potential lowercase replacement must be decided contextually.

Example:

```text
PriceCrawler.Worker -> PriceCrawler.Worker
```

but:

```text
Database=varprice
```

must remain unchanged.

---

## 2. Rename solution file

Rename the solution from the current `PriceCrawler`-based name to:

```text
PriceCrawler.sln
```

If the repository uses `.slnx`, preserve the existing solution format and rename consistently.

Update all scripts, CI workflows, documentation, and commands that reference the old solution file name.

Do not leave duplicate obsolete solution files unless the repository already intentionally maintains multiple solutions.

---

## 3. Rename project directories and project files

Rename all projects whose names use the old application prefix.

Example transformation:

```text
src/PriceCrawler.Web/
src/PriceCrawler.Worker/
src/PriceCrawler.Domain/
src/PriceCrawler.Application/
src/PriceCrawler.Infrastructure/
```

to:

```text
src/PriceCrawler.Web/
src/PriceCrawler.Worker/
src/PriceCrawler.Domain/
src/PriceCrawler.Application/
src/PriceCrawler.Infrastructure/
```

Apply the same principle to actual project names found in the repository.

Rename corresponding `.csproj` files.

Example:

```text
PriceCrawler.Worker.csproj
```

to:

```text
PriceCrawler.Worker.csproj
```

Use Git-aware file moves where practical so history remains traceable.

---

## 4. Update project references

Update all affected:

```xml
<ProjectReference Include="..." />
```

references.

Verify paths after folder and `.csproj` renames.

Also inspect:

- solution project entries;
- Dockerfiles;
- `docker-compose*.yml`;
- build scripts;
- PowerShell scripts;
- shell scripts;
- GitHub Actions workflows;
- test scripts;
- publish scripts.

No file may reference a renamed path that no longer exists.

---

## 5. Rename namespaces and code identifiers

Rename application namespaces from:

```csharp
PriceCrawler.*
```

to:

```csharp
PriceCrawler.*
```

Update:

- `namespace` declarations;
- `using` directives;
- fully-qualified type names;
- InternalsVisibleTo declarations;
- reflection-based assembly names;
- test namespaces;
- assembly references;
- namespace strings used by DI scanning or reflection.

Pay special attention to patterns such as:

```csharp
typeof(SomeType).Assembly
Assembly.GetAssembly(...)
Assembly.Load(...)
StartsWith("PriceCrawler.")
```

Do not assume all namespace coupling is compile-time checked.

---

## 6. Check project metadata

Inspect `.csproj`, `Directory.Build.props`, `Directory.Build.targets`, and related files for:

```xml
<AssemblyName>
<RootNamespace>
<PackageId>
<Product>
<Title>
<Description>
```

Change values that represent the application identity.

Do not modify package identifiers that must remain stable for compatibility unless they are clearly internal and unused externally.

If a compatibility risk exists, leave the identifier unchanged and document it in the final summary.

---

## 7. Rename test projects consistently

Rename test projects, test directories, and test namespaces that use `PriceCrawler`.

Examples:

```text
PriceCrawler.Tests
PriceCrawler.Domain.Tests
PriceCrawler.IntegrationTests
```

become:

```text
PriceCrawler.Tests
PriceCrawler.Domain.Tests
PriceCrawler.IntegrationTests
```

Adapt this mapping to the actual repository structure.

Update:

- solution entries;
- project references;
- test namespace declarations;
- `InternalsVisibleTo`;
- CI test paths;
- coverage configuration;
- snapshot/baseline paths if path-based.

Do not rename test fixtures or data whose `varprice` text is intentionally a database name.

---

## 8. Update documentation

Update product/application naming in:

- `README.md`;
- `AGENTS.md`;
- `PLANS.md`;
- `docs/**/*.md`;
- architecture documents;
- setup instructions;
- development instructions;
- deployment instructions;
- examples and command snippets.

Where documentation describes a database command such as:

```text
psql -d varprice
```

or:

```text
Database=varprice_stage
```

keep the database name unchanged.

The documentation should clearly distinguish:

```text
Product / solution: PriceCrawler
```

from:

```text
Database identifiers: varprice, varprice_stage
```

---

## 9. Inspect configuration files

Inspect:

- `appsettings.json`;
- `appsettings.Development.json`;
- `appsettings.Stage.json`;
- `appsettings.Production.json`;
- environment variable templates;
- `.env.example`;
- launch settings;
- Docker Compose files;
- CI variables.

Rename application identity strings only where appropriate.

Do not rename:

```text
varprice
varprice_stage
```

when they identify PostgreSQL databases.

Do not rename persistent volume identifiers.

Do not expose secrets while editing or reporting results.

---

## 10. Inspect GitHub Actions and deployment scripts

Search `.github/workflows/`.

Update old paths or commands such as:

```text
dotnet restore PriceCrawler.sln
dotnet build PriceCrawler.sln
dotnet test PriceCrawler.sln
```

to the new solution name.

Also update project-specific publish paths.

Example:

```text
src/PriceCrawler.Worker/PriceCrawler.Worker.csproj
```

to:

```text
src/PriceCrawler.Worker/PriceCrawler.Worker.csproj
```

Use the actual repository structure, not assumptions from this ticket.

---

# Required verification

After the rename, run the complete verification sequence available in the repository.

At minimum:

```powershell
dotnet restore PriceCrawler.sln
dotnet build PriceCrawler.sln --no-restore
dotnet test PriceCrawler.sln --no-build
```

If the repository uses a different solution format or central build script, use the correct equivalent.

Also validate Docker configuration without destroying data.

Where available:

```powershell
docker compose config
```

Do not run destructive Docker commands.

Do not run:

```text
docker compose down -v
docker volume rm ...
```

or any equivalent destructive action.

---

# Residual old-name audit

After all changes, perform a repository-wide search for:

```text
PriceCrawler
varprice
PRICECRAWLER
```

Review every remaining occurrence.

Expected acceptable remaining occurrences may include:

```text
Database=varprice
Database=varprice_stage
var_pg_data
```

and historical references that must remain stable.

Unexpected remaining occurrences include:

```text
namespace PriceCrawler...
using PriceCrawler...
PriceCrawler.Worker.csproj
src/PriceCrawler.Worker/
dotnet build PriceCrawler.sln
```

Every remaining occurrence of the old name must be classified in the final report.

---

# Acceptance Criteria

The ticket is complete only when all of the following are true:

- [ ] The main solution is named `PriceCrawler`.
- [ ] All application projects using the old `PriceCrawler` prefix are renamed consistently.
- [ ] All corresponding project directories are renamed consistently.
- [ ] All C# namespaces use `PriceCrawler.*` where they represent application code.
- [ ] All `using` directives and fully qualified references are valid.
- [ ] All project references point to existing renamed `.csproj` files.
- [ ] Test projects and namespaces are renamed consistently.
- [ ] CI workflows no longer reference obsolete solution or project paths.
- [ ] Docker build paths remain valid.
- [ ] Documentation uses `PriceCrawler` as the product/application name.
- [ ] PostgreSQL database names `varprice` and `varprice_stage` remain unchanged.
- [ ] Existing persistent Docker volume names remain unchanged.
- [ ] Historical EF Core migration history is not unnecessarily rewritten.
- [ ] `dotnet restore` succeeds.
- [ ] `dotnet build` succeeds with zero errors.
- [ ] `dotnet test` succeeds.
- [ ] Docker Compose configuration validation succeeds where Docker Compose is part of the repository.
- [ ] Every remaining `PriceCrawler`/`varprice` occurrence has been reviewed and justified.
- [ ] No unrelated refactoring is included.

---

# Constraints

1. Keep the change focused strictly on the rename.
2. Do not introduce architecture changes.
3. Do not modify crawler behavior.
4. Do not change database schemas.
5. Do not rename databases.
6. Do not recreate Docker volumes.
7. Do not reset or delete development/stage data.
8. Do not rewrite migration history unnecessarily.
9. Do not perform unrelated formatting across the repository.
10. Preserve existing behavior.
11. Keep the diff reviewable.
12. Prefer compiler-safe and IDE-aware renaming for C# symbols over uncontrolled textual replacement.
13. Treat configuration and infrastructure identifiers separately from product naming.

---

# Suggested commit structure

Prefer several logical commits rather than one opaque bulk commit.

Example:

```text
refactor: rename solution and projects to PriceCrawler
refactor: rename namespaces to PriceCrawler
chore: update CI and build paths after rename
docs: update PriceCrawler branding to PriceCrawler
```

Do not split changes in a way that leaves the branch permanently difficult to review, but keep major rename categories understandable.

---

# Final report required from Codex

At completion, provide:

## 1. Changed structure

List renamed:

- solution;
- project directories;
- project files;
- namespaces;
- test projects.

## 2. Intentionally preserved identifiers

Explicitly list preserved infrastructure identifiers, including:

```text
varprice
varprice_stage
```

and any persistent Docker volume names.

## 3. Verification results

Report results for:

```text
dotnet restore
dotnet build
dotnet test
docker compose config
```

where applicable.

## 4. Residual search results

List all remaining occurrences of:

```text
PriceCrawler
varprice
PRICECRAWLER
```

and explain why each category remains.

## 5. Risks or compatibility notes

Document any identifier that was deliberately not renamed due to compatibility or deployment risk.

---

# Definition of Done

The repository builds and tests successfully under the new `PriceCrawler` solution/project/namespace naming, while existing databases and persistent infrastructure identifiers continue working unchanged.
