# PRE_TICKET_WORKFLOW.md

> Mandatory repository-intelligence workflow for Codex before every non-trivial
> PriceCrawler ticket, bugfix, feature, refactor, database change, deployment change,
> investigation, implementation-planning task, or code review.

## 0. Authority and purpose

This workflow defines how the repository must be investigated, changed, and validated.

The repository uses:

1. **Graphify** for architectural and semantic repository orientation.
2. **code-review-graph (CRG)** for concrete structural relationships and impact analysis.

Neither graph is authoritative.

**Current source, SQL, tests, build output, runtime behavior, and maintained documentation
remain authoritative.**

Explicit user instructions take precedence, followed by applicable `AGENTS.md`, followed by
this workflow.

## 1. Workflow levels

### Level 0 — trivial

Examples:

- spelling;
- formatting;
- comment-only edits;
- metadata-only edits;
- documentation changes that cannot alter operational instructions.

Required:

- read applicable instructions;
- inspect working-tree state;
- validate the edited file.

No graph preflight required.

### Level 1 — local change

Examples:

- isolated bugfix in one known service;
- narrow controller or DTO correction;
- focused test change;
- local extractor fix;
- small configuration correction.

Required:

- repository baseline;
- CRG scoped analysis;
- source validation;
- focused tests;
- Graphify reuse/query when architecture context is relevant;
- no blind Graphify rebuild.

### Level 2 — structural or operational change

Examples:

- feature spanning projects;
- discovery/crawler strategy change;
- queue or catalog lifecycle change;
- database schema/routine/mapping change;
- concurrency, reservation, retry, or scheduling change;
- Worker CLI change;
- dashboard data contract change;
- deployment or environment-selection change;
- broad refactor or architecture review.

Required:

- full Graphify preflight;
- full CRG preflight;
- investigation;
- implementation plan;
- source and SQL validation;
- post-change CRG update;
- targeted plus broader validation;
- Graphify refresh when architecture changed.

When uncertain, choose Level 2.

## 2. Execution order

1. Read instructions.
2. Resolve repository root and record Git state.
3. Classify workflow level.
4. Identify affected projects and operational boundaries.
5. Check Graphify when applicable.
6. Check CRG.
7. Gather scoped graph context.
8. Validate findings in C#, SQL, configuration, tests, and docs.
9. Assess database/environment safety.
10. Produce `investigation.md` and `implementation-plan.md` for Level 2.
11. Implement the smallest coherent change.
12. Update CRG.
13. Inspect blast radius and review context.
14. Run tests/build from narrowest to broadest.
15. Perform database/Docker/manual validation when required and available.
16. Refresh Graphify only for structural changes.
17. Update docs/changelog when required.
18. Produce an implementation report.

Do not skip directly to implementation.

## 3. Repository baseline

Run from the repository root:

```powershell
git rev-parse --show-toplevel
git branch --show-current
git rev-parse HEAD
git status --short
dotnet --version
```

Read:

- root `AGENTS.md`;
- `.codex/PRE_TICKET_WORKFLOW.md`;
- ticket and acceptance criteria;
- applicable nested `AGENTS.md`;
- `README.md`;
- `CONTRIBUTING.md`;
- relevant docs, SQL, and configuration.

Identify:

- affected project(s);
- host process: Web, Worker, test, SQL/bootstrap, deploy script;
- current database target and safety guards;
- build/test commands;
- pre-existing user changes.

Never clean, reset, stash, revert, or overwrite unrelated user changes.

## 4. Graphify preflight

Follow `$graphify-repository-analysis`.

For Level 2 tasks, determine:

- owning subsystem and project;
- main orchestration flow;
- cross-project contracts;
- relevant strategies/adapters;
- queue/catalog/data-flow boundaries;
- related Web/Worker paths;
- likely test areas.

Expected working output may be under `graphify-out/`.

Exclude:

- `bin`, `obj`, publish output;
- artifacts and logs;
- `.code-review-graph`;
- `graphify-out`;
- coverage and test results;
- vendored frontend assets, especially `PriceCrawler.Web/wwwroot/vendor`.

