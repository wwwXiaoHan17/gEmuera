# Core Upgrade File Map

This document maps gEmuera files to their counterparts in the reference core (`Emuera1824+v24+EMv18+EEv55+Lazyloadingv2+Skia3`), indicating migration status and constraints.

## Legend

| Symbol | Meaning |
|--------|---------|
| = | Direct port or near-direct adaptation |
| ~ | Significantly adapted for Godot/mobile |
| x | Godot-specific; no reference counterpart |
| o | Reference core file not yet migrated |
| - | N/A or intentionally omitted |

## Test / Verification Artifacts

| gEmuera File | Reference Counterpart | Status | Notes |
|--------------|----------------------|--------|-------|
| `Docs/core-upgrade-test-matrix.md` | - | x | Manual release-gate matrix for v18/v24/lazyload/snake/Android checks |
| `Docs/test-erb/` | - | x | Minimal ERB smoke cases for v24 core, GameView, and lazyload behavior |

## Godot UI Layer (Never Migrate)

| gEmuera File | Reference Counterpart | Status | Notes |
|--------------|----------------------|--------|-------|
| `Scripts/EmueraMain.cs` | x | x | Godot node entry; GPU work queue; config map loading |
| `Scripts/EmueraThread.cs` | x | x | Background thread wrapper; ManualResetEventSlim bridge |
| `Scripts/EmueraContent.cs` | x | x | Core VBoxContainer renderer; line management; CBG layers; Godot-side HTML image/div rendering |
| `Scripts/EmueraImage.cs` | x | x | Custom Control with `_Draw()` for Texture2D |
| `Scripts/SpriteManager.cs` | x | x | Texture cache; `ImageTexture` / `AtlasTexture` |
| `Scripts/GenericUtils.cs` | x | x | Bridge shims from EmueraConsole to EmueraContent |
| `Scripts/FirstWindow.cs` | x | x | Launcher UI; game directory scanning |
| `Scripts/Inputpad.cs` | x | x | On-screen input pad |
| `Scripts/QuickButtons.cs` | x | x | Quick-access button panel |
| `Scripts/Scalepad.cs` | x | x | UI scaling controls |
| `Scripts/OptionWindow.cs` | x | x | Settings popup |
| `Scripts/ColorMatrixGPU.cs` | x | x | GPU shader material for ColorMatrix |
| `Scripts/SpriteDebugViewer.cs` | x | x | Debug overlay (desktop only) |

## Compatibility Shim (Never Migrate)

| gEmuera File | Reference Counterpart | Status | Notes |
|--------------|----------------------|--------|-------|
| `Scripts/uEmuera/Drawing.cs` | x | x | `Bitmap`, `Color`, `Font`, `Rectangle`, `Point`, `Size` |
| `Scripts/uEmuera/Forms.cs` | x | x | `Timer`, `MessageBox`, `ScrollBar`, `ToolTip`, `TextBox` |
| `Scripts/uEmuera/Window.cs` | x | x | `MainWindow` / `DebugDialog` stubs |
| `Scripts/uEmuera/Application.cs` | x | x | Application compatibility |
| `Scripts/uEmuera/Media.cs` | x | x | Media stubs |
| `Scripts/uEmuera/Properties.cs` | x | x | Property compatibility |
| `Scripts/uEmuera/VisualBasic.cs` | x | x | VB compatibility |

## Console / GameView Layer (Adapt, Never Directly Replace)

| gEmuera File | Reference Counterpart | Status | Notes |
|--------------|----------------------|--------|-------|
| `Scripts/Emuera/GameView/EmueraConsole.cs` | `Emuera/UI/Game/Console*.cs` | ~ | Adapted for Godot rendering bridge; image layer ColorMatrix and `followScroll` state are stored for Godot rendering |
| `Scripts/Emuera/GameView/EmueraConsole.Print.cs` | `Emuera/UI/Game/Console*.cs` | ~ | Print methods adapted |
| `Scripts/Emuera/GameView/ConsoleDisplayLine.cs` | `Emuera/UI/Game/ConsoleDisplayLine.cs` | ~ | Line model preserved; Godot node creation removed |
| `Scripts/Emuera/GameView/ConsoleButtonString.cs` | `Emuera/UI/Game/ConsoleButtonString.cs` | = | Near-direct port |
| `Scripts/Emuera/GameView/ConsoleDivPart.cs` | `Emuera/Runtime/Utils/EvilMask/ConsoleDivPart.cs` | ~ | Godot-adapted `<div>` data part; stores box/background/border/layout data without Skia draw code |
| `Scripts/Emuera/GameView/ConsoleImagePart.cs` | `Emuera/UI/Game/ConsoleImagePart.cs` | ~ | Godot-adapted image part; supports `xpos`, `display`, and `cm` ColorMatrix without Skia draw code |
| `Scripts/Emuera/GameView/ConsoleShapePart.cs` | `Emuera/UI/Game/ConsoleShapePart.cs` | = | Near-direct port |
| `Scripts/Emuera/GameView/HtmlManager.cs` | `Emuera/UI/Game/HtmlManager.cs` | ~ | HTML parsing preserved; `<img display='relative/absolute-lefttop/absolute-leftbottom' cm='...'>`, HTML-aware substring splitting, and `<div>` box attributes are adapted |

