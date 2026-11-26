# 快捷按钮面板固定右下角（推翻贴顶决策）+ 翻转语义收敛为左下角换边

前序关联：本任务推翻 [2026-11-16-screen-rotation-follow.md](2026-11-16-screen-rotation-follow.md) 补遗 11 的「垂直一律贴顶」决策（该文件已追加补遗 13 指向本文）；`Quick面板翻转`（QuickFlip）选项随 2026-08-16 悬浮面板返工引入（[2026-08-16-quick-floating-panel.md](2026-08-16-quick-floating-panel.md) 分支链），本任务保留该选项但语义收敛。

## 1. 任务目标与成果

用户最初要求「修改快捷键面板的位置，无论怎么翻转始终为右下角」，中途追问「面板翻转即映像翻转？」并裁定「恢复左边能力」。最终成果：快捷按钮面板（`QuickButtons`）**垂直恒贴底**——默认右下角，屏幕旋转后经 `RefreshSafeAreaLayout` 重排仍落在当前方向右下角；「Quick面板翻转」选项**恢复保留**，语义收敛为纯位置换边：翻转后 = **左下角**（内容永不镜像渲染，宽度调节条同步移到面板内侧）。改动文件：`scripts/Panels/QuickButtons.cs`、`scripts/Panels/OptionWindow.cs`。

## 2. 关键决策与 Why

- **「翻转」歧义走了两步才收敛**：请求里「翻转」既可指设置项「Quick面板翻转」（移动面板右→左，`assets/lang/zh_cn.txt:127`），也可口语化指手机横竖屏旋转。第一轮按「始终右下角」的字面最强语义执行，移除了翻转选项并删除全部代码引用（编译验证通过）；用户随后追问「面板翻转即映像翻转？」——说明用户关心的正是那个设置项的含义——并直接裁定「恢复左边能力」。**被推翻的中间结论**：「flip 退役、位置不受任何开关影响」不成立；终态为 flip 保留、仅影响水平边。
- **翻转的最终语义**：纯位置换边（右下 ↔ 左下），垂直恒贴底。与旧实现的两点差异：①旧版翻转后面板在**左上**（贴顶时代），新版在**左下**；②旧版注释含糊其辞，新版明确「内容不镜像」（用户问过「即映像翻转？」，语义必须在注释里钉死）。`AnchoredLeft => configuredFlip || (configuredFloating && floatingHosted)` 恢复原式。
- **悬浮浮层默认右下角不回退**：`ApplyFloatingOverlayPanelSize` 的默认位置（从未拖动时）保持本次改定的右下角——翻转开关只作用于内嵌锚点布局，悬浮模式本就是自由拖拽 + 坐标持久化，二者语义正交（与 2026-08 起的既有行为一致）。
- **不 bump `QuickFloatingPositionVersion`**：未拖动过的用户坐标为 -1（自动落新默认）；拖动过的坐标是用户明确意图，清空反而丢状态。
- **保留竖屏右侧菜单栏避让**（`rightMargin += 48+16`）：功能冲突避让（12 个竖排菜单图标不可被面板 `MouseFilter=Stop` 挡住），与锚点无关；翻转（贴左）时面板在左边缘，天然不与右侧菜单栏冲突，无需对称避让。
- **保留 `maxHeight = safeSize.Y - 66`**：贴底后高面板向上伸展，66px 顶部预留继续防止盖住横屏顶部菜单栏；语义从「贴顶边距」变为「高度上限预留」，数值不动。
- **语言文件条目全程未动**：第一轮移除代码时保留的 `OptionWindow.QuickFlip`/`QuickFlipHint` 四语言条目直接被恢复的开关复用，零成本回滚得到验证。
- **排除项（查过，非缺陷）**：桌面 `QuickFloatingWindow.GetDefaultPosition` 本就是主窗口右下角；`FloatingHosted`（桌面 Window 承载）时窗口内容区内贴左贴顶与屏幕位置无关，均保持原样。

## 3. 验证记录

