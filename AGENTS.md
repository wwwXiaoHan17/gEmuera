# AGENTS.md

本文是 AI Agent（Claude Code、AI CLI、AI IDE）进入项目的首读文档。读完本文后，按需查阅「必读与配套文档」中的详细文档。

## 项目简介

本项目基于 **Godot 4.7 mono** + C# 开发，旨在使用开源技术 + Vibe Coding 开发新的 Emuera 模拟器。

Emuera 核心编译器以 C# 编写，为减少开发成本、方便 AI 对接核心、降低 Token 消耗，开发初期即放弃 Godot 推崇的 GDScript，**以 C# 为主语言**。

- 首要运行与测试目标：Android/手机端，方案以导出 APK 可用为准。
- 桌面端可用于调试，但不能替代 APK 验证结论。
- 参考实现：`E:\MyCode\Era\emuera_lazyloading_selfmodified_version-develop-skiasharp (2)\emuera_lazyloading_selfmodified_version-develop-skiasharp`（snake fork，Skiav12.1，2026-09-04 盘点基线，zip 解压无 git 历史）、`E:\MyCode\Era\emuera.em-master`（v24）、`E:\MyCode\GodotCode\gemuera\uEmuera-0.2.9d`、`E:\MyCode\GodotCode\gemuera\XEmuera-0.5.1等`。
- 涉及 Godot 架构、UI、性能、平台适配时，使用 `godot-master` skill 辅助判断。
- Snake 接口（`emuera_lazyloading_*`）语义对齐：修改 `Scripts/Emuera/` 解释器前对照
  `E:\MyCode\Era\emuera_lazyloading_selfmodified_version-develop-skiasharp (2)\emuera_lazyloading_selfmodified_version-develop-skiasharp`（CHANGELOG.md 即版本变更契约），保证 ERB 语义不偏离。

## 必读与配套文档

| 文档                                              | 状态     | 说明                                                                     |
| ----------------------------------------------- | ------ | ---------------------------------------------------------------------- |
| `docs/xEmueraCodeWiki`                          | 外部参考   | XEmuera-R：一款专门为Emuera1824+v24+EE+EM适配的模拟。这是使用gpt5.6-sol读取其代码并编写的架构指导文档 |
| `governance/`                                   | 任务收尾必做 | Agent 自我进化机制：任务复盘记录（evolution-log/）+ 用户提示词模式库（prompt-patterns/）。强制流程、验收标准与提交前自检见下文「Agent 自我进化（governance）」 |
| `addons/gdUnit4/ADDON.md`                       | 按需     | GDUnit4 插件使用指南（WHY/WHEN/WHERE/HOW），用 GDUnit 做 TDD 时阅读                  |
| `docs/ERBAPI.md`                                | ERB解释器接口 | 需要为新的Era游戏做适配，且当前的Erb语法解析无法实现时，又或者需要更新Erb语法解释器时，指导Agent对接              |
| `docs/logging-convention.md`                    | 按需     | 日志内容层规范：文案句式、EventId 命名空间、语言政策、文件落点清单、旁路禁令。新增诊断日志或排查日志问题前阅读                |
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

## 构建与验证（实测经验，2026-08；2026-09-13、2026-11-03 补充）

- **C# 编译/构建首选 Godot mono**：`Godot_v4.7-stable_mono_win64_console.exe --headless
  --path <项目根> --build-solutions --quit`。
- **构建成功的判定**：检查 `.godot/mono/temp/bin/Debug/gemuera-c#.dll` 时间戳已更新，
  不要等进程退出——无头/受限环境下 Godot 可能卡在收尾阶段，但编译早已完成。