## Content / Resource Layer (Adapt)

| gEmuera File | Reference Counterpart | Status | Notes |
|--------------|----------------------|--------|-------|
| `Scripts/Emuera/Content/AppContents.cs` | `Emuera/Runtime/Content/AppContents.cs` | ~ | Godot `ImageTexture` replaces Skia bitmap |
| `Scripts/Emuera/Content/ConstImage.cs` | `Emuera/Runtime/Content/ConstImage.cs` | = | Near-direct port |
| `Scripts/Emuera/Content/CroppedImage.cs` | `Emuera/Runtime/Content/CroppedImage.cs` | = | Near-direct port |
| `Scripts/Emuera/Content/GraphicsImage.cs` | `Emuera/Runtime/Content/GraphicsImage.cs` | ~ | Godot `Image.BlendRect` replaces Skia canvas |

## Config Layer (Partially Migrated)

| gEmuera File | Reference Counterpart | Status | Notes |
|--------------|----------------------|--------|-------|
| `Scripts/Emuera/Config/Config.cs` | `Emuera/Runtime/Config/Config.cs` | ~ | Extended with v24/EE/EM items |
| `Scripts/Emuera/Config/ConfigData.cs` | `Emuera/Runtime/Config/ConfigData.cs` | ~ | Extended |
| `Scripts/Emuera/Config/ConfigCode.cs` | `Emuera/Runtime/Config/ConfigCode.cs` | = | Near-direct port |
| `Scripts/Emuera/Config/ConfigItem.cs` | `Emuera/Runtime/Config/ConfigItem.cs` | = | Near-direct port |
| `Scripts/Emuera/Config/KeyMacro.cs` | `Emuera/Runtime/Config/KeyMacro.cs` | = | Near-direct port |

## GameProc / Execution Layer (Partially Migrated)

| gEmuera File | Reference Counterpart | Status | Notes |
|--------------|----------------------|--------|-------|
| `Scripts/Emuera/GameProc/Process.cs` | `Emuera/Runtime/Script/Process.cs` | ~ | Main execution loop; extended for v24 |
| `Scripts/Emuera/GameProc/Process.ScriptProc.cs` | `Emuera/Runtime/Script/Process.ScriptProc.cs` | ~ | Script execution |
| `Scripts/Emuera/GameProc/Process.State.cs` | `Emuera/Runtime/Script/Process.State.cs` | ~ | State management |
| `Scripts/Emuera/GameProc/Process.SystemProc.cs` | `Emuera/Runtime/Script/Process.SystemProc.cs` | ~ | System procedures |
| `Scripts/Emuera/GameProc/Process.CalledFunction.cs` | `Emuera/Runtime/Script/Process.CalledFunction.cs` | ~ | Function call stack |
| `Scripts/Emuera/GameProc/Process.LazyLoading.cs` | `Emuera/Runtime/Script/Process.LazyLoading.cs` | ~ | Lazyload strategy |
| `Scripts/Emuera/GameProc/ErbLoader.cs` | `Emuera/Runtime/Script/ErbLoader.cs` | ~ | ERB loading; lazyload hooks added |
| `Scripts/Emuera/GameProc/LogicalLineParser.cs` | `Emuera/Runtime/Script/LogicalLineParser.cs` | ~ | Parser; v24 keywords added |
| `Scripts/Emuera/GameProc/LabelDictionary.cs` | `Emuera/Runtime/Script/LabelDictionary.cs` | ~ | Added `NoneventKeys` for ENUM* |
| `Scripts/Emuera/GameProc/UserDefinedFunction.cs` | `Emuera/Runtime/Script/UserDefinedFunction.cs` | = | Near-direct port |
| `Scripts/Emuera/GameProc/UserDefinedVariable.cs` | `Emuera/Runtime/Script/UserDefinedVariable.cs` | = | Near-direct port |

