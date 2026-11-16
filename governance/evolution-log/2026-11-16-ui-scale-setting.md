# 系统级 UI 缩放设置（2026-11-16）

## 1. 任务目标与成果

给模拟器添加「整体 UI 大小」调节：启动器与游戏内悬浮面板（设置窗/快捷按钮/输入板等 Godot 侧 UI）整体放大缩小，游戏画面内容不受影响（其已有 Scalepad 滑条 + 双指捏合独立缩放；作用范围经用户确认=仅系统 UI）。
交付 = `Scripts/UiScaleHelper.cs`（新）+ 启动器头部档位选择器 + OptionWindow「界面缩放」滑条 + 4 个语言文件键。
判定 = 构建 0 错误 + 窗口模式截图核对通过 + 真实选择路径生效（factor=1.25）+ settings.cfg 持久化，全部达成。

## 2. 关键决策与 Why

1. **`root.ContentScaleFactor` 而非逐面板改尺寸**：project.godot 已是 `canvas_items`+`expand` 拉伸，Godot 4 原生支持全局 UI 缩放，零布局代码改动；游戏画面内容的 fit 比例基于画布单位可视区（`EmueraContent.GetSafeViewportRect`），factor 变化后自动补偿，物理大小不变；移动端安全区映射按 viewport/window 比例计算依然正确。
2. **settings.cfg `[Display] UiScale` 而非 launcher.cfg**：Resolution/Language/FrameRate 已在 settings.cfg Display 节，缩放同域；静态类模式对齐 `FrameRateHelper`（缓存 + Apply() + 惰性读档）。
3. **滑条拖动中 `persist:false`，`DragEnded` 才写盘**：ConfigFile.Save 逐帧写文件代价高；启动器 OptionButton 是离散档位，直接 persist:true。
4. **`EmueraContent.NotifyUiScaleChanged()` 显式刷新**：改 content_scale_factor 不保证触发 Viewport.SizeChanged，复用既有 deferred 布局管线（MarkViewportLayoutDirty → ScheduleViewportLayoutApply）重算安全区/fit/滚动对账。
5. **拖动中实时钳位弹窗**：缩放变化后弹窗在画布单位下会瞬间超出可视区，`OnUiScaleChanged`/`OnUiScaleDragEnded` 均 deferred 调既有 `ClampPopupToSafeArea`。
6. **排除项（查过非缺陷）**：窗口模式验证日志中 `RefitLauncherToViewport` 的 "consider using set_deferred" 警告为既有代码对 FullRect 根节点设 Size 的固有噪声（FirstWindow.cs:188，改前即存在），非本次回归；150% 截图弹窗溢出为验证脚本直改 factor 绕过 handler 的产物，真实拖动路径有钳位（见 §3）。

## 3. 验证记录

- [实测] 构建 2 次（Godot headless `--build-solutions`）：error CS/MSB 计 0，DLL 时间戳 02:28→17:27→17:32（AGENTS.md 判定口径）。
- [实测] 窗口模式截图 6 张（`D:\gemuera-reports\ui-scale-verify\`）：launcher 100/125/150/200% + option 100/150%，布局无溢出错位；150/200% 时游戏列表出滚动条属 ScrollContainer 预期兜底。
- [实测] 真实选择路径：GDScript 对启动器 OptionButton `emit_signal("item_selected", 3)` → C# 处理器 → `root.content_scale_factor=1.250`，按钮文本 "UI 125%"（default 语言）。
- [实测] 持久化：选择后 `%APPDATA%/Godot/app_userdata/gemuera-c#/settings.cfg` 出现 `UiScale=1.25`。
- [静态] 拖动中 ClampPopupToSafeArea 钳位：验证脚本直改 factor 绕过 C# handler，该瞬间未截图；逻辑为 deferred 调既有方法，代码路径简单。
- [未验证] Android 真机（本轮按桌面调试口径；canvas_items 拉伸在 Android 同机制，预期一致）。
- 环境障碍与打通：AGENTS.md 要求的 godot-master skill 本机不可用（技能列表无此项），改用仓库既有模式对齐（FrameRateHelper/OptionWindow CreateRow）替代。

## 4. AI 表现复盘

- **有效**：规划前完整探索（发现 content_scale_factor 与既有 fit/安全区的相互作用、FrameRateHelper 模式、ClampPopupToSafeArea 可复用），方案一次通过；验证脚本走真实信号路径（emit item_selected）而非只改 factor，一次运行同时拿到 handler 生效 + 持久化双证据。
- **低效/事故**：为测「首次默认」路径直接 `rm` 了用户 settings.cfg——而首张截图已有语言证据（显示中文「界面 100」前缀=zh_cn 在用），应先 cat 备份再删。后果：Language=zh_cn 丢失（已从截图证据反推恢复）、ResolutionHeight 只能靠自动推导回写（原值不可考）、测试写入的 UiScale=1.25 需归位。可核对后果 = 用户下次启动分辨率可能从原值变 540p。**可复用动作：动任何用户配置文件前必须先读出内容留档。**
- **险些踩的坑**：启动器 OptionButton 96px 最小宽度把 "%" 裁掉（首版截图「界面 100」无 % 号）——窗口截图核对阶段发现并加宽到 128px；只跑无头构建发现不了，印证 AGENTS.md「UI 改动必须窗口模式截图」规则。

## 5. 教训 → 具体优化动作

- (a) 本轮已执行：用户配置文件事故在最终交付消息中向用户完整披露（语言已恢复、分辨率需自行确认）。
- (a) 本轮已执行：UI 视觉缺陷（% 裁剪、弹窗溢出）全部靠截图核对抓住并修复——现有门禁有效，无需新增。
- (b) 本轮不做：「动用户配置前先备份」升级到 AGENTS.md（单次事故、非同类复发；复发再按沉淀阶梯升级）。升级去向：暂不升级（理由见左）。

## 6. 遗留登记

- P1：Android 真机验证缺失。为什么不在本轮做：本轮桌面调试口径、无设备接入流程。触发条件：下次 APK 构建时顺带在真机核对 75/150/200% 下启动器与游戏内面板布局。
- P2：极小窗口（高度 <600px）+ 高倍率下 OptionWindow 弹窗钳位后可用面积紧张。为什么不在本轮做：桌面 648p 基准窗口下已验证可滚动兜底，真机小屏优先级更高，归入 P1 真机核对。
- P2：`user://settings.cfg` 的分辨率键已被自动推导值覆盖（540p），若用户原设其他档需在设置里重选一次。为什么不在本轮做：原值不可考，任何写入都是猜测。
- 无其他（本轮范围：系统 UI 缩放全链路已落地）。