- **方言/兼容层改动的门禁集**（2026-09）：改动 `Scripts/Emuera/Compatibility/`、指令/函数
  注册表或方言名单后，依次跑三个契约冒烟（任一红了不得提 PR）：
  `dotnet run --project tools/dialect-inventory/LegacyDialectSurfaceSmoke`、
  `dotnet run --project tools/dialect-inventory/LegacyDialectRuntimeSmoke`、
  `dotnet run --project tools/core-contracts/CoreContractSmoke.csproj`；
  引擎注册表/方言清单变化后先 `dotnet run --project tools/dialect-inventory/LegacyDialectInventoryGenerator -- <项目根>`
  再生清单（`LegacyDialectInventories.Generated.cs` + `tools/legacy-runner/profiles.generated.json`），
  最后用 legacy-runner 做 v24pure/snake/erafl 三 profile 无头执行级冒烟。
- **CompatPack 生命周期 P0 静态守卫**（2026-10）：改动 `Scripts/EmueraMain.cs`（StopLegacySession）、
  `Scripts/GodotHost/LegacySessionBackend.cs`、`Scripts/Emuera/Compatibility/CompatPackHost.cs`
  或 `src/Core/Compatibility/DialectRuntime.cs` 的会话生命周期/投影相关代码后，跑
  `powershell -NoProfile -ExecutionPolicy Bypass -File tools/compat-pack/Test-CompatPackLifecycle.ps1 -ProjectRoot <项目根>`
  （已接入 `tools/governance/Test-Governance.ps1`；四项检查：StopLegacySession 无
  `IsRunning` 条件早退、StopLegacyBaselineAsync 必须调用 `ClearCompatibilityPlan`、
  CompatPackHost 无投影静态残留、DialectPlan 保留 `PackModuleIds`/`VariantSelections`）。
  该守卫只防静态文本回归，**真机 e2e 仍是最终验收**。
- **CompatPack 发布门禁**（2026-11）：桌面档汇总 Core/Facade/ABI xUnit、三冒烟与 lifecycle
  守卫，入口
  `powershell -NoProfile -ExecutionPolicy Bypass -File tools/compat-pack/Test-CompatPackReleaseGate.ps1 -ProjectRoot <项目根>`
  （已接入 `tools/governance/Test-Governance.ps1`）。P-D 与社区包发布必须额外带
  `-RequireAndroid -RequireSixGame` 并提供 `reports/compatpack/android-evidence.json` /
  `reports/compatpack/six-game-evidence.json`（字段、采集步骤与归档见
  [docs/compat-pack-release-gates.md](docs/compat-pack-release-gates.md)）；证据缺失即 exit 1，
  桌面档通过不能替代真机证据。**真机 e2e 仍是最终验收**。
- **Godot 构建回调不可用时的逃生通道**（个别安装缺 MSBuild 程序集时）：
  `dotnet build <项目根>/gemuera-c#.csproj -nodeReuse:false -m:1`，判定口径同为
  `error CS/MSB` 计 0 且输出 DLL 时间戳更新。两个通用陷阱：必须带 `-nodeReuse:false -m:1`
  （陈旧工作节点会静默失败：报"生成失败"却 0 错误、不产 DLL）；不要用 `--no-incremental`
  （先清空输出，后续步骤失败连 DLL 一起丢）。遇 `MSB3491 Access denied` 多为其他 Agent
  沙箱身份在 `obj|bin` 的历史残留，把目录改名移出到 csproj 已排除的目录即可。
- **csproj 已排除 `Build\**`（已退役）、`export\**` 与 `reports\**` 的编译收录**：产物/工作区
  目录里若混入杂散 `.cs` 会以 CS0579 重复特性炸构建且报错文件毫不相干（csproj 已加
  `<Compile Remove>` 兜底，新建其它临时目录时仍需确认是否被默认 glob 收录）。
- **PowerShell 函数/别名命名避开内置别名**：别名解析优先于函数，`function Rd {...}` 会静默
  变成 `Remove-Item`。危险名至少包括 `rd`/`del`/`rm`/`mv`/`cp`/`ls`/`cd`/`cat`/`sc`/`gc`；
  脚本内联 `[IO.File]::ReadAllText(...)`，不要包成短名函数。
