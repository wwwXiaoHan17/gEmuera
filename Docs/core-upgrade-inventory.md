# Core Upgrade Migration Inventory

## Snapshot Date

2026-05-17

## Build Status

| Target | Status | Notes |
|--------|--------|-------|
| Desktop (.NET 8.0) | Pass | 0 errors, 24 warnings (pre-existing unused fields) |
| Android (.NET 9.0) | Pass | Same core, `GpuReady=false`, CPU ColorMatrix fallback |

## Core Profile Status

| Profile | Status | Notes |
|---------|--------|-------|
| `V24Pure` | Active | Default profile for all normal games |
| `Snake` | Active | Compatibility profile with temporary fallbacks |
| `SnakeModernMobile` | Defined | Auto-selected for `snake-modern` folders; mobile optimizations |
| `V24LazyLoading` | Not a profile | Controlled by `Config.UseLazyLoading` |

## Phase Completion Status

| Phase | Status | Completion % | Notes |
|-------|--------|--------------|-------|
| Phase 0: Baseline | Complete | 100% | File map and inventory created |
| Phase 1: Profile Unification | Complete | 100% | `EmueraCoreProfile.V24Pure` is default; launcher shows "v24" |
| Phase 2: v24 Type System | Partial | 95% | Main engine has real float variable paths, `#DIMF` double-precision default values, `#FUNCTIONF` double-return typing, float/OUT/variadic-aware function reference signatures, scalar and array reference tokens including `#REFF`, `SETVAR`/`VARSETEX` float writes, and float save/load; `SafeArithmetic` wired into `OperatorMethod.cs` integer paths; **SparseArray backing deleted with Modern/Script cleanup** |
| Phase 3: ERB/ERH Parser | Partial | 94% | `#FUNCTIONF`, `DIMF`, `#REF`/`#REFS`/`#REFF`, `OUT`, `VARIADIC`, `CALLSTR`/`TRYCALLSTR` all parse and run; remaining parity risk is runtime edge cases around OUT element references and sparse arrays |
| Phase 4: Expression / Built-in Functions | Complete | 100% | v24 public function/command table matches the reference core except Skia setters that are command instructions; DT/MAP/XML/SQL are in the main engine; stale duplicate snake registrations removed |
| Phase 5: Save Format | Partial | 90% | v18 saves readable; float binary save/load works; scalar and array values share a save-value path for normal binary/global binary/`SAVEVAR`; RuntimeDataStore (Map/Xml/DT) persists in binary saves after an explicit EOF marker and in text saves through a Base64 text extension block; old text saves clear MAP/XML/DT state instead of leaking previous runtime data; **zip save is NOT implemented** |
| Phase 6: Console / GameView | Partial | 95% | `BINPUT`/`BINPUTS`/`ONEBINPUT`/`ONEBINPUTS` and v24 tooltip commands registered in common v24 core; `SETIMAGELAYER` carries ColorMatrix and `followScroll` into the Godot path; CBG, FontStyle rendering, `<img>` `display`/`xpos`/`cm`, and Godot-adapted `<div>` box/background/border/padding/margin/absolute layout are implemented; underline/strikethrough rendering present; **Sprite flip and animation pause/resume implemented** |
| Phase 7: Lazyload Strategy | Complete | 100% | `UseLazyLoading` works; on-demand loading triggered by calls; `lazyloading.cfg` parsing and index generation work; graceful fallback when config missing |
| Phase 8: Snake Profile Convergence | Complete | 100% | Remaining public v24 fallback removed; generic v24 helpers no longer use snake-only class names; snake profile only keeps compatibility behavior |
| Phase 9: Documentation | In progress | 80% | READMEs updated; inventory/file map exist; test matrix records smoke runs; smoke ERBs expanded; unimplemented features documented honestly |

## Known Unimplemented / Deferred Features

These features are **explicitly NOT completed** and must not be claimed as done in user-facing docs:

