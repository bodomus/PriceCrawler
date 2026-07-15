# PriceCrawler Codex workflow package

This package was prepared for `bodomus/PriceCrawler` using the current repository
documentation and structure.

Copy files into the repository root while preserving paths:

```text
PriceCrawler/
├── AGENTS.md
├── .codex/
│   └── PRE_TICKET_WORKFLOW.md
├── .agents/
│   └── skills/
│       ├── graphify-repository-analysis/
│       │   └── SKILL.md
│       └── code-review-graph-analysis/
│           └── SKILL.md
└── .gitignore.recommended
```

Commit:

- `AGENTS.md`;
- `.codex/`;
- `.agents/skills/`.

Review `.gitignore.recommended` and merge the two graph-state rules into the existing
`.gitignore`.

The package deliberately does not include PowerShell wrappers for Graphify or CRG because
the exact installed CLI syntax and output configuration must first be confirmed on the
development machine. The workflow explicitly prohibits Codex from inventing commands.

Repository-specific assumptions incorporated here:

- solution: `PriceCrawler.sln`;
- .NET SDK pinned to `9.0.311`;
- current target framework documented as `net8.0`;
- C# 12, nullable enabled, analyzers enabled;
- Release warnings treated as errors;
- primary projects: Domain, Application, Infrastructure, Web, Worker, Web.Tests;
- PostgreSQL/EF/Npgsql persistence;
- focused `WorkerIntegrationTests` for write-side DB changes;
- explicit local-only destructive seed safety;
- Web dashboard and Worker operational contracts;
- documentation/changelog obligations from `CONTRIBUTING.md`.
