# Core Upgrade Test Matrix

This matrix tracks the manual tests needed before the v24+EE+EM pure core can replace the legacy v18 core with confidence.

## Required configurations

| ID | Platform | Profile | UseLazyLoading | Test folder / game | Required result |
|----|----------|---------|----------------|--------------------|-----------------|
| T01 | Windows desktop | `V24Pure` | `false` | real v18 game | Launch, title, input, print, save/load, resources, CBG |
| T02 | Windows desktop | `V24Pure` | `false` | `Docs/test-erb/v24-core-smoke` | Retested 2026-05-17 after ERH array-ref smoke was added; user reported completed, no failure details reported. Rerun needed after `SAVEVAR`/`LOADVAR` smoke addition |
| T02a | Windows desktop | `V24Pure` | `false` | `Docs/test-erb/v18-core-smoke` | New smoke: verify basic PRINT/INPUT/WAIT on V24Pure; expected PASS lines visible |
| T02b | Windows desktop | `V24Pure` | `false` | `Docs/test-erb/float-smoke` | New smoke: verify DIMF, FUNCTIONF, LOCALF, ARGF, RESULTF, TOSTRF; expected PASS lines and float values visible |
| T02c | Windows desktop | `V24Pure` | `false` | `Docs/test-erb/variadic-smoke` | New smoke: verify VARIADIC ARG, ARGLEN, GETARGCOUNT; expected PASS lines and counts visible |
| T02d | Windows desktop | `V24Pure` | `false` | `Docs/test-erb/callstr-smoke` | New smoke: verify CALLSTR and TRYCALLSTR; expected PASS lines visible |
| T02e | Windows desktop | `V24Pure` | `false` | `Docs/test-erb/map-smoke` | New smoke: verify MAP_CREATE, MAP_SET, MAP_GET, MAP_HAS, MAP_REMOVE, MAP_CLEAR, MAP_SIZE, MAP_GETKEYS, MAP_VALUES, MAP_MERGE; expected PASS lines visible |
| T02f | Windows desktop | `V24Pure` | `false` | `Docs/test-erb/sql-smoke` | New smoke: verify SQL_CONNECTION_OPEN, SQL_EXECUTE_NONQUERY, SQL_EXECUTE_READER, SQL_READER_READ, SQL_READER_GET_LONG, SQL_READER_GET_FLOAT, SQL_READER_GET_STRING, SQL_EXECUTE_SCALAR_*; expected PASS lines visible |
| T03 | Windows desktop | `V24Pure` | `false` | `Docs/test-erb/gameview-smoke` | Tested 2026-05-17; user reported completed, no failure details reported |
| T04 | Windows desktop | `V24Pure` | `true` | `Docs/test-erb/lazyload-smoke` | Tested 2026-05-17; user reported completed, no failure details reported |
| T05 | Windows desktop | `Snake` | `false` | real snake/TW game | Launch, title, input, save/load, SQL/resource paths |
| T06 | Android | `V24Pure` | `false` | real v18 game | Launch, input, resources, save/load under Android paths |
| T07 | Android | `SnakeModernMobile` | `false` | real snake/TW game | Launch, touch input, SQLite native path, save/load |

## Feature checks

