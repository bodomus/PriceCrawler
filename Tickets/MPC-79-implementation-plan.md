# MPC-79 Implementation Plan

1. Generate a deterministic schema-only snapshot from the isolated repository-created Test database, remove volatile dump directives, add an empty-database guard, add `schema_version`, add routine hash metadata, and register version `1` as `v0.4.1-alpha`.
2. Implement `bootstrap-schema-version.sql` as a transaction that validates required tables, columns/types/nullability, primary/foreign keys, unique/critical indexes, routine signatures, existing routine metadata, and any existing schema-version row before creating/inserting metadata.
3. Add Infrastructure schema contracts and startup orchestration with cancellation-aware asynchronous database access and actionable exceptions.
4. Register schema services/options in Infrastructure DI and replace direct bootstrap calls in both Web and Worker.
5. Configure explicit Development/Test initialization and validation-only Stage/Production behavior. Protected environments remain validation-only even if configuration is unsafe.
6. Add real PostgreSQL integration tests using disposable databases for baseline, bootstrap repeatability, structural rejection, conflicting metadata, version mismatch, and Production no-mutation guarantees.
7. Update release packaging to validate migration numbering and expected-version consistency, copy `db/migrations` and `db/scripts`, and emit database metadata in `release.json`.
8. Update database/operator documentation, README, Status, and CHANGELOG where applicable.
9. Run focused tests, PostgreSQL baseline/bootstrap checks, Development backup/index reconciliation/bootstrap, full Release build/tests, and release ZIP verification.
10. Refresh CRG, inspect the post-change impact radius, refresh Graphify for the new startup relationships, and write `Review/review-MPC-79.md` plus verification details.

