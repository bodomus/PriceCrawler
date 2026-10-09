# MPC-82 Implementation Plan
1. Harden release-builder inputs and canonical metadata resolution.
   - Add `-OutputDirectory` and `-ReplaceExistingArtifact`.
   - Resolve NBGV application version through MSBuild when `-Version` is omitted.
   - Resolve and validate the exact Git commit and UTC build timestamp.

2. Implement reusable release content validation.
   - Validate migration filename format, unique/ordered versions, baseline/bootstrap presence, and target version.
   - Define required archive paths and forbidden path/extension/secret rules.
   - Validate both staging tree and final ZIP metadata/content.

3. Make package production inspectable and deterministic.
   - Create archive entries in stable ordinal path order with normalized forward-slash names.
   - Generate `release.json` with explicit component presence and `1 -> 1` schema compatibility.
   - Generate `<archive>.sha256`, verify it, and print version/commit/schema/path/size/hash.
   - Never silently replace an existing ZIP or checksum.

4. Add behavioral tests.
   - Metadata/schema contract tests.
   - Migration inventory and duplicate/missing-file failure tests.
   - Real package layout/safety/checksum test from a different caller working directory.
   - Existing-artifact refusal and explicit replacement test.

5. Update operator and repository documentation.
   - Exact command, output layout, metadata fields, checksum inspection, replacement rules, forbidden content, determinism guarantee and `ValidateOnly` boundary.
   - Update `CHANGELOG.md`, `Status.md`, `README.md`, `Scripts/howdeploy.md`, `db/README.md`, and applicable database-environment documentation.

6. Validate and report.
   - Update CRG and inspect blast radius.
   - Run focused tests, actual package build/inspection, Release build and full tests.
   - Produce `Review/review-MPC-82.md`, release inventory and verification report.
   - Update YouTrack fields and attach/report generated artifacts as supported.
