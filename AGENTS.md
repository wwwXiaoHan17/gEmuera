# AGENTS.md

本文是 AI Agent（Claude Code、AI CLI、AI IDE）进入项目的首读文档。读完本文后，按需查阅「必读与配套文档」中的详细文档。

## 项目简介

本项目基于 **Godot 4.7 mono** + C# 开发，旨在使用开源技术 + Vibe Coding 开发新的 Emuera 模拟器。

Emuera 核心编译器以 C# 编写，为减少开发成本、方便 AI 对接核心、降低 Token 消耗，开发初期即放弃 Godot 推崇的 GDScript，**以 C# 为主语言**。

- 首要运行与测试目标：Android/手机端，方案以导出 APK 可用为准。
- 桌面端可用于调试，但不能替代 APK 验证结论。
- 参考实现：`E:\MyCode\Era\emuera_lazyloading_selfmodified_version-main-skiasharp`、`E:\MyCode\Era\emuera.em-master`（v24）、`E:\MyCode\GodotCode\gemuera\uEmuera-0.2.9d`、`E:\MyCode\GodotCode\gemuera\XEmuera-0.5.1等`。
- 涉及 Godot 架构、UI、性能、平台适配时，使用 `godot-master` skill 辅助判断。
- Snake 接口（`emuera_lazyloading_*`）语义对齐：修改 `Scripts/Emuera/` 解释器前对照
  `E:\MyCode\Era\emuera_lazyloading_selfmodified_version-develop-skiasharp`，保证 ERB 语义不偏离。

## 必读与配套文档

| 文档                                              | 状态     | 说明                                                                     |
| ----------------------------------------------- | ------ | ---------------------------------------------------------------------- |
| `docs/xEmueraCodeWiki`                          | 外部参考   | XEmuera-R：一款专门为Emuera1824+v24+EE+EM适配的模拟。这是使用gpt5.6-sol读取其代码并编写的架构指导文档 |
| `governance/`                                   | 按需     | Agent 自我进化机制：任务复盘记录（evolution-log/）+ 用户提示词模式库（prompt-patterns/）。任务结束提交 PR 前按 `governance/README.md` 写进化记录 |
| `addons/gdUnit4/ADDON.md`                       | 按需     | GDUnit4 插件使用指南（WHY/WHEN/WHERE/HOW），用 GDUnit 做 TDD 时阅读                  |
| `ERBAPI.md`                                     | ERB解释器接口 | 需要为新的Era游戏做适配，且当前的Erb语法解析无法实现时，又或者需要更新Erb语法解释器时，指导Agent对接              |
| `readme/README.md`（另有 en/ja 版）              | 项目概述   | 项目结构、构建、致谢；结构变更后请同步更新                                       |

> 历史 `docs/OriginalFrameworkDesign/`、`docs/gEmueraCodeWiki/`、`docs/staging/` 已删除（其内容已过时或被本文与 `src/Core/` 取代）。
> `docs/plans/2026-08-11-spike-checkpoint-pipeline-workflow.md`（检查点管线工作流 spike 记录）与 `docs/NewFrameworkDesign/generated/dialect-registry-snapshots.json`（方言证据快照）仍保留且在使用中。
> 原 AI 执行指南 `_CLAUDE.md` 亦已删除（2026-08-05），内容见 git 历史。
> 不要重新创建 M0/M1…M7 之类"阶段代号"命名，AGENT 初见必须能从名字直接理解用途。

## 使用的工具与项目

1. **GD Agentic** — 专业 Godot 指导 skill，整合架构、设计、UI/UX 等知识。实际开发中只用到其中的 `godot-master`。<https://github.com/thedivergentai/gd-agentic-skills>
2. **CodeGraph（魔改版）** — 基于红黑树为大型项目建立代码模型，Agent 通过 MCP 工具快速检索代码模型，替代内置 grep 搜索，降低 Token 消耗、加快检索。魔改版支持简单跨项目检索。<https://github.com/colbymchenry/codegraph>
3. **CodeGraph-mcp** — CodeGraph 的配套 MCP 工具，让 AI CLI / AI IDE 能使用 CodeGraph 功能。
4. **GDUnit4** — Godot 社区单元测试框架，测试 GD 脚本、C# 脚本和场景。本项目用于对 Godot 场景及需 Godot 组件的功能进行 TDD 开发。详见 `addons/gdUnit4/ADDON.md`。
5. **xUnit** — 本项目 Emuera 核心（ERB 语法解释器，C# 编写）的原生 C# 单元测试框架，用于语法解释器更新时快速高效地做 TDD。

