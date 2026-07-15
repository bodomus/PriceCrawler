---
name: graphify-repository-analysis
description: Use Graphify to orient within PriceCrawler architecture, discover relationships across Domain, Application, Infrastructure, Web, Worker, SQL, and tests, and produce source-verified context before structural implementation or review.
---

# Graphify Repository Analysis

## Repository workflow precedence

When `AGENTS.md` or `.codex/PRE_TICKET_WORKFLOW.md` requires this skill, the repository
workflow takes precedence.

- Level 2: mandatory full preflight.
- Level 1: reuse or query when architectural context is relevant.
- Level 0: normally unnecessary.

## Purpose

Use Graphify for architecture and candidate discovery across PriceCrawler.

Graphify is not authoritative. Validate important conclusions in current C#, SQL,
configuration, tests, and runtime/build evidence.

## Scope model

Orient around:

- `PriceCrawler.Domain`;
- `PriceCrawler.Application`;
- `PriceCrawler.Infrastructure`;
- `PriceCrawler.Web`;
- `PriceCrawler.Worker`;
- `PriceCrawler.Web.Tests`;
- relevant `db/` routines and maintained docs.

Use Graphify to find:

- project and subsystem ownership;
- orchestration paths;
- discovery strategies;
- queue and catalog concepts;
- extractors and adapters;
- Web/Worker shared flows;
- query/read-model boundaries;
- test neighborhoods;
- cross-project relationships.

## Exclusions

Exclude:

- `.git`, `.idea`, `.vs`;
- `bin`, `obj`, publish output;
- artifacts, logs, test results, coverage;
- `.code-review-graph`, `graphify-out`;
- vendored/minified frontend assets;
- local databases, model caches, and backups.

Specifically exclude `PriceCrawler.Web/wwwroot/vendor/`, but retain project-owned
JavaScript and CSS under `wwwroot`.

## Workflow

1. Resolve repository root.
2. Read `AGENTS.md`, workflow, ticket, README, CONTRIBUTING, and relevant docs.
3. Confirm Graphify availability and exact installed commands.
4. Assess existing graph usability and freshness.
5. Reuse, build, or refresh only when justified.
6. Query with concrete ticket symbols and domain terms.
7. Build a compact candidate set.
8. Validate findings in source and SQL.
9. Record commands, findings, validation, and limitations.

Do not invent `/graphify` commands or update flags.

A previously successful local command may have been:

```powershell
graphify label "." --backend ollama --batch-size 40
```

The environment may use:

```powershell
$env:OLLAMA_BASE_URL = "http://localhost:11434/v1"
$env:OLLAMA_API_KEY = "ollama"
$env:OLLAMA_MODEL = "qwen25coder14b:latest"
```

Confirm all syntax and configuration locally before execution. Do not commit machine-specific
settings or silently switch backend/model.

## Query guidance

Use ticket-specific questions around:

- `IProductUrlDiscoveryService`;
- discovery strategy factories/implementations;
- catalog refresh orchestration;
- price collection;
- queue reservation;
- extractors;
- `crawler_run` and `ingestion_run`;
- `product_catalog`;
- `price_collect_queue`;
- snapshots/errors;
- dashboard query sources;
- Worker commands;
- DB bootstrap/routines.

Use `path` only as navigation. It is not runtime proof.

## Source-validation rules

Verify:

- actual project references;
- DI registration;
- interface implementation;
- controller/Worker entry points;
- EF mappings;
- SQL routine invocation;
- queue transitions;
- cancellation flow;
- configuration binding;
- test assertions;
- JavaScript consumers for Web contracts.

When graph and source disagree, source wins.

## Failure handling

If unavailable or failing:

- record exact confirmed command/error;
- preserve existing graph data;
- continue with CRG, `rg`, source, SQL, build, and tests;
- report partial or unavailable Graphify analysis.

## Definition of done

- availability/freshness assessed;
- graph reused/refreshed only when justified;
- focused queries performed;
- relevant C#/SQL findings verified;
- exclusions avoided vendor/build noise;
- limitations reported accurately.
