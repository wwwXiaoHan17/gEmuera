# 字体大小与内容缩放 API 分离（2026-11-15）

## 1. 任务目标与成果

把"字体大小"和"内容缩放"拆成两个独立运行时 API。现状耦合：OptionWindow 的字号滑杆是
内容缩放的代理（`requested/coreSize` 换算成 scale 后调 `EmueraContent.SetContentScale`），
字体与缩放共用唯一状态 `contentScale`，核心字号 `Config.FontSize` 运行时无 setter。

交付：
- 运行时字号 API：`EmueraContent.RequestFontSize(int)`（新 partial `scripts/EmueraContent.FontSize.cs`）
  → 解释器空闲帧应用 → `Config.SetRuntimeFontSize(int)`（新核心 mutator）
  → `GlobalStatic.Console.InvalidatePrintCWidthCache()` + `RefreshFontSize()` + `QueueScaleBoundsUpdate()`。
- 纯数学进 Core：`src/Core/Ui/FontSizeScaling.cs`（字号下限钳制 + 行高等比换算，镜像 SetConfig 规则），xUnit 覆盖。
- OptionWindow 字号滑杆改调字号 API；`SyncFontSizeFromContentScale` → `SyncFontSizeFromConfig`（滑杆显示真实核心字号）。
- Scalepad 缩放路径零改动。
- 判定 = Core xUnit 全绿 + 构建 0 error + 行为钉扎 31/32（唯一失败为基线既有漂移，见 §3）+ 桌面冒烟全过（eraFL0.48 实测）。

范围决策（用户三项确认）：仅新输出生效（旧行几何在输出时固化为像素）；会话内生效不持久化；缩放只在 Scalepad。

## 2. 关键决策与 Why

1. **字号应用延迟到解释器空闲帧**（`ProcessPendingFontSize` 检查 `!EmueraThread.instance.Running()`）：
   `Config.ClearFont()` 清 fontDic 与解释器线程并发读存在竞态（Dictionary 并发读写可抛异常），
   输入等待点是安全写入窗口。代价 = 拖动滑杆不立即生效（通常下一个输入等待即生效，实测即时）。
2. **必须同步 ConfigData 条目**：ERB `GETCONFIG("フォントサイズ")` 读的是 `ConfigData.Instance.GetItem(...)`，
   不是 static Config（`ConfigData.cs:540-568`）；只改 static 会让 ERB 看到旧值。
   `ConfigItem<int>.SetValue<U>` 有 `(ConfigItem<U>)this` 强转，提交前核查过 FontSize/LineHeight 条目均为
   `ConfigItem<int>`（`ConfigData.cs:255-256`），无 InvalidCastException 风险。
3. **printCWidth/printCWidthL 显式失效**：二者按 `Config.Font` 尺寸懒计算且无过期机制
   （`EmueraConsole.Print.cs:552,577`），字号变了 PRINTC 分栏阈值仍是旧值；`-1` 即懒重算，重置成本极低。
4. **RefreshFontSize 只当 aux UI 刷新用**：探索确认它对控制台正文无效（正文行是自绘 `ConsoleTextPart`，
   不吃 theme override，且其 `fontSize` 字段 readonly）。核心字号对渲染的生效路径 = Canvas 后端
   `DrawText` 实时读 `EffectiveLineHeight` + 新输出行在创建时取新 metrics。不试图用它做正文重排。
5. **不动 snake-alignment 钉扎段**：`Test-SnakeReferenceSurface.ps1` 把 `CreateTextPart`（valign 序列）与
   div ARGB 段钉在 `EmueraContent.cs` 路径上，本 PR 不移动不修改这两段（新代码全部进新 partial）。
6. **新代码进新 partial 而非主文件**：`EmueraContent.cs` 8378 行 >2000，FILE_STANDARD §2 只减不增
   （本 PR 主文件净增 +1 行），新功能域默认建 `EmueraContent.FontSize.cs` shard（§3 mode A）。
7. **排除项（查过确认非缺陷）**：`RefreshFontSize` 零调用方不是遗漏而是"半成品预留"——设计上只覆盖
   aux UI，本轮按此定位复用之，未扩大其职责。

## 3. 验证记录

