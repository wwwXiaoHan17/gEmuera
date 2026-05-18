# Core Upgrade ERB Smoke Tests

These files are small manual smoke tests for the v24+EE+EM core migration.

## How to use

1. Copy one test folder into a temporary Era game directory.
2. Point gEmuera at that directory.
3. Run with the target profile and config listed below.
4. Check that the visible output matches the comments in each ERB file.

These tests are intentionally small. They are not a replacement for testing real games.

## Test folders

| Folder | Target | Purpose |
|--------|--------|---------|
| `v18-core-smoke` | `V24Pure`, `UseLazyLoading=false` | Basic PRINT/INPUT/WAIT on V24Pure |
| `float-smoke` | `V24Pure`, `UseLazyLoading=false` | DIMF, FUNCTIONF, LOCALF, ARGF, RESULTF, TOSTRF |
| `variadic-smoke` | `V24Pure`, `UseLazyLoading=false` | VARIADIC ARG, ARGLEN, GETARGCOUNT |
| `callstr-smoke` | `V24Pure`, `UseLazyLoading=false` | CALLSTR and TRYCALLSTR dynamic dispatch |
| `map-smoke` | `V24Pure`, `UseLazyLoading=false` | MAP_CREATE, MAP_SET, MAP_GET, MAP_HAS, MAP_REMOVE, MAP_CLEAR, MAP_SIZE, MAP_GETKEYS, MAP_VALUES, MAP_MERGE |
| `sql-smoke` | `V24Pure`, `UseLazyLoading=false` | SQL_CONNECTION_OPEN, SQL_EXECUTE_NONQUERY, SQL_EXECUTE_READER, SQL_READER_READ, SQL_READER_GET_*, SQL_EXECUTE_SCALAR_* |
| `v24-core-smoke` | `V24Pure`, `UseLazyLoading=false` | Float, FUNCTIONF, CALLSTR, ERD name lookup, variadic, MAP, DT, XML, SAVEVAR/LOADVAR, SQL (combined) |
| `gameview-smoke` | `V24Pure`, `UseLazyLoading=false` | HTML text, `<div>`, `<shape>`, display positioning |
| `lazyload-smoke` | `V24Pure`, `UseLazyLoading=true` | Lazyload index generation and dynamic CALLSTR |

## Expected pass criteria

- No parse error during startup.
- The first screen prints all `PASS:` lines.
- `lazyload-smoke` should print `PASS: lazy target loaded` after the delayed function is called.
- `gameview-smoke` should show a bordered colored block and absolute-positioned text/div content without crashing.
- `v18-core-smoke` should show basic PRINT/INPUT/WAIT working without v24-specific syntax.
- `float-smoke` should show float values (e.g., `1.250000`) next to PASS lines.
- `variadic-smoke` should show correct argument counts (3, 5, 4, 0).
- `callstr-smoke` should show PASS for direct, variable, missing silent, and existing TRYCALLSTR cases.
- `map-smoke` should show string values and integer sizes matching expectations.
- `sql-smoke` should show `1` for successful connection/create/insert and correct scalar/reader values.

## Known limits

- These tests do not validate old v18 save files.
- Image-layer visual tests still need real image assets from a game or a generated fixture.
- Android needs the same folders copied into an accessible game directory before testing.
- `BEFORE_THROW` / `BEFORE_ERROR` smoke is not yet included (feature exists but needs dedicated validation).
