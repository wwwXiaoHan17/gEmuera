# gEmuera Core Upgrade Plan

## 目标

将 gEmuera 的普通核心从当前的 v18 公共主干升级为统一的 `Emuera1824+v24+EMv18+EEv55` 公共核心，并把 lazyload 降级为一个可选加载策略，而不是一套独立核心。

最终目标结构：

```text
普通 v18 游戏        -> v24+EE+EM pure core, UseLazyLoading=false
普通 v24/EE/EM 游戏  -> v24+EE+EM pure core, UseLazyLoading=false
大型游戏可选加速      -> 同一核心, UseLazyLoading=true
snake/TW 游戏        -> 同一公共核心 + snake compatibility profile
```

不再把 `Emuera1824+v18` 作为普通游戏运行时核心维护。v18 只作为历史回滚基线或对照资料保存。

## 设计原则

### 1. 单核心优先

普通核心、v24 功能、EE/EM 扩展、lazyload、snake 兼容不应分裂成多套引擎。公共解析、变量、表达式、函数注册、执行、保存系统应只有一套。

保留 profile 的目的不是复制核心，而是打开或关闭兼容行为：

```text
CoreProfile.V24Pure
CoreProfile.V24LazyLoading
CoreProfile.Snake
CoreProfile.SnakeModernMobile
```

其中 `V24LazyLoading` 也可以不做成独立 profile，而只是 `Config.UseLazyLoading=true`。

### 2. Lazyload 是加载策略，不是核心语义

属于 v24+EE+EM 公共核心的能力：

- `DIMF`, `FUNCTIONF`, `LOCALF`, `ARGF`, `RESULTF`
- float 表达式、变量、保存、函数返回值
- `CALLSTR`, `TRYCALLSTR`, `TRYCCALLSTR`
- `EVAL`, `EVALF`, `EVALS`
- `MAP_*`
- `SQL_*`
- `SETIMAGELAYER`
- `GETCSVNOBY*`, `MATCHALL`, `MATCHALLEX`
- `BEFORE_THROW`, `BEFORE_ERROR`
- `SELECTCASE` 行为和可选优化
- `#REF`, `#REFS`, `#REFF`, `OUT`, `VARIADIC`
- EE/EM 配置项、保存项、兼容项

只属于 lazyload 的能力：

- `UseLazyLoading`
- `lazyloading.cfg`
- lazyload 索引表读取、生成、更新
- 初始 ERB 扫描时跳过可延迟文件
- `CALL`/`CALLSTR`/`EXISTFUNCTION` 等路径触发补载
- lazyload 相关系统消息、诊断、索引错误处理

### 3. Godot 层不可被 WinForms/Skia 覆盖

参考核心 `E:\MyCode\Era\emuera_lazyloading_selfmodified_version-main-skiasharp` 是 WinForms + SkiaSharp 版本。gEmuera 的表现层是 Godot：

- `Scripts/EmueraMain.cs`
- `Scripts/EmueraThread.cs`
- `Scripts/EmueraContent.cs`
- `Scripts/EmueraImage.cs`
- `Scripts/SpriteManager.cs`
- `Scripts/GenericUtils.cs`
- `Scripts/uEmuera/`

迁移时只吸收核心语义和数据结构，不直接迁移 WinForms 控件、SkiaSharp View、OpenGL 控件、发布配置、Windows-only UI 入口。

### 4. 每阶段保持可构建

每个阶段结束至少满足：

```powershell
dotnet build "E:\MyCode\GodotCode\gemuera-c#\gemuera-c#\gemuera-c#.csproj"
```

构建通过后再进入下一阶段。警告可以分级处理，但不能引入新的运行时核心错误。

## 当前状态摘要

### gEmuera 当前结构

主项目：

```text
E:\MyCode\GodotCode\gemuera-c#\gemuera-c#
```

关键目录：

```text
Scripts/Emuera/       当前内嵌 Emuera 核心
Scripts/uEmuera/      System.Drawing / WinForms 兼容层
Scripts/              Godot UI、线程、输入、渲染桥接
NativeLibs/           Android SQLite native library
Text/                 config 编码映射
Docs/                 现有设计文档
```