## 开发方式

本项目采取 简单/中等困难功能：快速开发，无需使用TDD。重要/核心功能：**TDD（测试驱动开发）**，GDUnit4 与 xUnit 协同配合。平衡Agent的代码编写效率与代码质量。

- GDUnit4：测试 Godot 场景与需 Godot 组件的功能。
- xUnit：测试 Emuera 核心（ERB 语法解释器）的纯 C# 逻辑。

**游戏适配铁律（2026-09-07 起强制）**：为任何 Era 游戏做适配时，行为差异**必须**以方言模块方式实现（eraFL 模式：Core 声明模块 + BuiltInDialectCatalog 注册 + LegacyCompatibilityModule 的 Declare/Apply 会话门控 + policy flag 消费），**禁止直接修改 v24 基线共享路径**（Parser/VM/字典的未门控代码）。判断标准：改动在 v24pure/snake/erafl 会话下必须逐字节等价，并以方言快照名录对比（新旧 profile 指令/函数清单零差异）作为"未选择侧不变"的硬证据。参考先例：`governance/evolution-log/2026-08-22-erafl-dialect-manifest.md` 与 `2026-09-07-eramegan-adaptation.md`（含一次违规返工的完整记录）。

任务结束后，删除冗余的临时测试文件，避免造成垃圾文件。

## 构建与验证（实测经验，2026-08）

- **C# 编译/构建用 Godot mono**：`Godot_v4.7-stable_mono_win64_console.exe --headless
  --path <项目根> --build-solutions --quit`。不要直接 `dotnet build`
  （Godot.NET.Sdk 依赖 Godot 环境解析，命令行下常因 SDK resolver/证书问题失败）。
- **构建成功的判定**：检查 `.godot/mono/temp/bin/Debug/gemuera-c#.dll` 时间戳已更新，
  不要等进程退出——无头/受限环境下 Godot 可能卡在收尾阶段，但编译早已完成。
- Android 相关结论必须以 APK 实测为准；桌面端仅用于调试。

## 架构速览

核心运行链：

```text
project.godot -> first_window.tscn -> FirstWindow._Ready()
  -> main.tscn -> EmueraMain._Ready() -> EmueraThread.Start()
  -> Program.Main() -> Process.Initialize()
  -> Process.DoScript() / runScriptProc()
  -> EmueraConsole / GenericUtils / EmueraContent
```

核心层：

- `Scripts/` — Godot UI、主入口、线程桥、精灵缓存、输入面板、缩放、诊断入口。
- `Scripts/Emuera/GameView/` — 控制台显示模型（文本、按钮、HTML、图片、形状、输入等待）。
- `Scripts/Emuera/GameProc/` — ERB 加载、逻辑行解析、label 索引、脚本执行状态机、lazy loading。
- `Scripts/Emuera/GameData/` — 变量、表达式、常量、函数方法、角色数据。
- `Scripts/Emuera/Content/` — 图片、精灵、Graphics surface、ColorMatrix 绘制。
- `Scripts/LegacyRunner/` — 旧版显示/输入回放诊断（原 `Scripts/M0/`，命名空间 `gEmuera.LegacyRunner`）。
- `Scripts/GodotHost/` — Godot 生命周期/平台桥组件（AppBootstrap、PlatformGateway、Emuera*Component）。
- `Scripts/uEmuera/` — `System.Drawing` / `System.Windows.Forms` 兼容层。
- `Scripts/Diagnostics/` — 运行期诊断、日志路由、导出、输入回放、诊断面板。
- `src/Core/` — 纯 C# 核心契约（`GEmuera.Core`，独立编译，不含 Godot 依赖）。
- `assets/` — 全部 Godot 运行时资源（字体/图标/语言/场景/文本模板/主题）。

## 文件管理规范（强制）

以下规范是项目习惯，AGENT 新建/移动文件时必须遵守：

1. **禁止 `Resources`/`Text`/`Fonts` 这类根目录散落命名**：所有 Godot 运行时资源统一放在
   `assets/` 下，按用途分 `fonts/`、`icons/`、`lang/`、`scenes/`、`text/`、`theme/`。
   不要在 `assets/` 之外另建资源目录，也不要出现 `resources/`、`Resources/` 等拼写变体。