| Feature | Covered by | Status |
|---------|------------|--------|
| Float variables, `#DIMF` defaults, double-return `#FUNCTIONF`, and ERH `#FUNCTIONF` function references | T02 | Passed 2026-05-17 smoke; user reported no failure details |
| `GETVAR` / `GETVARS` / `GETVARF` default behavior and indexed `VARSETEX` float/array behavior | T02 | Passed 2026-05-17 smoke; user reported no failure details |
| ERD config keys, `VARSIZE` ERD dimension mode, `GETNUM(..., ..., index)`, and `ERDNAME(var, value, index)` | Real-game logs, T02 parser coverage | Fixed after 2026-05-17 real-game log review; rerun affected games and T02 needed |
| v18 startup speed with ERD disabled | T01, compare against `main` branch startup timing | Fast path restored after 2026-05-17 review: `UseERD` now defaults to `false`, ERD file-name preparation and user-defined name loading are skipped unless enabled; rerun T01 needed |
| Compatibility parsing for `#DIM/#DIMS DYNAMIC out` and system-method-name variable declarations | Real-game logs, T02 parser coverage | Fixed after 2026-05-17 real-game log review; rerun affected games and T02 needed |
| Public v24/EE `BINPUT` / `BINPUTS` / `ONEBINPUT` / `ONEBINPUTS` and `TOOLTIP_SETFONT` / `TOOLTIP_SETFONTSIZE` / `TOOLTIP_CUSTOM` / `TOOLTIP_FORMAT` / `TOOLTIP_IMG` | `.erablue resort` startup log, T02 load-only parse coverage | Moved from snake-only registration to common v24 core after 2026-05-17 real-game log review; button-input execution now validates current-generation buttons; rerun affected games and T02 needed |
| Float binary save/load | T01, T02 with manual save/load | Verified 2026-05-17: `EraBinaryDataWriter`/`Reader` handle `double`/`double[]`/`double[,]`/`double[,,]`; `VariableData.LoadVariableBinary` and `CharacterData.LoadFromStreamBinary` cover all float `EraSaveDataType`s; built-in `RESULTF` is intentionally not persisted (temporary scalar, same as reference core semantics) |
| Float text save/load for user `SAVEDATA` variables | T02 with `SystemSaveInBinary=false` and manual save/load | Verified 2026-05-17: `SaveFloatToStreamExtended`/`TryLoadFloatFromStreamExtended` and global variants handle user-defined float variables in text format |
| `CALLSTR` dynamic dispatch | T02, T04, T02d | Passed 2026-05-17 smoke; user reported no failure details |
| `REF` binding for ERH-declared float function references | T02 | Passed 2026-05-17 smoke; user reported no failure details |
| ERH and function-local scalar `#REF` / `#REFS` / `#REFF` declarations | T02 | Passed 2026-05-17 smoke; user reported no failure details |
| ERH `#DIM REF` / `#DIMF REF` / `#DIMS REF` array declarations | T02 | Passed 2026-05-17 retest; user reported no failure details |
| ERH function-reference `OUT` and variadic `...` signatures | T02 | Passed 2026-05-17 smoke; user reported no failure details |
| Variadic arguments | T02, T02c | Passed 2026-05-17 smoke; user reported no failure details |
| MAP functions | T02, T02e | Passed 2026-05-17 smoke; user reported no failure details |
| XML functions | T02 | Passed 2026-05-17 smoke; user reported no failure details |
| DT functions and float cells | T02 | Passed 2026-05-17 smoke; user reported no failure details |
| `SAVEVAR` / `LOADVAR` selected variable binary persistence | T02 | Added to smoke after 2026-05-17 retest; rerun T02 needed |
| MAP/XML/DT binary persistence | T02 with manual save/load | Verified 2026-05-17: `SaveRuntimeDataStore`/`LoadRuntimeDataStore` persist Map/Xml/DT after EOF marker in binary saves |
| MAP/XML/DT text persistence | T02 with `SystemSaveInBinary=false` and manual save/load | Verified 2026-05-17: `SaveRuntimeDataStoreText`/`TryLoadRuntimeDataStoreText` persist Map/Xml/DT via Base64-encoded text blocks |
| MAP/XML/DT stale-state cleanup | T01/T02, then start new game or load old text save | Verified 2026-05-17: `TryLoadRuntimeDataStoreText` calls `RuntimeDataStore.Clear()` before attempting load; old saves without the marker correctly clear store |
| SQL functions | T02, T02f, T05, T07 | Passed 2026-05-17 T02 smoke; T05/T07 still pending |
| HTML text/style | T03 | Passed 2026-05-17 smoke; user reported no failure details |
| HTML `<div>` box layout | T03 | Passed 2026-05-17 smoke; user reported no failure details |
| HTML absolute positioning | T03 | Passed 2026-05-17 smoke; user reported no failure details |
| Lazyload index generation | T04 | Passed 2026-05-17 smoke; user reported no failure details |
| Real v18 save compatibility | T01, T06 | Manual test pending |
| Real v24 save compatibility | Real v24 game | Manual test pending |
| v18 core smoke (basic PRINT/INPUT) | T02a | New smoke added 2026-05-17; manual run pending |
| Float isolated smoke (DIMF/FUNCTIONF/TOSTRF) | T02b | New smoke added 2026-05-17; manual run pending |
| Variadic isolated smoke (VARIADIC/ARGLEN) | T02c | New smoke added 2026-05-17; manual run pending |
| CALLSTR isolated smoke (CALLSTR/TRYCALLSTR) | T02d | New smoke added 2026-05-17; manual run pending |
| MAP isolated smoke (MAP_CREATE/SET/GET...) | T02e | New smoke added 2026-05-17; manual run pending |
| SQL isolated smoke (SQL_CONNECTION_OPEN/EXECUTE...) | T02f | New smoke added 2026-05-17; manual run pending |

## Release gate

The upgrade should not be considered release-ready until T01 through T07 have been run on the `upgrade-v24-pure-core` branch and this file is updated with dates, game names, and observed failures.

New isolated smokes (T02a-T02f) should be run at least once to confirm they produce the documented expected output.