当前项目已经具备的迁移基础：

- `net8.0` desktop，Android 为 `net9.0`
- 已接入 `Microsoft.Data.Sqlite`
- 已有 `UseLazyLoading=false` 默认配置
- 已有 `Process.LazyLoading.cs`
- 已有 `Snake` / `SnakeModernMobile` profile
- 已有 `Scripts/Emuera/Modern/Script`，包含部分现代化实验实现
- 已有 `DIMF/FUNCTIONF/ARGF` 相关兼容逻辑，但不是完整 v24 主干
- 已有 Godot Image / Texture 路径替换原 GDI/Skia 图像处理

### 参考核心状态

参考目录：

```text
E:\MyCode\Era\emuera_lazyloading_selfmodified_version-main-skiasharp
```

关键特征：

- `net8.0-windows`
- `1824+v24+EMv18+EEv55+Lazyloadingv2+Skia3`
- `Runtime/Script` 主干已重组
- 包含 SkiaSharp、NAudio、SQLite、插件系统
- README/CHANGELOG 记录了 v24/EE/EM/lazyload/Skia 的功能来源

该参考核心可作为语义来源，但不能作为直接覆盖来源。

## 目标架构

### Core Profile

建议重构 `Program.CoreProfile`：

```csharp
public enum EmueraCoreProfile
{
    V24Pure,
    V24LazyLoading,
    Snake,
    SnakeModernMobile,
}
```

兼容过渡期可保留旧名字：

```csharp
Emuera1824V18 -> V24Pure
```

但内部不要再把普通游戏当作 v18 核心处理。

### 配置关系

`UseLazyLoading` 作为普通配置项继续存在：

```text
UseLazyLoading:NO   -> 全量加载 ERB
UseLazyLoading:YES  -> 使用 lazyload 策略
```

运行时判断建议统一为：

```csharp
bool useLazyLoading = Config.UseLazyLoading && Program.SupportsLazyLoading;
```

其中 `SupportsLazyLoading` 可以在所有 v24 profile 中为 true，在特殊兼容或调试 profile 中临时关闭。

### 分层边界

```text
Godot UI Layer
  EmueraMain / EmueraContent / SpriteManager / Inputpad / QuickButtons

Compatibility Layer
  uEmuera.Drawing / uEmuera.Forms / uEmuera.Window / uEmuera.Utils

Console Layer
  EmueraConsole / ConsoleDisplayLine / Console*Part / HtmlManager

Runtime Core
  Process / ErbLoader / LogicalLineParser / FunctionIdentifier
  ExpressionParser / VariableEvaluator / VariableData

Data and IO Layer
  ConstantData / GameBase / AppContents / EraDataStream / EraBinaryData*
```

迁移时优先保护上两层，替换和升级下三层。

## 阶段计划

## Phase 0: 基线冻结和迁移清单

目标：在修改核心前建立可回滚基线和客观差异清单。

任务：

1. 建立迁移分支。
2. 确认当前 `dotnet build` 通过。
3. 记录当前普通 v18 游戏启动日志。
4. 记录当前 snake/TW 游戏启动日志。
5. 记录当前 Android 构建是否可用。
6. 生成文件对照表：
   - gEmuera 当前 `Scripts/Emuera`
   - 参考核心 `Emuera/Runtime`
   - 参考核心 `Emuera/UI/Game`
   - 参考核心 `Emuera/Runtime/Config`
   - 参考核心 `Emuera/Runtime/Utils`

输出：

```text
Docs/core-upgrade-inventory.md
Docs/core-upgrade-file-map.md
```

验收：

- 当前项目 build 通过。
- 有至少 1 个 v18 游戏启动基线。
- 有至少 1 个 snake 游戏启动基线。
- 明确列出哪些文件要迁移、哪些文件不能迁移。

## Phase 1: Profile 统一

目标：把普通游戏从概念上切到 `V24Pure`，但暂不大规模改核心。

涉及文件：