- [实测] 终态 `dotnet build gemuera-c#.csproj -nodeReuse:false -m:1`：**0 错误**、3 警告（CS8632，`scripts/Emuera/Runtime/AgentBridge/AgentLlmMethods.cs` 既有问题）；首轮（flip 移除态）与终态（flip 恢复态）各构建一次均 0 错误。
- [静态] 首轮全局 grep flip 符号 0 残留；终态 grep `FlipEnabled|QuickFlip`：QuickButtons.cs 4 行、OptionWindow.cs 6 行，与恢复面一致，无悬空引用。
- [静态] 锚点数学自查：贴底时 `AnchorTop=AnchorBottom=1`、`OffsetTop=-margin-height`、`OffsetBottom=-margin`；`_Ready` 初始锚点（`AnchoredLeft/AnchoredTop` 派生）与新布局一致；翻转开启时水平走贴左分支（左下角）、宽度调节条在面板右侧（内侧），`_Input` 拖宽方向随 `AnchoredLeft` 自动正确。
- [静态] 即时生效链路：`OnQuickFlipToggled → RefreshQuickButtonSettings → RefreshSizing → RequestPanelSizeUpdate → ApplyPanelSize`，开关切换无需重开面板。
- [未验证] 真机视觉位置——eraTW 的 ERB 未定义 `QUICKBUTTON`，桌面面板无按钮无尺寸，无法截图确认（与 2026-11-16 补遗 11 同样的桌面验证盲区）。**最终验收必须 Android APK 实测**：横屏/竖屏/手动旋转（仅本次）三种方向下面板默认均在右下角；开启翻转后在左下角；竖屏不盖右侧菜单栏；悬浮模式默认右下角、拖动后位置记忆不受翻转影响。

## 4. AI 表现复盘

- **有效**：动手前先读 `2026-11-16-screen-rotation-follow.md` 补遗 11，确认「贴顶」本身是上次用户反馈的产物——本次是推翻而非修 bug；歧义未收敛时先 AskUserQuestion（虽未获答复，但留下了裁定轨迹）。
- **返工 1 次（用户指出的违规方向）**：第一轮把「始终右下角」执行成了「无条件右下角 + flip 退役」，用户追问「即映像翻转？」后裁定「恢复左边能力」→ flip 回归。**触发点**：用户两条追问消息；**可核对后果**：QuickButtons.cs/OptionWindow.cs 各多一轮删除-恢复编辑 + 两次构建；**可复用动作**：歧义请求未获用户答复时，宁可保守保留既有能力、只做明确要求的部分——「无条件」表述不足以推断要退役一个既有开关。
- **险些踩的坑（未踩）**：差点 bump `QuickFloatingPositionVersion` 清"旧默认坐标"——未拖动过本就是 -1、拖动过的是用户意图，bump 只会丢状态；「不要删除用户配置」记忆（`never-delete-user-config`）在删除 flip 配置读取时起了刹车作用，只停止消费、不删键——恢复时配置读取原样回归，用户旧设置无缝生效。

## 5. 教训 → 具体优化动作

1. **（本 PR 已执行）语义收敛必须写进注释**：`QuickButtons.cs` 锚点派生注释明确「翻转=纯位置换边（内容不镜像）」——用户问过「即映像翻转？」，说明原命名/提示不足以自解释；`OptionWindow.cs` 行注释同步注明「左下角」。
2. **（本 PR 已执行）能力保留优先于字面推断**：未获用户答复的歧义点，按「既有能力不退役」兜底；退役类改动（删开关/删配置）必须拿到用户明确确认才能执行。
3. **升级去向：暂不升级**。「歧义 + 用户未答复 → 保守兜底」尚未复现第二次，暂不进 prompt-patterns/AGENTS.md；再出现一次同类返工再评估。

## 6. 遗留登记

