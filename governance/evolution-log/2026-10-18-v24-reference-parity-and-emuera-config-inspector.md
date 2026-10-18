# 2026-10-18 v24 参考语义核对（含 CHKDATA 回归修复）+ emuera.config 可视化调试面板

## 任务

用户提供 Godot 4.7 mono（`C:\Users\30873\Desktop\Godot\Mono\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64.exe`），要求：
1. 对照 `E:\MyCode\eraCode\emuera.em-master`（v24，`1.824+v24+EMv18+EEv56`）检查 gEmuera 的 v24 语义是否出现偏差，有则修复；
2. 确认 Emuera 会读取的 `emuera.config` 在 gEmuera 是否也被读取；若没有考虑补上；
3. 决定是否增加一个可视化的、让用户能自主调试 `emuera.config` 配置的面板，并一并实现。

## A. v24 参考核对（方法与结论）

### 方法
- 构建两参考 DLL：v24 `emuera.em-master`（`-c Release-NAudio`）、snake v16 `main-skiasharp`（`-c Release`），
  放入 `artifacts/upstream-{v24,snake}-build/...`；运行 `LegacyDialectReflectionDiff`（net10.0-windows）生成
  `docs/NewFrameworkDesign/generated/legacy-dialect-reflection-diff.json`。
- 定向静态核对：配置默认值/配置项集合（脚本对比两棵树 `ConfigItem<...>`）、PR#32 共享路径改动逐点对照参考源码、
  09-22 backlog（`2026-09-22-v24-core-upstream-alignment.md`）中 P1 项的现状抽查。
- 运行级验证（Godot 4.7 headless）：`--build-solutions`、GDUnit4、`legacy-runner` v24pure/snake 真实游戏首等待。

### 反射对比结果（新参考构建）
- v24：instructions `301/303`（current 侧多 2 个既有端口指令，工具不计 mismatch）、functions `266/266`，
  instruction/function 声明错配 **0**；Snake：`324/326`、`349/351`，错配 **0**。
- 与入库旧报告 diff：仅 Snake Expected `347→349`、Current `349→351`（旧基线是 Skiav12.2 DLL，本次换成 v16 DLL
  并含 PR#32 的 EXIST_IN_CSV/ERD）；**v24 侧无新增错配**。
- 局限：`FunctionBehaviorMatrix` 的 covered=0（工具只把声明错配键传入比较器），因此反射**不能**证明行为等价；
  行为层结论来自源码核对与运行期首等待，不覆盖全部 ERB 语义。

### 发现并修复的**新引入**回归
1. **CHKDATA `RESULT:1` 门控方向错误（PR#32 `53a1a2c` 引入）**：
   - 复核参考：v24 `emuera.em-master/Runtime/Script/Statements/Function/Creator.Method.cs` 的 `CheckdataMethod`
     与 snake v16 同名方法**都无条件**写 `RESULT_ARRAY[1] = result.Version`。
   - PR#32 依据错误的"v24 参考不写"结论把它改成 `Snake.ReturnsChkdataVersion` 门控 → v24pure 的 CHKDATA
     不再返回存档版本（ERB 可见回归）。
   - 处置：在 PR#32 分支撤销门控、恢复无条件写，删除 `save.chkdata-version.v1` capability 与 policy 属性
     （账本 20→19），同步修正 snake 模块注释/SurfaceSmoke 注释/方言策略文档/本批 evolution-log 勘误。
     修复提交 `7bf424c` 已推送 PR#32。

### 仍存在的 v24 偏差（本批未强改，按证据登记）
- **配置默认值**（已修，见 §B）：SearchSubdirectory、SystemSaveInBinary、TextDrawingMode、UseScopedVariableInstruction。
- **配置项集合差异**：gEmuera 缺参考的 23 项（Ctrl_Z_Enabled / 剪贴板 CB* / Rikai* / EmueraLang / EmueraIcon /
  EnglishConfigOutput / ReplaceContinuationBR 等），其中多数是 gEmuera 未移植功能；`configArray` 容量 80 已满，
  加项需扩容量。现表现为读游戏自带 emuera.config 时对未知键打 `CONFIG.WARN`（运行期 diagnostics 可见），
  非致命。按"不做死契约"原则本批不塞无效配置项。