- [实测] TDD 红→绿：先写 `FontSizeScalingTests.cs`（CS0103 红），实现 `FontSizeScaling.cs` 后 16/16 绿。
- [实测] Core 全量 xUnit：429 passed / 1 skipped / 0 failed（含 `FirstPartyPackEquivalenceTests` 4/4，
  作为共享路径未变语义的硬证据）。
- [实测] 构建：`dotnet build gemuera-c#.csproj -nodeReuse:false -m:1` → `error CS/MSB` 计 0，
  DLL 时间戳 1794735486 → 1794736074。`Godot --build-solutions` 同样编译成功（DLL 已更新）但进程卡收尾
  不退出（AGENTS.md 已记载的已知行为），按口径以 DLL 时间戳 + 逃生通道复核判定。
- [实测/环境障碍] snake 门禁：本机无 `-ReferenceRoot` 参考仓库（AGENTS.md 的 E:\ 路径不存在于本机），
  完整门禁无法运行。替代：从门禁脚本原样提取 `behaviorChecks` 数组独立复跑（提取脚本动态取数组并补齐
  脚本内变量 `$project/$currentInstructionPath/$currentLexicalAnalyzerPath`，逻辑与门禁 105-117 行一致）
  → **31/32 通过**；唯一失败 `SEQUENCEINPUT \e lexical preservation` 针对 `Scripts/Emuera/Sub/LexicalAnalyzer.cs`
  —— 该文件不在本 PR 改动清单（与 dev 逐字节一致），`case 'e':` 已重构为 `Snake.UsesEscapeESequence`
  门控分支（LexicalAnalyzer.cs:591-595），钉扎正则未跟上 → **dev 基线既有漂移**，与本任务无关（→遗留 P1）。
  本 PR 相关的两条 EmueraContent.cs 钉扎（Control font valign / div border ARGB）均通过。
- [实测] 桌面冒烟（eraFL0.48，DEBUG 构建，1920p 窗口）：
  1. 选项窗口字号滑杆显示 **16** = 游戏 emuera.config 真实核心字号（`SyncFontSizeFromConfig` 生效，
     不再是缩放代理值）；
  2. 滑杆调 32 → 立即生效（解释器处于输入等待）；
  3. 旧行（[编辑数据与服装] 等）保持 16 号原行高；新输出行「平原和森林、山岳…」以 32 号完整渲染、
     行高正常无裁切 —— "仅新输出生效"符合设计；
  4. Scalepad 1.0x → 2.0x 独立缩放（整体 transform），与字号互不影响；
  5. 复位 1:1 后干净退出（exit 0）。
- [静态] FILE_STANDARD 自检：`git diff --numstat` = EmueraContent.cs +1/-0（≤50 预算）、OptionWindow.cs +14/-14、
  Config.cs +27、EmueraConsole.Print.cs +11、lang ×4 +1；新文件 `EmueraContent.FontSize.cs`/`FontSizeScaling.cs`/
  `FontSizeScalingTests.cs` 均带 `.uid` 侧车入库。
- [未验证] Android 真机 e2e（最终验收口径）；PRINTC 分栏阈值重置的运行时效果（冒烟未覆盖 PRINTC 排版场景）；
  选项窗口 tooltip 新文案的四语言显示（仅补键未逐语言截图）。

## 4. AI 表现复盘

- 有效做法：
  1. 探索阶段双 Explore 并行 + 亲自复核三个关键细节（钉扎正则原文、printCWidth 字段、GlobalStatic.Console
     类型），规划阶段就锁死"不动钉扎段/不搬 CreateTextPart"，实施零触发门禁红线。
  2. `ConfigItem<T>.SetValue<U>` 强转风险在写代码后、提交前主动核查条目实例类型，避免上线后才炸的
     InvalidCastException。
  3. 桌面冒烟前先读 `FirstWindow.cs:1104` 确认扫描根 = Godot exe 同级 compat\，一次点对游戏。
