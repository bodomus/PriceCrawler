# MPC-83 — Implement `deploy-stage.ps1`

YouTrack: https://bodomus.youtrack.cloud/issue/MPC-83

Implement a guarded Stage deployment workflow for a validated PriceCrawler release ZIP:

1. Validate ZIP structure, SHA-256 sidecar, `release.json`, schema metadata, and forbidden paths.
2. Acquire an exclusive deployment lock.
3. Verify Stage/Development/Production database identities and PostgreSQL tooling.
4. Create and verify a custom-format Stage backup before any database mutation.
5. Refresh Stage from Development only behind an explicit switch and never target Production.
6. Apply only missing ordered forward migrations with the deploy identity; never downgrade.
7. Stop Worker before Web using verified PID files and executable paths.
8. Extract into a temporary directory, preserve an immutable versioned release, and safely replace `stage/current`.
9. Overlay external Web and Worker Stage configuration while enforcing the Stage database and `ValidateOnly`.
10. Start Web, verify listener ownership and `/health`, then start Worker and verify stabilization.
11. Produce phase-aware text logs, a JSON deployment report, and deployment state without secrets.
12. Provide a non-mutating `-WhatIf` mode and automated tests.

Production deployment, schema downgrade, implicit Development refresh, automatic database restore, secret packaging/logging, and starting Worker before Web health are forbidden.

The complete original specification is attached to MPC-83 as `pricecrawler-deploy-stage-ticket.md`.
