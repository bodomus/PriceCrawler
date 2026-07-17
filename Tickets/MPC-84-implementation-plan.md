# MPC-84 implementation plan

1. Validate package and exact successful Stage approval before Production access.
2. Fail closed on confirmation, paths, database identity, independence marker, roles, external configs, schema, URL, and deployment lock.
3. Create and verify the mandatory Production backup.
4. Stop Worker then Web by verified PID metadata; verify port release.
5. Apply only missing ordered forward migrations and Production-only runtime grants.
6. Extract an immutable release, overlay external config into `current.new`, and switch safely.
7. Start Web in Production/`ValidateOnly`, verify listener and health, then start/stabilize Worker.
8. Persist state, text log, JSON report, backup and failure evidence; never perform automatic DB rollback.
9. Add contract, approval mismatch, marker, config, and non-mutating dry-run tests; run DB/runtime and full solution validation.
10. Update operational docs, refresh repository graphs, review blast radius, and commit without pushing.
