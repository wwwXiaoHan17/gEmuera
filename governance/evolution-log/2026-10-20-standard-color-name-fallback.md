# 2026-10-20 标准颜色名回退 Black 导致 eraBEMANI 角色创建行消失

## 背景

eraBEMANI 角色创建界面（`ERB/1-タイトル,ニューゲーム処理/3-ニューゲーム処理.ERB`）
的以下行不可见：

- `名字`、`性别`、`种族`
- `体格`、`胸围`
- `兽耳`、`尾巴`、`翅膀`
- `毛茸茸`、`生命活动`
- `肛门经验`、`高潮经验` 等经验行

而 `【基础】`、`【外貌】`、`【性经验】`、`[11] - 无` 等普通 `PRINTFORML` 行正常。
`display.json` 中所有行都存在，说明是**颜色渲染**问题，不是输出丢失。

## 根因

游戏通过：

```erb
CALL PRINTCOLOR, @"...", "lightskyblue"
CALL PRINTCOLOR, @"...", "lightcoral"
```

打印这些行。`PRINTCOLOR` 内部：

```erb
SETCOLOR COLOR_FROMNAME(色名)
PRINTFORM 本文
RESETCOLOR
```

`COLOR_FROMNAME` 走 `uEmuera.Drawing.Color.FromName`。参考 Emuera 使用
`System.Drawing.Color.FromName`，认识全部 141 个标准颜色名；gEmuera 的兼容层原先只硬编码
32 个颜色名，且 `StandardColorNames` 141 名表只在 megaten 的
`ui.standard-color-name.v1` 会话声明下启用。

`lightskyblue` / `lightcoral` 不在 32 名表内，v24pure 下回退为 `Black`；黑底黑字 →
整行视觉消失。`PRINTCOLOR` 的 `COLOR_FROMNAME` 返回 0 而不是 -1，因此也不会走
`RESETCOLOR` 分支，问题被隐藏。

## 修复

`scripts/uEmuera/Drawing.cs` 的 `Color.FromName` 在 32 名表未命中后，**无条件**查
`StandardColorNames` 141 名表（与参考 `System.Drawing.Color.FromName` 一致），最后才
回退 Black 并记 warning。移除 megaten 门控，因为标准颜色名是 v24 基线语义，不是 megaten
专属能力。

## 验证

- 构建 0 error。
- eraBEMANI 角色创建截图
  `E:\tmp\gemuera-reports\bemani-charcolor-fix\run-001\viewport.png`：
  `名字/性别/种族/体格/兽耳/尾巴/翅膀/毛茸茸/生命活动` 以及经验行全部出现；
  采样像素含 `(135,206,250)` lightskyblue 和 `(240,128,128)` lightcoral。
- eratoho v24pure 回归：`Passed`，semantic
  `4859125d3f984f087eb143dd83fa54f3b754055187cb87a9e6b166d4505754a1` 不变。
- 三冒烟、CoreContractSmoke、xUnit 131+35+3、governance 4/4 全绿。

## 相关

- `scripts/uEmuera/Drawing.cs`
- `scripts/uEmuera/StandardColorNames.cs`
- 游戏 `ERB/9-ヘッダーファイル,システム構成関数/7-式中関数,汎用関数.ERB` 的
  `@PRINTCOLOR` / `@PRINTCOLORL` / `@PRINTCOLORLC` 定义
