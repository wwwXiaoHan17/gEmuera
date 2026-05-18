# gEmuera v24 Core Upgrade Team Playbook

本文档用于协调多个 Agent 在 `upgrade-v24-pure-core` 分支上继续完成 `PLAN.md` 的迁移任务。目标不是让每个 Agent 各自判断方向，而是让所有人按同一套边界、交接格式、验证标准协作，最终把 gEmuera 的公共核心稳定升级为 `Emuera1824+v24+EMv18+EEv55`，并把 lazyload 保持为同一核心上的可选加载策略。

## 共同目标

当前项目路径：

```text
E:\MyCode\GodotCode\gemuera-c#\gemuera-c#
```

语义参考核心：

```text
E:\MyCode\Era\emuera_lazyloading_selfmodified_version-main-skiasharp
```

最终目标：

```text
v18 游戏              -> V24Pure, UseLazyLoading=false
v24/EE/EM 游戏        -> V24Pure, UseLazyLoading=false
大型游戏可选 lazyload -> 同一核心, UseLazyLoading=true
snake/TW 游戏         -> 同一核心 + snake compatibility profile
```

核心原则：

- 参考项目只作为语义来源，不直接搬 WinForms、SkiaSharp、Windows-only UI。
- Godot 表现层必须保留：`EmueraMain`、`EmueraContent`、`SpriteManager`、`uEmuera` 适配层不能被参考核心覆盖。
- `UseLazyLoading=false` 必须始终是稳定默认路径。
- `UseERD=true` 可以默认开启，但启动期应只做低成本索引，真实解析按需发生。
- 所有阶段必须能 `dotnet build`。
- 不回滚、不覆盖、不格式化无关文件。

## 当前大致进度

按最近一次人工评估和代码状态，整体约为 60% 到 70%。

已基本完成：

- Profile 从 v18 主路径切到 V24Pure/Snake/SnakeModernMobile。
- float 变量、`DIMF`、`FUNCTIONF`、`RESULTF/LOCALF/ARGF` 主干。
- 大量 v24/EE/EM 函数公共注册。
- MAP/XML/DT/SQL 运行时函数层。
- Lazyload 基础骨架。
- GameView/HTML/ImageLayer 的主要公共语义。
- ERD 默认开启，并改为 lazy 解析以降低 v18 游戏启动成本。

仍需重点推进：

- SafeArithmetic 接入主表达式/变量写入路径。
- Modern/Script 实验层和主核心边界清理。
- SparseArray 是否进入主变量系统的决策与实现。
- SelectCaseJumpTable 或等价 SELECTCASE 优化。
- BEFORE_THROW / BEFORE_ERROR。
- zip save、DT/XML/Map 持久化策略。
- CALLSTR/TRYCALLSTR/EXISTFUNCTION 与 lazyload 补载的真实游戏验证。
- FontStyle 位掩码、下划线/删除线、Sprite 翻转、动画暂停/恢复。
- 文档和测试矩阵补齐。

## 分工模型

每个 Agent 都必须只拥有明确文件范围。若任务需要跨范围修改，先在交接说明中声明，再由 Lead 整合。

### Lead Integrator

职责：

- 维护 `PLAN.md`、`TEAM.md`、`Docs/core-upgrade-inventory.md`、`Docs/core-upgrade-test-matrix.md` 的真实状态。
- 分配任务，避免两个 Agent 同时改同一组核心文件。
- 审查每个 Agent 的 diff，确认没有覆盖 Godot UI 或用户改动。
- 负责最终 build、smoke、真实游戏日志汇总。

主要文件：

```text
PLAN.md
TEAM.md
Docs/core-upgrade-*.md
Docs/test-erb/**
```

### Agent A: Type System Owner

职责：

- 统一 `EraType`、float、变量 token、变量 evaluator。
- 评估并接入 `SafeArithmetic`。
- 决定 `SparseArray<T>` 是否进入主变量存储路径。
- 清理 Modern/Script 中和主核心重复的类型系统，避免两套系统长期并存。

主要文件：

```text
Scripts/Emuera/GameData/EraType.cs
Scripts/Emuera/GameData/Expression/**
Scripts/Emuera/GameData/Variable/**
Scripts/Emuera/Modern/Script/**
```

参考文件：

```text
E:\MyCode\Era\emuera_lazyloading_selfmodified_version-main-skiasharp\Emuera\Runtime\Script\EraType.cs
E:\MyCode\Era\emuera_lazyloading_selfmodified_version-main-skiasharp\Emuera\Runtime\Script\VariableDescriptor.cs
E:\MyCode\Era\emuera_lazyloading_selfmodified_version-main-skiasharp\Emuera\Runtime\Script\SparseArray.cs
E:\MyCode\Era\emuera_lazyloading_selfmodified_version-main-skiasharp\Emuera\Runtime\Script\SafeArithmetic.cs
```