| Feature | Status | Reason / Plan |
|---------|--------|---------------|
| `SafeArithmetic` | Implemented | Wired into `OperatorMethod.cs` integer paths; overflow clamps with warning; zero-division returns 0 with warning |
| `SparseArray<T>` | Isolated in Modern layer | Modern layer implementation is complete; not yet integrated into main `VariableData` backing; large arrays still use dense storage |
| `SelectCaseJumpTable` | Not implemented | No optimization yet; `SELECTCASE` uses linear comparison; acceptable for current performance |
| `BEFORE_THROW` / `BEFORE_ERROR` | Partial | `BEFORE_ERROR` event dispatch exists in `Process.cs` and `Process.State.cs`; `BEFORE_THROW` skeleton exists but minimal real-game validation; smoke test needed |
| Zip save | Not implemented | No zip compression support in this port. Can be added later without breaking existing saves because the reader rejects unknown headers gracefully. |
| Sprite flip | Implemented | `FlipX`/`FlipY` on `ConsoleImagePart`/`EmueraImage`; negative CBG width/height triggers flip |
| Animation pause/resume | Implemented | `SpriteAnime.PauseAnimation()`/`ResumeAnimation()` with `StartTime` compensation; `EmueraContent._Process()` auto-pauses off-screen CBG |
| `DT/XML/Map` text persistence robustness | Partial | RuntimeDataStore text block exists; edge cases with very large data or special characters not fully verified |

## Game Compatibility Baseline

### v18 Games

- Launch: Yes
- Title screen: Yes
- Input: Yes
- PRINT/HTML_PRINT: Yes
- SAVE/LOAD: Yes (v18 format)
- Resources: Yes
- CBG: Yes

### v24 / EE / EM Games

- Launch: Yes (with profile=`V24Pure`)
- Float syntax parsing: Yes
- Float execution: Yes for main variable/function paths; sparse-array backing remains pending
- `CALLSTR`/`TRYCALLSTR`: Yes
- `VARIADIC`: Yes
- `#REF`/`#REFS`/`#REFF`: Yes
- `SETIMAGELAYER`/`CLEARIMAGELAYER`: Yes
- `EVAL`/`EVALF`/`EVALS`: Yes

### Snake / TW Games

- Launch: Yes (with profile=`Snake`)
- Title screen: Yes
- Input: Yes
- SAVE/LOAD: Yes
- Resources: Yes

## Function Migration Status

### Public v24 Functions (Implemented in Main Engine)

See the full table in `Docs/core-upgrade-file-map.md` under "Public v24 Functions". All listed functions are registered in the main engine.

### Remaining Snake Fallback Stubs

None. Public v24 functions are registered by default in the main engine.

## Data Structure Migration Status

| Structure | Main Engine | Modern Layer | Notes |
|-----------|-------------|--------------|-------|
| Int variables | Yes | Yes | |
| String variables | Yes | Yes | |
| Float variables | Yes | Yes | Main engine supports float scalar/array/user-defined save/load, double-precision `#DIMF` defaults, double-return `#FUNCTIONF`, `GETVARF`, `SETVAR`, indexed `VARSETEX`, and `#REFF`; sparse backing remains pending |
| Sparse arrays | No | Yes | `SparseArray<T>`, `SparseArray2D<T>` in modern layer |
| DT (data tables) | Yes | Yes | Full implementation in main engine via `RuntimeDataStore` |
| MAP (dictionaries) | Yes | Yes | Full implementation in main engine via `RuntimeDataStore` |
| XML documents | Yes | Yes | Full implementation in main engine via `RuntimeDataStore` |
| SQL database | Yes | Yes | SQLite connected; main engine wrappers in `Creator.Method.Sql.cs`; float reader/scalar methods return double |

## Risk Assessment

| Risk | Level | Mitigation |
|------|-------|------------|
| Variable system migration incomplete | High | Modern layer validates semantics; main engine ports gradually; no known runtime crash from current float path |
| Save format incompatibility | Medium | v18 reads supported; v24 binary saves separate normal variables from RuntimeDataStore; text saves append optional float user-variable and RuntimeDataStore extension blocks; old text saves clear MAP/XML/DT state instead of leaking previous runtime data |
| Godot thread violations | Medium | UI queue system enforced; no direct node access from background |
| Snake fallback pollution | Low | Public functions registered by default; no public v24 function is hidden behind snake fallback |
| Skia logic reintroduction | Low | No SkiaSharp package references in main project |
| Lazyload / EXISTFUNCTION interaction | Medium | Tested; on-demand loading triggers correctly |