- **P1｜真机 APK 验收未做**：右下角/左下角锚定在横竖屏与手动旋转下的表现未经 APK 实测（桌面无法产生 QUICKBUTTON 面板内容，见验证记录）。不在本轮原因：真机链路（构建导出 + 安装 + 多方向人工核对）超出本会话范围且需用户参与。触发条件：用户真机测试反馈异常时，优先核对本文与 `ApplyPanelSize` 锚点分支。
- **P2｜「翻转」若实指设备 180° 倒持**：右下角锚点对 90° 旋转恒成立（视口随系统旋转），但「Reverse 方向无效」是既有平台硬事实（memory `android-orientation-fullscreen`），倒持时面板会出现在物理上的另一角。不在本轮原因：用户追问的是面板设置项语义，未提及倒持；支持 180° 需动 Java 层 FULL_SENSOR/方向链路，属独立任务。触发条件：用户明确提出"倒过来拿手机时面板也要在右手边"。

---

# 扩展任务（同日）：悬浮窗翻转即时化 + 浮层拖动跟手修复

用户反馈：「调整悬浮窗翻转，翻转不灵敏且悬浮窗移动效果差」。同一 feature 面的追加修复，改动文件：`scripts/panels/QuickButtons.cs`、`scripts/panels/QuickFloatingWindow.cs`、`scripts/EmueraContent.cs`、`scripts/panels/OptionWindow.cs`。

## 根因（三个独立缺陷叠加）

1. **拖动自反馈（移动效果差的主根因）**：`OnFloatingDragBarGuiInput` 用 `touch.Position`/`drag.Position` 直接差分——Godot 经 `_gui_call_input` 把触摸/拖拽事件坐标**本地化到控件**，而拖动条随面板一起动：手指世界系走 d 时，局部坐标只报回 `d - 面板位移`，递推收敛后面板仅以约一半手指速度跟随，呈橡皮筋式迟滞。鼠标路径用 `GlobalPosition`（视口画布坐标，不受控件位移影响）无此问题——所以桌面拖动条手感尚可、真机触摸拖动很糟。
2. **异步落位（次根因）**：拖动走 `MoveFloatingPanelBy → RequestPanelSizeUpdate → UpdatePanelSize`，内部 `await ToSignal(ProcessFrame)` 一帧后才摆位，再加一帧延迟。
3. **翻转无可见效果（翻转不灵敏）**：悬浮浮层自由坐标不理会翻转（设计如此），桌面悬浮窗内容区面板恒贴左贴顶且窗口尺寸紧贴面板——拨开关两条路径都看不到变化。

## 修复

- `ToDragBarCanvasPosition`：触摸/拖拽局部坐标经 `floatingDragBar.GetGlobalTransform()` 换算到画布坐标再差分（`QuickButtons.cs:1346`），自反馈消除。
- `MoveFloatingPanelBy` 改为经 `ApplyFloatingOverlayLayoutNow`（新增，`QuickButtons.cs:221`）**同帧**落位：按当前面板尺寸复用 `ApplyFloatingOverlayPanelSize` 的钳制+三件套摆位，不再触发异步布局。
- `NotifyFlipSettingChanged`（新增，`QuickButtons.cs:236`）：悬浮浮层把 X 按 `safeRect` 水平镜像到对侧、即时落位并持久化；从未定位过则保持 -1 态（下次布局按新方向取默认位）；`ApplyFloatingOverlayPanelSize` 默认位改为响应翻转（右下 ↔ 左下）。
- `QuickFloatingWindow.OnFlipSettingChanged`（新增）：悬浮窗水平镜像到主窗口对侧并 `SavePosition`；未显示过时镜像 savedX（下次 Show 生效）；`GetDefaultPosition` 同样响应翻转。
- 拖动条按下提亮（`SetFloatingDragBarVisual`），松手复原——「已抓住」的视觉反馈。
- 接线：OptionWindow 翻转 handler → `EmueraContent.NotifyQuickFlipChanged`（新增）→ 面板 + 悬浮窗各自镜像。

## 验证记录

- [实测] `dotnet build` 0 错误，DLL 时间戳 15:01 刷新。
- [静态] 跨四文件符号 grep 一致性核对（Move/Apply/Notify/To/Set 五组新符号 + OnFlipSettingChanged 全部对齐，无悬空调用）。
- [静态] 坐标数学自查：`GetGlobalTransform() * localPos` 恒等于手指世界位置（控件动则变换跟着动，两个事件各用各自的变换反推，差分=纯手指位移），CanvasLayer 变换包含在内。
- [未验证] 真机手感与翻转镜像的视觉效果——需要 APK 实测：拖动条拖拽应 1:1 跟手；拨翻转开关悬浮浮层/悬浮窗应立刻水平换边；翻转后再拖动、再旋转屏幕，落位与钳制正常。