- **raw 参数预剥空白**（09-22 backlog P1-5）：非 print 指令当前预 `SkipWhiteSpace`，参考只消费 1 个分隔符；
  影响 `SETCOLORBYNAME`/`CUSTOMDRAWLINE` 等带前导空格的参数。审计给出低风险修法（扩 raw 白名单），单独 PR。
- **颜色名表**：`uEmuera.Drawing.Color.FromName` 仅 32 名 + Black 回退，参考 `System.Drawing.Color.FromName`
  141 名；141 名表已存在但仅 megaten 能力生效 → v24pure 表外颜色名静默变黑。表外色名属低频用法，单独评估。
- **EncodingHandler 缺失**（P1-6）：SJIS 无 BOM 文件仍按 UTF-8 读；Android CodePages 可用性未验证，风险大，未动。
- 其它 backlog（ICVariable 存储名大小写、_rename、SPRITE/HTML 细节等）见 09-22 文档；审计更正 P1-4 前提：
  参考实际用大小写不敏感比较器，与当前折叠语义等价，残留仅为存储名大小写，影响 ENUM* 返回值与诊断文案。

## B. emuera.config 读取结论与默认值修复

**结论：gEmuera 已经读取 `emuera.config`，不需要新增读取逻辑。** 证据：
- `Scripts/Emuera/Config/ConfigData.cs` `LoadConfig()` 依次读 `CsvDir/_default.config`（或 `default.config`）、
  `Program.ExeDir + "emuera.config"`、`CsvDir/_fixed.config`（或 `fixed.config`），最后 `Config.SetConfig(this)`；
  `Scripts/Emuera/Program.cs:163` 在每次会话初始化时调用；`Program.ExeDir` 由宿主设置为游戏根
  （`EmueraMain.cs:140`、`LegacySessionBackend.cs:167`），Android 同样适用；文件缺失时会按默认值生成。
- 运行期证据：legacy-runner 载入真实游戏时 diagnostics 出现 `CONFIG.WARN`（读到了游戏自带 config 中 gEmuera
  不认识的键，如 `EMUERAの表示言語`），说明读取链真实工作。

**默认值修复（对齐参考 v24/snake 源码）：**
| 配置项 | 修复前 | 修复后 | 参考证据 |
|---|---|---|---|
| `SearchSubdirectory` | true | false | v24 & snake `ConfigData.cs` 均 `false` |
| `SystemSaveInBinary` | true | false | 同上 |
| `TextDrawingMode` | `SKIASHARP` | `TEXTRENDERER` | v24 默认 `TEXTRENDERER`（gEmuera 实际走 Godot 渲染，该值只影响 GETCONFIG 可见结果） |
| `UseScopedVariableInstruction` | true（ConfigData 项 + JSONConfigData + Config 属性三处） | false | 参考在 `setting.json`（JSONConfigData）缺省 false |

风险控制：样例游戏（eratw/erablue/eratohoYMAEM/era-game/Era Demo）的 `emuera.config`/`setting.json` 都显式写了这些键，
读取时显式值覆盖默认值；legacy-runner 前后同一游戏 `semanticSha256` 不变（`4859125d…`）佐证无回归。
JSON 配置缺省变化对已有 `setting.json` 无影响（样本均显式 false）。

## C. emuera.config 可视化调试面板（新功能）

决定：**要加，但只做"查看 + 手动改 + 显式保存 + 重载"的调试面板，不改变配置读取语义**。理由是
emuera.config 的键/值类型（bool/int/颜色/枚举）不适合纯文本手改，且用户排障时需要一个可见的当前值来源。

