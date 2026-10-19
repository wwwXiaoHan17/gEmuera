# 2026-10-20 SPRITEDISPOSEALL 后 Canvas 图片 overlay 仍复用旧纹理

## 背景

eraBEMANI 教程结束后进入 `[10003] 角色·物品购买`、再到 `[2000] 奴隶购买`，左侧仍显示
tutorial 的 `DJ_USI`/牛等 `<img>`，覆盖商店列表。用户要求从参考实现源头定位，而不是
继续在 overlay 清理层打补丁。

## 源头核对

游戏 `ERB/9-ヘッダーファイル,システム構成関数/7-式中関数,汎用関数.ERB`：

```erb
@GIDDEL
...
;ついでにスプライトも消す
SPRITEDISPOSEALL 0
```

`@SHOP画面処理`、`@CHARA_LIST` 等进入新界面前都会 `CALL GIDDEL`，因此 tutorial 动态
`SPRITECREATE` 的图标 sprite 已被游戏主动销毁，不是 ERB 没清。

参考 v24 `ConsoleImagePart.DrawTo`：

```csharp
ASprite img = cImage;
if ((isSelecting || isFocus) && cImageB != null)
    img = cImageB;
if (img != null && img.IsCreated)
{
    // draw
}
```

`SPRITEDISPOSEALL` 后 `ASprite.IsCreated == false`，参考实现直接不绘制。

gEmuera canvas 的 `EmueraImage` overlay 在 `TryResolveCanvasImage` 失败时保留了
`node.SourceTexture`：

```csharp
node.Visible = node.SourceTexture != null && ...
```

这是为“异步补图/差分图刷新”设计的，但无法区分 **sprite 已被销毁** 和 **纹理异步加载中**。
所以 `SPRITEDISPOSEALL` 后旧纹理仍留在 overlay 节点上，看起来图片永远不消失。

## 修复

`scripts/EmueraContent.Canvas.cs`：

1. 新增 `IsCanvasImageSourceDisposed(ConsoleImagePart, bool isSelecting)`：
   - `image.Image` / `ImageBackground` 仍是 `ASprite` 但 `IsCreated == false` → disposed；
   - 或 `AppContents.GetSprite(resourceName) == null` → 资源已不存在。
2. `UpdateCanvasImageOverlay` 解析失败时，若源 sprite 已销毁，直接 `Visible = false`，
   不再保留旧纹理；只有真正异步补图才走旧逻辑。
3. `RefreshCanvasOverlayVisibilityForLine` 同样先检查源 sprite 是否已销毁。

这样与参考 `img.IsCreated` 判断对齐：sprite 销毁后，历史 console 行里的 `<img>` 立即
不再绘制，而不是靠后续行删除/overlay 清理来补救。

## 验证

- 构建 0 error。
- runner：tutorial→角色创建→商店→道具购买→奴隶购买流程截图
  `E:\tmp\gemuera-reports\bemani-shop-spritedispose\run-001\viewport.png`：
  左侧无 tutorial 图标，商店列表完整。
- eratoho v24pure 回归：`Passed`，semantic `4859125d3f98…` 不变。
- 之前的 overlay 行删除清理保留为防御层，但根因修复是上述 `IsCreated` 检查。

## 参考

- `E:\MyCode\eraCode\emuera.em-master\Emuera\UI\Game\ConsoleImagePart.cs`
  `DrawTo` 的 `img.IsCreated` 分支
- `E:\MyCode\eraCode\emuera.em-master\Emuera\UI\Game\Image\AppContents.cs`
  `SpriteDisposeAll` 会移除动态 sprite