## AI 表现复盘（扩展段）

- **有效**：没有把「移动效果差」当作纯手感问题去调灵敏度参数，而是先推演输入坐标空间——识别出控件本地化坐标 × 控件自身位移的自反馈（收敛到 d/2 的递推），一次定位主根因；鼠标路径正常、触摸路径异常的对照（`GlobalPosition` vs 本地 `Position`）佐证了诊断。
- **低效**：修 `QuickFloatingWindow` 时凭摘要记忆写 old_string 被打回（文件未读），重读后发现记忆里的方法名/键名与真实文件有出入（`OnPanelWindowDrag` 等）——摘要上下文里的"文件内容"不可直接当 old_string 用，必须重读。

---

# 扩展任务 2（同日）：启动后转屏延迟/不转 + 浮层跨方向不落角

用户反馈：「在启动翻转会有延迟或者不翻转的情况，并且无论开不开悬浮窗都会」。与悬浮窗无关的表述 + 「延迟」提示问题在**屏幕方向系统**；同时悬浮浮层的跨方向落位也确有一个真实缺口，一并修复。改动文件：`scripts/GodotHost/AndroidWindowOrientationController.cs`、`scripts/panels/QuickButtons.cs`。

## 根因与修复

1. **方向守护节拍太稀（转屏延迟）**：Activity 重建/resume/沉浸全屏窗口模式切换（surface 重建）都会让系统按 manifest（fullUser）重写方向请求；原实现只靠 1s 一拍的守护夺回，用户感知为「刚启动转屏要等一下、偶尔转不了」。修复：① `ApplyImmersiveWindowMode` 在窗口模式**实际切换**的同一拍立即重申 FULL_SENSOR（`TryApplyFullSensor`）；② 新增 10s 加速收敛窗（`MarkGuardFastWindow`），窗口内守护节拍 1s→0.25s（`CurrentGuardIntervalSeconds`），`ApplyOrientationMode`（startup/resume/开关切换）都会打开收敛窗；收敛期结束自动回到 1s 常规节拍，无常驻开销。
2. **浮层跨方向不落角（面板缺口）**：浮层位置是绝对坐标，旋转后仅被钳进新安全区、不会落到新方向的右下角。修复：`RefreshSafeAreaLayout` 检测安全区**主从关系翻转**（横↔竖，宽高比较结果变化）→ 浮层重置为未定位态 → 下一次布局按当前翻转方向落新方向目标角；键盘弹出等 inset 微调不翻转主从关系，不会误触发，同方向内手动拖拽位置保留。内嵌模式（悬浮关）由锚点=1 天然跟随视口，无需改动——用户「无论开不开悬浮窗都会」的体感由修复 1（转屏本身延迟）解释。

## 验证记录

- [实测] `dotnet build` 0 错误。
- [静态] 守护时序自查：`ApplyOrientationMode`（startup/resume）→ 标记收敛窗 + 下一拍 0.25s；窗口模式切换 → 同拍重申 + 标记收敛窗；10s 后 `CurrentGuardIntervalSeconds` 回落 1s。`RotateScreen`（手动旋转）置 `fullSensorApplied=false` 走轮询，不受收敛窗影响。
- [静态] 浮层重落角自查：`lastOverlaySafeSize` 初值 Zero → 首次布局只记基线不触发；悬浮关时整段跳过；跨方向触发后 `floatingPanelPosition=(-1,-1)`，`ApplyFloatingOverlayPanelSize` 的默认分支按 `FlipEnabled` 取右下/左下。
- [未验证] 真机启动转屏时延（应从 ~1s 级降到不可感）与浮层跨方向落角——需 APK 实测：启动后立即旋转手机应即时跟随；悬浮开/关两种模式分别验证；悬浮模式旋转后面板应在新方向右下角（翻转开则左下角）。

