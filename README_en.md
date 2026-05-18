[中文](README.md) | **English** | [日本語](README_ja.md)

# gEmuera

A cross-platform Emuera text game engine port built with Godot 4.6 + .NET 8.0.

Emuera is the execution engine for Japanese eramaker-series text games, parsing `.ERB` script files and `.CSV` data files to run games. This project replaces the original Windows Forms / GDI+ rendering stack with Godot's node system, enabling cross-platform support for desktop and Android.

## Features

- Unified `Emuera1824+v24+EMv18+EEv55` core
- v18 game compatibility; native v24 / EE / EM extension support
- `DIMF` / `FUNCTIONF` / `LOCALF` / `ARGF` / `RESULTF` floating-point support
- `VARIADIC` variadic functions, `#REF` / `#REFS` / `#REFF` reference parameters
- `SETIMAGELAYER` / `CLEARIMAGELAYER` image layer control, `SETANIMETIMER` animation timer
- `EXISTFUNCTION` with lazyload on-demand loading trigger
- Optional Lazyload acceleration (`lazyloading.cfg` + on-demand loading)
- Snake compatibility profile (compatibility config on the same core, not a separate core)
- Save compatibility: v18 saves are readable; v24 new saves are not guaranteed backward to old v18 engines
- Honest unimplemented list: SafeArithmetic, sprite flip, animation pause/resume, zip-compressed saves, etc. are not yet implemented
- ERB script execution, CSV data loading, SHIFT-JIS/UTF-8 encoding support
- GPU-accelerated ColorMatrix color transforms (desktop, character portrait tinting)
- Godot native `Image.BlendRect` sprite compositing (high-performance pixel blending)
- Fixed line-height rendering model with Y-offset image overlay (original-compatible)
- Node count cap (max 1000 lines) to prevent unbounded memory growth
- HTML `<img>` tag inline images, shape drawing, button interaction
- Dual-thread architecture: UI rendering separated from script execution
- On-screen input pad, quick buttons, scaling controls
- Multi-language support (Japanese/Chinese)
- Android adaptive screen-width layout

## Platform Support

| Platform | Framework | Status |
|----------|-----------|--------|
| Windows | .NET 8.0 + D3D12 | Available |
| Linux | .NET 8.0 | Available |
| Android | .NET 9.0 | Available |

## Quick Start

### Requirements

- Godot 4.6 (.NET edition)
- .NET 8.0 SDK
- (Android builds) .NET 9.0 SDK

### Game File Placement

Place game folders in the following locations (folder name must start with `era`):

- **Desktop**: Same directory as the executable, or under the Godot project's `res://` directory
- **Android**: `/storage/emulated/0/emuera/`

Game folder structure:

```
eraGameName/
├── csv/              # Required — game data CSV files
├── erb/              # Required — script ERB files
├── resources/        # Optional — image resources (PNG, JPG, WEBP, BMP, TGA)
└── fonts/            # Optional — external TTF fonts
```

### Running