## Modern/Script Experimental Layer Inventory (2026-05-17)

**Status: Cleaned.** The Modern/Script directory contained a parallel type system that was never wired into the main core. After inventory and build verification, all dead code was removed.

### Boundary with Main Core

The main core references exactly **one** class from Modern/Script:

| Class | Main Core Consumers | Decision |
|-------|---------------------|----------|
| `ModernSqlManager` | `Creator.Method.Sql.cs` (all SQL function wrappers), `Program.ConfigureModernMobileCoreAdapters()`, `GlobalStatic.Reset()` | **Kept** — actively used by SnakeModernMobile profile and v24 SQL functions |

### Deleted Files (Dead Code — Zero Main-Core References)

All of the following formed a self-contained experimental parser/execution engine with no edges to the main core:

**Root:**
- `EraType.cs` — superseded by `GameData/EraType.cs`
- `ExecutionContext.cs` — dead experimental runtime context
- `SparseArray.cs` — isolated; main core uses dense arrays
- `VariableDescriptor.cs` — dead experimental metadata
- `ModernScriptModule.cs` — dead experimental module container
- `ModernScriptModuleParser.cs` — dead experimental ERB parser

**Expressions/ (11 files):**
- `AExpression.cs`, `Term.cs`, `ModernBinaryExpression.cs`, `ModernComparisonExpression.cs`, `ModernLogicalExpression.cs`, `ModernUnaryExpression.cs`
- `ModernExpressionContext.cs`, `ModernExpressionParser.cs`

**Functions/ (5 files, kept 1):**
- `ModernFunctionArgumentBinding.cs`, `ModernFunctionCallExpression.cs`, `ModernFunctionEvaluator.cs`, `ModernFunctionMethod.cs`, `ModernScriptFunctionMethod.cs`

**Statements/ (13 files):**
- `ModernStatement.cs`, `ModernAssignmentStatement.cs`, `ModernBlockStatement.cs`, `ModernDoLoopStatement.cs`, `ModernForStatement.cs`, `ModernIfStatement.cs`, `ModernLineParser.cs`, `ModernLoopControlStatement.cs`, `ModernReturnStatement.cs`, `ModernScriptParser.cs`, `ModernSelectCaseStatement.cs`, `ModernSifStatement.cs`, `ModernWhileStatement.cs`

**Variables/ (9 files):**
- `ModernPrivateVariableStore.cs`, `ModernUserVariableDefinition.cs`, `ModernVariableData.cs`, `ModernVariableEvaluator.cs`, `ModernVariableParser.cs`, `ModernVariableReference.cs`, `ModernVariableSizing.cs`, `ModernVariableTerm.cs`, `ModernVariableToken.cs`, `VariableCode.cs`

### Integration Decision

- **ModernSqlManager** remains the sole integration point. It is a stateful static SQLite manager with no dependencies on the deleted experimental layer.
- **SparseArray** remains unintegrated. If sparse backing is needed later, the implementation can be retrieved from git history (`SparseArray.cs` + `SparseArray2D<T>`).
- **SafeArithmetic** is already wired into main core `OperatorMethod.cs`; the deleted Modern expression classes had their own checked arithmetic which was redundant and unreachable.

## Recommendations for Next Steps

1. **Run and record Phase 9 manual test matrix** (`Docs/core-upgrade-test-matrix.md`)
2. **Wire SafeArithmetic** to main integer expression paths (Agent A) — **DONE** |
3. **Verify `BEFORE_THROW`/`BEFORE_ERROR`** with a minimal smoke ERB (Agent B)
4. **Run the expanded `Docs/test-erb/` smoke scenarios** to verify float, dynamic calls, MAP/XML/DT, and SQL behavior in the actual runtime
5. Document zip save as unsupported — done
