# 2026-10-20 HTML shape 行高、移动端竖屏画布、emuera.config 与字号调整

## 背景

用户反馈五类问题（Android 真机为主）：

1. 竖屏缩放文字超出屏幕后无法拖动。
2. 竖屏识别不到自适应屏幕大小，影响后续所有字符绘制；建议横竖屏都使用竖屏宽高。
3. 从竖屏切回横屏后画面异常。
4. gEmuera 读取的 `emuera.config` 不跟随游戏，而是 gEmuera 默认状态。
5. eraBEMANI 许可协议页的 `<shape type='rect'> + <font bcolor> + <button>` HTML 无法显示。

并新增功能：解锁文字大小调整（自绘屏幕）。

## 根因与修复

### 1. `<shape>` 把逻辑行高撑高，后续黑色文字被推到白框下方

参考 v24 的 `ConsoleShapePart` 是 escaped part：矩形可以越过本行向下绘制，但逻辑行高仍为 `Config.FontSize`。gEmuera 的 `EmueraContent.GetPartBottom` 对 shape 返回 `part.Bottom`，导致 shape 行高变成矩形高度（许可协议页约 92px），后续 `PRINTL` 文本行被推到白框下方；`SETCOLOR 0,0,0` 的黑字落在黑底上，看起来整页只剩白框。

- 修复：`GetPartBottom` 对 `ConsoleShapePart` 返回 `baseLineHeight`，保持 shape 只作为覆盖绘制。
- 验证：删除 global.sav 后复现许可协议页，修复前只有白框，修复后白框内显示黑字和 `[1] - 次へ`，与参考 v24 截图一致。
- 影响：所有使用 `<shape>` 的游戏（包括旧屏历史）行高恢复参考语义；eratoho v24pure 回归 `semanticSha 4859125d…` 不变。

### 2. 移动端竖屏画布与旋转

- 新增 `Config.UpdateWindowSize(width, height)`（只运行时改写，不写回游戏配置）。
- `Program.ApplyAndroidWindowWidthPolicy` 不再用物理屏幕安全宽度覆盖 `Config.WindowX`；改为严格读取游戏自身 `emuera.config` 的宽高，只做长短边归一化（始终以竖屏宽高作为逻辑画布），横屏也沿用同一逻辑尺寸。
- `EmueraContent.ApplyMobileViewportPolicy` 在会话启动、`Viewport.SizeChanged` 时按物理安全宽度计算 fit scale（只缩小不放大），旋转只重算缩放，不改行宽/行高。
- 双指缩放后只松开一根手指时，原先会停留在 pinch 手势模式，剩余手指的拖动被吞掉（表现为“缩放后拖不动屏幕”）。新增 `EndPinchAsSingleFingerDrag`，结束捏合、完成 scale focus 恢复并把剩余手指转成普通内容拖动。
- `contentScaleUserAdjusted` 记录用户主动缩放，旋转时不再被自动 fit 覆盖。

### 3. emuera.config 跟随游戏

- 配置路径本来就是 `Program.ExeDir + "emuera.config"`（`Program.ExeDir = Sys.ExeDir = 游戏根目录`），但 Android 的动态窗口宽度会覆盖其 `WindowX`。
- 移除该宽改写后，运行时只使用游戏 `emuera.config` 的字体/行高/颜色/窗口值；新增 `[CONFIG]` 证据日志（记录 root/emuera/default/fixed 路径和加载后的 font/line/window），便于真机确认。
- 逻辑画布的长短边归一化是用户明确要求的“横竖屏都用竖屏宽高”，不写回配置文件，不持久化污染游戏目录。

### 4. 字号调整解锁

- 选项窗口的字体滑杆由 `Editable = false` 改为可调整；按“渲染后字号 = Config.FontSize × 内容缩放”映射到 `EmueraContent.SetContentScale`，不修改游戏的 `emuera.config`。
- 打开选项时从当前内容缩放同步滑杆值；不改变 ERB 换行（自绘画布的整体缩放），避免运行期改 FontSize 导致已输出行重排/字体对象失效。

## 验证

- 构建：`dotnet build gemuera-c#.csproj -nodeReuse:false -m:1` 0 error。
- 许可协议页：`E:\tmp\gemuera-reports\bemani-license-fix3\run-001\viewport.png`，白框内文字/按钮可见。
- eraBEMANI 主界面：先输入 `1` 五次接受许可，再走原 flow，截图 `E:\tmp\gemuera-reports\bemani-license-flow\run-001\viewport.png`，主菜单/角色信息/HTML 面板正常。
- eratoho v24pure 无头回归：`Passed`，semantic `4859125d3f984f087eb143dd83fa54f3b754055187cb87a9e6b166d4505754a1` 不变。
- 门禁：三冒烟 pass（v24 561/266、snake 670/351）；CoreContractSmoke pass（DOTNET_ROLL_FORWARD=LatestMajor）；xUnit 131+35+3 全通过；governance 4/4。
- **Android 真机尚未测试**（本机没有连接 adb 设备）：竖屏 fit、旋转、pinch→单指拖动、字号滑杆需要用户 APK 实测确认。

## 风险/边界

- `Config.WindowX` 在 Android 上会被归一化为竖屏宽高，这是显示层适配，不是把 gEmuera 默认值写入游戏；桌面端不应用。
- 字号滑杆是整体内容缩放而不是重排字体；如需真正按 ERB 重排字号，需要重新解析所有显示行，风险更高，另立任务。
- shape 行高修复是 v24 共享渲染路径的参考语义修正，不涉及方言门控；eratoho 回归已覆盖未选择侧。