Do not exclude project-owned JavaScript/CSS under `wwwroot`.

Use only confirmed installed Graphify commands. Do not invent slash commands, backend options,
or update flags.

A previously used environment may rely on Ollama and `qwen25coder14b:latest`, but verify local
configuration before execution. Do not commit or alter machine-specific model settings.

Validate every implementation-relevant conclusion in source.

## 5. CRG preflight

Follow `$code-review-graph-analysis`.

Use only confirmed repository-supported CRG commands.

Expected local state may be under `.code-review-graph/`.

For the ticket scope, identify:

- concrete symbols;
- interfaces and implementations;
- callers/callees;
- DI registration;
- EF configurations;
- SQL routines;
- controllers/CLI entry points;
- tests;
- expected blast radius.

Do not treat database-file existence as freshness. Confirm with a successful update/query.

## 6. Mandatory investigation

Answer before implementation:

1. What is the current behavior?
2. What is the expected behavior?
3. What is the root cause or missing capability?
4. What is the smallest correct change?
5. Which projects, symbols, SQL routines, configuration keys, and tests are affected?
6. Which public or operational contracts must remain compatible?
7. What is the expected blast radius?
8. Does the change affect database schema or existing data?
9. Does it affect dev/test/stage/prod safety?
10. Does it affect idempotency, retries, locking, cancellation, or concurrency?
11. Does it affect Web/Worker consistency?
12. Which validation commands are required?
13. Which documentation or changelog files require updates?
14. Is there graph/source/documentation disagreement?

For Level 2, write:

```text
investigation.md
implementation-plan.md
```

## 7. Database change gate

For any change touching EF entities/configuration, SQL, queue persistence, catalog scheduling,
snapshots, crawler runs, ingestion runs, or errors:

1. inspect `DbContext`, entity configurations, affected entities, repositories, and routines;
2. inspect `db/routines/*.sql`, initialization/bootstrap scripts, and relevant seeds;
3. inspect focused integration tests;
4. define forward compatibility and rollback/recovery expectations;
5. verify defaults, nullability, backfill, indexes, constraints, and query plans where relevant;
6. verify transaction and locking boundaries;
7. verify idempotency under retry;
8. verify stage/prod safeguards;
9. do not run destructive seeds outside local/dev.

A database change may not be declared complete based only on compilation.

## 8. Crawler and queue change gate

For crawler, discovery, extraction, listing, queue, catalog, retry, or scheduling changes,
inspect:

- URL normalization;
- duplicate handling;
- page-kind classification;
- listing/filter discovery;
- product extraction;
- reservation/lease state;
- retry classification;
- transient versus terminal errors;
- HTTP status versus parse success;
- timeouts and cancellation;
- rate limiting and concurrency;
- run/ingestion completion;
- scheduling state updates;
- progress counters;
- tests with representative URLs.

Do not increase pressure on VARUS without explicit requirements.

## 9. Web/dashboard change gate

For Web or dashboard changes:

- inspect controller, application contract, infrastructure query source, DTO/ViewModel,
  Razor/JavaScript consumer, and tests;
- preserve anti-forgery on writes;
- avoid direct EF/SQL in controllers;
- preserve deterministic default loading;
- keep live VARUS refresh explicit unless deliberately redesigned;
- verify server-side paging/filter/sorting;
- verify JSON field names and nullability;
- inspect project-owned static assets, not vendor bundles.

## 10. Worker CLI change gate

For Worker changes:

- validate before host creation and DB bootstrap;
- preserve exit codes and documented aliases;
- verify command conflicts and unknown arguments;
- prevent CLI arguments from becoming accidental host configuration;
- preserve redirected-output behavior;
- verify progress rendering and file logging separation;
- verify cancellation and process termination;
- update README/help/tests together.

## 11. Implementation rules

