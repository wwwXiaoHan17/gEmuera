# 2026-10-18 snake 对齐 Emuera-SKIA v16（ERD 合并 / EXIST_IN_* / RAND 伪变量 / CHKDATA 门控）+ 垂直同步开关

## 任务

用户指令（原文）：「请更新 snake 兼容包，同时在 setting 中加一个垂直同步开关（垂直同步可能会影响性能）」，
并附参考源码路径 `E:\MyCode\eraCode\emuera_lazyloading_selfmodified_version-main-skiasharp`
（Emuera-SKIA `1824+v24+EMv18+EEv56+Skiav16`，稳定版 v16）。

本批把 gEmuera 的 snake 兼容层从约 Skiav12.2 语义推进到上游 v16 的 ERB 可见语义增量，
并按用户要求在游戏内设置（`OptionWindow` 的 Display 组）加入垂直同步开关。

## 落地内容

### A. snake 语义同步（全部走 capability / 模块表面门控，v24pure 加载路径零额外分配、零表面增减）

1. **预设变量同名 ERD 合并**（上游 13.0，`csv.preset-erd-merge.v1`）：
   - 纯规则层 `src/Core/Compatibility/PresetErdMerge.cs`（解析/CSV 优先/空槽填充/跨槽重名/
     ITEM 第三列价格来源追踪；CSV 显式 0 价不得被 ERD 覆盖；重名行整体跳过）；
   - 引擎粘合层 `Scripts/Emuera/GameData/ConstantData.PresetErd.cs`（扫 ERB 目录 `*.erd`/`*.ERD`
     去重排序、`EraStreamReader` 读取、`ParserMediator.Warn` 落警告），接入
     `ConstantData.LoadData` 的「全部 CSV 之后、逆引字典之前」位置，来源数组在 CSV 并行加载时按槽标记；
   - 门控条件严格对齐上游：`Config.UseERD && Program.Compatibility.Snake.MergesPresetErdFromErbDir`。
2. **EXIST_IN_CSV / EXIST_IN_ERD**（上游 13.0）：`Scripts/Emuera/GameData/Function/Creator.Method.PresetErd.cs`
   实现探针（首参字符串变量名、次参整数索引；未知名/越界/负值/超 int 一律返回 0，不抛 CodeEE），
   注册进共享函数表并加入 `LegacySnakeCompatibilityModule.FunctionNames`（Declare 全 profile 隐藏、
   snake Apply 暴露 ⇒ v24pure/eraFL 表面逐字节不变）。
3. **RAND 伪变量钳制补齐**（上游 12.2 余项）：`VariableToken.cs` 的 `RandToken` 在 `UsesRandClamp`
   下钳制为 0 并只告警一次；v24pure 保持原 CodeEE 语义。复用既有 `math.rand-clamp.v1`，无新增 capability。
4. **CHKDATA 版本写回（勘误：本批曾误门控后撤销）**：最初按"v24 参考不写 RESULT:1"的审计结论，
   把 `RESULT_ARRAY[1] = result.Version` 改为 `save.chkdata-version.v1` 门控；后续对参考源码复核
   （`emuera.em-master Creator.Method.cs` CheckdataMethod、`main-skiasharp Creator.Method.cs` 同名方法）
   确认**两侧参考都无条件写**，门控反而制造了 v24pure 回归。已撤销门控、删除该 capability/policy 属性，
   恢复无条件写并留注释说明。capability 账本 18 → 19（仅 ERD 合并一项）。
5. **账本与表面**：`SnakeCompatibilityCapabilities` 18 → 19 项（+`csv.preset-erd-merge.v1`）；
   `SnakeCompatibilityModule.ModuleVersion` 1.0.0 → 1.1.0 并更新血统注释；
   生成清单 `LegacyDialectInventories.Generated.cs` / `tools/legacy-runner/profiles.generated.json`
   经 `LegacyDialectInventoryGenerator` 再生（snake 函数 349 → 351，METHOD 投影指令 668 → 670）。

### B. 垂直同步开关（游戏内设置）

- 新增 `Scripts/VSyncHelper.cs`：期望值持久化到 `user://settings.cfg [Display] VSyncEnabled`（默认开启），
  `Apply()` 调 `DisplayServer.WindowSetVsyncMode` 后**读回实际模式**，不一致时置 `LastRequestUnsupported`
  （不抛异常）；`FirstWindow._Ready()` 启动时应用一次。
- `OptionWindow` Display 组帧率行之后新增 V-Sync CheckButton（120×44、多语言 key
  `OptionWindow.VSync`/`OptionWindow.VSyncHint`，四份 lang 文件同步）；提示行仅在"请求关闭/实际被回退"时显示。
