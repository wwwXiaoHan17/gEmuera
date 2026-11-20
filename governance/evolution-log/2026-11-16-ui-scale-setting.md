# 系统级 UI 缩放设置（2026-11-16）

> 补遗（2026-11-16，用户反馈返工）：上文 §2-1「游戏画面内容由 fit 自动补偿、物理大小不变」的结论**被推翻**——那只在移动端 auto-fit 未贴 clamp 时数值巧合成立；fit 贴 0.35 下限（era 游戏 DrawableWidth 大、竖屏画布窄的常态）、用户手动缩放过、或桌面端（无 fit）时，游戏内容都会跟着 UI 缩放整体变大（用户实测指出「移动端也是整体缩放」）。返工方案：EmueraContent 引入 UI 补偿因子 `uiScaleCompensation = 1/root.ContentScaleFactor`，实际渲染 Scale = contentScale × 补偿，与窗口因子相乘后物理大小恒定——fit-clamp/手动缩放/桌面全路径成立。所有布局/滚动/命中/坐标换算（虚拟光标、MOUSEX/MOUSEY、输入回放、HTML div 锚定、pinch 焦点、重绘缓存键）统一改走 `GetSafeContentScale()`（有效缩放），用户值语义（pinch 起点、ContentScale 属性、Scalepad 显示、字体滑条算式）保留裸 contentScale。fit 目标改为「有效缩放填满安全区」（÷补偿），Scalepad Fit 同步 ÷补偿。**验证 [实测]**：真游戏（eraTW，v24pure）窗口模式截图——UI 缩放 100/150/200% 下游戏文本物理尺寸逐像素一致（行宽 425/425/424px、字高 12px 全同、4× 放大目视几何一致；像素级差异仅为非整数倍重采样的清晰度噪声），右上系统 UI 面板同步变大；SetContentScale(1.6)（字体滑条同路径）下文本明显放大且系统 UI 不动——字体缩放=游戏内容层（文本+图片同一 Scale 变换，机制上连带图片）缩放 ✓。首轮验证曾出现一次 150% 档未补偿的异常截图，加插桩后连续两轮未复现（每次通知均正确读取因子并更新补偿），判定为一次性捕获竞态。
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

> 补遗（2026-11-16，用户裁定方案 B：字体滑条回归内容层缩放）
>
> 背景：本分支 rebase 到最新 dev 时发现 dev 已合入 PR #2「separate font size API from content scale」
> （`EmueraContent.RequestFontSize` + `Config.SetRuntimeFontSize` + `src/Core/Ui/FontSizeScaling`），
> 它把 OptionWindow 字号滑条改为独立字号 API——只改字号、只对新输出行生效、**不缩放图片**。
> 这与用户本轮诉求「字体缩放也要把图片一同缩放」相反。用户明确选择方案 B。
>
> 改动（`scripts/Panels/OptionWindow.cs`）：
> - `OnFontSizeChanged` 改回 `EmueraContent.SetContentScale(scale)`：一次 Scale 变换作用于
>   lineContainer / consoleRenderSurface / cbgContainer / htmlIslandContainer，文本、行内图片、
>   立绘、CBG 背景、HTML 岛一起缩放，**旧输出行同样缩放**。
> - 换算按物理字号解耦：目标 `contentScale = requested / coreSize / UiScaleCompensation`
>   （UI 补偿见 §2-1 补遗），保证滑条数值 = 屏幕上的真实字号，与 UI 缩放互不干扰。
> - `SyncFontSizeFromConfig` 改为按 `coreSize × (ContentScale × UiScaleCompensation)` 反算显示值
>   （打开设置看到的是渲染后字号）。
> - 四语言 `OptionWindow.FontSizeHint` 文案改为「缩放游戏画面：文本、行内图片与立绘一起放大/缩小
>   （含已有输出行）」。
> - dev#2 引入的 `RequestFontSize` / `Config.SetRuntimeFontSize` / `FontSizeScaling` 及其 xUnit
>   测试**保留不删**：它们仍是有效基础设施（可被其他入口调用、测试仍绿），只是 UI 滑条不再走该路径。
>
> 验证 [实测]：eraTW（v24pure）窗口模式，滑条由 16→30 后 `ContentScale=1.875`（=30/16）且
> `Config.FontSize` 保持 16 不变（证明走内容层而非字号 API）；截图对比——启动时输出的旧行
> 与文本一起放大，设置窗自身尺寸不变。构建 error CS 计 0。
>
> 被推翻的中间结论：上文未涉及字号路径，故无推翻；本条取代 dev#2「字号与缩放严格分离」在
> UI 滑条上的适用性，属用户裁定的产品语义选择，非遗漏。