验收：

- v18 基础脚本启动不变慢、不报新解析错误。
- `DIMF`、`FUNCTIONF`、`GETVARF`、`SETVARF`、`VARSETEX` smoke 通过。
- 整数溢出相关行为与参考核心一致，或文档说明差异。

### Agent B: Parser and Function Owner

职责：

- 完成 ERB/ERH 解析器缺口。
- 验证独立预处理器 `#REF/#REFS/#REFF`。
- 完成 BEFORE_THROW / BEFORE_ERROR。
- 检查 CALLSTR/TRYCALLSTR/TRYCCALLSTR 参数绑定、动态解析、错误处理。
- 补齐 SelectCaseJumpTable 或写出不实现的兼容理由。

主要文件：

```text
Scripts/Emuera/GameProc/LogicalLineParser.cs
Scripts/Emuera/GameProc/ErbLoader.cs
Scripts/Emuera/GameProc/UserDefinedFunction.cs
Scripts/Emuera/GameProc/UserDefinedVariable.cs
Scripts/Emuera/GameProc/Function/**
Scripts/Emuera/GameData/Function/**
Scripts/Emuera/Sub/LexicalAnalyzer.cs
Scripts/Emuera/Sub/WordCollection.cs
```

参考文件：

```text
E:\MyCode\Era\emuera_lazyloading_selfmodified_version-main-skiasharp\Emuera\Runtime\Script/**
E:\MyCode\Era\emuera_lazyloading_selfmodified_version-main-skiasharp\Emuera\Runtime\Utils\Preload.cs
```

验收：

- `FUNCTIONF`、`DIMF`、`#REF/#REFS/#REFF`、OUT、VARIADIC smoke 通过。
- `CALLFORM/TRYCALL/CALLSTR` 对新参数体系不倒退。
- parser warning/error 行号仍可读。

### Agent C: Save and Data IO Owner

职责：

- 完成 v24 save format 差异清点。
- 验证 v18 save 可读。
- 决定 zip save 是否本阶段实现。
- 决定 Map/XML/DT 是否作为 `EraSaveDataType` 持久化，若不实现必须写明兼容边界。

主要文件：

```text
Scripts/Emuera/Sub/EraBinaryDataReader.cs
Scripts/Emuera/Sub/EraBinaryDataWriter.cs
Scripts/Emuera/Sub/EraDataStream.cs
Scripts/Emuera/GameData/Variable/VariableData.cs
Scripts/Emuera/GameData/Variable/CharacterData.cs
Scripts/Emuera/GameData/Function/RuntimeDataStore.cs
Scripts/Emuera/GameData/Function/Creator.Method.Map.cs
Scripts/Emuera/GameData/Function/Creator.Method.Xml.cs
Scripts/Emuera/GameData/Function/Creator.Method.DT.cs
```

验收：

- v18 旧存档读取路径不破坏。
- float `SAVEDATA`、`SAVEVAR/LOADVAR` smoke 通过。
- 如果 zip save 未实现，README 和 compatibility 文档必须说明。

### Agent D: Lazyload and ERD Owner

职责：

- 保证 `UseLazyLoading=false` 不访问 lazyload 表。
- 保证 `UseLazyLoading=true` 下 CALL/CALLSTR/EXISTFUNCTION 能触发或识别尚未加载函数。
- 维护 ERD 默认开启 + lazy parsing 的启动速度。
- 处理 lazyload 索引损坏、缺失、大小写路径等错误提示。

主要文件：

```text
Scripts/Emuera/GameProc/Process.LazyLoading.cs
Scripts/Emuera/GameProc/Process.CalledFunction.cs
Scripts/Emuera/GameProc/Process.cs
Scripts/Emuera/GameProc/ErbLoader.cs
Scripts/Emuera/GameProc/HeaderFileLoader.cs
Scripts/Emuera/GameData/ConstantData.cs
Scripts/Emuera/GameData/Expression/ExpressionParser.cs
```

验收：

- `Docs/test-erb/lazyload-smoke` 通过。
- `CALLSTR "DELAYED_FUNC"` 可以补载目标函数。
- `EXISTFUNCTION` 对 lazyload 表中存在但未加载的函数返回正确结果。
- v18 游戏启动速度与 main 分支旧核心接近；若不接近，提交启动耗时分析。

### Agent E: GameView and Godot UI Owner

