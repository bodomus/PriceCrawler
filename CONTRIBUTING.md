# Contributing

Thanks for your interest in improving Magazine Price Crawler.

## Prerequisites
- .NET SDK 9.0.311 (see `global.json`)
- Docker (optional, for Postgres via `docker compose`)

## Build
```bash
dotnet build
```

## Run
```bash
dotnet run --project VarPrice.Web
```

## Tests
Automated tests are not added yet. If you add tests, run:
```bash
dotnet test
```

## Linting and analyzers
The project relies on built-in .NET analyzers (see `Directory.Build.props`) and formatting via `.editorconfig`.
A standard check is:
```bash
dotnet build
```

## Pull requests
- Keep changes focused and describe the impact.
- Update `CHANGELOG.md` for user-visible changes.
- Ensure formatting follows `.editorconfig`.