> 补遗（2026-11-20，用户裁定删除设置窗「字体大小」滑条）
>
> 方案 B 落地后，设置窗「字体大小」滑条与缩放板（Scalepad / 菜单第 10 个「缩放」）
> 走同一 API `SetContentScale`，是同一功能的两个入口（仅刻度不同：字号 8–48 vs 倍率
> 0.5–3.0）。用户确认冗余，裁定删除字体滑条、只保留缩放板。
>
> 移除内容（`scripts/Panels/OptionWindow.cs`）：fontRow 创建块、fontSizeSlider/
> fontSizeLabel/fontLabel 字段、OnFontSizeChanged、SyncFontSizeFromConfig、
> ShowPopup 里的同步调用、ApplyLanguageTexts 的 fontLabel 分支；
> 四语言删除 `OptionWindow.FontSize` / `FontSizeHint` 键。
> dev#2 的 RequestFontSize / FontSizeScaling / Config.SetRuntimeFontSize 基础设施
> 与 xUnit 测试继续保留（仍有效，其他入口可用）。
>
> 验证 [实测]：桌面打开设置窗截图——显示组依次为 界面缩放/自动旋转/旋转屏幕/
> 最大行数/左右留白/分辨率/帧率，无字体行、无空洞；构建 0 error。

> 补遗（2026-11-20，用户要求按 XEmuera/uEmuera 实现恢复字体大小滑条）
>
> 背景：上一条补遗删除了字体滑条（与缩放板同 API 冗余）。用户随后询问 uEmuera 的实现，
> 查 `docs/xEmueraCodeWiki/ConfigSystem.md`：XEmuera 系用 `Config.FontSize`（基础字号）×
> `Config.FontScale`（运行时缩放板乘数）做**排版级缩放**——字号/行高/窗口宽度/PRINTC 列数
> 全部按新度量重算，文字重渲染（锐利），图片不跟随。用户裁定字体滑条按此语义恢复
> （dev#2 的 `RequestFontSize`/`Config.SetRuntimeFontSize`/`FontSizeScaling` 基础设施正是其镜像）。
>
> 实现（`scripts/Panels/OptionWindow.cs`）：
> - 字体滑条重新加入显示组（界面缩放之后）：8–48 步进 1，值 = 核心字号
>   `EmueraContent.instance.ConsoleFontSize`（Config.FontSize）
> - `OnFontSizeChanged` → `RequestFontSize(requested)`（解释器空闲帧应用，避免
>   ClearFont 与解释器线程竞态；仅新输出行生效——旧行几何输出时已固化）
> - `ShowPopup` → `SyncFontSizeFromCore`（显示 Config.FontSize 当前值，与缩放板倍率无换算关系）
> - 四语言恢复 `FontSize`/`FontSizeHint` 键，文案写明排版级语义与「仅之后输出的行生效」
>
> 至此两条缩放机制并存且语义清晰：
> - 字体大小 = 排版级（XEmuera 语义）：文字重渲染、行高/PRINTC 重算、图片不变、新行生效
> - 缩放板 = 内容层矩阵：一切等比缩放（含图片与已有行）
>
> 验证 [实测]：桌面 + 测试游戏（PRINTBUTTON×2 + INPUT），滑条 18→32 后点击选项触发
> 新输出——旧行保持 18 号、新行 `RESULT={RESULT}` 按 32 号锐利渲染；构建 0 error。
> [未验证] PRINTC 分栏重算（需 PRINTC 密集输出的游戏）、真机表现。

> 补遗（2026-11-20，用户指出「API 不要搞一起」）
>
> 排查发现：上一条补遗（删除字体滑条）留下的注释仍写着「字号调节已并入缩放板…只保留
> 缩放板」——与紧随其后重新加回的字体滑条直接矛盾，是把两套 API 混为一谈的错误表述。
> 代码本身无混合（三条独立链路：UiScaleHelper / RequestFontSize / SetContentScale 各自闭合）。
>
> 修复：该注释改写为三条机制的分工说明（界面缩放 / 字体大小 / 缩放板），并标注「不要合并」。
>
> 验证 [实测]：grep 确认无 RequestFontSize↔SetContentScale 交叉引用；构建 0 error。