## AI 表现复盘（扩展段 2）

- **有效**：用户说「无论开不开悬浮窗都会」——没有把它当成悬浮窗缺陷硬修，而是按「与悬浮正交的方向系统 + 悬浮自身真实缺口」两条线各修各的，两条都对着「始终右下角」的总裁定。
- **可复用动作**：Android 上「运行时方向请求被系统按 manifest 重写」不是一次性事件——窗口模式切换、resume 都会重放；凡做方向类运行时请求，必须在每个已知重放点之后同步重申，不能只依赖周期守护兜底。

---

# 扩展任务 3（同日）：悬浮浮层「隐藏→打开」回初始角，弃用跨开关位置记忆

用户需求：①悬浮窗关闭后再打开自动恢复初始位置；②开启翻转时恢复到翻转后的位置（左下角）；③竖屏里拖过的悬浮窗位置在后续打开/转屏后导致悬浮窗「丢失」。改动文件：`scripts/panels/QuickButtons.cs`（ShowPad 单处）。

## 决策与实现

- **位置记忆降级为「单次打开会话内有效」**：ShowPad 在「隐藏→可见」边沿把 `floatingPanelPosition` 重置为未定位态，下一次布局走默认分支——右下角；`FlipEnabled` 开=左下角（翻转后的默认位此前已实现，本次直接复用）。同一次打开期间的拖拽、翻转镜像仍即时生效。
- **边沿限定（防回归）**：`ShowPad` 存在面板已可见时的重复调用路径（自动唤起，`EmueraContent.cs` 5461/5538/5555/6371 等），若无条件重置会把用户刚拖的位置半途拽回角落——重置条件加 `!Visible`。
- **「丢失」的结构性闭环**：①打开即回角（本轮）；②跨方向旋转即回角（扩展任务 2）；③拖拽即席钳制（扩展任务 1）。三条路径下绝对坐标不再跨上下文沿用，「丢失」无产生渠道。
- **遗留（P2）**：`SaveFloatingPosition` 仍会写 `QuickFloatingX/Y` 键但运行期不再消费（_Ready 载入值在首次 Show 即被弃）。保留写入以便未来加「记忆位置」开关时零成本恢复；若用户确认永不恢复，随下次触达删除写入点。

## 验证记录

- [实测] `dotnet build` 0 错误（两轮：初版无条件重置 → 加 `!Visible` 边沿后复验均通过）。
- [静态] 重置条件 `!Visible && floatingOverlayMode && position>=0`：关闭态打开→回角；已可见的重复 ShowPad→位置不动；内嵌/桌面原生窗模式→不受影响（桌面窗位置由 OS 管理，仍保留跨开合记忆，属调试场景不动）。
- [未验证] 真机：拖拽→关闭→重开应回右下角（翻转开=左下角）；竖屏拖拽→转屏→重开应在新方向目标角。

---

# 扩展任务 4（同日）：遗留事项台账落地（流程沉淀）

用户指示「遗留问题记录，到时候对接时就不会不知道遗留了什么」——把散在各 evolution-log 遗留登记里的未决项升级为统一台账，对接/验收时有单一入口。改动文件：`governance/backlog.md`（新增）、`AGENTS.md`（governance 节两处）。

## 内容

1. **新增 `governance/backlog.md`**：使用规则（任务收尾同步、关闭/废弃不删条目、每条必含优先级/状态/来源/触发条件、细节留源日志防双源漂移）+ 当日种子条目（P1 真机 APK 验收清单一条；P2 四条：QuickFloatingX/Y 死配置键、180° 倒持不支持、桌面悬浮位置记忆语义不一致、历史日志 open 项待增量迁移）。
2. **AGENTS.md governance 节**：机制总览句后追加台账指引（「任务收尾与 evolution-log 同步更新，对接/验收/开新任务前必读」）；收尾自检清单新增一条「新增/关闭的遗留条目已同步 backlog.md」。升级去向：本条即 (b) 形态「本 PR 已执行」——用户点名要的机制，直接入首读文档。

## 验证记录

