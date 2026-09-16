# 进化记录：日志配置持久化修复 + Quick 悬浮窗回归原生窗口（2026-08-21）

> 任务：① DEBUG/日志系统问题修复；② Quick 面板悬浮模式样式返工（保留 Windows 原生组件）。

## 成果

### 任务 1：日志/诊断配置系统三个确定性缺陷

- **缺陷 1（保存即丢失）**：`RuntimeDiagnosticsConfigWriter.BuildToml` 只写出约 15 个精简键，
  而诊断面板暴露 200+ 专家项。面板改 `debug_model`/触摸细项/`quick_debug.preset` 后保存
  提示成功，重启全部回退默认值。
  **修复**：Writer 全量重写——与 `RuntimeDiagnosticsConfigLoader.BuildConfig` 的读取键
  一一对应，覆盖全部 30+ section。
- **缺陷 2（AI 配置被清空）**：loader 优先级 user:// > res://，user://config.toml 一旦生成
  就屏蔽 res://config.toml；旧 Writer 不写 `[agent.llm]` → 保存一次诊断设置后 LLM 配置
  永久丢失。
  **修复**：`SaveUserConfig` 保存前解析现有文件，`ExtractForeignSections` 把非诊断管理的
  section 原文块（含注释/行尾注释）原样拼回输出末尾。
- **缺陷 3（minimal 展开吞专家键）**：`ApplyMinimalLoggingSwitches` 无条件
  `DisableAllDiagnostics()` + 固定组合重建，完整格式文件里的专家细项也会被重置；
  且 `RestoreExplicitCategories` 漏恢复 touch/statement_recognition/performance 三类。
  **修复**：`HasExpertDiagnosticsSections` 判定完整格式（含 quick_debug/debug_model/
  logging.rate_limit/debug.touch/debug.image 任一 section）→ 跳过破坏性展开、按字面读取，
  仅补读 `[logging] panel_visible/runtime_panel` 等价键；旧精简格式（仓库 res://config.toml）
  保持原语义不变。分类恢复补齐三类。

### 任务 2：Quick 悬浮窗回归原生窗口

- 上一版无边框透明窗 + 自绘标题条/卡片在 Windows 上拖动/命中/Z 序全靠手写模拟，体验差。
- **返工**：`QuickFloatingWindow` 回归原生窗口组件——标准 OS 标题栏（拖动/关闭）、
  `Unresizable=true`（尺寸由面板内容驱动）、`Transient=true`（总在主窗口之上）、
  标题栏 ✕ 走 Godot `CloseRequested` → `HidePad()`（显隐状态与系统菜单一致）。
- 删除自绘 chrome（chromeRoot/header/card）与 `_Input` 手写拖动；QuickButtons 的
  `FloatingTopChrome` 一并移除（无自绘标题条即无需顶部让位）。
- 4 个语言文件的悬浮开关提示文案同步更新。

## 关键决策与 Why（后续任务直接复用）

1. **"完整格式 vs 精简格式"双轨判定**：以文件是否含专家 section 为准，而不是加版本号。
   好处：旧文件无需迁移，新文件天然字面读取；判定函数集中在 loader 一处。
2. **外来 section 保留用"原文块拼接"而非"解析后重组"**：注释、空行、行尾注释原样保留，
   不依赖 TOML 解析器的表达能力（本仓库 parser 是最小实现，不支持数组/多行字符串）。
3. **Godot 4.7 C# Window 属性名陷阱**：可缩放属性是 `Unresizable`（不是 Resizable/
   ResizeEnabled）；"跟随焦点"是 `TransientToFocused`。查证方法：反汇编字符串或
   `GodotSharp.xml` 文档 grep `P:Godot.Window.*`。
4. **smoke 工程模式复用**：`tools/core-contracts/CoreContractSmoke.csproj` 的
   "链接宿主源文件 + 依赖无关验证"模式可直接复制到其它纯逻辑域
   （本次 `tools/diagnostics-config-smoke/` 用最小 Godot stub 编译诊断配置四件套）。
5. **主工程默认 Compile 通配会吞 tools/ 下新增目录**：新建 smoke 工程必须在
   `gemuera-c#.csproj` 加 `<Compile Remove="tools\<dir>\**" />` 排除，否则 stub 类型
   泄漏进宿主编译引发大面积 CS0117/CS0436。

## 验证记录

- 主工程 `dotnet build gemuera-c#.csproj`：0 警告 0 错误。
- Godot mono `--build-solutions`：构建回调 DONE（首次失败是 stub 泄漏，排除后通过）。
- 新增 `tools/diagnostics-config-smoke/`：往返一致性 + 外来 section 保留 + 精简格式
  兼容三类断言全部通过。
- 既有 xUnit 套件：20/20 通过。

## 待用户实测

- [ ] 桌面：Quick 悬浮窗原生标题栏拖动/关闭手感；窗口是否保持主窗口之上。
- [ ] 桌面：诊断面板改专家项 → 保存 → 重启后设置保留；保存后 `[agent.llm]` 未丢。
- [ ] APK：日志系统行为不受影响（Android 无悬浮窗，quick 面板仍为画布内嵌）。
