# 2026-11-20 原生化（Rust/C++）候选点全景扫描

## 任务目标与成果

分析 gEmuera 全项目（Scripts/ ≈14.5 万行 C# + src/Core），找出"适合用 Rust 或 C++ 原生实现"的代码位置并按收益/风险排序。成果：一份含 file:line 证据的候选清单（P0 存档 codec、启动文件枚举/stat、GraphicsImage 像素算子、文本测量+折行、宽度工具；P1 HTML 解析、运行期重解析、整文件解码；含"不建议原生化"清单与集成路径建议），交付于本次会话最终消息。纯分析任务，除本日志外无工作树文件改动，无 PR。

基线说明（开工时 `git status --short`）：分支 `ai/ui-scale-setting`，**基线已含既存改动**：`export_presets.cfg`、`scripts/GenericUtils.DiagnosticsLogging.cs`、`scripts/GodotHost/AndroidWindowOrientationController.cs`、`scripts/SpriteDebugNotifier.cs`、`scripts/SpriteDebugViewer.cs` 已修改，另有 23 个 `*.cs.uid` 未跟踪文件——均为其他会话（UI 缩放任务）产物，不计入本任务成果。

## 关键决策与 Why

- **否决"CP932/Shift-JIS 解码原生化"**：Emuera 惯例直觉认为 CP932 是加载热点，实测代码证据相反——`Config.Encode` 固定 `Encoding.UTF8`（`Scripts/Emuera/Config/Config.cs:24`），CP932 只在包身份探测两处低频出现（`Scripts/GodotHost/CompatPackHost.cs:355-374`、GameContentProbe）。真正的解码热点是整语料 UTF-8→UTF-16 全量转换。查证后排除，避免了一个错误 P0。
- **否决"GDExtension 作为集成方式"**：解释器/工具类候选全部是纯计算、不持有 Godot 对象，P/Invoke（项目已有 `libSkiaSharp.so`、`libe_sqlite3.so` 经 csproj 复制进 APK 的成熟先例，`gemuera-c#.csproj:80-116`）更简单且不动 Godot 生命周期。GDExtension 仅在将来需要原生节点时再考虑。
- **否决"用 Skia 替换 ColorMatrix/混合算子"**：仓库内已有位级实测否决记录（`Scripts/Emuera/Content/GraphicsImage.cs:527` 注释：Skia 约 1/3 像素位级不一致、System.Numerics SIMD 0.94x 无收益），任何重写必须显式控制 banker's rounding + 位级一致，这正好是 Rust/C++ SIMD 的适用面。
- **否决"整体重写解释器主循环为本轮候选"**：受 v24pure 逐字节等价铁律 + `FirstPartyPackEquivalenceTests` 约束，且解释器骨架深度耦合 `GlobalStatic`/`EmueraConsole`/方言门控；只搬表达式树单段没有意义（`Process.cs:619-679` `#FUNCTION` 会递归重入解释器）。列入遗留，作为中期方向而非本轮推荐。
- **排除项（查过、确认非热点/非候选，附依据）**：SQLite（已是原生 e_sqlite3，仅服务 `SQL_*` 内建）、音频（解码/混音已在 Godot 原生侧，C# 只剩 O(11)/帧 账本）、`GDI.cs`/`WinmmTimer`（前者 P/Invoke 全注释的死壳，后者应换 Stopwatch 而非原生化）、src/Core（契约层无 Godot 依赖、基本未被 legacy 热路径消费，且不应塞 P/Invoke 破坏独立单测设计）。

## 验证记录

- 本报告全部结论为**静态代码分析**，证据为 file:line（由 4 个并行 Explore 代理产出，来源文件见最终交付报告）。**无构建/运行验证**。
- 抽查复核 4 处核心论断，全部命中 [静态]：`Scripts/Emuera/GameView/StringMeasure.cs:108`（每次测量 `new SKPaint`）；`Scripts/Emuera/Sub/EraBinaryDataReader.cs:356/637/744/860`（读取路径 `ToArray()`+`FromArray()` 双拷贝）；`Scripts/Emuera/GameView/HtmlManager.cs:956`（每个 HTML 标签跑一次完整词法分析）；`Scripts/Emuera/GameData/Function/Creator.Method.cs:841/1016`（STRCOUNT/REPLACE 每次调用 `new Regex`）。
- 路径修正 1 处：代理曾报 EraBinaryDataReader 位于 `GameProc/`，实测在 `Scripts/Emuera/Sub/`（`wc -l` 输出与 `find` 双重确认）。
- "41.8 ms/Mpx" ColorMatrix 基线为**仓库内既有实测注释**（`GraphicsImage.cs:527`），非本次测量，引用时保留原出处。
- 环境障碍与打通：无（纯只读分析，未遇构建/权限障碍）。

