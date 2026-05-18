# Unified Core Runtime Architecture

This project uses a single unified execution core (`Emuera1824+v24+EMv18+EEv55`) with multiple compatibility profiles, rather than maintaining separate legacy and modern cores.

## Architecture Principle

All games run on the same v24+EE+EM public core. Differences in behavior are controlled by **profiles** and **configuration flags**, not by switching to a different runtime engine.

```
┌─────────────────────────────────────────┐
│  Unified Core: v24+EE+EM                │
│  Process / ErbLoader / ExpressionParser │
│  VariableEvaluator / FunctionMethod     │
├─────────────────────────────────────────┤
│  Profile Layer                          │
│  V24Pure / Snake / SnakeModernMobile    │
├─────────────────────────────────────────┤
│  Optional Strategies                    │
│  UseLazyLoading / UseLazyResourceIndex  │
├─────────────────────────────────────────┤
│  Godot UI / Rendering / Input           │
│  EmueraContent / SpriteManager          │
└─────────────────────────────────────────┘
```

## Core Profiles

| Profile | Purpose |
|---------|---------|
| `V24Pure` (default) | Standard v24+EE+EM core for all normal games. v18 games are backward-compatible on this profile. |
| `Snake` | Enables Snake-specific compatibility behaviors on the same unified core. |
| `SnakeModernMobile` | Same core, optimized for Android with mobile-specific frame pacing, SQLite paths, and resource loading. |

`V24LazyLoading` is **not** a separate profile. Lazyload is controlled by `Config.UseLazyLoading` on any profile.

## What Changed from the Old "Dual Core" Model

The previous architecture attempted to maintain separate runtime identities:
- a v18-labeled normal path for classic games
- `SnakeModernMobile` as a modern Snake/TW path

This has been replaced by a **unified core** approach:

1. **Single parser**: All games use the same ERB/ERH parser that understands v24/EE/EM syntax.
2. **Single variable system**: Float variables (`DIMF`, `LOCALF`, `ARGF`, `RESULTF`) exist in the core regardless of profile.
3. **Single function registry**: v24 functions are registered by default; Snake profile only changes compatibility behavior.
4. **Single save format**: The core can read both v18 and v24 saves.

The `SnakeModernMobile` profile is now a **runtime configuration** that adjusts:
- Frame pacing and `Await` timing
- Android file paths and SQLite behavior
- Resource loading strategy (`UseLazyResourceIndex`)
- Plugin loading availability

It does **not** switch to a different parser, variable system, or function set.

## Save Format Boundaries

### Zip Save Compression

**Status: Deferred.**

This port does not implement zip-compressed saves (`ZipHeader` + `GZipStream`), even though the reference core supports it via `Config.ZipSaveData`. Rationale:

1. Zip save is an EM-private extension, not standard v24 semantics.
2. Text-game save files are typically small; compression benefit is marginal.
3. Adding it would require a second binary header path and careful cross-version testing.
4. It can be added later without breaking existing saves because `EraBinaryDataReader.CreateReader` rejects unknown headers gracefully.

If needed in the future, the implementation path is: buffer writes to `MemoryStream`, compress with `System.IO.Compression.GZipStream` on dispose, and write the `ZipHeader` instead of `Header`.

### Map / XML / DT Persistence

Map, XML, and DataTable runtime structures **do persist** across `SAVE`/`LOAD` in this port, but they are stored differently from the reference core:

- **Reference core**: embeds them as `EraSaveDataType.Map` (0x20), `Xml` (0x21), `DT` (0x22) inside the variable stream, keyed by name.
- **This port**: stores them in a `RuntimeDataStore` custom block that appears **after** all variable data:
  - **Binary saves**: after the second `EOF`, using `__RDS__` marker + `ReadInt64`/`ReadString` pairs.
  - **Text saves**: after the float extension block, using `__RDS_TEXT__` marker + Base64-encoded strings.

This achieves the same persistence semantics with a simpler implementation. Stale-state cleanup is handled by `RuntimeDataStore.Clear()` at the start of every load attempt, so loading an old save that lacks the block correctly resets the store instead of leaking previous runtime data.

### v18 Old Save Read Path

The v18 text save format (versions 1700/1708/1729/1803/1808) remains fully readable. `VariableEvaluator.LoadFrom` attempts binary first; if the header does not match `EraBDConst.Header`, it falls back to the text reader. `LoadGlobal` uses the same fallback. No v18 read path was modified during the v24 upgrade.

## Compatibility Boundaries

### Godot-Specific Layer (Non-Migratable)

These files are specific to the Godot port and must never be replaced by reference core code:

```
Scripts/EmueraMain.cs
Scripts/EmueraThread.cs
Scripts/EmueraContent.cs
Scripts/EmueraImage.cs
Scripts/SpriteManager.cs
Scripts/GenericUtils.cs
Scripts/uEmuera/**
Scripts/Emuera/Content/GraphicsImage.cs
Scripts/Emuera/Content/AppContents.cs
```

### Unified Core (Migratable Semantics)

These areas absorb v24/EE/EM semantics from the reference core but retain their Godot/mobile adaptations:

```
Scripts/Emuera/GameData/Expression/
Scripts/Emuera/GameData/Function/
Scripts/Emuera/GameData/Variable/
Scripts/Emuera/GameProc/
Scripts/Emuera/GameView/
Scripts/Emuera/Config/
```

### Modern Experimental Layer

`Scripts/Emuera/Modern/Script` contains an isolated experimental modern core implementation (`EraType`, `SparseArray`, `ExecutionContext`, `ModernVariableEvaluator`, etc.). This layer is **not yet wired to the main engine** and serves as a reference for future full core migration.

Until it is fully integrated:
- Normal games use the main engine's v24-compatible runtime
- The modern layer validates v24 semantics independently
- Functions are ported from the modern layer to the main engine as `FunctionMethod` subclasses

## Rules

1. **Do not** reference the reference project's `Emuera.csproj` directly. It targets `net8.0-windows` with Windows Forms, SkiaSharp, NAudio, and desktop-only APIs.
2. **Do not** reintroduce snake-only stubs for public v24 functions. Missing public functions should be implemented in the unified registry.
3. **Do not** reintroduce "legacy v18 core" as a separate execution path. v18 compatibility is handled by the unified core with appropriate defaults.
4. Keep the Godot shell: launcher, Android directory scanning, `user://` storage, Godot UI, input bridge, image/sound adaptation.
5. Port only script/runtime logic, then replace desktop dependencies with existing Godot/uEmuera adapters.

## Completion Criteria

- `TOFLOAT`, `TOSTRF`, `#DIMF`, `LOCALF`, `ARGF`, `RESULTF`, `#FUNCTIONF` work as real Float features in the unified core.
- `VARIADIC ARG/ARGS/ARGF` works with modern call binding.
- `EXISTFUNCTION` triggers lazyload on-demand loading when `UseLazyLoading=true`.
- Snake-profile games start without relying on a separate core runtime.
- Android builds use the same core as desktop, only differing in profile configuration.