- [静态] 纯文档任务：`backlog.md` 已创建且表格/条目格式可解析；AGENTS.md 两处替换均带唯一性断言（count==1）执行成功；与 AGENTS.md「收尾自检」「何时必须写」既有表述无矛盾（台账是登记动作的汇总落点，不新增时点）；无构建/运行验证。
- [静态] 种子条目与今日日志各扩展段的遗留登记一一对应，无凭空条目。

---

# 扩展任务 5（同日）：启动器「高级兼容设置」独立页签

用户反馈：启动器游戏选择页里，高级兼容（折叠块）的滚动条会被游戏页滚动条遮挡；要求高级兼容独立成界面。改动文件：`scripts/FirstWindow.cs`、`assets/lang/{default,zh_cn,en_us,jp}.txt`、`tools/launcher-compat-tab/run_check.gd`（新增验收驱动）。

## 实现与关键决策

- 新增 `LauncherTab.Compat` 页签（侧栏第 2 位「兼容」），`CreateCompatibilitySettings` 内联折叠块整体迁移为 `CreateCompatTabContent` 独立页：标题 + 说明 + 高级兼容开关 + profile 下拉 + 兼容包勾选列表 + 手动路径，页内自持一层 `ScrollContainer`（宽滚动条样式复用 `ApplyWideVerticalScrollbar`）。折叠开关删除——整页即设置，无需折叠。
- 游戏页回归纯游戏选择（列表 + 浏览/移除 + 开始游戏）；外层 `ScrollContainer` 保留作小窗兜底（2026-10-18 引入的职责收窄，注释更新）。
- 页签切换语义：Compat 重点不重扫（与 Game 一致）；`ScanGames` 从 `!showingManual` 收紧为 `tab == Game`（原逻辑 Diagnostics 提前 return，行为等价，新写法显式化）。兼容包候选随游戏选中刷新（`OnGameSelected → LoadCompatPackEditForGame`）不依赖所在页签，跨页安全。
- 语言条目 ×4 文件（`FirstWindow.CompatTab/CompatTabTitle/CompatTabHint`），插在 GameTab 行后；旧 `CompatSettingsToggle` 键保留为孤儿（无消费方，无害）。

## 验证记录

- [实测] `dotnet build` 0 错误。
- [实测] 窗口模式验收驱动 `tools/launcher-compat-tab/run_check.gd`（沿用 markdownlabel-tests 的 SceneTree 脚本模式）：PASS——①游戏页无旧折叠开关；②侧栏「兼容」按钮存在可按；③按下后出现「高级兼容设置」标题与「高级兼容模式」开关；④两张截图落盘 `D:/gemuera-reports/launcher-compat-tab/`。人工核对：游戏页无兼容块、列表滚动条无遮挡；兼容页全部控件可见、兼容包列表滚动条完整可见不被覆盖；侧栏 4 页签选中态正确；profile 记忆（snake）正常加载。
- [未验证] Android 真机上启动器「兼容」页签布局（88px 侧栏 4 按钮、兼容包列表触控滚动）——并入台账 P1 真机验收清单。

## 环境障碍与打通

`where Godot` 无（PATH 无 Godot）；实际位置 `D:/Godot_v4.7-stable_mono_win64/Godot_v4.7-stable_mono_win64/Godot_v4.7-stable_mono_win64_console.exe`。窗口模式截图判定：`root.get_texture().get_image().save_png()`（绝对路径指向项目外 `D:/gemuera-reports/`，符合「报告产物不入 res://」铁律）。

---

# 扩展任务 6（同日，PR#10 后变更）：回位功能入口改系统菜单 + 坐标陈旧判定重构 + 位置持久化整体移除

用户反馈（真机）：①「关闭悬浮窗会直接钉死在最后移动的位置，这不是我所说的返回初始位置」——关闭必须**立即**回角，而非等下次打开；②「竖屏下的快捷键面板横屏很难看到，应该是坐标的问题」；③「不要在拖动条那加啊，看不到的话怎么办啊」——回位入口不能放在面板自身上（面板不可见时面板按钮同样够不着）。改动文件：`scripts/panels/QuickButtons.cs`、`scripts/EmueraContent.cs`、`assets/icons/rehome.svg`（新增）、`tools/launcher-compat-tab/OverlayCheck.cs`+`overlay_check.tscn`（新增验收）、语言文件 ×4（回滚 HomeButton 键）。

