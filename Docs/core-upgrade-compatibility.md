# Core Upgrade Compatibility Notes

## Overview

The gEmuera engine core has been upgraded from `Emuera1824+v18` to a unified `Emuera1824+v24+EMv18+EEv55` public core.

This document describes the compatibility implications for games, save files, and profiles.

## Core Profiles

| Profile | Description |
|---------|-------------|
| `V24Pure` (default) | Standard v24+EE+EM core with all public features enabled. v18 games are backward-compatible on this profile. |
| `Snake` | Same core as `V24Pure`, but with Snake compatibility behaviors enabled. |
| `SnakeModernMobile` | Same core, optimized for Android with mobile-specific frame pacing, SQLite, and resource paths. |

`V24LazyLoading` is **not** a separate profile. Lazyload is controlled by `Config.UseLazyLoading` on any profile.

## Game Compatibility

### v18 Games

v18 games continue to run on the upgraded core. The v24 core retains backward compatibility for:

- Standard integer/string variables and expressions
- Classic ERB instructions (`PRINT`, `CALL`, `JUMP`, `IF`, `FOR`, `REPEAT`, etc.)
- CSV data loading
- Save/load format (v18 saves are readable)

### v24 / EE / EM Games

These games now run natively without requiring a separate core:

- Float variables (`DIMF`, `LOCALF`, `ARGF`, `RESULTF`) and float expressions
- Float user-defined functions (`#FUNCTIONF`)
- `VARIADIC` keyword for variadic functions
- `#REF` / `#REFS` / `#REFF` reference parameters
- `SETIMAGELAYER` / `CLEARIMAGELAYER` / `CLEARIMAGELAYER_ALL` image layer commands
- `SETANIMETIMER` / `GETANIMETIMER` animation controls
- `EXISTFUNCTION` with lazyload trigger support
- `EVAL` / `EVALF` / `EVALS` dynamic evaluation
- `MAP_*` and `DT_*` data structure APIs
- `CALLSTR` / `TRYCALLSTR` / `TRYCCALLSTR` dynamic calls
- `HTML_STRINGLEN`, `HTML_SUBSTRING`, `HTML_STRINGLINES`
- `EXISTSOUND`, `EXISTSIMAGELAYER`, `GETSOUNDORBGMINFO`
- `SIN`, `COS`, `TAN`, `ASIN`, `ACOS`, `ATAN`, `FLOOR`, `CEIL`, `ROUND`

## Save File Compatibility

| Direction | Compatibility |
|-----------|---------------|
| v18 save -> v24 core | Readable. Float variables will be initialized to 0.0. |
| v24 save -> v18 engine | **Not guaranteed.** v24 saves contain float data that v18 engines cannot interpret. |
| v24 save -> v24 core | Fully compatible. |

## Lazyload

Lazyload is an **optional loading strategy**, not a separate core.

- Enable via `emuera.config`: `UseLazyLoading: true`
- Or create `lazyloading.cfg` in the game directory listing ERB subdirectories to lazy-load
- When enabled, ERB files are loaded on first function call rather than at startup
- `EXISTFUNCTION`, `CALL`, `CALLSTR`, and other call paths automatically trigger on-demand loading
- If `lazyloading.cfg` is missing, the engine falls back to full load gracefully

## Snake Compatibility Profile

The Snake profile enables backward-compatible behaviors for games that depend on Snake-specific extensions or lenient parsing:

### Behaviors Available to All Profiles (Moved to Public v24)

- `VARIADIC` keyword reservation
- `HTML_PRINT` with optional second argument (buffer flag)
- ASCII art preformatting heuristic in HTML rendering
- Performance diagnostics (`[PROC]`, `[LOADTIME]` logs, gated by `Config.DisplayReport`)
- ERD / user-defined variable string-key resolution (`isUserDefined`) when `UseERD` is enabled
- Resource CSV semicolon-stripping (`;name,file` treated as `name,file`)
- `PrepareERDFileNames` and `LoadUserDefinedNameData` when `UseERD` is enabled; this stays off by default to preserve v18 startup speed

### Behaviors Still Snake-Only