```text
Scripts/Emuera/Program.cs
Scripts/FirstWindow.cs
Scripts/Emuera/Config/ConfigData.cs
Scripts/Emuera/Config/Config.cs
README*.md
CLAUDE.md
Docs/dual-core-mobile.md
```

任务：

1. 新增或重命名普通 profile 为 `V24Pure`。
2. 将 launcher 中的 `v18` 文案改为 `v24 pure` 或 `normal`。
3. 保留用户可见兼容说明：v18 游戏仍选择普通核心。
4. 默认 `UseLazyLoading=false`。
5. 为后续 lazyload 添加显式开关来源：
   - `emuera.config`
   - 启动参数
   - 或 launcher UI 选项

验收：

- 普通游戏仍能启动。
- snake 游戏仍走 snake profile。
- 日志显示 `CoreProfile=V24Pure` 或等价名称。
- 不改变现有渲染和输入行为。

## Phase 2: v24 类型系统主干迁移

目标：迁移 v24/EE/EM 的类型基础，使 float 和新变量语义成为公共核心能力。

重点来源：

```text
参考核心:
Emuera/Runtime/Script/EraType.cs
Emuera/Runtime/Script/VariableDescriptor.cs
Emuera/Runtime/Script/SparseArray.cs
Emuera/Runtime/Script/SafeArithmetic.cs
Emuera/Runtime/Script/ExecutionContext.cs
```

gEmuera 目标区域：

```text
Scripts/Emuera/GameData/Variable/
Scripts/Emuera/GameData/Expression/
Scripts/Emuera/GameProc/
Scripts/Emuera/Modern/Script/
```

任务：

1. 决定保留 `Scripts/Emuera/Modern/Script` 作为实验层，还是合并入主核心。
2. 将 `EraType` 统一到主表达式系统，而不是只存在于 `Modern/Script`。
3. 引入 `VariableDescriptor` 或等价表，替换大量 `typeof(long)` / `typeof(string)` 分支。
4. 支持 `double` float 路径：
   - `SingleFloatTerm`
   - float operator
   - `GetFloatValue`
   - `SetValue(double)`
5. 引入 `RESULTF`, `LOCALF`, `ARGF` 的真实变量存储。
6. 引入或统一 `SparseArray<T>`，避免大数组内存爆炸。
7. 将整数加减乘等敏感路径接到 `SafeArithmetic`。

验收：

- `dotnet build` 通过。
- v18 游戏不使用 float 时行为不变。
- 最小 ERB 用例可通过：

```erb
@EVENTFIRST
#DIMF X
X = 1.5
PRINTVL TOSTRF(X)
WAIT
```

风险：

- 变量存储结构变化会影响保存读取。
- 表达式类型不一致会导致运行时转换错误。
- `ARGF` 若只做整数兼容，会与真实 float 语义冲突。

回滚点：

- 如果 float 全量迁移过大，可以先实现 v24 API 的解析和函数注册，但禁止保存 float 数据。

## Phase 3: ERB/ERH 解析器升级

目标：让普通核心完整识别 v24/EE/EM 的声明、函数、引用、可变参数语法。

涉及文件：

```text
Scripts/Emuera/GameProc/LogicalLineParser.cs
Scripts/Emuera/GameProc/ErbLoader.cs
Scripts/Emuera/GameProc/UserDefinedFunction.cs
Scripts/Emuera/GameProc/UserDefinedVariable.cs
Scripts/Emuera/GameData/ParserMediator.cs
Scripts/Emuera/Sub/LexicalAnalyzer.cs
Scripts/Emuera/Sub/Word*.cs
```

任务：

1. 迁移 `FUNCTIONF`。
2. 迁移 `DIMF`。
3. 迁移 `#REF`, `#REFS`, `#REFF`。
4. 迁移 `OUT` 参数。
5. 迁移 `VARIADIC ARG/ARGS/ARGF`。
6. 统一用户函数参数绑定逻辑。
7. 处理 `CALLFORM` / `TRYCALL` / `CALLSTR` 对新参数体系的兼容。
8. 保证 lazyload 关闭时仍做全量解析。