职责：

- 只迁移显示语义，不迁移 WinForms/Skia UI。
- 完成 ImageLayer、HTML display mode、ColorMatrix、FontStyle、下划线/删除线。
- 检查 Sprite 翻转、动画暂停/恢复。
- 保证 Godot 主线程边界，不让后台线程直接改 Godot 节点或 Texture。

主要文件：

```text
Scripts/Emuera/GameView/**
Scripts/Emuera/Content/**
Scripts/EmueraContent.cs
Scripts/EmueraImage.cs
Scripts/SpriteManager.cs
Scripts/GenericUtils.cs
Scripts/uEmuera/Drawing.cs
```

禁止覆盖：

```text
Scripts/EmueraMain.cs
Scripts/EmueraThread.cs
Scripts/FirstWindow.cs
Scripts/uEmuera/**
```

验收：

- `Docs/test-erb/gameview-smoke` 通过。
- `SETIMAGELAYER/CLEARIMAGELAYER/EXISTSIMAGELAYER` 最小真实图像用例通过。
- Android 路径不引入 Windows-only API。

### Agent F: Documentation and QA Owner

职责：

- 把每个完成项映射回 `PLAN.md`。
- 维护测试矩阵、真实游戏日志、已知限制。
- 把用户实测目录和结果写入 Docs。
- 为每个阶段补最小 ERB smoke。

主要文件：

```text
Docs/core-upgrade-inventory.md
Docs/core-upgrade-file-map.md
Docs/core-upgrade-test-matrix.md
Docs/core-upgrade-compatibility.md
Docs/test-erb/**
README.md
README_en.md
README_ja.md
CLAUDE.md
```

验收：

- 文档能回答：v18 能不能跑、v24/EE/EM 能不能跑、lazyload 怎么测、哪些功能尚未实现。
- 每个 smoke 都有期望输出。
- 未实现功能不写成已完成。

## 交接协议

每个 Agent 完成工作后，必须在最终交接中包含以下内容：

```text
Agent:
任务:
修改文件:
参考核心文件:
实现摘要:
未完成/风险:
验证命令:
验证结果:
建议下一位 Agent 接手点:
```

示例：

```text
Agent: D Lazyload and ERD Owner
任务: CALLSTR 触发 lazyload 补载
修改文件:
- Scripts/Emuera/GameProc/Process.CalledFunction.cs
- Scripts/Emuera/GameProc/Process.LazyLoading.cs
参考核心文件:
- ...\Emuera\Runtime\Utils\Preload.cs
实现摘要:
- 在动态函数名解析后先查已加载标签，再查 lazyload index。
- 补载成功后重新解析标签并调用。
未完成/风险:
- ReloadPartialErb 与 lazyload index 更新尚未验证。
验证命令:
- dotnet build ...
- 手动运行 Docs/test-erb/lazyload-smoke
验证结果:
- build 0 errors
- lazyload-smoke 输出 PASS: lazy target loaded
建议下一位 Agent 接手点:
- EXISTFUNCTION 对未加载函数的返回值验证。
```

## 开发规则

### Git 和工作树

- 当前工作树很脏，所有 Agent 都必须假设已有改动来自用户或其他 Agent。
- 禁止 `git reset --hard`。
- 禁止 `git checkout -- <file>` 回滚他人文件。
- 修改前先看目标文件当前内容。
- 只改自己任务范围内文件。
- 如果必须改别人范围，先在交接中说明原因，由 Lead Integrator 合并。

建议每次任务开始先执行：

```powershell
git status --short --branch
```

### 构建命令

基础验证：

```powershell
dotnet build "E:\MyCode\GodotCode\gemuera-c#\gemuera-c#\gemuera-c#.csproj"
```

较干净验证：

```powershell
dotnet build "E:\MyCode\GodotCode\gemuera-c#\gemuera-c#\gemuera-c#.csproj" --no-incremental
```

验收标准：

```text
0 errors
warning 数量不得因本任务明显增加，除非交接说明原因
```

### Smoke 测试目录

当前手动 smoke：

```text
Docs/test-erb/v24-core-smoke
Docs/test-erb/gameview-smoke
Docs/test-erb/lazyload-smoke
```

用户曾使用的运行目录：

```text
E:\Godot_v4.6.2-stable_mono_win64\v24-core-smoke
E:\Godot_v4.6.2-stable_mono_win64\gameview-smoke
E:\Godot_v4.6.2-stable_mono_win64\lazyload-smoke
```

任何新增核心行为都应该补一个最小 smoke，除非该行为只能通过真实游戏验证。

## 任务队列

### P0: 真实启动阻断