- **诚实提示**：Godot 4.7 文档明确「V-Sync modes other than Enabled are only supported in Forward+ and
  Mobile rendering methods, not Compatibility」；本工程两种渲染方法均为 `gl_compatibility`，因此关闭请求
  可能被引擎回退为 Enabled。UI 文案不承诺生效，设置值保留并在下次启动重试；真正关闭需切换渲染方法（本批不做）。

## 验证

- `dotnet build gemuera-c#.csproj -nodeReuse:false -m:1`：0 error / 4 warning（均为既有）。
- `dotnet test tests/xUnitTest/GEmuera.Core.Tests -c Release`：**131/131**（新增 `PresetErdMergeTests` 19 例）。
- `LegacyDialectSurfaceSmoke`：通过（含新增 EXIST_IN_CSV/ERD 的 v24 不可见、snake 可见、eraFL 隔离断言；
  capability↔policy 映射穷尽性由既有反射断言覆盖 19 项）。
- `LegacyDialectRuntimeSmoke`：通过（`v24Functions=266; snakeFunctions=351`）。
- `LegacyDialectInventoryGenerator`：再生零意外漂移（仅 snake delta 增 2 函数/2 投影指令）。
- `CoreContractSmoke`：通过（需 `DOTNET_ROLL_FORWARD=LatestMajor`，与基线同）。
- `Test-Governance.ps1`：4/4 PASS。
- `tests/xUnitTest/EmueraFacade.Tests` 35/35、`EmueraPluginAbi.Tests` 3/3（本批未改其路径，回归确认）。

## 未完成 / 未验证

- **行为级 e2e 未跑**：本机无 Godot 4.7（只有 4.5.1/4.6.x mono），GDUnit、legacy-runner（Godot 场景驱动）与
  APK 导出均无法执行。ERD 合并 / RAND 伪变量 / CHKDATA 门控的"运行期可见行为"目前只有纯规则单测 +
  表面/契约门禁证据，需在具备 Godot 4.7 的环境补：合成 fixture（`ITEM.CSV` + `ITEM.erd`）跑
  v24pure/snake 双 profile 的 legacy-runner，以及 Android 真机抽测。
- **垂直同步实机行为未验证**：Windows/Android 上 Compatibility 渲染器 `WindowGetVsyncMode` 的回退行为需实机确认；
  Android 的 vsync 通常由系统控制，开关可能无实际效果（UI 提示已如实说明）。
- 上游 v14–v15 调试工具链、v16 插件加载健壮性与 Skia 字体修复按策略不随本批移植（见
  `docs/designs/dialect-capability-strategy.md` §4 增补）。

## 关键决策与 Why

1. **ERD 合并严格保留 `Config.UseERD`**：上游合并条件就是 `Config.UseERD`；若 snake 游戏显式关 ERD，
   上游也不合并，gEmuera 不应"更聪明"。capability 只决定"该方言是否认识这条语义"。
2. **纯规则下沉 Core**：`Scripts` 层无法进 xUnit（Godot 依赖），把解析/优先级/价格来源规则
   做成 `src/Core/Compatibility/PresetErdMerge.cs` 才能 TDD 且供后续 CompatPack 复用；引擎层只做 IO/警告。
3. **CHKDATA 不写 0**：v24 参考是"不触碰 RESULT:1"；写 0 会在 GETTIME 等前序写入场景产生新的可观察分叉，
   所以门控只跳过赋值，不补默认值。
4. **EXIST_IN_* 不加 capability**：它们是纯新增名字（可见性），不是同名行为分叉；capability 账本给"同名不同行为"用。
   加 capability 会迫使 SurfaceSmoke 多一个无消费 policy 属性，反而偏离账本语义。
5. **V-Sync 读回校验**：Godot 文档已说明 Compatibility 只支持 Enabled，但不同平台回退实现可能不同；
   "写后读回"把事实交给 UI，而不是在 C# 里假设 4.7 的行为。

## AI 表现复盘

- 有效：先读上游 CHANGELOG + 源码定位四处语义，再用「纯规则 Core + 引擎粘合」拆分，规则用表驱动 xUnit 覆盖
  （含上游 continue 语义：跨槽重名时连价格也跳过）；门禁按 Surface → Runtime → 清单再生 → CoreContract 顺序收口。
- 低效/返工：`PresetErdMergeTests` 初版误用 PowerShell `Get-Content/Set-Content` 改文件，把 UTF-8 中日文
  写成 mojibake——教训同 2026-09-22 记录：**文本改写一律用 read/edit/write 工具，PowerShell 只做只读命令**。
- 待补：行为级 fixture 与真机验证；垂直同步的真实回退行为需在具备 Godot 4.7 的设备上确认。
