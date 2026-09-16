# 进化记录：quick 面板悬浮模式修复（2026-08-16）

> 任务：quick 面板开启悬浮模式后显示问题（移动端不显示、桌面按钮过大）修复 +
> DEBUG/日志面板移动端检查 + 项目优化检查 + 本次启动的 Agent 自我进化机制。

## 成果

- `Scripts/Panels/QuickFloatingWindow.cs`：原生标题栏 → **无边框透明窗口**
  （Borderless + Transparent + TransparentBg），内容 1:1（移除 ContentScaleFactor 放大）。
- `Scripts/Panels/QuickButtons.cs`：新增 `FloatingHosted` / `WindowDragEnabled` /
  `WindowDragRequested`，拖动语义 = 内容无滚动余量时移动窗口、有滚动余量时滚动。
- `Scripts/EmueraContent.cs`：内嵌挂载时复位悬浮宿主状态（移动端不受悬浮设置影响锚点）。
- 4 个语言文件悬浮开关提示文案更新。
- `governance/` 自我进化机制（本目录）。

## 关键决策与 Why（后续任务直接复用）

1. **悬浮窗按钮过大的根因**：`ContentScaleFactor = GetMainWindowScale()` 把整个窗口内容
   按主窗口 stretch 比例放大（1080p→1.5x，4K→3x）。修复 = 移除缩放，1:1 渲染，
   按钮大小即配置值（用户调节所见即所得）。
2. **Android 不挂载 Window**：嵌入 Window 在 gl_compatibility 下内容不渲染
   （与日志面板/DEBUG 面板同因）。日志/DEBUG 面板的 Android 方案是"全屏 Control 覆盖层"；
   quick 面板本身就是 CanvasLayer 覆盖层（Layer=90），移动端保持内嵌挂载即可，
   **不要**在 Android 上创建 Window。
3. **无标题栏的代价**：失去原生拖动/关闭。拖动改为"面板拖动 → 窗口位移"
   （QuickButtons 发 `WindowDragRequested(delta)`，宿主 `Position += delta`）；
   关闭复用系统菜单 quick 按钮（不加 ✕，保持透明样式纯净）。
4. **移动端"不显示"的防御**：`AnchoredLeft/AnchoredTop` 必须只在真正被 Window 承载时
   生效（`FloatingHosted`），否则桌面同步来的悬浮设置会让 Android 面板意外贴左贴顶。
5. **窗口尺寸同步**：Window `Size = panel.Size + 四周留白`（无标题栏高度、无缩放系数），
   `_Process` 内先比较再赋值，避免每帧无谓设置。

## AI 表现复盘

### 有效（保留）
- 动手前先并行读取相关文件（挂载链、面板、设置入口），一次摸清全貌。
- 识别出工作区未提交改动是"上次会话的悬浮功能开发"，在其基础上迭代而非推倒重来。
- 平台门控结论（Android 嵌入 Window 不渲染）直接复用日志/DEBUG 面板已验证的方案，
  没有重新发明轮子。

### 低效（改进）
- **"移动端不显示"大量静态猜测**：无法实测 APK 的环境下，花了较多时间推演根因，
  最终只能做防御性修复（FloatingHosted 锚点隔离）。教训：先明确"可静态判定 vs 需实测"
  两类结论，把实测项列入交付清单交给用户验证，不无限推演。
- **构建验证绕路**：先试 `dotnet build`（失败：Godot.NET.Sdk 需 Godot 环境解析），
  再试 Godot `--build-solutions`（沙箱证书读取被拒），最终在 full-access 下构建超时，
  但编译其实早已完成（DLL 时间戳）。教训：**项目 C# 构建验证 = Godot mono
  `--build-solutions`，验证成功 = 检查 DLL 时间戳，不要等进程退出**。
  沙箱内 Godot 构建若卡住，先查 `.godot/mono/temp/bin/Debug/gemuera-c#.dll` 时间戳。

## 针对性优化动作

- [x] AGENTS.md 增加"构建与验证"说明（Godot mono 构建命令 + DLL 时间戳验证法）。
- [x] 新建 `governance/` 自我进化机制（本目录 README 为总览）。
- [x] 子代理代码审查（只读报告）：已当场修复 Q6/Q7（QuickFloatingWindow 按显隐开关
      `SetProcess`，消除隐藏态每帧 native 回调）与 Q8（`DetachQuick` 显式退订，
      消除同帧迁移双订阅风险）。
- [x] **gEmuera 启动预设进化（最终落地）**：`~/.dsh/.agent-presets/gemuera/agent.cordis.yml`
      persona 新增「构建与验证」「平台约束（Windows/Android 双轨）」「自我进化闭环」
      「用户提示词模式精要」四章节；`preset.yml` 描述同步；YAML 结构验证通过。
      本文件与 prompt-patterns/ 为临时沉淀，精华已合并进预设，下次会话直接生效。
- [ ] 待用户实测 APK：移动端悬浮设置下面板显示位置（应保持右下角）与拖动滚动是否正常。
- [ ] 待用户实测桌面：无边框透明悬浮窗在 4K/高分屏下的按钮大小与拖动手感。

## 代码审查发现（子代理报告摘要，后续任务按需处理）

严重度分布：无高；中：Q1、Q9、Q11、Q14；其余低。已修 Q6/Q7/Q8，其余列入 backlog：

- **Q1** `AcceptQuickInput` 的 `eventSource` 形参是死参数（QuickButtons.cs:649）。
- **Q9** `Clear()` 与 `FinishQuickButtonPointer` 各自复位同一组拖拽状态字段 → 抽
  `ResetQuickPointerState()` 共用，防漏复位（拖拽/惯性状态泄漏是潜在 bug 源）。
- **Q11** `RuntimeDiagnosticsPanelContent` 与 `EmueraDebugDialogContent` 结构重复
  （都是"纯 Control 内容 + Window/覆盖层双宿主"）→ 可抽共享宿主感知基类。
- **Q14** `IsControlAlive`/`TryGet*Meta` 在 QuickButtons 本地定义，EmueraContent 各处
  只用 `IsInstanceValid`（漏 `IsQueuedForDeletion`，语义不统一）→ 提到共享层统一。
- **Q2** `ApplyPanelSize()` 无参重载与 `UpdatePanelSize` 各一份尺寸公式，防漂移应合并。
- **Q3** `GetMax{Vertical,Horizontal}Scroll` 在拖拽/resize/惯性热路径逐事件触发布局
  查询 → 建议 dirty 标记后重算。
- **结构**：`EmueraContent.cs`（9113 行）建议按职责拆 partial：先拆 quick 域
  （`EmueraContent.QuickHost.cs`，约 6800-7100 行），再 Audio/Layout/Overlay/Lines。