- **GDScript / 独立 addon 验证**：纯 GDScript 逻辑（如 `addons/markdownlabel/`）用自建契约测试跑
  `Godot --headless --path <项目根> --script res://tools/<suite>/run_tests.gd`（SceneTree 脚本，有失败 `quit(1)`；
  参考 `tools/markdownlabel-tests/`，2026-11-03 起）；RichTextLabel/UI 布局类改动必须再用**窗口模式**脚本截图核对
  （`--headless` 无渲染；用 `root.get_texture().get_image().save_png(...)` 生成 PNG 后人工查看），不要只信无头输出。
- Android 相关结论必须以 APK 实测为准；桌面端仅用于调试。

## 架构速览

核心运行链：

```text
project.godot -> assets/scenes/first_window.tscn -> FirstWindow._Ready()
  -> assets/scenes/main.tscn -> EmueraMain._Ready() -> EmueraThread.Start()
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
5. **编译产物不入库**：`.gitignore` 已排除 `.godot/`、`bin/`、`obj/`、`/export/`、
   `*.apk/aab/exe/pck/idsig`、`reports/`、`artifacts/`。APK 导出所需文件统一放根目录 `export/`
   工作区（`android/` gradle 导出工程、`NativeLibs/` csproj 自愈还原的原生库、`releases/` APK
   产物、`keystore/` 签名密钥——均不入库；需要版本化的导出配置放 `tools/`，测试固件声明位于
   `tools/fixture-manifest/manifest.json`）。`Build/` 已于 2026-09-16 退役，禁止重建。
   不要把新的编译/导出物加进 git。
6. **C# 文件规模与细分**：新建文件硬上限 1500 行；既有 >2000 行文件"只减不增"，触碰时按功能域
   顺手拆分（`Type.Feature.cs` partial 模式，纯移动不混逻辑改动）。拆分前必须核对仓库六类路径
   钉扎/链接机制（方言证据链、契约测试、tools 工程源链接、场景脚本引用等），详见
   [FILE_STANDARD.md](docs/FILE_STANDARD.md) §6。
7. **游戏内容与运行产物不得进入项目根（res://）**：Godot 编辑器会递归扫描导入 res:// 下全部资源，
   一个 era 游戏意味着上千 CSV/上万文件，会令编辑器卡死并在游戏目录生成大量 `*.translation` 垃圾
   （2026-09-07 实证：经 junction 链入后编辑器导入 1457 个 translation 文件）。因此：游戏本体只放
   启动器扫描根（桌面编辑器运行=Godot exe 同级 `compat\<profile>\<游戏>`；Android=
   `/storage/emulated/0/emuera/`）；`legacy-runner` 的 `-OutputDirectory` 必须指向**项目外**目录
   （如 `D:\gemuera-reports\`）；禁止用 junction/symlink/复制把游戏放进 `D:\gemuera` 内。

定位文件：优先用 CodeGraph（`codegraph explore "符号名"`）或 `src/Core`/`Scripts` 目录结构判断；
不要靠猜测。

> **CodeGraph 不可用时的降级检索**（是否安装因机器而异，先试再定）：改用
> `grep`（按正则找内容）→ `glob`（按路径找文件）→ `read`（按行号读区间），
> 配合 `Scripts/`、`src/Core/` 的目录结构判断。另见 `docs/FILE_STANDARD.md` §6.7：
> `.codegraph/` 是可再生缓存，即便装了也需在大型拆分后重建，否则旧索引会系统性误导检索。

## Agent 自我进化（governance）

`governance/` 是本项目的自我进化基础设施，**不是可选动作**：会话之间不共享上下文，本次任务踩的坑不沉淀，下次会话（或下一个 Agent）会原样重踩。机制总览与记录模板见 `governance/README.md`；本节是强制流程与验收标准，与仓库内其他文档、历史记录或各工具启动预设的旧表述冲突时一律以本节为准（旧表述逐步收敛为指向本节的指针，历史日志只追加修正说明、不回改）。本节自 2026-11-03 起对**新增/追加记录**生效，历史日志不追溯、不重写。

### 何时必须写

- **触发判据（满足任一即写）**：本次任务改过工作树文件（新增/修改/删除，含最终被回退的）；或产出了后续任务会复用的结论（代码审查、方案评估、对照分析报告也算，即使没有文件改动）。
- **落盘截止点**：提交 PR 前；无 PR 的（用户中断/取消、由 Lead 或父 Agent 合并、子 Agent 交付）在最终交付消息前。中途被取消但已有文件改动的也要写，并在「遗留登记」记中断原因。
- **文件与命名**：`governance/evolution-log/YYYY-MM-DD-<英文 kebab-case 任务简述>.md`（日期=完成日；同一任务/同一 PR 的返工复审→原文件追加「复审/补遗（YYYY-MM-DD）」段；跨会话的新一轮独立任务→新文件并在 H1 下链接前一份；不同任务同日才用 `-2`/`-3` 区分）。
- **返工/复审必补写**：result-review（独立复审代理打分并产出 P0/P1/P2 清单）的得分、P0/P1 修复、被推翻的中间结论、用户指出的违规。复审无返工→在已有日志补一行"复评 YYYY-MM-DD：<结论>"；这类记录价值最高，不补写视为任务未收尾。
- **升级动作（与记录同属本任务交付物，不算"混入无关变更"）**：发现新用户提示词模式→更新 `governance/prompt-patterns/README.md`（只增不改）；命中规范级/流程级教训→同一 PR 更新 `AGENTS.md` 或规范文档的**具体小节**，只写在日志里等于没写。
- **豁免（仅三类）**：纯只读问答、单次检索、不产出结论的临时咨询。边界拿不准时写；不写必须能说明属于哪一类。
- **多 Agent 协作**：同一用户任务由多个 Agent 完成时由主控（Lead）汇总写一份；只读评审/检索子 Agent 不单独写。

### 每条记录必须包含（按内容判定，小节标题可沿用既有写法；缺项视为不合格）

1. **任务目标与成果**：一句话可验收（做了什么 + 结果如何判定），不写成工作流水账；修复/回归类任务先写**现象与根因**。
2. **关键决策与 Why**：写清"为什么不选另一条路"，含被否决路线、被推翻的中间结论，以及"查过但确认非缺陷"的**排除项（附依据）**。
3. **验证记录**：按本文「构建与验证」口径给可复查证据（构建判定、门禁/冒烟、真机/无头日志摘录、`文件:行`、语义哈希——同一输入两次运行的输出哈希一致）；每条结论行内标 **[实测] / [静态] / [未验证]**，无证据只能标 [未验证]；遇构建/工具/权限/环境障碍时单列"环境障碍与打通"（替代命令 + 判定口径）。纯文档/配置任务：验证 = 改动文件清单 + 被引用的文件/章节存在且可解析 + 与相关文档无矛盾，并声明"无构建/运行验证"。
4. **AI 表现复盘**：有效做法 ≥1 条、低效/返工 ≥1 条；每条含三件事：**触发点**（`文件:行`/命令/用户原话）→ **可核对后果**（返工次数、失败命令、被推翻结论数等具体事实）→ **可复用动作**。零返工时不编造，改写"险些踩的坑 X + 为何没踩"；经过 result-review 的记录"首轮得分 → 必修项 → 复验结论"。
5. **教训 → 具体优化动作**：每条只能是两种形态之一：(a) 本 PR 已执行——给出 `文件:行` 或命令/测试名；(b) 本轮不做——进「遗留登记」并给编号+优先级+触发条件。只写"更新 XX 文档/加个检查"、没到节名或命令名的按空话论处。
6. **遗留登记**：按 P0（阻塞）/P1（重要）/P2（建议）列出，每条写"为什么不在本轮做"（超范围/依赖未就绪/待用户决策/成本>收益）；确实没有则写"无（本轮范围 X 已全部落地）"，不允许只写"无"。

### 写法红线

- **只记本任务**：开工先用 `git status --short` 记基线；成果只列本任务改动的文件；其他会话/其他 Agent 的未提交改动不得写成本任务成果或验证证据（基线已有改动要标注"基线已含既存改动：<路径>"）。
- **完成态与计划分离**：成果条目必须能在本 PR 的 `git diff --name-only` 找到对应改动，否则只能写"计划/未落地"；只引用本任务亲自执行的命令输出，引用历史输出须给来源与时间；不复制模板占位行。
- **记录只增不改**：只约束已提交/已合并的历史段落——不删除、不覆写；旧结论被推翻时保留原段并紧接 `> 补遗（YYYY-MM-DD）：……（推翻上文 X）`（"附记"等同义写法也可，须带日期）；未合并草稿可直接改稿，未落盘的结论按证据直接改。
- **日志与 PR 同源**：日志必须在本 PR 的文件清单内；PR 后又有提交→追加"PR 后变更"段；无 PR 的任务在最终交付消息给出日志路径与"进化记录"行。
- **写给零上下文 Agent**：缩写、黑话、代号首次出现给全称或仓库路径，禁止"如前所述""上面那个文件"等会话内指代。

### 沉淀阶梯（教训往哪升级，别只停在日志里）

evolution-log（过程资产）→ **prompt-patterns**（用户提示词模式）→ **AGENTS.md / 规范文档**（首读铁律）→ **门禁 / 工具 / 测试**（自动化，不依赖记忆）。升级判据（命中取最高一层，先 `grep governance/evolution-log/` 查是否已有同类教训）：用户提示词结构→prompt-patterns；同类教训在旧记录已出现 ≥2 次、或单次代价为 P0/违规返工→AGENTS.md 或规范文档；能用现有测试/脚本判定→门禁；自动化成本高于复发成本→日志标"暂不自动化（理由）"；跨项目通用→各工具全局预设（由用户确认后改，Agent 只登记）。本轮不升级的教训在条目内标 `升级去向：暂不升级（<理由>）`；升级到文档必须写明"哪份文档 + 哪一节 + 一句话改法"。

### 收尾自检（提 PR 前逐项勾选；无证据的勾选视为未完成）

开工时把"写 evolution-log + 跑本节自检"加进 todo，收尾逐项勾选并附证据位置。
- [ ] 6 项要素齐全（证据：日志 `## ` 标题命中 6 项或行号）；
- [ ] 验证记录逐条带 `[实测]/[静态]/[未验证]`，证据与结论一致（证据：日志行号）；
- [ ] 若本轮经过复审/返工→原日志已追加复审/补遗段（证据：段首行号）；
- [ ] 命中新提示词模式/规范级教训→对应文档已更新（证据：`文件:行`）；
- [ ] 只记本任务改动，未把其他会话未提交改动计入（证据：`git status --short` 基线对照）；
- [ ] PR 描述（无 PR 则最终交付消息）含一行 `进化记录: governance/evolution-log/<文件名>.md`；
- [ ] 临时测试/调试文件已删除，无产物/游戏内容进入 `res://`（证据：`git status --short`）。

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
| 主场景    | `assets/scenes/first_window.tscn`、`assets/scenes/main.tscn`              |
| 主入口    | `Scripts/FirstWindow.cs`、`Scripts/EmueraMain.cs`                          |
| 后台线程   | `Scripts/EmueraThread.cs`                                                 |
| UI 渲染  | `Scripts/EmueraContent.cs`                                                |
| 脚本执行   | `Scripts/Emuera/GameProc/Process*.cs`                                     |
| 控制台输出  | `Scripts/Emuera/GameView/EmueraConsole*.cs`                               |
| 表达式/变量 | `Scripts/Emuera/GameData/Expression/`、`Scripts/Emuera/GameData/Variable/` |
| 图片/精灵  | `Scripts/Emuera/Content/`                                                  |
| 诊断日志   | `Scripts/Diagnostics/`、`Scripts/GenericUtils.cs`                          |
| 运行期配置  | `config.toml`、`Scripts/Diagnostics/RuntimeDiagnosticsConfig*.cs`          |