优先级最高。任何真实游戏无法启动的问题都优先于清单补全。

处理步骤：

1. 收集 `emuera.log`、`config_debug.log`、`chara_debug.log`。
2. 对照参考核心确认语义。
3. 写最小 ERB 复现。
4. 修主核心。
5. build。
6. 更新 Docs/test matrix。

### P1: CALLSTR + Lazyload 验证

负责人：Agent D + Agent B。

目标：

- 动态字符串调用未加载函数时能补载。
- `EXISTFUNCTION` 能识别 lazyload index。
- `UseLazyLoading=false` 完全不访问 lazyload 表。

验收：

- `lazyload-smoke` 增加 CALLSTR/TRYCALLSTR/EXISTFUNCTION 覆盖。
- 用户运行目录复测通过。

### P1: SafeArithmetic

负责人：Agent A。

目标：

- 从参考核心迁移或等价实现 `SafeArithmetic`。
- 接入整数加减乘除、取模、负号等风险路径。
- 不改变 v18 正常数值行为。

验收：

- 新增 overflow smoke。
- 与参考核心行为一致，或文档记录差异。

### P1: BEFORE_THROW / BEFORE_ERROR

负责人：Agent B。

目标：

- 对照参考核心实现系统事件。
- 异常路径不吞掉原错误位置。
- lazyload 和非 lazyload 下行为一致。

验收：

- 最小 ERB 能触发 BEFORE_ERROR。
- 原错误仍可读。

### P2: Save Format Decisions

负责人：Agent C。

目标：

- 明确 zip save 是否做。
- 明确 Map/XML/DT 是否持久化。
- 不实现的项目必须写入 compatibility 文档。

验收：

- v18 save 读写 smoke。
- float save smoke。
- README 说明存档回退边界。

### P2: GameView Polish

负责人：Agent E。

目标：

- FontStyle 位掩码。
- 下划线/删除线。
- Sprite 翻转。
- 动画暂停/恢复。

验收：

- gameview-smoke 能肉眼确认显示。
- 不引入 SkiaSharp/WinForms。

### P3: Modern/Script Cleanup

负责人：Agent A + Lead。

目标：

- 列出 Modern/Script 中仍未被主核心引用的类。
- 决定保留为实验层、删除、或迁移入主核心。
- 文档说明边界，避免后续 Agent 误以为现代层已经生效。

验收：

- `Docs/core-upgrade-inventory.md` 明确记录。
- 未接入类不计入 PLAN 完成项。

## 参考核心使用规则

允许参考：

```text
Runtime/Script/**
Runtime/Config/**
Runtime/Utils/Era*
Runtime/Utils/Preload.cs
UI/Game/HtmlManager.cs
UI/Game/Console*.cs
UI/Game/ImageLayerManager.cs
UI/Game/image/ColorMatrixHelper.cs
```

禁止直接迁移：

```text
WinForms MainWindow
SkiaSharp View / Canvas
EraPictureBox
FontFactory UI path
NAudio / WMP desktop audio path
OpenGL / Skia Views
Publish profiles
Windows manifest
```

迁移方式：

```text
先读参考语义 -> 找 gEmuera 对应层 -> 用 Godot/uEmuera 现有抽象重写 -> 加 smoke -> build
```

## 冲突处理

如果两个 Agent 都需要同一文件：

1. 先由 Lead 指定主 Owner。
2. 次要 Agent 只提交分析，不改文件。
3. 必须改时，拆成顺序任务，不并行写。

高冲突文件：

```text
Scripts/Emuera/GameData/Function/Creator.Method.cs
Scripts/Emuera/GameData/Function/Creator.cs
Scripts/Emuera/GameData/Variable/VariableData.cs
Scripts/Emuera/GameProc/Process.cs
Scripts/Emuera/GameProc/Process.CalledFunction.cs
Scripts/Emuera/GameProc/HeaderFileLoader.cs
Scripts/Emuera/GameView/EmueraConsole.cs
```

这些文件每次修改后必须给出精确摘要。

## 完成定义

本团队任务完成时，应满足：

- `dotnet build --no-incremental` 通过。
- v18 游戏能用 V24Pure 默认路径启动。
- v24/EE/EM 游戏能用 V24Pure 默认路径启动。
- lazyload 只是开关，不是另一套核心。
- snake profile 只是兼容层，不拥有公共 v24 功能。
- smoke 三套目录通过。
- 至少一个真实 v18 游戏、一个真实 v24/EE/EM 游戏、一个 lazyload 游戏有记录。
- README 和 Docs 清楚说明已实现、未实现、兼容边界。