## GameData / Expression Layer (Partially Migrated)

| gEmuera File | Reference Counterpart | Status | Notes |
|--------------|----------------------|--------|-------|
| `Scripts/Emuera/GameData/ExpressionParser.cs` | `Emuera/Runtime/Script/ExpressionParser.cs` | ~ | Extended for float |
| `Scripts/Emuera/GameData/Expression/IOperandTerm.cs` | `Emuera/Runtime/Script/Expression/IOperandTerm.cs` | = | Interface preserved |
| `Scripts/Emuera/GameData/Expression/SingleTerm.cs` | `Emuera/Runtime/Script/Expression/SingleTerm.cs` | ~ | Extended for float wrapping |
| `Scripts/Emuera/GameData/Function/Creator.cs` | `Emuera/Runtime/Script/Function/Creator.cs` | ~ | Many v24 functions added |
| `Scripts/Emuera/GameData/Function/Creator.Method.cs` | `Emuera/Runtime/Script/Function/Creator.Method.cs` | ~ | 40+ new methods added |
| `Scripts/Emuera/GameData/Variable/VariableData.cs` | `Emuera/Runtime/Script/Variable/VariableData.cs` | ~ | Float channels partial |
| `Scripts/Emuera/GameData/Variable/VariableEvaluator.cs` | `Emuera/Runtime/Script/Variable/VariableEvaluator.cs` | ~ | Float access partial |
| `Scripts/Emuera/GameData/Variable/VariableToken.cs` | `Emuera/Runtime/Script/Variable/VariableToken.cs` | = | Near-direct port |
| `Scripts/Emuera/GameData/Variable/UserDefinedVariableToken.cs` | `Emuera/Runtime/Script/Variable/UserDefinedVariableToken.cs` | = | Near-direct port |
| `Scripts/Emuera/GameData/IdentifierDictionary.cs` | `Emuera/Runtime/Script/IdentifierDictionary.cs` | ~ | Added `VarKeys` for ENUM* |
| `Scripts/Emuera/GameData/ConstantData.cs` | `Emuera/Runtime/Script/ConstantData.cs` | = | Near-direct port |
| `Scripts/Emuera/GameData/GameBase.cs` | `Emuera/Runtime/Script/GameBase.cs` | = | Near-direct port |
| `Scripts/Emuera/GameData/CharacterData.cs` | `Emuera/Runtime/Script/CharacterData.cs` | = | Near-direct port |
| `Scripts/Emuera/GameData/VariableData.cs` | `Emuera/Runtime/Script/VariableData.cs` | ~ | Extended |

## Sub / Utility Layer (Partially Migrated)

| gEmuera File | Reference Counterpart | Status | Notes |
|--------------|----------------------|--------|-------|
| `Scripts/Emuera/Sub/EraBinaryDataReader.cs` | `Emuera/Runtime/Utils/EraBinaryDataReader.cs` | ~ | Float support partial |
| `Scripts/Emuera/Sub/EraBinaryDataWriter.cs` | `Emuera/Runtime/Utils/EraBinaryDataWriter.cs` | ~ | Float support partial |
| `Scripts/Emuera/Sub/EraDataStream.cs` | `Emuera/Runtime/Utils/EraDataStream.cs` | = | Near-direct port |
| `Scripts/Emuera/Sub/LexicalAnalyzer.cs` | `Emuera/Runtime/Script/LexicalAnalyzer.cs` | ~ | v24 keywords added |
| `Scripts/Emuera/Sub/Word*.cs` | `Emuera/Runtime/Script/Word*.cs` | = | Near-direct port |

## Modern Experimental Layer (Isolated)

These files exist only in gEmuera and are **not** in the reference core's structure. They represent a future migration target:

