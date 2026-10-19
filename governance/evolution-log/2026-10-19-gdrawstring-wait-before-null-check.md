# 2026-10-19 GDRAWSTRING 文本消失：等待渲染完成前提前判空

## 现象

eraBEMANI（机翻版 1.5）在 **v24pure** 下进入「MODE SELECT / 模式选择」界面时：

- 模式横幅的**底图**能显示（`MODE.png` 的白色平行四边形）；
- 但横幅上的模式名、说明文字（`GDRAWTEXT` 绘制）**全部消失**；
- `HTML_PRINT` 本身正常执行（日志确认传入 `<nobr><button value='7'>...` 完整字符串），
  顶部普通 `PRINT` 文本也正常，所以不是 HTML/解析问题；
- 截图证据：`E:\tmp\gemuera-reports\bemani-gdrawfix2\run-001\viewport.png`（修复后文字恢复）。

这一界面来自 `ERB\1-タイトル,ニューゲーム処理\3-ニューゲーム処理.ERB`，流程为
`GCREATEFROMFILE` 加载 `MODE.png` → `GSETBRUSH` / `GSETFONT` → `GDRAWTEXT` 写文字 →
`SPRITECREATE` 建 sprite → `HTML_PRINT STRJOIN(HTML用, "")` 输出按钮图像。

## 根因

`scripts/Emuera/Content/GraphicsImage.cs` 的 `GDrawString`：

```csharp
var item = EmueraMain.SubmitTextRender(...);
if (item == null || item.ResultImage == null)   // ← 这里
    return false;
item.Completed.Wait(Timeout.Infinite);
if (item.ResultImage == null)
    return false;
```

`EmueraTextRenderComponent.Submit` 对**首次**（未命中缓存）文本只是入队并立即返回
`TextRenderItem`，此时 `ResultImage` 必然是 `null`，要等 Godot 主线程 `_Process`
消费队列、渲染完成并 `Set()` 才有图。上面的提前判空使**所有首次 GDRAWSTRING 都直接
返回 false**，`GDRAWTEXT` 因此从未真正把文字画到 graphics 上；只有缓存命中（相同
文本/字号/颜色）的第二次调用才会成功——所以每块横幅的文字都整片缺失。

## 修复

`scripts/Emuera/Content/GraphicsImage.cs`：先判 `item == null`，再
`item.Completed.Wait(Timeout.Infinite)`，**等完成后再检查 `ResultImage`**。
保留原有“无界等待”语义（注释本意就是避免 500ms 超时静默丢字），不引入新的超时兜底。

## 验证

- 修复前 v24pure × eraBEMANI 模式界面：8 块白色横幅无任何文字；
- 修复后同路径：模式名/说明/价格/期限等全部显示（截图
  `E:\tmp\gemuera-reports\bemani-gdrawfix2\run-001\viewport.png`），标题画面文字也正常；
- 无头模式同路径仍 `Passed / inputs_consumed`，无死锁（`EmueraTextRenderComponent`
  在 headless 下同样完成队列）；
- 门禁：build 0 error；三冒烟全过（v24 561/266、snake 670/351 表面不变）；
  xUnit 131+35+3；governance 4/4；
- 改动仅在 `GraphicsImage.cs`，不涉及方言表面/清单，三个 profile 共享修复。

## 备注

`GDrawString` 的 `SubmitTextRender` 在**主线程**调用会返回 null（设计如此），
所以该路径只应在 Emuera 后台线程执行；本次修复不改变这一约束。