验收用例：

```erb
@TEST_REF
#FUNCTION
#DIM OUT X
X = 100
RETURN 1
```

```erb
@TEST_VARIADIC
#FUNCTION
#DIM VARIADIC ARG
RETURN ARGLEN()
```

```erb
@TEST_FLOAT_FUNC
#FUNCTIONF
#DIMF ARG
RETURNF ARG:0 + 0.5
```

验收：

- 所有用例可解析并运行。
- 普通 v18 游戏不出现新增解析警告。
- snake profile 下既有兼容语法不倒退。

## Phase 4: 表达式和内置函数升级

目标：迁移 v24/EE/EM 公共函数，不把它们绑死在 snake profile 下。

涉及文件：

```text
Scripts/Emuera/GameData/Function/Creator.cs
Scripts/Emuera/GameData/Function/Creator.Method.cs
Scripts/Emuera/GameProc/Function/FunctionIdentifier.cs
Scripts/Emuera/GameProc/Function/BuiltInFunctionCode.cs
Scripts/Emuera/GameProc/Function/ArgumentBuilder.cs
Scripts/Emuera/GameProc/Function/ArgumentParser.cs
Scripts/Emuera/GameProc/Function/Instraction.Child.cs
```

迁移功能：

- `CALLSTR`
- `TRYCALLSTR`
- `TRYCCALLSTR`
- `EVAL`
- `EVALF`
- `EVALS`
- `TOFLOAT`
- `TOSTRF`
- `SIN`, `COS`, `TAN`, `ASIN`, `ACOS`, `ATAN`
- `FLOOR`, `CEIL`, `ROUND`
- `GETCSVNOBYNAME`
- `GETCSVNOBYNICKNAME`
- `GETCSVNOBYCALLNAME`
- `GETCSVNOBYMASTERNAME`
- `MATCHALL`
- `MATCHALLEX`
- `EXISTFUNCTION` 与 lazyload 交互
- `BIT*`
- `MAP_*`
- `SQL_*`

原则：

- 不再把这些函数放在 `AddSnakeCompatibilityMethods` 里作为 snake fallback。
- snake profile 只覆盖 snake 特殊行为，不拥有通用 v24 函数。

验收：

- `Creator.cs` 中 v24 公共函数默认注册。
- snake-only fallback 只剩下确实属于 snake 特殊行为的函数。
- SQL 在 desktop 构建中可初始化。
- Android SQLite native path 不倒退。

## Phase 5: 保存格式升级

目标：支持 v24/EE/EM 新变量类型和保存类型，同时明确与 v18 存档的兼容边界。

涉及文件：

```text
Scripts/Emuera/Sub/EraBinaryDataReader.cs
Scripts/Emuera/Sub/EraBinaryDataWriter.cs
Scripts/Emuera/Sub/EraDataStream.cs
Scripts/Emuera/GameData/Variable/VariableData.cs
Scripts/Emuera/GameData/Variable/CharacterData.cs
Scripts/Emuera/GameData/Variable/UserDefinedVariableToken.cs
Scripts/Emuera/GameData/Variable/VariableEvaluator.cs
```

需要支持：

- `Float`
- `FloatArray`
- `FloatArray2D`
- `FloatArray3D`
- `Map`
- `Xml`
- `DT`
- 可选 zip save

兼容策略：

```text
读取旧 v18 存档: 必须支持
读取 v24 存档: 必须支持
v24 存档回旧 v18 引擎: 不保证
```

配置建议：

- 默认不启用压缩保存。
- 默认保持普通游戏最大兼容。
- 用户启用 v24 新保存类型后，在 README 中声明不可回旧 v18。

验收：

- v18 游戏旧存档可读取。
- v18 游戏新存档可读取。
- float 变量保存后可读取。
- `SAVEGLOBAL` / `LOADGLOBAL` 不倒退。
- Android 文件路径使用 `uEmuera.Utils`，不要直接依赖 Windows IO 假设。

## Phase 6: Console/GameView 升级

