# 2026-10-20 Canvas 图片 overlay 残留：教程图标盖在商店界面上

## 背景

用户从 eraBEMANI 教程进入 `[10003] 角色·物品购买` 后，左侧仍残留 tutorial 的
`DJ_USI`/牛等 `<img>` 图标，覆盖在道具列表上。用户问是游戏 ERB 还是 gEmuera 解释器问题。

## 结论

**不是 ERB 没有清除。** 商店入口 `ERB/2-SHOPコマンド関連/1-SHOP.ERB` 的
`@SHOP画面処理` 明确调用了：

```erb
CALL GIDDEL
...
CALL LOOP_CLEARLINE
```

`@LOOP_CLEARLINE` 会按 `VARLINE` 执行 `CLEARLINE LINECOUNT-VARLINE`。所以游戏侧确实
发出了清行指令。

问题在 gEmuera 的 **canvas 图片 overlay 生命周期**：`ConsoleImagePart` 的图片不是
普通文字行的一部分，而是 `lineContainer` 下的独立 `EmueraImage` 节点。

`RefreshCanvasOverlayRows` 原先只更新 `canvasRowsWithPositionedNodes`，而
`RefreshCanvasOverlayVisibility` 对越界/历史图片只用 `node.Position` 做可见性判断，
没有按当前 layout 的 `lineY` 重新定位。`CLEARLINE` 删除/复用行后，如果 overlay 索引
或行布局更新存在一帧延迟，旧图片节点会停留在它最后一次可见的屏幕坐标上，看起来
“不消失”。

## 修复

`scripts/EmueraContent.Canvas.cs`：

1. 新增 `ReleaseStaleCanvasOverlayRows()`：在 overlay 行刷新时释放不再存在于
   `lineNumbers` 中的 image/div overlay 节点；
2. `RefreshCanvasOverlayRows` 对不在 `lineNumbers` 的 positioned row 立即
   `ReleaseCanvasImageOverlays` / `ReleaseCanvasDivOverlays`；
3. `RefreshCanvasOverlayVisibilityForLine` 先用 `GetLineTopByLayout(lineNo)` 取当前
   lineY，必要时调用 `UpdateCanvasImageOverlay` 重新定位，再做可见性判断；
   行已不存在时立即释放 overlay。

这三处都是防御性生命周期修复，不改变正常图片/div 的可见内容和绘制顺序。

## 验证

- 构建 0 error。
- 从教程/角色创建一路走到 `[10003] 角色·物品购买` 的 runner 流程截图
  `E:\tmp\gemuera-reports\bemani-shop-final-overlay\run-001\viewport.png`：
  道具列表完整，左侧不再有 tutorial 图标残留；底部 tutorial 提示正常。
- 加载 save94 后打开商店截图同样干净。
- eratoho v24pure 回归：`Passed`，semantic `4859125d3f98…` 不变。

## 风险

- 该修复只处理 canvas 后端的独立 image/div overlay；若真机仍见残留，需要确认是否
  走 `UseCanvasRenderBackend=false` 的 Control fallback，或图片属于 CBG/HTML island
  另一条渲染路径。