1. Open the project with Godot 4.6 (.NET)
2. Place game folders in the correct location
3. Run the project and select a game from the launcher

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│  Godot UI Layer                                         │
│  EmueraContent (VBoxContainer, fixed line-height)       │
│  EmueraImage, Button, Label, ColorRect, Inputpad        │
├─────────────────────────────────────────────────────────┤
│  Console Layer (GameView)                               │
│  EmueraConsole → ConsoleDisplayLine → parts             │
│  PrintStringBuffer, StringStyle, HtmlManager            │
├─────────────────────────────────────────────────────────┤
│  Process Layer (GameProc)                               │
│  Process → runScriptProc → Instruction execution        │
│  ErbLoader, LogicalLineParser, LabelDictionary          │
├─────────────────────────────────────────────────────────┤
│  Data Layer (GameData)                                  │
│  VariableEvaluator, ExpressionParser, GameBase          │
│  ConstantData, CharacterData, IdentifierDictionary      │
└─────────────────────────────────────────────────────────┘
```

### Threading Model

The engine uses a dual-thread architecture:

- **Main thread** (Godot): UI rendering, input handling, GPU work queue, max 1 texture load per frame
- **Background thread** (EmueraThread): ERB script execution, sprite compositing, texture file I/O

Cross-thread communication:
- Background → Main: `GenericUtils.uiQueue` (ConcurrentQueue)
- Main → Background: `EmueraThread.Input()` + `ManualResetEventSlim`
- GPU work: `EmueraMain.gpuQueue` (desktop only)

### Rendering Model

Uses a fixed line-height rendering model matching the original Emuera:

- Each `ConsoleDisplayLine` occupies `EffectiveLineHeight` pixels vertically (computed from font metrics + line spacing)
- Images with negative Y offset (`ypos`) draw above their line, overlapping previous lines
- Line content allows overflow (`ClipContents = false`) for image overlay effects
- Node cap at 1000 lines; oldest 100 lines are batch-removed when exceeded
- Sprite compositing uses Godot native `Image.BlendRect` (C++ implementation, significantly faster than C# pixel loops)

### ColorMatrix Color Transforms

Supports the 7-argument `GDRAWSPRITE` ERB command via 5x5 ColorMatrix for character portrait tinting:

- GPU path: SubViewport + canvas_item shader real-time rendering (desktop only)
- CPU path: per-pixel matrix multiplication (Android fallback)
- Matrix convention: GDI+ format `cm[input][output]`, images auto-converted to RGBA8

### Android-Specific Behavior

- `Config.WindowX` is overridden to match actual screen width for full-width layout
- GPU ColorMatrix unavailable; falls back to CPU path
- `SpriteManager.UpdateOtherThreads` limited to 1 texture per frame to prevent main-thread stalls
- Game folders scanned from `/storage/emulated/0/emuera/`

## Project Structure

```
gemuera-c#/
├── project.godot              # Godot project config
├── gemuera-c#.csproj          # .NET project file
├── first_window.tscn          # Launcher scene
├── main.tscn                  # Main game scene
├── Scripts/
│   ├── EmueraMain.cs          # Godot entry point, GPU rendering pipeline
│   ├── EmueraThread.cs        # Background thread wrapper
│   ├── EmueraContent.cs       # UI renderer (line layout, node management)
│   ├── EmueraImage.cs         # Texture drawing control
│   ├── ColorMatrixGPU.cs      # GPU ColorMatrix shader management
│   ├── SpriteManager.cs       # Texture cache (rate-limited loading)
│   ├── GenericUtils.cs        # Engine↔UI bridge
│   ├── FirstWindow.cs         # Launcher (game scanning)
│   ├── Emuera/                # Core Emuera engine
│   │   ├── Config/            # Configuration system
│   │   ├── Content/           # Image/resource management (native BlendRect)
│   │   ├── GameData/          # Data models, expressions, variables
│   │   ├── GameProc/          # Script execution engine
│   │   └── GameView/          # Console emulation and rendering
│   ├── Shaders/
│   │   └── color_matrix.gdshader
│   └── uEmuera/               # System.Drawing/Forms compatibility layer
├── Fonts/                     # Embedded fonts (MS Gothic)
└── addons/                    # Godot editor plugins
```

## Building

```bash
# Desktop build
dotnet build

# Android build (requires .NET 9.0 SDK)
dotnet build -p:GodotTargetPlatform=android
```

## Acknowledgments

- [Emuera](http://osdn.jp/projects/emuera/) — Original Windows engine
- [XEmuera](https://github.com/xerysherry/XEmuera) — Xamarin/SkiaSharp mobile port (reference implementation)
- [uEmuera](https://github.com/xerysherry/uEmuera) — Unity port (reference implementation)

## License

This project is a port of the original Emuera engine and follows its original license terms.
