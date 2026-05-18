# gemuera-c# (Godot Emuera Port)

A Godot 4.6 + .NET 8.0 port of the Emuera text game engine. The unified core targets `Emuera1824+v24+EMv18+EEv55` compatibility. Emuera is a Japanese derivative of eramaker that executes `.ERB` script files and reads `.CSV` data files. This project replaces the original Windows Forms / GDI rendering stack with Godot nodes and a `System.Drawing` compatibility shim (`uEmuera` namespace).

## Tech Stack

- **Engine**: Godot 4.6 (C# backend)
- **Framework**: .NET 8.0 (`Godot.NET.Sdk/4.6.2`)
- **Target Frameworks**: `net8.0` (desktop), `net9.0` (Android)
- **Rendering**: Mobile renderer, D3D12 on Windows, `canvas_items` stretch mode
- **Editor Plugins**: gdUnit4, godot_mcp
- **Excluded from compilation**: `uEmuera-0.2.9d/`, `XEmuera-0.5.1/` (reference/legacy code)

## Architecture

The project follows a 4-layer architecture:

```
┌─────────────────────────────────────────────────────────┐
│  Godot UI Layer (Nodes)                                 │
│  EmueraContent → HBoxContainer/VBoxContainer rows       │
│  EmueraImage, Button, Label, ColorRect, Inputpad, etc.  │
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

## Entry Points & Game Loop

1. **`first_window.tscn`** → `FirstWindow._Ready()` scans for `era*` game folders and lists them.
2. User selects a game → scene changes to **`main.tscn`**.
3. **`EmueraMain._Ready()`** resolves `Sys.ExeDir`, loads SHIFT-JIS/UTF-8 config maps, resets `GlobalStatic`, sets up GPU renderer, adds `EmueraContent` to the scene tree, and starts `EmueraThread.instance.Start()`.
4. **`EmueraThread.Work()`** calls `Program.Main()` on a dedicated background `Thread`.
5. **`Program.Main()`** sets directory paths (`csv/`, `erb/`, `resources/`), loads config, creates a `MainWindow` stub, and starts the engine.
6. **`Process.Initialize()`** loads CSV/ERB files, builds `LabelDictionary`, and begins execution at the title screen.
7. **`Process.ScriptProc.runScriptProc()`** is the inner script execution loop.
8. **`EmueraConsole`** manages all output; **`GenericUtils`** forwards display operations to `EmueraContent`.
9. **User input** is captured by Godot UI nodes → forwarded to `EmueraThread.instance.Input()` → wakes the background thread via `ManualResetEventSlim` → fed into `console.PressEnterKey()`.

## Key Components

### Godot UI Layer (`Scripts/`)

| File | Role |
|------|------|
| `EmueraMain.cs` | Main entry Node; bootstraps engine, loads config maps, GPU work queue, starts `EmueraThread` |
| `EmueraThread.cs` | Runs `Program.Main()` on a background `Thread`; bridges Godot input with Emuera's blocking model via `ManualResetEventSlim` |
| `EmueraContent.cs` | Core Godot UI renderer; fixed-height line layout in VBoxContainer with overflow for images; manages menu bar, input pad, quick buttons, CBG layer, message boxes; enforces MaxVisibleLines=1000 node cap; `EffectiveLineHeight` adapts to font metrics |
| `EmueraImage.cs` | Custom `Control` that draws `Texture2D` / `AtlasTexture` regions via `_Draw()`; MouseFilter=Ignore |
| `SpriteManager.cs` | Static texture cache; loads images into `ImageTexture`; manages `AtlasTexture` sprites; `UpdateOtherThreads` processes max 1 texture per frame to avoid main-thread stalls |
| `GenericUtils.cs` | Bridge shims connecting `EmueraConsole` → `EmueraContent` (`AddText`, `ClearText`, `SetBackgroundColor`, `RefreshCBG`, etc.) |
| `FirstWindow.cs` | Launcher UI; scans for `era*` game folders (desktop: exe dir; Android: `/storage/emulated/0/emuera`) and transitions to `main.tscn` |
| `ColorMatrixGPU.cs` | GPU ColorMatrix shader material creation and uniform management |
| `SpriteDebugViewer.cs` | Debug overlay for sprite inspection (F3 toggle, desktop only) |
| `Inputpad.cs` | On-screen input pad for text/number entry |
| `QuickButtons.cs` | Quick-access button panel for recent choices |
| `Scalepad.cs` | UI scaling controls |
| `OptionWindow.cs` | Settings/options popup |

### Core Emuera Engine (`Scripts/Emuera/`)

| Layer | Key Files | Role |
|-------|-----------|------|
| **Config** | `Config/Config.cs`, `ConfigData.cs`, `ConfigCode.cs`, `ConfigItem.cs`, `KeyMacro.cs` | Engine configuration, key macros, replace dictionaries; Android overrides `WindowX` to screen width |
| **GameData** | `GameData/ConstantData.cs`, `GameBase.cs`, `VariableData.cs`, `VariableEvaluator.cs`, `Expression/`, `Function/`, `Variable/` | Game state: variables, constants, expressions, character data |
| **GameProc** | `GameProc/Process.cs`, `Process.ScriptProc.cs`, `Process.State.cs`, `Process.SystemProc.cs`, `ErbLoader.cs`, `LogicalLineParser.cs`, `LabelDictionary.cs` | Script execution engine; parses and runs `.ERB` files; manages CALL/JUMP/RETURN flow |
| **GameView** | `GameView/EmueraConsole.cs`, `EmueraConsole.Print.cs`, `ConsoleDisplayLine.cs`, `ConsoleButtonString.cs`, `ConsoleImagePart.cs`, `ConsoleShapePart.cs`, `HtmlManager.cs` | Console emulation; display model (lines → styled strings / buttons / images / shapes); input/timers/button generations |
| **Content** | `Content/AppContents.cs`, `ConstImage.cs`, `CroppedImage.cs`, `GraphicsImage.cs` | Resource loading (CSV → sprites); dynamic graphics surfaces for ERB `GCREATE`/`GDRAWG`/`GDRAWCIMG` commands; uses Godot native `Image.BlendRect` for sprite compositing |

### Compatibility Shim (`Scripts/uEmuera/`)

The `uEmuera` namespace provides drop-in replacements for `System.Drawing` and `System.Windows.Forms` so the original Emuera code compiles with minimal changes.

| Original Concept | uEmuera Replacement |
|------------------|---------------------|
| `System.Drawing.Bitmap` | `uEmuera.Drawing.Bitmap` + `BitmapTexture` wrapping `Godot.ImageTexture` |
| `System.Drawing.Graphics` | `uEmuera.Drawing.Graphics` (stub) |
| `System.Drawing.Color/Font/Rectangle/Point/Size` | `uEmuera.Drawing.Color`, `Font`, `Rectangle`, `Point`, `Size` |
| `System.Windows.Forms.Timer` | `uEmuera.Forms.Timer` (static HashSet updated manually in the input loop) |
| `System.Windows.Forms.MessageBox/ScrollBar/ToolTip` | `uEmuera.Forms.MessageBox`, `ScrollBar`, `ToolTip` (stubs) |
| `MainWindow` / `PictureBox` | `uEmuera.Window.MainWindow` / `uEmuera.Forms.PictureBox` (stubs bridging to Godot) |

Key shim files:
- `uEmuera/Drawing.cs` — `Bitmap`, `BitmapTexture`, `Graphics`, `Color`, `Font`, `Rectangle`, etc.
- `uEmuera/Forms.cs` — `Timer`, `MessageBox`, `ScrollBar`, `ToolTip`, `TextBox`, `PictureBox`
- `uEmuera/Window.cs` — `MainWindow` / `DebugDialog` stubs with `Update()` refresh logic
- `uEmuera/Application.cs`, `Media.cs`, `Properties.cs`, `VisualBasic.cs` — Additional compatibility stubs

## Project Structure

```
gemuera-c#/
├── project.godot              # Godot project config
├── gemuera-c#.csproj          # .NET 8.0 project file
├── first_window.tscn          # Launcher scene
├── main.tscn                  # Main game scene
├── icon.svg
│
├── Scripts/
│   ├── EmueraMain.cs          # Godot entry point, GPU work queue
│   ├── EmueraThread.cs        # Background thread wrapper
│   ├── EmueraContent.cs       # Godot UI renderer
│   ├── EmueraImage.cs         # Texture drawing control
│   ├── ColorMatrixGPU.cs      # GPU shader management
│   ├── SpriteManager.cs       # Texture cache
│   ├── SpriteDebugViewer.cs   # Debug sprite viewer (F3)
│   ├── GenericUtils.cs        # Bridge shims
│   ├── FirstWindow.cs         # Launcher UI
│   ├── Inputpad.cs            # On-screen input
│   ├── QuickButtons.cs        # Quick buttons
│   ├── Scalepad.cs            # Scale controls
│   ├── OptionWindow.cs        # Options popup
│   ├── MultiLanguage.cs       # Localization
│   ├── ResolutionHelper.cs    # Display resolution
│   │
│   ├── Emuera/                # Core Emuera engine (ported from Windows)
│   │   ├── Program.cs         # Original entry point
│   │   ├── GlobalStatic.cs    # Singleton registry
│   │   ├── Config/            # Configuration
│   │   ├── Content/           # Image/resource management
│   │   ├── GameData/          # Data models, expressions, variables
│   │   ├── GameProc/          # Script execution engine
│   │   └── GameView/          # Console emulation and rendering
│   │
│   ├── Shaders/
│   │   └── color_matrix.gdshader  # ColorMatrix canvas_item shader
│   │
│   └── uEmuera/               # System.Drawing/Forms compatibility shim
│       ├── Drawing.cs         # Bitmap, Color, Font, Rectangle, etc.
│       ├── Forms.cs           # Timer, MessageBox, ScrollBar, etc.
│       ├── Window.cs          # MainWindow stub
│       ├── Application.cs
│       ├── Media.cs
│       ├── Properties.cs
│       ├── VisualBasic.cs
│       └── partial/           # Partial class extensions
│
├── addons/                    # Godot editor plugins
│   ├── gdUnit4/               # Testing framework
│   └── godot_mcp/             # MCP server addon
│
├── Fonts/                     # Embedded fonts (MS Gothic)
│
└── eraAkumaMaid0.305-CH-正式版/  # Game content (excluded from build)

# Reference directories (excluded from compilation):
# uEmuera-0.2.9d/             # Previous Unity port reference
# XEmuera-0.5.1/              # Xamarin/SkiaSharp port reference
```

## Build & Development Notes

- **Target framework**: .NET 8.0 (desktop), .NET 9.0 (Android when `GodotTargetPlatform == android`)
- **Godot version**: 4.6 (C# backend)
- **Editor plugins**: gdUnit4 (testing), godot_mcp (MCP server integration)
- **Autoload**: `McpRuntimeAgent` (from godot_mcp addon)
- **Viewport stretch**: `mode=canvas_items`
- **Rendering**: Mobile method, D3D12 driver on Windows

### Game Directory Layout

Game files must be placed in a folder named `era*` (e.g., `eraAkumaMaid0.305-CH-正式版`) with the following structure:

```
eraGameName/
├── csv/              # Required — game data CSV files
├── erb/              # Required — script ERB files
├── resources/        # Optional — image resources (PNG, JPG, WEBP, BMP, TGA)
└── fonts/            # Optional — external TTF fonts
```

**Search paths:**
- Desktop: `res://` and executable directory
- Android: `/storage/emulated/0/emuera/`

### Threading Model

The engine uses a dual-thread architecture:
- **Main thread** (Godot): UI rendering, input handling, GPU work queue processing, per-frame texture loading (max 1 per frame via `UpdateOtherThreads`)
- **Background thread** (`EmueraThread`): ERB script execution, sprite compositing (`GraphicsImage.BlendRect` via Godot native `Image.BlendRect`), file I/O for texture loading

Cross-thread communication:
- Background → Main: `GenericUtils.uiQueue` (ConcurrentQueue of UI actions)
- Main → Background: `EmueraThread.Input()` via `ManualResetEventSlim`
- GPU work: `EmueraMain.gpuQueue` (ConcurrentQueue of ColorMatrix items, desktop only)

### Rendering Model

The display uses a fixed-height line model matching the original Emuera:
- Every `ConsoleDisplayLine` occupies `EffectiveLineHeight` pixels vertically (computed from font metrics + line spacing)
- Images with negative `ypos` draw above their line (overflow visible, no clipping)
- Node cap: max 1000 line Controls in the VBoxContainer; oldest lines are batch-removed (100 at a time)
- ColorMatrix applied via GDI+ convention: `cm[input][output]`, transposed for shader uniforms
- Sprite compositing uses Godot native `Image.BlendRect` for performance (replaces manual pixel loop)

### Android-Specific Behavior

- `Config.WindowX` is overridden to match `EmueraContent.ContentWidth` (actual screen width) for full-width layout
- `EmueraMain.GpuReady` is always `false` on Android — ColorMatrix falls back to CPU path
- Texture loading in `SpriteManager.UpdateOtherThreads` is limited to 1 per frame to prevent main-thread stalls
- Game folders are scanned from `/storage/emulated/0/emuera/`

### Config Maps

The engine reads three config map files at startup for Japanese text encoding support:
- `emuera_config_shiftjis.bytes`
- `emuera_config_utf8.txt`
- `emuera_config_utf8_zhcn.txt`

These are MD5-based dictionaries used to translate SHIFT-JIS text to UTF-8.

## Code Style & Conventions

- **Namespaces**: Core engine uses `MinorShift.Emuera.*`; Godot UI uses global or `uEmuera.*`
- **Comments**: Mix of Japanese (original Emuera code) and Chinese (port additions)
- **Access Modifiers**: Many `internal` classes and methods; some use `partial` classes
- **Compatibility**: Original Windows Forms code is commented out rather than removed, preserving the port history
