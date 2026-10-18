# 2026-10-19 游戏界面左右留白可调（全面屏/曲面屏安全区修正）

## 任务

用户报告：部分全面屏/曲面手机会出现**左侧留出的 padding 无法正常显示**的问题；要求增加一个可调整
游戏界面左右 padding 的设置项。同一轮要求修复上一张截图的启动器布局问题——该问题已在 PR #34 修复并已合入
`dev`（merge commit `c99544b`），本分支基于该提交，包含修复。

## 实现

### 1. 游戏内容区左右留白微调（`Scripts/EmueraContent.cs`）
- 新增公开设置 `EmueraContent.GameViewLeftPadding` / `GameViewRightPadding`：
  - 持久化到 `user://settings.cfg [Display] GameViewLeftPadding/GameViewRightPadding`；
  - 范围 `[-240, 240]` px（正值增加该侧留白，负值减少，收回最多到视口边缘）；
  - setter 立即调用 `ApplySafeAreaLayout(true)` → 重新计算安全区、套用 root/cbg 矩形，并按现有
    Android 动态宽度策略更新 `Config.WindowX`（触发 console 重排/Refresh）。
- `GetSafeViewportRect()` 在系统安全区（`DisplayServer.GetDisplaySafeArea`）之上叠加该修正，并保持
  `MinDynamicContentWidth=320` 下限，避免布局宽度与根内容宽度不一致。
- **仅移动端生效**（`OS.HasFeature("mobile")` 门控）：桌面端 `Config.WindowX` 不由安全区驱动，
  若在桌面改内容区宽度不做同步会与 legacy 布局宽度错位；桌面默认行为零变化。

### 2. 设置 UI（`Scripts/Panels/OptionWindow.cs` 的 Display 组）
- 在「最大行数」之后新增「界面左留白」「界面右留白」两个滑条（步长 2px，显示 `+N px`/`-N px`），
  下方一行说明文案；桌面端滑条禁用并附 tooltip 说明"仅移动端生效"。
- 新增 3 个 i18n key（zh/en/jp/default）：`OptionWindow.GameViewLeftPadding`、
  `OptionWindow.GameViewRightPadding`、`OptionWindow.GameViewPaddingHint`。

## 验证

- `dotnet build gemuera-c#.csproj -nodeReuse:false -m:1`：0 error。
- Godot 4.7 headless 探针（实例化 `OptionWindow.tscn`）：
  - 找到 2 个范围 ±240 的留白滑条与提示 Label；
  - 程序化设 left=+40 / right=-20 后 `user://settings.cfg [Display]` 实际写入 40/-20，重置后回到 0；
  - `EmueraContent` 静态属性 getter 在同一进程内返回持久化值（setter 在无游戏实例时只持久化，不崩）。
- 三冒烟 / `CoreContractSmoke` / xUnit / `Test-Governance.ps1`：见提交时门禁输出。
- **未验证**：真机/Android APK 上安全区修正的实际视觉效果（本机无 Godot 4.7 导出模板，且桌面端该设置被门控禁用）。
  需要一台全面屏/曲面屏设备做 left/right ± 值抽测；若某些机型仍不够，可继续扩大范围或改成按 dp 表达。

## 关键决策与 Why

1. **只改安全区矩形、不改渲染/缩放模型**：现有 Android 布局链路（安全区 → rootContent/cbgContainer →
   Android 动态 `Config.WindowX` → console 布局）已经统一，只要把用户修正叠加到安全区入口，
   所有下游（菜单安全区、输入板、缩放面板、弹窗 clamp）自动跟随。
2. **桌面端不生效**：桌面窗口宽度与 legacy 布局宽度没有安全区同步机制，强行缩小内容区会造成
   console 折行宽度与可视宽度不一致；移动端才是用户报告的场景。
3. **范围 ±240 px**：覆盖常见曲面屏边缘 inset；同时用 320 最小内容宽度兜底，避免滑条把布局压坏。
4. **默认 0**：不改变任何现有设备的行为；这是纯 opt-in 的排障选项。
