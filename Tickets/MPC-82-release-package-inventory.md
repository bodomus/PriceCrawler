# MPC-82 Release Package Inventory
## Verification artifact

- Archive: `artifacts/releases/mpc82-verification/PriceCrawler-v0.4.1-mpc82-verification.zip`
- SHA-256 sidecar: same path with `.sha256`
- Size: `51,112,183` bytes
- SHA-256: `79cc23933cb810bb04beeeff734b5304a9cc22a65dd41f651763ed1c76af176d`
- Archive files: `435`
- Root entries: `web`, `crawler`, `db`, `release.json`

Generated artifacts under `artifacts/` are ignored and are not production source.

## Required entries verified

```text
web/PriceCrawler.Web.dll
crawler/PriceCrawler.Worker.dll
db/README.md
db/migrations/0001_baseline.sql
db/scripts/bootstrap-schema-version.sql
db/scripts/provision-database-runtime-roles.ps1
release.json
```

No duplicated repository root or version directory is present.

## Database inventory

```text
0001_baseline.sql -> schema version 1
```

`release.json` declares:

```json
{
  "database": {
    "minimumSchemaVersion": 1,
    "targetSchemaVersion": 1,
    "migrations": [
      "0001_baseline.sql"
    ]
  }
}
```

## Forbidden inventory result

Independent archive entry scan returned `FORBIDDEN_COUNT=0` for:

- `.git`, IDE, Graphify and CRG state;
- test results and coverage;
- dumps, backups and logs;
- `.env` and `.pgpass`;
- Development/Test appsettings;
- duplicated `web/db`, `crawler/db`, and host `schema.sql`;
- repository nesting outside the four allowed roots.

Packaged base connection strings use placeholders. Stage/Staging/Production appsettings remain `ValidateOnly`.
