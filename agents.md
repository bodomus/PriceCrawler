# AGENTS.md

## Purpose
This file defines how Codex should work in the Varus Crawler repository.

Varus Crawler is a price collection and analysis project for e-commerce product data. Make focused, minimal, verifiable changes without breaking the current crawler workflow, database logic, or analyst-facing UI behavior.

---

## Working style
- Read the relevant code path before editing.
- For non-trivial tasks, make a short plan first.
- Keep changes minimal, local, and reversible.
- Do not refactor unrelated code.
- Preserve existing behavior unless the task explicitly requires changes.
- State assumptions clearly in the final response.

---

## Project scope
Typical flow:
1. load source URLs or sitemap data
2. prepare crawler run
3. fetch product pages
4. extract product/card data
5. save normalized data
6. write price snapshots and run metadata
7. expose results in UI for analysis

Do not change the high-level workflow unless explicitly required.

---

## Important areas
- crawler / worker entry points
- extraction / parsing logic
- repository / infrastructure layer
- database models and migrations
- UI pages for run history / snapshots / errors
- `tests/` — automated tests
- `README.md` / `docs/` — documentation
- config files (`.json`, `.yaml`, `.yml`) — settings

If the actual structure differs, follow the repo instead of inventing a parallel one.

---

## Technical assumptions
- PostgreSQL is the primary database.
- EF Core / Npgsql may be part of the stack.
- Docker / docker-compose may be used for local database runtime.
- Large URL volumes are expected.
- Network calls may fail, timeout, or be rate-limited.
- Existing schema names, status flows, and analyst UI behavior are important.

---

## Commands Codex should prefer
### Build
- `dotnet build`

### Run
Use the repository’s existing startup command first.

Typical examples:
- `dotnet run --project <project-path>`
- `docker compose up --build`

### Tests
Prefer targeted tests first:
- `dotnet test`
- `dotnet test <solution-or-project> --filter <name>`

### Validation before finishing
Run the smallest sufficient validation set:
1. build
2. targeted tests for touched area
3. focused run or smoke check if appropriate

Do not run large production-scale crawling unless needed.

---

## Editing rules
- Prefer editing existing files over creating new ones.
- Do not create duplicate crawler flows or parallel repository paths unless requested.
- Do not rename public routes, config keys, status values, or schema objects unless explicitly required.
- Preserve backward compatibility where practical.
- Keep changes easy to review.

---

## Coding rules
- Follow existing naming and module boundaries.
- Prefer simple, explicit code over clever abstractions.
- Avoid adding new dependencies unless clearly necessary.
- Add comments only where logic is non-obvious.
- Be careful with async flows, cancellation tokens, retries, timeouts, and database transaction boundaries.

---

## Crawler rules
- Treat crawler throughput, retry policy, and timeout behavior as business logic.
- Do not increase request aggressiveness casually.
- Preserve protections against bans, throttling, and unstable remote responses.
- When changing fetch behavior, be explicit about:
  - timeout
  - retry count
  - concurrency
  - delay/throttling
  - cancellation
- Prefer controlled, measurable changes over broad rewrites.

---

## Parsing rules
- Keep extraction logic deterministic and debuggable.
- Do not silently swallow parsing failures.
- Preserve raw source details when needed for diagnostics.
- When selector logic changes, verify that existing success paths still work.
- If parser behavior changes, ensure logs and error recording remain useful.

---

## Database rules
- Treat schema changes as high-impact.
- For database changes:
  - update migrations correctly
  - preserve data where possible
  - document breaking changes
  - verify repository code and UI expectations still match schema
- Do not hardcode connection strings, passwords, or local machine values.
- Be explicit about nullability, indexes, foreign keys, and status references.

---

## Repository / infrastructure rules
- Do not swallow database exceptions.
- Errors in infrastructure code should be logged and surfaced in the existing application style.
- Preserve separation between domain logic, repository logic, and UI logic.
- If adding error handling, ensure it is consistent across similar repositories.

---

## Snapshot / data rules
- Preserve the intended meaning of:
  - product
  - price snapshot
  - crawler run / crawler
  - ingestion run
  - error records
  - collection queue
- Do not change deduplication or latest-record logic unless explicitly required.
- If changing snapshot logic, verify behavior for same-day duplicates and latest-per-product rules.

---

## UI rules
- Keep UI changes minimal and task-focused.
- Do not redesign the whole interface unless explicitly asked.
- Preserve analyst workflow.
- Manual actions should remain explicit; do not introduce automatic heavy requests on every click.
- If adding widgets or panels, wire them to real backend behavior and loading/error states.

---

## Logging and diagnostics
- Keep logs practical and useful.
- Prefer logs that help diagnose:
  - failed requests
  - parsing failures
  - DB write failures
  - migration issues
  - queue / batch progress
  - crawler run status
- Do not add unnecessary noise.
- If SQL logging exists, preserve its style.

---

## Performance rules
- Do not claim performance improvements without evidence.
- For changes related to throughput, batching, concurrency, or queue processing:
  - explain the expected effect
  - preserve correctness first
  - validate with at least a focused smoke test where possible
- Prefer bounded concurrency over uncontrolled parallelism.

---

## Config rules
- Preserve existing config schema unless the task explicitly changes it.
- If adding config keys:
  - choose clear names
  - document defaults
  - keep backward compatibility where possible
- Update example configs when config behavior changes.

---

## Tests policy
- Add or update tests for behavior changes when feasible.
- Prefer focused tests around:
  - parser behavior
  - repository behavior
  - deduplication logic
  - run status transitions
  - queue/batch logic
  - controller/page behavior where practical
- If full integration testing is too heavy, add the best lightweight coverage possible.
- If tests cannot be run, say so explicitly.

---

## Documentation policy
Update docs when changing:
- setup
- run commands
- crawler behavior
- throttling / concurrency behavior
- config schema
- DB schema
- analyst UI behavior
- troubleshooting steps

At minimum check:
- `README.md`
- example configs
- `docs/`
- changelog / release notes if present

---

## Safety and constraints
- Never hardcode secrets, tokens, passwords, or local absolute paths.
- Never invent successful command results if commands were not run.
- Never perform destructive data cleanup unless explicitly requested.
- Be careful with migrations, deletes, truncates, and bulk updates.
- If a task is risky, state the risk clearly.

---

## Definition of done
A task is done only when:
- the requested change is implemented
- touched files are internally consistent
- minimal necessary validation was performed
- relevant docs/config examples were updated if needed
- risks, limitations, or manual follow-up were stated clearly

---

## Final response format
When finishing a task, respond with:
1. Summary
2. Files changed
3. Validation performed
4. Risks / limitations
5. Manual steps, if any

Be specific. Do not claim tests passed unless they were actually run.

---

## Subdirectory overrides
More specific `AGENTS.md` files in subfolders may define stricter local rules.
When working in a subdirectory, prefer the nearest applicable instructions.