## 关键决策与 Why

- **位置持久化整体移除**（写入+读取+版本迁移块全部删除，`QuickFloatingX/Y` 键停止读写）：配置坐标是「重开/重启钉死在旧位置」的根源；用户两次向「永远回角」方向收敛，记忆位置已无需求方。旧配置文件中的遗留键**不删除**（遵守「不要删除用户配置」），只是无消费方。
- **关闭即回角**：`HidePad` 在关闭瞬间重置为未定位态；`ShowPad` 边沿保留为幂等兜底，并改为**无条件重置+请求布局**——空面板（游戏未定义 QUICKBUTTON）没有任何其他布局触发器，原「有旧坐标才请求」的条件会让未定位态永不物化（run_overlay_check 首轮 FAIL 的根因）。
- **坐标陈旧判定重构（锚定安全区）**：初版「基线+横竖翻转」有两个洞——①首次调用只建基线不重置，物化早于基线时陈旧坐标漏判；②只看宽高主从关系，同向尺寸变化（折叠/分屏）漏判。终版：`ApplyFloatingOverlayPanelSize` 物化坐标时记录 `floatingPanelAnchorRect`，`RefreshSafeAreaLayout` 对比当前安全区与锚定区，**不一致即重置**——坐标只对物化它时的安全区有效，转屏/折叠/分屏/留白调整全覆盖。
- **回位入口在系统菜单栏**（「面板回位」图标，紧跟「快捷」开关，非浮层模式置灰）：面板不可见时面板上的按钮不可达，入口必须在面板之外（用户明确裁定拖动条方案作废）。

## 验证记录

- [实测] `dotnet build` 0 错误（每轮）。
- [实测] `tools/launcher-compat-tab/run_overlay_check.gd`（GDScript）**不可用已删除**：GDScript 无法对 CSharpScript 调 `.new()`（`Nonexistent function 'new' in base 'CSharpScript'`，Godot mono 4.7 实证）——C# 类型验收驱动必须用 C# 节点 + `.tscn` 场景（`Godot --path . res://...tscn`）。
- [实测] 终版 `overlay_check.tscn`（C# 驱动）**PASS**：初始位右下角物化 → 拖动生效 → 关闭→重开回初始位 → 窗口 1152x648→800x1280（视口 1152x648→1152x1843）后重落新方向下半屏 → `RehomeOverlayPanel` 一键回位。
- [实测] `run_check.gd`（启动器页签回归）PASS。
- [静态] 初版 run_overlay_check 曾暴露并修复 ShowPad 空面板物化缺陷（见上），验收驱动先于交付发现真实缺陷——驱动留存价值实证。
- [未验证] Android 真机：菜单「面板回位」图标渲染与触控、转屏回角、关闭即回角——已并入 backlog P1 清单。

## AI 表现复盘（扩展段 6）

- **有效**：验收驱动两次在交付前抓到真实缺陷（空面板不物化、基线机制漏判）——「先写驱动再交付」在状态机类改动上收益明确。
- **返工 1 次（用户裁定方向修正）**：回位入口初版放拖动条，用户指出「面板看不到时按钮也够不着」——**触发点**：用户原话；**可核对后果**：撤回拖动条按钮改系统菜单；**可复用动作**：给「可能不可见的对象」加功能入口时，入口必须放在对象自身之外，先问「对象不可见时这个入口在哪」。
- **低效**：GDScript 驱动 C# 类的 `.new()` 不可用是跑完才发现——Godot mono 项目的 Node 级验收驱动应**默认直接用 C# 场景**，GDScript 只适合纯 GDScript 对象（升级去向：暂不升级，单次教训，memory 已记）。

## 遗留登记

无新增（P1 真机清单已在 backlog 更新；QuickFloatingX/Y 死键条目随本次关闭）。