- Snake-specific instructions (`PRINTN`, `JUMPSTR`, `CALLSHARP`, etc.)
- Mobile frame pacing (min FPS 60, faster `Await` timing)
- `UseLazyResourceIndex` on Android
- Plugin loading via `PluginManager`
- Startup error deduplication and `emuera_startup_errors.log`

### Temporary Compatibility Behaviors (Marked for Future Removal)

These behaviors are annotated with `TODO: Snake compatibility fallback` comments in the source:

- Private argument subscript relaxation in function definitions
- Continue startup despite ERB parse warnings (use `CompatiErrorLine` config instead)

No public v24 function is currently registered through a snake-only fallback stub.

## Known Limitations and Unimplemented Features

The following features are **not yet implemented** or are deferred. They are documented here to avoid false expectations:

| Feature | Status | Details |
|---------|--------|---------|
| `SafeArithmetic` | Implemented | Wired into `OperatorMethod.cs` integer paths (`PlusIntInt`, `MinusIntInt`, `MultIntInt`, `DivIntInt`, `ModIntInt`, `MinusInt`). Overflow clamps to `long.MaxValue`/`long.MinValue` with warning. Zero-division returns `0` with warning instead of throwing. |
| `SparseArray<T>` | Isolated in Modern layer | Not integrated into main `VariableData` backing. Large arrays still use dense storage. |
| `SelectCaseJumpTable` | Deferred | `SELECTCASE` uses linear comparison; no jump-table optimization yet. **Rationale:** The reference v24 implementation relies on `AExpression.IsConst` to bake constant `CASE` values into a `Dictionary` at load time. The current gEmuera expression hierarchy (`IOperandTerm` / `Term.cs`) does not expose an `IsConst` property, and adding it would require changes across the expression layer (`IOperandTerm.cs`, `Term.cs`, `VariableTerm.cs`, `FunctionMethodTerm.cs`, etc.) outside the permitted parser/instruction paths. Using `is SingleTerm` as a proxy is insufficient because constant folding (e.g., unary minus) does not guarantee reduction to `SingleTerm` depending on `CanRestructure` flags, making the optimization trigger rarely and unpredictably. Furthermore, typical ERA `SELECTCASE` blocks have <10 cases, so the marginal O(1) vs O(n) benefit does not justify the architectural churn and regression risk at this stage. |
| `BEFORE_THROW` | Minimal | Event skeleton exists in `Process.State.cs` but lacks thorough real-game validation. |
| Zip save | Not implemented | Save format uses plain binary/text. No zip compression support. |
| Sprite flip | Implemented | `ConsoleImagePart.cs` exposes `FlipX`/`FlipY`; `EmueraImage.cs` uses `DrawSetTransform` to flip around image center; CBG negative `width`/`height` triggers flip. |
| Animation pause/resume | Implemented | `CroppedImage.cs` `SpriteAnime` supports `PauseAnimation()`/`ResumeAnimation()` with `StartTime` freeze and duration compensation. `EmueraContent.cs` `_Process()` pauses off-screen CBG animations and resumes on-screen ones. |
| `DT/XML/Map` text persistence edge cases | Partial | RuntimeDataStore text block exists, but very large data or special-character edge cases are not fully verified. |

## Android-Specific Limitations

- GPU ColorMatrix is unavailable; CPU fallback is used
- `SpriteManager.UpdateOtherThreads` loads at most 1 texture per frame to prevent main-thread stalls
- Game folders are scanned from `/storage/emulated/0/emuera/`
- File write access may be restricted; lazyload indexes and modern SQL data fall back to `user://`

## Testing Matrix

Recommended test coverage after core upgrade:

| Platform | Profile | Lazyload | Game Type |
|----------|---------|----------|-----------|
| Desktop Windows | V24Pure | false | v18 game |
| Desktop Windows | V24Pure | false | v24/EE/EM game |
| Desktop Windows | V24Pure | true | Large game |
| Desktop Windows | Snake | false | Snake/TW game |
| Android | V24Pure | false | v18 game |
| Android | SnakeModernMobile | true/false | Snake/TW game |

Minimum test coverage per configuration:

- Launch and title screen display
- Keyboard/mouse/touch input
- `PRINT` / `HTML_PRINT` output
- `SAVE` / `LOAD`
- Resource image display
- Background / CBG rendering
- Exit and restart