2. **字体统一管理、复用**：字体文件只保留一份在 `assets/fonts/`，代码用 `res://assets/fonts/...`
   引用。禁止复制同一字体到多个目录。
3. **禁止"阶段代号"命名**：不创建 `M0`、`M1`、`M3-M7` 之类无法让 AGENT 初见即识别的目录/命名空间/类名。
   用描述用途的名字（如 `LegacyRunner`、`governance`）。
4. **Godot `.import` 侧车文件必须入库**：`*.import` 是导入设置（importer/uid/params），
   官方要求提交 VCS；`.godot/`（含 `imported/` 二进制缓存）才是应忽略的可再生目录。
5. **编译产物不入库**：`.gitignore` 已排除 `.godot/`、`bin/`、`obj/`、`Build/NativeLibs/`、
   `Build/android/`、`*.apk/aab/exe/pck/idsig`、`reports/`、`artifacts/`。不要把新的编译/导出物加进 git。
   构建/打包相关文件夹（android 导出工程、NativeLibs、Fixtures、APK 产物）统一放在根目录 `Build/` 下管理。
6. **C# 文件规模与细分**：新建文件硬上限 1500 行；既有 >2000 行文件"只减不增"，触碰时按功能域
   顺手拆分（`Type.Feature.cs` partial 模式，纯移动不混逻辑改动）。拆分前必须核对仓库六类路径
   钉扎/链接机制（方言证据链、契约测试、tools 工程源链接、场景脚本引用等），详见
   [FILE_STANDARD.md](FILE_STANDARD.md) §6。
7. **游戏内容与运行产物不得进入项目根（res://）**：Godot 编辑器会递归扫描导入 res:// 下全部资源，
   一个 era 游戏意味着上千 CSV/上万文件，会令编辑器卡死并在游戏目录生成大量 `*.translation` 垃圾
   （2026-09-07 实证：经 junction 链入后编辑器导入 1457 个 translation 文件）。因此：游戏本体只放
   启动器扫描根（桌面编辑器运行=Godot exe 同级 `compat\<profile>\<游戏>`；Android=
   `/storage/emulated/0/emuera/`）；`legacy-runner` 的 `-OutputDirectory` 必须指向**项目外**目录
   （如 `D:\gemuera-reports\`）；禁止用 junction/symlink/复制把游戏放进 `D:\gemuera` 内。

定位文件：优先用 CodeGraph（`codegraph explore "符号名"`）或 `src/Core`/`Scripts` 目录结构判断；
不要靠猜测。

## 协作规则

### GitHub

- 仓库：`https://github.com/wwwXiaoHan17/gEmuera`，默认协作分支 `dev`。
- 每个任务从最新 `dev` 新建分支：`ai/<任务简述>` 或 `fix/<问题简述>`。
- 不直接向 `dev` 或主分支提交代码；完成任务后提 PR 指向 `dev`，PR 标题与说明用中文。
- 每个 PR 只解决一个明确问题，禁止混入无关重构、格式化和资源变更。
- 禁止擅自强制推送、硬重置、删除远端分支、回滚他人提交。

## 常用文件入口

| 职责     | 文件                                                                        |
| ------ | ------------------------------------------------------------------------- |
| 主场景    | `first_window.tscn`、`main.tscn`                                           |
| 主入口    | `Scripts/FirstWindow.cs`、`Scripts/EmueraMain.cs`                          |
| 后台线程   | `Scripts/EmueraThread.cs`                                                 |
| UI 渲染  | `Scripts/EmueraContent.cs`                                                |
| 脚本执行   | `Scripts/Emuera/GameProc/Process*.cs`                                     |
| 控制台输出  | `Scripts/Emuera/GameView/EmueraConsole*.cs`                               |
| 表达式/变量 | `Scripts/Emuera/GameData/Expression/`、`Scripts/Emuera/GameData/Variable/` |
| 图片/精灵  | `Scripts/Emuera/Content/`                                                  |
| 诊断日志   | `Scripts/Diagnostics/`、`Scripts/GenericUtils.cs`                          |
| 运行期配置  | `config.toml`、`Scripts/Diagnostics/RuntimeDiagnosticsConfig*.cs`          |
