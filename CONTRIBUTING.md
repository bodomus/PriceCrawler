# Contributing

Thanks for your interest in improving VARUS Price Crawler.

## Prerequisites
- .NET SDK 9.0.311 (see `global.json`)
- Docker (optional, for Postgres via `docker compose`)

## Build
```bash
dotnet build PriceCrawler.sln
```

## Run
```bash
dotnet run --project PriceCrawler.Web
```

## Tests
Automated tests already exist in `PriceCrawler.Web.Tests`. Before submitting changes, run:
```bash
dotnet test PriceCrawler.sln
```

If you touch write-side DB routines or repository persistence, prefer the focused integration suite first:
```bash
dotnet test PriceCrawler.Web.Tests\PriceCrawler.Web.Tests.csproj --filter "FullyQualifiedName~WorkerIntegrationTests"
```

If you touch crawler persistence, also check that docs stay in sync with the normalized `product.id` /
`price_snapshot` / `crawl_error` model introduced in `MPC-21`.

## Linting and analyzers
The project relies on built-in .NET analyzers (see `Directory.Build.props`) and formatting via `.editorconfig`.
A standard check is:
```bash
dotnet build PriceCrawler.sln
```

## Pull requests
- Keep changes focused and describe the impact.
- Update `CHANGELOG.md` for user-visible changes.
- Update project documentation (`README.md`, `Status.md`, `docs/*`) when schema or workflow changes.
- If you change write-side persistence, keep `db/routines/*.sql` and `WorkerIntegrationTests` aligned.
- Ensure formatting follows `.editorconfig`.
