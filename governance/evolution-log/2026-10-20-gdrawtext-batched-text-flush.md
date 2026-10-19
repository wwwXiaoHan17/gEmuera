# 2026-10-20 HTML/GDRAWTEXT 响应优化：延迟文本绘制 + 批量 flush

## 目标

eraBEMANI 在 v24pure 下，模式选择/主界面等 HTML/G 系画面响应慢。要求不改变 ERB 语义与最终画面。

## 先 profiling（不盲优化）

临时 PerfProbe 记录（v24pure × eraBEMANI，从新游戏走到主界面的 10 次输入流程）：

| 路径 | 次数 | 总耗时 | 平均 | 最大 |
|---|---:|---:|---:|---:|
| `GDrawString` 同步等待 | 113 | **5636 ms** | 49.9 ms | 52 ms |
| `Html2DisplayLine`（HTML 解析） | 4 | 13.2 ms | 3.3 ms | 9 ms |
| `PrintHtml`（含解析+加行） | 4 | 13.9 ms | 3.5 ms | 9.6 ms |
| `erb.load.initial`（903 文件） | 903 | 5308 ms | 5.9 ms | 65 ms |

结论：HTML 解析本身不慢（十几毫秒）；瓶颈是 **每条 `GDRAWTEXT` 都同步提交 Godot 主线程离屏渲染并等待 2 帧**。eraBEMANI 模式界面 8 个横幅共 113 条文本，因此仅文字绘制就 5.6s。

## 优化 1：GDRAWTEXT 延迟 + 批量 flush

`scripts/Emuera/Content/GraphicsImage.cs`：

- `GDrawString` 不再立即提交/等待，而是把 `(text, x, y, font, size, style, brushColor)` 快照入队到该 `GraphicsImage` 的 pending 列表（字体/画刷快照保证后续 `GSETFONT/GSETBRUSH` 不影响已排队的绘制）；
- 新增 `FlushAllPendingText()`：一次性提交所有图层的 pending 请求，再统一 `Wait`，最后按原绘制顺序 `BlendRect`；主线程可在一个批次里并行消费多个渲染槽。

“图像被观察/消费”的 flush 点与同步绘制语义对齐：

- `Creator.Method.ReadGraphics`：除 `GDRAWTEXT`（继续追加）、`GSETFONT/GSETBRUSH`（状态修改，已快照）外，所有 `GCREATE/GDRAW*/GCROP/GDISPOSE/SPRITECREATE/...` 读取或创建 graphics 的操作前先 flush；
- `EmueraConsole.PrintHtml`（HTML `<img>`/sprite 引用前）；
- `EmueraConsole.WaitInput` 与 `ReadAnyKey`（BINPUT/WAIT/WAITANYKEY 等输入观察点）。

绘制顺序、字体/画刷快照、混合顺序与逐条同步路径一致；`GDRAWTEXT` 返回值在图像存在时仍视为成功（异常/场景释放路径的返回码不作为正常语义依赖）。

## 优化 2：文本渲染槽 4→8、完成帧 2→1

`scripts/GodotHost/EmueraTextRenderComponent.cs`：

- `RenderSlotCount = 8`：一批待渲染文本可并行占满更多 SubViewport；
- `RenderCompleteFrameCount = 1`：StartRender 后下一帧即读取纹理（原 2 帧会把批量尾延迟拉长一倍）。

## 验证

- **耗时**（同流程、同设备、headless runner）：
  - 优化前 `GDrawString` 113 条 = **5636 ms**；
  - 延迟队列 4 槽/2 帧 = 992 ms；
  - 最终 8 槽/1 帧 = **278 ms**，约 **20x**；
  - `Html2DisplayLine`/`PrintHtml` 仍为 10~30ms 量级；初始 ERB 加载 5.1s（903 文件）未改动，本次优化针对 HTML/G 画面响应。
- **画面 parity**：同一 v24pure 新游戏→主界面流程，优化前后 `viewport.png` 解码像素对比：**仅 5 个像素差、最大通道差 1**（`1280×1000`），属于渲染批次/抗锯齿抖动级别；所有模式横幅文字、主界面边框、日历、按钮位置一致。
- **状态 parity**：`state.json` 一致：`lineCount=2336`、`isWaitingInput=true`、`buttonGeneration=7`、errors 空。
- **门禁**：build 0 error；三冒烟（v24 561/266、snake 670/351 表面不变）；xUnit 131+35+3；governance 4/4；headless 与 display-server 两条路径均通过。

## 后续候选

- 初始 ERB 解析（903 文件、5.1s）需要单独立项：parallel parse 涉及 label/warning/动态变量顺序语义，本次未改。
- `GDRAWTEXT` 极端异常路径的返回值与同步路径存在细微差异（正常游戏路径不依赖），如需绝对语义可增加显式 `GDRAWTEXT` 返回值 flush 开关再评估。