目标：迁移 v24/EE/EM 的显示语义，同时保留 Godot 渲染。

涉及文件：

```text
Scripts/Emuera/GameView/
Scripts/Emuera/Content/
Scripts/EmueraContent.cs
Scripts/EmueraImage.cs
Scripts/SpriteManager.cs
Scripts/GenericUtils.cs
Scripts/uEmuera/Drawing.cs
```

功能：

- HTML display mode:
  - `relative`
  - `absolute-lefttop`
  - `absolute-leftbottom`
- `SETIMAGELAYER`
- `CLEARIMAGELAYER`
- `EXISTSIMAGELAYER`
- `CLEARIMAGELAYER_ALL`
- CBG width/height/opacity/ColorMatrix 参数
- 下划线、删除线
- FontStyle 位掩码兼容
- ColorMatrix 解析复用
- Sprite 翻转
- 动画暂停/恢复

关键决策：

参考核心的 SkiaSharp 代码不能直接迁移。应将图像语义映射到：

```text
Godot.Image
Godot.ImageTexture
Godot.Control._Draw
SpriteManager
EmueraContent CBG/layer nodes
```

验收：

- `HTML_PRINT` 基础行为不倒退。
- `SPRITE`/`GCREATE`/`GDRAWG` 不倒退。
- `SETIMAGELAYER` 最小用例可显示。
- Android 上 ColorMatrix fallback 仍可用。
- 桌面 GPU ColorMatrix 不被破坏。

## Phase 7: Lazyload 策略接入

目标：在 v24 pure 核心稳定后，让 `UseLazyLoading=true` 成为同一核心上的可选加载策略。

涉及文件：

```text
Scripts/Emuera/GameProc/ErbLoader.cs
Scripts/Emuera/GameProc/Process.LazyLoading.cs
Scripts/Emuera/GameProc/Process.cs
Scripts/Emuera/GameProc/Process.CalledFunction.cs
Scripts/Emuera/GameProc/Process.State.cs
Scripts/Emuera/GameData/Function/Creator.Method.cs
Scripts/Emuera/Config/ConfigData.cs
Scripts/Emuera/Config/ConfigCode.cs
Scripts/Emuera/Config/Config.cs
```

任务：

1. 明确 `UseLazyLoading=false` 的路径完全不访问 lazyload 表。
2. `UseLazyLoading=true` 时：
   - 读取 `lazyloading.cfg`
   - 读取或生成索引表
   - 初始加载跳过可延迟 ERB
   - 函数首次调用时补载
3. `EXISTFUNCTION` 在 lazyload 表中存在但尚未加载时返回正确结果。
4. `CALLSTR` 等动态调用也能触发补载。
5. reload partial ERB 和 lazyload 表更新保持一致。

验收：

- 同一个游戏：
  - `UseLazyLoading=false` 可启动。
  - `UseLazyLoading=true` 可启动。
  - 两种模式标题流程一致。
- 无 `lazyloading.cfg` 时应优雅降级或跳过。
- lazyload 索引损坏时应给出可读错误，不应静默死锁。

## Phase 8: Snake profile 收敛

目标：把 snake 从“半套核心”收敛为“同一核心上的兼容配置”。

当前 snake 相关区域：

```text
Program.IsSnakeProfile
Program.IsSnakeModernMobileProfile
Program.UseLegacySnakeCompatibilityFallbacks
AddSnakeCompatibilityMethods
addSnakeCompatibilityFunctions
SnakeSqlManager
SNAKE_* instructions
```

任务：

1. 分类 snake 行为：
   - 已成为 v24 公共能力的，移出 snake-only。
   - snake 特有语义，保留 profile 分支。
   - 临时 fallback，标注计划移除。
2. 保留 snake 游戏能启动的最小行为。
3. 不让 snake profile 改写普通 v24 核心的函数语义。
4. 对 `SnakeModernMobile` 另列 Android 文件路径、SQLite、编码、资源路径特殊处理。

验收：