- 低效/返工：
  1. snake 门禁首跑缺 `-ReferenceRoot` 必选参数（没先读 param 块）；补跑后又因 bash 内联 PowerShell 的
     转义把正则弄坏（`\$behaviorChecks` 被 bash 吃成 `\`），再因提取数组引用门禁脚本内变量未定义而
     0/0/空路径——**三轮才跑通**。可复用动作：内联复杂 PowerShell 一律直接写临时 .ps1 文件；
     提取脚本片段前先 grep 它引用的全部变量并补齐。
  2. CUA 每次调用 worker 重置：bootstrap 重复粘贴 5 次；应把"每次 cell 必须先 bootstrap"当作固定前奏。
  3. 关闭选项弹窗用坐标点击 关闭 按钮两次无效，Esc 一次成功——Godot 自绘 UI 的坐标点击命中率低，
     优先尝试键盘（Esc/快捷键），坐标只作兜底。
- 险些踩的坑：`git status` 中途出现 1518 行非本任务空白改动（4 空格→tab，`GenericUtils.DiagnosticsLogging.cs`/
  `SpriteDebugViewer.cs`/`SpriteDebugNotifier.cs`，并行 Agent 所为）+ 15 个构建 import 生成的无关 `.uid`。
  若 `git add -A` 会把无关重构混入 PR。逐文件 staged add 规避；基线开工时干净、中途污染已在 §3/§6 标注
  （符合"只记本任务"红线：这些改动不计入本任务成果与证据）。

## 5. 教训 → 具体优化动作

1. （本 PR 已执行）新功能域代码进 `Type.Feature.cs` partial、主文件只留接线（`EmueraContent.cs:+1`）——
   FILE_STANDARD §2/§3 的正确姿势，实施零摩擦。
2. （本 PR 已执行）运行时 mutator 的三件套清单：static 值 + 派生值重算 + 缓存失效 + ConfigData 同步——
   `SetRuntimeFontSize` 四者齐备，后续给其他 Config 字段做运行时 API 时照此清单核对。
3. （升级去向：遗留登记 P1）`Test-SnakeReferenceSurface.ps1` 的 'SEQUENCEINPUT \e lexical preservation'
   钉扎在 dev 基线上已红（钉扎正则落后于 `Snake.UsesEscapeESequence` 门控重构）——说明该门禁的
   behaviorChecks 部分最近未被完整执行。本轮不修（非本任务文件，需先裁定"更新钉扎正则"还是"恢复字面实现"）。
4. （升级去向：暂不升级——单次环境差异，AGENTS.md 已有"先试再定"条款覆盖）本机无 snake 参考仓库时
   完整门禁不可运行；如复发 ≥2 次再考虑在门禁脚本加 `-SkipReference` 降级开关。

## 6. 遗留登记

- **P1 现有行重排**：字号变化后已显示行重排需要核心 re-measure API（ConsoleStyledString/按钮串宽度）+
  按 `ToLogString()` HTML 重建 image/div/rect-shape 部件 + 修复 `DisplayLine2Html` 图片丢失缺陷
  （emits AltText 而非 LogText）。不在本轮：独立大任务，触及 v24 共享 GameView 路径，需单独规划。
- **P1 snake 门禁基线漂移**：见 §5-3。为什么不在本轮：非本任务文件，修复方向需用户裁定。
- **P2 字号持久化**：user://settings.cfg [Display] + 启动恢复钩子（需找 Config 加载完成与 UI 就绪的交汇点）。
  用户已确认本轮不做（与 contentScale 不持久化的现状对称）。
- **P2 Canvas 后端旧行字形跟随现象**：字号变更后已存在行由 Canvas 按新 `EffectiveLineHeight` 实时重绘字形
  （视觉变大）但行高保持旧值（可能轻微裁切）——桌面冒烟实拍确认。是"可接受的过渡态"还是缺陷待裁定；
  Controls 后端不受影响（旧行完全不变）。
- **P2 构建副产物 .uid**：本轮 `--build-solutions` 的 import 步骤为**既有** .cs 生成了 15 个此前缺失的
  `.uid`（packs/*、src/Core/Audio、SnakeLegacySurfaceNames 等），按 FILE_STANDARD 应入库但不属于本 PR
  范围，留待单独提交。
- **P2 语义提示文档化**：`HTML_STRLEN` 在字号变化后立即反映新值；GraphicsImage/CBG 内嵌文字为栅格快照
  不受运行时字号影响（仅新 GraphicsImage 取新默认值）。两者是运行时字号 API 的固有语义边界，应在
  用户文档中说明（本轮仅记录于此）。