| gEmuera File | Purpose | Status |
|--------------|---------|--------|
| `Scripts/Emuera/Modern/Script/EraType.cs` | Unified type enum (Int/Str/Float/Map/Xml/Dt) | Complete |
| `Scripts/Emuera/Modern/Script/SparseArray.cs` | Sparse 1D/2D array implementations | Complete |
| `Scripts/Emuera/Modern/Script/ExecutionContext.cs` | Runtime context for modern expressions | Complete |
| `Scripts/Emuera/Modern/Script/AExpression.cs` | Modern expression base class | Complete |
| `Scripts/Emuera/Modern/Script/SingleLongTerm.cs` | Modern integer literal term | Complete |
| `Scripts/Emuera/Modern/Script/SingleStrTerm.cs` | Modern string literal term | Complete |
| `Scripts/Emuera/Modern/Script/SingleFloatTerm.cs` | Modern float literal term | Complete |
| `Scripts/Emuera/Modern/Script/VariableCode.cs` | Modern variable metadata codes | Complete |
| `Scripts/Emuera/Modern/Script/VariableDescriptor.cs` | Modern variable descriptors | Complete |
| `Scripts/Emuera/Modern/Script/VariableDescriptorTable.cs` | Descriptor lookup table | Complete |
| `Scripts/Emuera/Modern/Script/ModernVariableData.cs` | Modern variable storage (incl. float) | Complete |
| `Scripts/Emuera/Modern/Script/ModernVariableToken.cs` | Modern variable access token | Complete |
| `Scripts/Emuera/Modern/Script/ModernVariableTerm.cs` | Modern variable expression term | Complete |
| `Scripts/Emuera/Modern/Script/ModernVariableEvaluator.cs` | Modern variable evaluator | Complete |
| `Scripts/Emuera/Modern/Script/ModernExpressionParser.cs` | Modern expression parser | Partial |
| `Scripts/Emuera/Modern/Script/ModernVariableParser.cs` | Modern variable parser | Partial |
| `Scripts/Emuera/Modern/Script/ModernFunctionEvaluator.cs` | Modern function registry | Complete |
| `Scripts/Emuera/Modern/Script/ModernStatement.cs` | Modern statement base | Complete |
| `Scripts/Emuera/Modern/Script/ModernAssignmentStatement.cs` | Modern assignment | Complete |
| `Scripts/Emuera/Modern/Script/ModernLineParser.cs` | Modern line parser | Partial |
| `Scripts/Emuera/Modern/Script/ModernScriptParser.cs` | Modern multi-line script parser | Partial |
| `Scripts/Emuera/Modern/Script/ModernBlockStatement.cs` | Modern block statements (IF/FOR/WHILE) | Complete |

## Files That Must Never Be Replaced by Reference Core

These contain Godot-specific logic and must be protected during any migration:

```
Scripts/EmueraMain.cs
Scripts/EmueraThread.cs
Scripts/EmueraContent.cs
Scripts/EmueraImage.cs
Scripts/SpriteManager.cs
Scripts/GenericUtils.cs
Scripts/uEmuera/**
Scripts/Emuera/Program.cs              (entry point adapted)
Scripts/Emuera/Content/GraphicsImage.cs
Scripts/Emuera/Content/AppContents.cs
Scripts/Emuera/GameView/EmueraConsole.cs
Scripts/Emuera/GameView/EmueraConsole.Print.cs
Scripts/Emuera/GameView/ConsoleDisplayLine.cs
Scripts/Emuera/GameView/HtmlManager.cs
```

## Files That Can Receive Semantic Migrations

These can absorb new logic from the reference core with adaptation:

```
Scripts/Emuera/GameData/Function/Creator.cs
Scripts/Emuera/GameData/Function/Creator.Method.cs
Scripts/Emuera/GameData/ExpressionParser.cs
Scripts/Emuera/GameData/Variable/VariableData.cs
Scripts/Emuera/GameData/Variable/VariableEvaluator.cs
Scripts/Emuera/GameProc/Process.LazyLoading.cs
Scripts/Emuera/GameProc/ErbLoader.cs
Scripts/Emuera/GameProc/LogicalLineParser.cs
Scripts/Emuera/Sub/EraBinaryDataReader.cs
Scripts/Emuera/Sub/EraBinaryDataWriter.cs
```

## Migration Strategy Summary

1. **Protect the Godot shell**: Never replace UI, rendering, or thread bridge files.
2. **Absorb semantics into main engine**: Port v24 functions as `FunctionMethod` subclasses in `Creator.Method.cs`.
3. **Use modern layer as reference**: The `Modern/Script` implementations validate correct behavior before main-engine porting.
4. **Gradual variable system upgrade**: Float support can be enhanced incrementally without breaking v18 games.
5. **DT/MAP/XML as optional modules**: These complex subsystems can be added to the main engine without affecting games that don't use them.