## AI 表现复盘

- **有效做法**：开工先扫 `csproj` 的 NativeLibs/`DllImport` 分布，确认了"项目已有 P/Invoke 原生库先例"这一决定集成方式的关键事实，使后续建议直接落在成熟路径上（触发点：`gemuera-c#.csproj:80-116`；可核对后果：最终报告集成章节零猜测；可复用动作：原生化类分析先查"已有原生依赖"再谈方案）。
- **有效做法**：4 个并行 Explore 代理按"执行热路径/启动加载/渲染文本/契约与存档"正交切分，覆盖 ~14.5 万行无遗漏（可核对后果：四份报告合计 600+ 个 file:line 引用，交叉印证 SparseArray 已优化、GDI 死壳等两处重叠结论一致）。
- **险些踩的坑（未踩）**：直觉假设"CP932 解码是 Emuera 启动热点"（原版 Emuera 如此），若直接写入报告会误导原生化优先级；因要求代理对编码做专项查证（`Config.cs:24` 固定 UTF-8）而被证伪。可复用动作：跨 fork 移植的性能直觉必须先在本仓库找代码证据再下结论。
- **低效/返工**：代理产出中 1 处文件路径前缀错误（EraBinaryDataReader），依赖最终抽查环节用 `find` 纠正（返工 1 次）。可复用动作：对外交付前对报告的 file:line 做固定数量抽查应成为收尾固定步骤（本轮已执行）。

## 教训 → 具体优化动作

1. **(a) 本轮已执行**：原生化候选结论必须区分"纯计算高收益"（存档 codec、像素算子、测量）与"语义耦合高风险"（解释器骨架），两类给出不同的验证门槛（前者位级/round-trip 等价测试，后者 `FirstPartyPackEquivalenceTests` + 三冒烟）——见最终交付报告"集成路径与铁律约束"节。
2. **(a) 本轮已执行**：交付前抽查报告核心论断（4 处全命中 + 1 处路径修正），未让未验证引用流入结论。
3. **(b) 本轮不做**：`RuntimeDiagnosticsConfig` 的 `startup_timing` 是死配置（无消费者，`Scripts/Diagnostics/RuntimeDiagnosticsConfig.cs:157`），且 `load_times.log` 只有 7 个粗阶段——原生化启动链前必须先细化 `MarkLoad` 分段拿到真实数据。见遗留 L1。

## 遗留登记

- **L1（P1，不在本轮原因：需要改产品代码，超出纯分析范围）**：细化 `Process.cs:68-83` 的 `MarkLoad` 分段（erb-enum/lazy-index/erb-read/erb-parse/setlabels/checkscript/lazy-save）并接活 `startup_timing` 配置，为后续原生化提供量化依据。
- **L2（P1，不在本轮原因：属 bug 修复任务，非本任务范围）**：扫描中发现的真实缺陷——`Process.ScriptProc.cs:492/539` SETCOLORBYNAME/SETBGCOLORBYNAME 空色名会 NRE（`str` 局部变量为 null）而非预期 CodeEE；`Process.cs:828/834` 每条 warning 重读整个源文件取一行；`ErbLoader.cs:115` `loadedFirstFiles` 用 `List.Contains` O(n²) 判重。
- **L3（P2，不在本轮原因：依赖 L1 数据与产品决策）**：解释器骨架（调用帧/表达式求值核）的中期原生化评估，以及解析结果落盘缓存子系统（数量级改善但属新工程）。
- **L4（P2，不在本轮原因：成本>本轮收益）**：为首个 native spike 验证 Android 构建链（cargo-ndk/clang → `export/NativeLibs/` → csproj 复制 → 16KB page 对齐），待用户确认原生化立项后启动。
