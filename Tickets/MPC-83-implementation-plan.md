# MPC-83 implementation plan

1. Replace legacy parameters with explicit package, Stage, database, tool-mode, configuration, Web URL, and Worker-command inputs.
2. Add fail-closed identifier/environment guards, secret-redacted logging, lock management, and phase timing.
3. Validate sidecar hash, ZIP paths/roots/critical entries, `release.json`, component declarations, schema range, migration names, and entry points.
4. Add Native/Docker PostgreSQL adapters for existence/version checks, dump verification, explicit Development-to-Stage refresh, and forward migrations.
5. Stop only PID-file-owned Stage processes in Worker→Web order and wait for the Web port to release.
6. Extract through a temporary directory, create an immutable versioned release, prepare `current.new`, overlay separately validated external configs, and switch directories safely.
7. Start Web with Stage/ValidateOnly environment, verify listener ownership and health, then start the explicitly selected Worker command and verify stabilization.
8. Persist deployment state, phase-rich text log, and secret-free JSON report; retain evidence and backup on failure.
9. Implement non-mutating `-WhatIf` and package-validation-only support for focused tests.
10. Add automated process tests, run focused tests, package/dry-run validation, Release build/full tests, and post-change CRG impact analysis.
11. Update operator docs, changelog/status, verification report, and `Review/review-MPC-83.md`; update YouTrack and commit.