- snake/TW 用例可启动。
- 普通 v24 游戏不受 snake fallback 影响。
- `Program.UseLegacySnakeCompatibilityFallbacks` 的用途明确减少。

## Phase 9: 文档、测试和发布准备

目标：将升级后的行为明确写入项目文档，并建立回归清单。

文档更新：

```text
README.md
README_en.md
README_ja.md
CLAUDE.md
Docs/dual-core-mobile.md
Docs/core-upgrade-compatibility.md
```

必须说明：

- 普通核心已升级到 v24+EE+EM。
- v18 游戏仍兼容。
- v18 存档可读，但 v24 新存档不保证可回旧 v18 引擎。
- lazyload 是可选开关。
- snake 是兼容 profile。
- Android 文件路径和 SQLite 限制。

测试矩阵：

```text
Desktop Windows, UseLazyLoading=false, v18 game
Desktop Windows, UseLazyLoading=false, v24/EE/EM game
Desktop Windows, UseLazyLoading=true, large game
Desktop Windows, Snake profile
Android, UseLazyLoading=false, v18 game
Android, Snake profile
```

每个测试至少覆盖：

- 启动
- 标题显示
- 输入
- PRINT/HTML_PRINT
- SAVE/LOAD
- 资源图片
- 背景/CBG
- 退出/重启

## 文件迁移策略

### 不直接覆盖的文件

这些文件承载 Godot 适配，不应被参考核心覆盖：

```text
Scripts/EmueraMain.cs
Scripts/EmueraThread.cs
Scripts/EmueraContent.cs
Scripts/EmueraImage.cs
Scripts/SpriteManager.cs
Scripts/GenericUtils.cs
Scripts/uEmuera/**
Scripts/Emuera/Program.cs
Scripts/Emuera/Content/GraphicsImage.cs
Scripts/Emuera/Content/AppContents.cs
```

### 可作为语义源的文件

这些文件适合对照迁移：

```text
参考核心/Emuera/Runtime/Script/**
参考核心/Emuera/Runtime/Config/**
参考核心/Emuera/Runtime/Utils/Era*
参考核心/Emuera/Runtime/Utils/Preload.cs
参考核心/Emuera/Runtime/Utils/尊尼获加/SqlManager.cs
参考核心/Emuera/UI/Game/HtmlManager.cs
参考核心/Emuera/UI/Game/Console*.cs
参考核心/Emuera/UI/Game/ImageLayerManager.cs
参考核心/Emuera/UI/Game/image/ColorMatrixHelper.cs
```

### 需要重写适配的文件

这些功能不能照搬，需要 Godot 化：

```text
SkiaSharp rendering
WinForms MainWindow
EraPictureBox
FontFactory
Rikaichan UI
OpenGL / Skia Views
NAudio / WMP desktop audio path
Publish profiles
Windows manifest
```

## 风险清单

### 高风险

1. 变量系统迁移不完整。
   - 表现：脚本能加载，运行时变量读写异常。
   - 对策：先迁移变量描述和类型，再迁移函数。

2. 保存格式不兼容。
   - 表现：旧存档读取失败，或新存档损坏。
   - 对策：保存迁移单独成阶段，先读旧，再写新。

3. Godot 主线程限制被破坏。
   - 表现：后台线程直接创建/修改 Godot 节点或 Texture 导致崩溃。
   - 对策：所有 UI 和 Texture 更新继续通过队列回主线程。

4. Skia 逻辑误搬。
   - 表现：构建引入 Windows-only 包，Android 失效。
   - 对策：Skia 只作为语义参考，不引入 package。

5. snake fallback 污染普通核心。
   - 表现：普通游戏使用了 snake 特有容错，隐藏脚本错误。
   - 对策：公共 v24 函数默认注册，snake 只保留特有行为。

### 中风险

1. lazyload 与 `EXISTFUNCTION`、动态 `CALL` 交互复杂。
2. `ReloadPartialErb` 与 lazyload 索引更新冲突。
3. Android 文件系统大小写、权限、SQLite native library 路径差异。
4. 字体度量与原 Emuera/Skia 不完全一致。
5. `SELECTCASE` 优化改变边缘错误提示顺序。