实现：
- `Scripts/Emuera/Config/ConfigData.cs` 改 partial + 新增 `ConfigData.Inspector.cs`：最小 internal API
  （顺序快照、bool 判定/读取、`TryParse` 写入、值文本、Save、Reload），不暴露数组本身、尊重 `Fixed`。
- `Scripts/Panels/OptionWindow.EmueraConfig.cs`：游戏内设置 System 组之后新增「高级：emuera.config」折叠区
  （默认收起、首次展开才构建，避免 70+ 行拖慢普通用户）；显示配置路径与存在性、重新加载/打开目录按钮、
  布尔项 CheckButton（fixed 禁用）、非布尔项 LineEdit（回车/失焦提交，非法值红字提示且不落盘）。
- 4 语言 `OptionWindow.EmueraConfig*` 文案；`OptionWindow.cs` 仅加两处调用。
- 明确不承诺"立即全部生效"（窗口/字体/FPS/已加载 ERB 等需重启），文案与 XML 注释均写明。

## 验证记录

- 构建：`dotnet build gemuera-c#.csproj -nodeReuse:false -m:1` → 0 error / 既有 4 warning。
- 门禁：SurfaceSmoke、RuntimeSmoke（v24 266 / snake 351）、CoreContractSmoke、`Test-Governance.ps1` 4/4 全绿；
  xUnit Core 131/131、Facade 35/35、PluginAbi 3/3。
- Godot 4.7 headless（`--build-solutions --quit`）：构建成功（MSBuild 0 error；干净态两次构建 DLL SHA256 相同），
  本次修复的 Godot 侧构建 exit 0；`GDUnit4` 1 套件 4 用例全 PASSED、0 orphans。
- `legacy-runner`（真实游戏、隔离副本）：v24pure × eratohoYMAEM 1 次 / 3 次均 `PassedRunnerConsistency`，
  `first_wait_reached`、semanticSha `4859125d…` 三次一致；snake × eratw 1 次首等待通过。
- 未验证：Android APK（本机无 4.7 导出模板）、配置面板真机交互/输入法、面板 Reload 与解释器线程的竞态、
  V-Sync 显示行为、反射行为矩阵（0 覆盖）、六游戏矩阵。

## 关键决策与 Why
1. **CHKDATA 回归优先修**：v24pure 是"未选择侧不变"的基线，任何 v24 可见漂移优先级高于方言新功能。
2. **默认值按参考改，但不强改高风险 backlog**：默认值有明确参考证据且样例游戏显式覆盖；EncodingHandler /
   raw 参数 / 颜色表涉及全量文件或渲染语义，需独立 PR + 矩阵验证。
3. **面板不自动保存、不自动重载**：调试功能不应在用户未确认时改游戏目录文件；重载只刷新静态 Config，
   明确提示"部分项需重启"。
4. **反射报告更新入库**：新参考 DLL 构建后 Snake 计数变化是预期结果（v16 + EXIST 函数），v24 错配 0 作为回归证据。

## AI 表现复盘
- 有效：反射对比 + 参考源码逐行复核抓住 CHKDATA 误判（上一批 snake 同步报告的结论有误，本批以源码为准推翻）；
  运行级验证首次在 Godot 4.7 上打通 headless build/GDUnit4/legacy-runner。
- 低效：早期把"审计队友的结论"当作参考事实直接落代码（CHKDATA 门控），教训：**跨方言 feature attribution 必须
  回到两棵参考树逐行核对，不能只信上批审计摘要**。
- 环境事实：Godot 4.7 mono 可用（路径见任务节）；无 4.7 导出模板，APK 仍不能在本机验证。

## 关联
- PR#32（snake v16 同步 + V-Sync）：`7bf424c` 修复 CHKDATA 回归、`f92c306` 补 .uid 侧车。
- 本分支 PR：v24 默认值对齐 + emuera.config 面板（stacked 于 PR#32 之上）。