- Follow architecture and dependency direction.
- Preserve user changes.
- Keep scope ticket-focused.
- Avoid unrelated refactoring.
- Propagate cancellation.
- Avoid sync-over-async.
- Avoid hidden global state.
- Use parameterized EF/SQL.
- Add or update tests.
- Re-query CRG when unexpected dependencies appear.
- Re-query Graphify when an unexpected subsystem boundary appears.
- Keep SQL, mappings, routines, docs, and tests synchronized.

## 12. Post-change CRG validation

After code changes:

1. run the confirmed CRG update;
2. inspect changed symbols;
3. inspect callers/dependants;
4. inspect DI and registration reachability;
5. inspect related tests;
6. inspect blast radius;
7. investigate unexpected cross-project impact;
8. verify new code is reachable and obsolete paths are not unintentionally active.

If impact exceeds the plan, stop and reassess before expanding the change.

## 13. Validation order

Use the narrowest applicable command first.

Examples:

```powershell
dotnet test PriceCrawler.Web.Tests\PriceCrawler.Web.Tests.csproj --filter "<relevant filter>"
dotnet test PriceCrawler.Web.Tests\PriceCrawler.Web.Tests.csproj --filter "FullyQualifiedName~WorkerIntegrationTests"
dotnet build PriceCrawler.sln
dotnet test PriceCrawler.sln
dotnet build PriceCrawler.sln -c Release
```

When relevant and available:

```powershell
docker compose up -d postgres
```

Also consider:

- application startup;
- `/health`;
- one controlled Worker command;
- database routine/bootstrap validation;
- dashboard/manual flow;
- deploy-stage dry run or safe stage validation.

Never claim execution that did not occur.

## 14. Graphify post-change policy

Refresh Graphify only if the change affected:

- project/module boundaries;
- major orchestration;
- discovery strategy architecture;
- queue/catalog workflow;
- public entry points;
- substantial cross-project relationships;
- broad refactoring.

CRG should be updated after every non-trivial code change.

## 15. Documentation obligations

Evaluate:

- `CHANGELOG.md`;
- `README.md`;
- `Status.md`;
- `docs/*`;
- Worker help/CLI docs;
- SQL/schema docs;
- deployment docs;
- configuration examples.

User-visible, operational, schema, CLI, or deployment changes require documentation updates.

## 16. Failure handling

If Graphify or CRG fails:

- record the exact confirmed command;
- capture the concise error;
- do not fabricate findings;
- continue with source, `rg`, SQL, build, and tests when safe;
- report degraded analysis.

If database/Docker validation is unavailable:

- state what was unavailable;
- state what alternative validation ran;
- state the remaining risk.

## 17. Required implementation report

```markdown
# Implementation Report

## Ticket
<ticket id and summary>

## Workflow
- Level: 1 / 2
- Graphify: used / unavailable / not required
- CRG: used / unavailable
- Working tree before changes: clean / dirty

## Scope
- Projects:
- Database impact:
- Operational impact:
- Public contract impact:

## Investigation
- Current behavior:
- Expected behavior:
- Root cause or implementation gap:
- Main symbols:
- SQL/configuration:
- Expected blast radius:

## Changes
- ...

## Graph and source validation
- Graphify findings:
- CRG findings:
- Source/SQL validations:
- Discrepancies:

## Post-change impact
- CRG updated:
- Blast radius:
- Unexpected dependants:
- Migration/deployment concerns:

## Validation
- Focused tests:
- Integration tests:
- Solution build:
- Solution tests:
- Release build:
- Docker/database:
- Manual validation:

## Documentation
- Updated:
- Not required because:

## Remaining risks
- ...
```

## 18. Non-negotiable rules

1. Do not start non-trivial implementation before applicable preflight.
2. Do not invent Graphify or CRG commands.
3. Do not trust graphs without source validation.
4. Do not destroy unrelated user changes.
5. Do not weaken database-environment safeguards.
6. Do not run destructive seeds outside local/dev.
7. Do not claim persistence correctness from compilation alone.
8. Do not claim tests or builds passed unless executed.
9. Update CRG after implementation.
10. Keep SQL, C# persistence, tests, and docs aligned.
11. Source and executable evidence win over graph inference.