### 低风险

1. 文案从 v18 改成 v24 pure。
2. README 更新。
3. 配置项默认值调整。
4. warning 清理。

## 回滚策略

### Git 分支

建议：

```text
main
legacy-v18-core
upgrade-v24-pure-core
upgrade-v24-lazyload
```

阶段性 tag：

```text
before-v24-core-upgrade
phase-1-profile-unified
phase-2-type-system
phase-3-parser
phase-4-functions
phase-5-save-format
phase-6-gameview
phase-7-lazyload
phase-8-snake-profile
```

### 技术回滚

每阶段只允许一个主风险落地。例如：

- Phase 2 只做类型系统，不改渲染。
- Phase 4 只做函数，不改保存。
- Phase 6 只做显示语义，不改 lazyload。

如果阶段失败，回滚该阶段，不回滚用户无关改动。

## 验证用最小 ERB 集

建议创建 `Docs/test-erb/` 或独立测试游戏目录。

### v18 基础

```erb
@EVENTFIRST
PRINTL HELLO
INPUT
RETURN 0
```

### Float

```erb
@EVENTFIRST
#DIMF X
X = 1.25
PRINTVL TOSTRF(X)
WAIT
```

### FunctionF

```erb
@EVENTFIRST
PRINTVL TOSTRF(CALLF(TEST_FLOAT, 2))
WAIT

@TEST_FLOAT
#FUNCTIONF
#DIM ARG
RETURNF ARG:0 + 0.5
```

### Variadic

```erb
@EVENTFIRST
PRINTVL CALL(TEST_VARIADIC, 1, 2, 3)
WAIT

@TEST_VARIADIC
#FUNCTION
#DIM VARIADIC ARG
RETURN ARGLEN()
```

### CALLSTR

```erb
@EVENTFIRST
CALLSTR "TARGET"
WAIT

@TARGET
PRINTL CALLSTR OK
RETURN 0
```

### SQL

```erb
@EVENTFIRST
PRINTVL SQL_CONNECTION_OPEN("test")
PRINTVL SQL_EXECUTE_NONQUERY("test", "CREATE TABLE IF NOT EXISTS t (id INTEGER)")
WAIT
```

### MAP

```erb
@EVENTFIRST
PRINTVL MAP_CREATE("m")
PRINTVL MAP_SET("m", "k", "v")
PRINTSL MAP_GET("m", "k")
WAIT
```

### Lazyload

```erb
@EVENTFIRST
PRINTL TITLE
CALLSTR "DELAYED_FUNC"
WAIT

@DELAYED_FUNC
PRINTL DELAYED OK
RETURN 0
```

该用例需要配合 lazyload 配置将 `DELAYED_FUNC` 所在文件设为可延迟。

## 推荐实施顺序

最推荐：

```text
Phase 0 -> Phase 1 -> Phase 2 -> Phase 3 -> Phase 4 -> Phase 5 -> Phase 6 -> Phase 7 -> Phase 8 -> Phase 9
```

如果希望先验证方向，可以做缩小版：

```text
Phase 1
Phase 2 的 EraType/Float 最小闭环
Phase 4 的 TOSTRF/FUNCTIONF 最小闭环
Phase 5 的 Float 保存最小闭环
```

不要先做 lazyload。lazyload 会放大解析、函数注册、动态调用、reload、存档之外的所有问题，必须等 v24 pure 普通路径稳定后再接入。

## 完成定义

该计划完成时应满足：

1. 普通核心不再依赖 v18 语义作为主路径。
2. v18 游戏默认走 v24+EE+EM pure core。
3. v24/EE/EM 游戏可使用核心新增语法和函数。
4. `UseLazyLoading=false` 是稳定默认路径。
5. `UseLazyLoading=true` 是同一核心上的可选策略。
6. snake profile 不再维护独立公共核心，只维护兼容差异。
7. Desktop build 通过。
8. Android build 路径明确，SQLite 和文件访问不倒退。
9. README 和 Docs 明确说明兼容边界。

