---
name: code-review-graph-analysis
description: Use code-review-graph in PriceCrawler for exact symbols, DI and inheritance relationships, callers, callees, dependants, tests, review context, and blast-radius analysis before and after implementation.
---

# Code Review Graph Analysis

## Repository workflow precedence

When `AGENTS.md` or `.codex/PRE_TICKET_WORKFLOW.md` requires this skill, that workflow
takes precedence.

- Level 1 and Level 2: mandatory scoped preflight and post-change update.
- Level 0: normally unnecessary.

## Purpose

Use CRG as a structural dependency and review aid.

It complements but does not replace source/SQL inspection, compilation, integration tests,
database validation, or runtime checks.

## Scope

Analyze relationships across:

- Domain entities/contracts;
- Application use cases/interfaces/DTOs;
- Infrastructure implementations, EF mappings, query sources, crawler adapters;
- Web controllers, ViewModels, Razor/JavaScript contracts;
- Worker CLI/orchestration;
- automated and integration tests;
- C# references to SQL routines and configuration.

## Exclusions

Exclude build, publish, artifact, log, coverage, test-result, graph database, backup,
vendored/minified frontend, and generated files.

Retain project-owned JavaScript when it consumes or defines application contracts.

## Workflow

1. Resolve repository root.
2. Read instructions and ticket.
3. Inspect diff first for reviews.
4. Discover exact CRG command/configuration.
5. Verify graph usability with update/query, not file existence alone.
6. Collect ticket-specific symbols and relationships.
7. Validate every important relationship in source.
8. After implementation, update CRG and inspect blast radius.
9. Report coverage and limitations.

Do not invent CLI syntax.

Possible commands such as these are examples only:

```powershell
code-review-graph --help
code-review-graph build
code-review-graph update --brief
code-review-graph detect-changes --brief
```

Confirm locally before execution.

## Required analysis

Inspect as applicable:

- interfaces and implementations;
- constructors and DI registrations;
- callers/callees;
- extension-method registration;
- hosted services and entry points;
- controllers/actions;
- Worker command dispatch;
- EF entities/configurations/repositories;
- queue/catalog orchestration;
- extractors and discovery strategies;
- DTO/ViewModel/JSON consumers;
- tests;
- cancellation, retries, transactions, and error paths.

Answer:

1. What symbols change?
2. Who calls them?
3. What depends on them?
4. What registrations make them reachable?
5. Which tests assert the behavior?
6. Which SQL/configuration contracts are adjacent?
7. What is the blast radius?
8. Are new paths disconnected?
9. Are obsolete paths still reachable?
10. Does the change cross project or environment boundaries unexpectedly?

## Database caveat

CRG may not fully represent dynamic SQL, EF translation, database routines, data migrations,
configuration-driven behavior, reflection, or runtime DI resolution.

Verify those directly in:

- `DbContext` and mappings;
- repository/query code;
- SQL routines/bootstrap;
- integration tests;
- actual build/runtime evidence.

## Graphify comparison

Classify findings as:

- both tools plus source;
- Graphify plus source;
- CRG plus source;
- tool disagreement resolved by source;
- unresolved due to incomplete coverage.

Do not force agreement.

## Post-change review

After code changes:

- update CRG;
- inspect changed symbols;
- inspect dependants and tests;
- inspect DI reachability;
- inspect cross-project impact;
- identify migration/deployment risk;
- investigate unexpected blast radius.

## Failure handling

Record the confirmed command and concise error. Preserve existing data. Continue with Graphify,
`rg`, source, SQL, build, and tests. Report unavailable/partial structural analysis.

## Definition of done

- availability/freshness assessed;
- confirmed invocation used or absence reported;
- scoped dependency analysis completed;
- important relationships source-verified;
- tests and operational adjacencies examined;
- graph updated after implementation;
- blast radius and limitations documented.
